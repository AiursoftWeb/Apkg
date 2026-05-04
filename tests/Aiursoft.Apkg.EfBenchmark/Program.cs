// Benchmark: EF Core graph-traversal UPDATE flood
//
// Reproduces the MirrorSyncJob bug where calling db.DbSet.Update(mirror) after a
// ChangeTracker.Clear() causes EF to re-attach stale navigation-property entities as
// Modified and emit a flood of unnecessary UPDATE statements.
//
// Run with:   dotnet run -c Release

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Diagnostics;

const int TotalPackages = 5000;
const int BatchSize = 1000;

Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║   EF Core Graph-Traversal UPDATE Flood — Benchmark              ║");
Console.WriteLine($"║   {TotalPackages} packages, batch-save every {BatchSize}                              ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
Console.WriteLine();

var buggy = await RunScenario("BUGGY  — db.DbSet.Update(mirror)", useBuggyPath: true);
var fixed_ = await RunScenario("FIXED  — db.Entry(mirror).State=Modified", useBuggyPath: false);

Console.WriteLine();
Console.WriteLine("┌──────────────────────────────────────────┬──────────────┬──────────┐");
Console.WriteLine("│ Scenario                                 │ UPD Packages │    Time  │");
Console.WriteLine("├──────────────────────────────────────────┼──────────────┼──────────┤");
Console.WriteLine($"│ {"BUGGY  — db.DbSet.Update(mirror)",-40} │ {buggy.UpdatePkgs,12} │ {buggy.Elapsed.TotalMilliseconds,6:F0} ms │");
Console.WriteLine($"│ {"FIXED  — db.Entry(mirror).State=Modified",-40} │ {fixed_.UpdatePkgs,12} │ {fixed_.Elapsed.TotalMilliseconds,6:F0} ms │");
Console.WriteLine("└──────────────────────────────────────────┴──────────────┴──────────┘");
Console.WriteLine();
Console.WriteLine($"  Unnecessary UPDATE statements eliminated : {buggy.UpdatePkgs - fixed_.UpdatePkgs}");
Console.WriteLine($"  Speed-up (final SaveChangesAsync call)   : {buggy.FinalSaveMs / fixed_.FinalSaveMs:F1}x faster");
Console.WriteLine();

// ─────────────────────────────────────────────────────────────────────────────

async Task<(int UpdatePkgs, TimeSpan Elapsed, double FinalSaveMs)> RunScenario(
    string name, bool useBuggyPath)
{
    var dbPath = Path.GetTempFileName();
    try
    {
        var counter = new SqlCounter();

        // ── DB setup ──────────────────────────────────────────────────────────
        using (var db = new BenchmarkDb(dbPath, counter))
        {
            await db.Database.EnsureCreatedAsync();
            var mirror = new Mirror { Suite = "noble" };
            db.Mirrors.Add(mirror);
            await db.SaveChangesAsync();
        }

        counter.Reset();
        var swTotal = Stopwatch.StartNew();

        using (var db = new BenchmarkDb(dbPath, counter))
        {
            var mirror = await db.Mirrors.FirstAsync();
            mirror.LastPullTime = DateTime.UtcNow;

            // ── Step 1: Create secondary bucket (intentional Update — bucket has no
            //   packages yet, so the graph walk is safe and necessary here)
            var bucket = new Bucket { CreatedAt = DateTime.UtcNow };
            db.Mirrors.Update(mirror);
            mirror.SecondaryBucket = bucket;
            await db.SaveChangesAsync();   // atomic: INSERT bucket + UPDATE mirror.SecondaryBucketId

            // ── Step 2: Batch-insert packages (simulates FetchAndInsertComponentAsync)
            //
            //   While `bucket` is tracked, EF relationship-fixup automatically populates
            //   bucket.Packages for each package we add whose BucketId matches bucket.Id.
            //   After ChangeTracker.Clear(), `bucket` is detached but bucket.Packages in
            //   memory still holds the first-batch entities (with real, non-zero IDs).
            //   Subsequent batches are NOT fixup'd (bucket is detached) so only the first
            //   batch of stale references accumulates.
            for (int i = 0; i < TotalPackages; i++)
            {
                db.Packages.Add(new Package
                {
                    BucketId = bucket.Id,
                    Name = $"libfoo-{i}",
                    Version = "2.1.0",
                    Architecture = "amd64",
                    Description = $"A fake package #{i} used for benchmarking EF graph traversal"
                });

                if ((i + 1) % BatchSize == 0)
                {
                    await db.SaveChangesAsync();
                    db.ChangeTracker.Clear();   // ← detaches bucket; bucket.Packages still has first-batch refs
                }
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            // ── Step 3: Final mirror update — THE CRITICAL DIFFERENCE ────────────
            mirror.LastPullSuccess = true;
            mirror.LastPullResult = $"Synced {TotalPackages} packages.";

            var swFinal = Stopwatch.StartNew();

            if (useBuggyPath)
            {
                // db.Update() does a deep graph walk:
                //   mirror → SecondaryBucket (=bucket) → bucket.Packages (1st-batch stale entities)
                //   → marks all N packages as Modified → SaveChangesAsync emits N UPDATE statements
                db.Mirrors.Update(mirror);
            }
            else
            {
                // db.Entry().State = Modified is shallow: only the mirror row is marked,
                // navigation properties are NOT traversed.
                db.Entry(mirror).State = EntityState.Modified;
            }

            await db.SaveChangesAsync();
            swFinal.Stop();

            swTotal.Stop();
            Console.WriteLine($"  [{name,-44}]");
            Console.WriteLine($"    INSERT Package  statements : {counter.InsertPkgs}");
            Console.WriteLine($"    UPDATE Package  statements : {counter.UpdatePkgs}  ← should be 0 if fixed");
            Console.WriteLine($"    UPDATE Mirror   statements : {counter.UpdateMirrors}");
            Console.WriteLine($"    Total time                 : {swTotal.Elapsed.TotalMilliseconds:F0} ms");
            Console.WriteLine($"    Final SaveChanges time     : {swFinal.Elapsed.TotalMilliseconds:F0} ms");
            Console.WriteLine();

            return (counter.UpdatePkgs, swTotal.Elapsed, swFinal.Elapsed.TotalMilliseconds);
        }
    }
    finally
    {
        File.Delete(dbPath);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Minimal entity model that reproduces the exact navigation-property relationship
// present in the real AptMirror → AptBucket → AptPackage graph.
// ═════════════════════════════════════════════════════════════════════════════

class Mirror
{
    public int Id { get; set; }
    public string Suite { get; set; } = "";
    public int? PrimaryBucketId { get; set; }
    public Bucket? PrimaryBucket { get; set; }
    public int? SecondaryBucketId { get; set; }
    public Bucket? SecondaryBucket { get; set; }
    public DateTime? LastPullTime { get; set; }
    public bool? LastPullSuccess { get; set; }
    public string? LastPullResult { get; set; }
}

class Bucket
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Package> Packages { get; set; } = [];   // ← the navigation property that causes the flood
}

class Package
{
    public int Id { get; set; }
    public int BucketId { get; set; }
    public Bucket? Bucket { get; set; }
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string Description { get; set; } = "";
}

// ─────────────────────────────────────────────────────────────────────────────

class BenchmarkDb(string dbPath, SqlCounter counter) : DbContext
{
    public DbSet<Mirror> Mirrors => Set<Mirror>();
    public DbSet<Bucket> Buckets => Set<Bucket>();
    public DbSet<Package> Packages => Set<Package>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options
            .UseSqlite($"Data Source={dbPath}")
            .AddInterceptors(new SqlInterceptor(counter));
}

// ─────────────────────────────────────────────────────────────────────────────

class SqlCounter
{
    public int UpdatePkgs;
    public int UpdateMirrors;
    public int InsertPkgs;

    public void Reset() { UpdatePkgs = 0; UpdateMirrors = 0; InsertPkgs = 0; }

    public void Count(string sql)
    {
        if (sql.StartsWith("UPDATE") && sql.Contains("\"Packages\"")) Interlocked.Increment(ref UpdatePkgs);
        else if (sql.StartsWith("UPDATE") && sql.Contains("\"Mirrors\"")) Interlocked.Increment(ref UpdateMirrors);
        else if (sql.StartsWith("INSERT") && sql.Contains("\"Packages\"")) Interlocked.Increment(ref InsertPkgs);
    }
}

class SqlInterceptor(SqlCounter counter) : DbCommandInterceptor
{
    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        counter.Count(command.CommandText);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        counter.Count(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        counter.Count(command.CommandText);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        counter.Count(command.CommandText);
        return ValueTask.FromResult(result);
    }
}
