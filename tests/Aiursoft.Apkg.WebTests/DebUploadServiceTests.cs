using System.Diagnostics;
using System.Security.Cryptography;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.FileStorage;
using Aiursoft.Apkg.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiursoft.Apkg.WebTests;

/// <summary>
/// Tests for <see cref="DebUploadService"/> idempotency / deduplication.
///
/// Core guarantee: publishing the same package version more than once — whether via the GUI,
/// the REST API, or the apkg CLI — must never write a new DB row or a new CAS file.  The
/// server must respond with 409 Conflict and leave storage unchanged.
///
/// Two distinct 409 scenarios are covered:
///   1. SHA-256 conflict  — the exact same .deb binary is uploaded again.
///   2. Slot conflict     — a different binary with the same
///                          (repo, package, version, arch, component) is uploaded.
///
/// Background: arch=all packages are built once per suite target.  Without
/// SOURCE_DATE_EPOCH=0 each build embeds different mtimes, producing a different SHA-256
/// for the same logical content.  If the slot-conflict guard were missing those separate
/// builds would silently accumulate conflicting rows in AptPackages (different SHA-256,
/// same pool/Filename), causing apt "File has unexpected size" errors.
/// See: docs/design.md §8 "Known pitfalls — arch=all hash drift"
/// </summary>
[TestClass]
public class DebUploadServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal .deb using dpkg-deb --build.
    ///
    /// <paramref name="extraPayloadBytes"/> is written to usr/share/test-pkg/payload.bin
    /// so that two calls with different byte arrays produce binaries with different SHA-256
    /// values (needed for the slot-conflict scenario).
    /// </summary>
    /// <summary>
    /// Each call produces its own unique output directory so two builds of the same
    /// package+version never overwrite each other's .deb file.
    ///
    /// Pass <paramref name="reproducible"/>=true (default) to set SOURCE_DATE_EPOCH=0
    /// for deterministic output.  Pass false to get a non-deterministic build whose
    /// SHA-256 depends on the current timestamp — used to simulate the "different CI
    /// build" scenario for the slot-conflict test.
    /// </summary>
    private static async Task<string> BuildMinimalDebAsync(
        string testRoot,
        string packageName,
        string version,
        byte[]? extraPayloadBytes = null,
        bool reproducible = true)
    {
        var buildId = Guid.NewGuid().ToString("N");
        var stagingDir = Path.Combine(testRoot, $"staging-{buildId}");
        var outputDir  = Path.Combine(testRoot, $"output-{buildId}");
        var debianDir  = Path.Combine(stagingDir, "DEBIAN");
        Directory.CreateDirectory(debianDir);
        Directory.CreateDirectory(outputDir);

        await File.WriteAllTextAsync(Path.Combine(debianDir, "control"),
            $"Package: {packageName}\n" +
            $"Version: {version}\n" +
            "Architecture: all\n" +
            "Maintainer: Test <test@example.com>\n" +
            "Description: Test package for DebUploadService idempotency tests\n");

        if (extraPayloadBytes != null)
        {
            var payloadDir = Path.Combine(stagingDir, "usr", "share", packageName);
            Directory.CreateDirectory(payloadDir);
            await File.WriteAllBytesAsync(Path.Combine(payloadDir, "payload.bin"), extraPayloadBytes);
        }

        var debPath = Path.Combine(outputDir, $"{packageName}_{version}_all.deb");

        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = "dpkg-deb",
            ArgumentList = { "--build", stagingDir, debPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (reproducible)
            proc.StartInfo.Environment["SOURCE_DATE_EPOCH"] = "0";

        proc.Start();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"dpkg-deb failed: {err}");
        }

        return debPath;
    }

    private static ServiceProvider BuildServiceProvider(string storagePath, string dbName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApkgDbContext, SqliteContext>(
            options => options.UseSqlite(dbName));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["Storage:Path"] = storagePath }!)
            .Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<StorageRootPathProvider>();
        services.AddSingleton<FeatureFoldersProvider>();
        services.AddTransient<DebPackageParserService>();
        services.AddTransient<DebUploadService>();

        return services.BuildServiceProvider();
    }

    private static string Sha256OfFile(string path)
    {
        using var fs = File.OpenRead(path);
        return BitConverter.ToString(SHA256.HashData(fs)).Replace("-", "").ToLowerInvariant();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Uploading the identical .deb binary a second time must return 409 and must NOT
    /// add a second row to LocalPackages or a second file in the CAS store.
    ///
    /// This simulates an accidental re-trigger of the same CI job or a user clicking
    /// "publish" twice in the GUI.
    /// </summary>
    [TestMethod]
    public async Task UploadDeb_SameFileTwice_SecondUploadIsRejectedWithConflict_AndNothingIsWritten()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "deb-upload-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var dbName = $"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared";
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbName);
        connection.Open();

        var storagePath = Path.Combine(tempRoot, "storage");
        await using var provider = BuildServiceProvider(storagePath, dbName);

        await using var scope1 = provider.CreateAsyncScope();
        var db = scope1.ServiceProvider.GetRequiredService<ApkgDbContext>();
        await db.Database.EnsureCreatedAsync();

        var repo = new AptRepository
        {
            Distro = "anduinos",
            Name = "noble-addon",
            Suite = "noble-addon",
            Components = "main",
            Architecture = "amd64",
        };
        db.AptRepositories.Add(repo);

        // LocalPackage.UploadedByUserId is a FK to the Users table — seed a minimal user.
        var userId = Guid.NewGuid().ToString();
        db.Users.Add(new Aiursoft.Apkg.Entities.User
        {
            Id = userId,
            UserName = "test-user",
            NormalizedUserName = "TEST-USER",
            Email = "test@example.com",
            NormalizedEmail = "TEST@EXAMPLE.COM",
            DisplayName = "Test User",
        });
        await db.SaveChangesAsync();

        var debPath = await BuildMinimalDebAsync(tempRoot, "idempotency-test-pkg", "1.0.0");
        var originalSha256 = Sha256OfFile(debPath);

        // ── First upload: must succeed ────────────────────────────────────────
        var svc = scope1.ServiceProvider.GetRequiredService<DebUploadService>();
        var result1 = await svc.UploadDebToRepositoryAsync(repo, "main", debPath, userId);

        Assert.AreEqual(200, result1.StatusCode,
            $"First upload should succeed. Error: {result1.Error}");
        Assert.IsNotNull(result1.Package);

        var casFilePath = Path.Combine(storagePath, "Objects",
            originalSha256[..2], $"{originalSha256}.deb");
        Assert.IsTrue(File.Exists(casFilePath), "CAS file should exist after first upload.");

        // ── Second upload of the identical file ──────────────────────────────
        // The original file was moved to CAS; write a fresh copy for the second upload.
        var debPath2 = await BuildMinimalDebAsync(tempRoot, "idempotency-test-pkg", "1.0.0");

        await using var scope2 = provider.CreateAsyncScope();
        var svc2 = scope2.ServiceProvider.GetRequiredService<DebUploadService>();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApkgDbContext>();

        var result2 = await svc2.UploadDebToRepositoryAsync(repo, "main", debPath2, userId);

        Assert.AreEqual(409, result2.StatusCode,
            "Second upload of the same binary must return 409 Conflict (SHA-256 already exists).");
        Assert.IsTrue(result2.Error!.Contains("SHA256"),
            $"Error message should mention SHA256. Got: {result2.Error}");

        var rowCount = await db2.LocalPackages
            .CountAsync(p => p.RepositoryId == repo.Id && p.Package == "idempotency-test-pkg");
        Assert.AreEqual(1, rowCount,
            "Exactly one LocalPackage row must exist — the second upload must not write a new row.");
    }

    /// <summary>
    /// Uploading a DIFFERENT binary for the same (package, version, arch, component) slot
    /// must also be rejected with 409.  The second binary must not be written to the CAS store.
    ///
    /// This is the cross-suite arch=all scenario: without SOURCE_DATE_EPOCH=0 each suite
    /// builds a slightly different .deb for the same package version.  The slot-conflict
    /// guard ensures that only the first suite's upload wins; subsequent suites are blocked.
    /// Without this guard, multiple conflicting SHA-256 rows would accumulate in AptPackages
    /// under the same pool/Filename, causing non-deterministic apt hash mismatches.
    /// </summary>
    [TestMethod]
    public async Task UploadDeb_DifferentBinaryForSameSlot_SecondUploadIsRejectedWithSlotConflict()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "deb-upload-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var dbName = $"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared";
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbName);
        connection.Open();

        var storagePath = Path.Combine(tempRoot, "storage");
        await using var provider = BuildServiceProvider(storagePath, dbName);

        await using var scope1 = provider.CreateAsyncScope();
        var db = scope1.ServiceProvider.GetRequiredService<ApkgDbContext>();
        await db.Database.EnsureCreatedAsync();

        var repo = new AptRepository
        {
            Distro = "anduinos",
            Name = "questing-addon",
            Suite = "questing-addon",
            Components = "main",
            Architecture = "amd64",
        };
        db.AptRepositories.Add(repo);

        var userId = Guid.NewGuid().ToString();
        db.Users.Add(new Aiursoft.Apkg.Entities.User
        {
            Id = userId,
            UserName = "test-user-2",
            NormalizedUserName = "TEST-USER-2",
            Email = "test2@example.com",
            NormalizedEmail = "TEST2@EXAMPLE.COM",
            DisplayName = "Test User 2",
        });
        await db.SaveChangesAsync();

        // Two .deb files: same (Package, Version, Arch) but different payload → different SHA-256.
        // reproducible: false means SOURCE_DATE_EPOCH is NOT set, so each build embeds the
        // current wall-clock mtime — guaranteeing a different binary even for identical sources.
        // This simulates the pre-fix state where each suite CI job produced a different SHA-256.
        var debPathA = await BuildMinimalDebAsync(tempRoot, "slot-conflict-pkg", "2.0.0",
            extraPayloadBytes: [0xAA, 0xBB, 0xCC], reproducible: false);
        await Task.Delay(1100); // ensure a different mtime second for the second build
        var debPathB = await BuildMinimalDebAsync(tempRoot, "slot-conflict-pkg", "2.0.0",
            extraPayloadBytes: [0xDD, 0xEE, 0xFF], reproducible: false);

        var sha256A = Sha256OfFile(debPathA);
        var sha256B = Sha256OfFile(debPathB);
        Assert.AreNotEqual(sha256A, sha256B,
            "Pre-condition: the two test .deb files must have different SHA-256 values.");

        var svc = scope1.ServiceProvider.GetRequiredService<DebUploadService>();

        // ── First upload (e.g. noble-addon CI): must succeed ─────────────────
        var result1 = await svc.UploadDebToRepositoryAsync(repo, "main", debPathA, userId);
        Assert.AreEqual(200, result1.StatusCode,
            $"First upload should succeed. Error: {result1.Error}");

        // ── Second upload (e.g. questing-addon CI, different binary): must be blocked ─
        await using var scope2 = provider.CreateAsyncScope();
        var svc2 = scope2.ServiceProvider.GetRequiredService<DebUploadService>();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApkgDbContext>();

        var result2 = await svc2.UploadDebToRepositoryAsync(repo, "main", debPathB, userId);

        Assert.AreEqual(409, result2.StatusCode,
            "Second upload of a different binary for the same slot must return 409 Conflict.");
        Assert.IsFalse(result2.Error!.Contains("SHA256"),
            "This 409 is a slot conflict, not a SHA-256 conflict.");

        // Verify the second binary was NOT stored in CAS
        var casPathB = Path.Combine(storagePath, "Objects",
            sha256B[..2], $"{sha256B}.deb");
        Assert.IsFalse(File.Exists(casPathB),
            "The second binary must NOT be saved to the CAS store when rejected by slot conflict.");

        // Verify only one row exists for this slot
        var rowCount = await db2.LocalPackages
            .CountAsync(p => p.RepositoryId == repo.Id && p.Package == "slot-conflict-pkg");
        Assert.AreEqual(1, rowCount,
            "Exactly one LocalPackage row must exist for this (package, version) slot.");
    }
}
