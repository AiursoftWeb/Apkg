using System.Diagnostics;
using Aiursoft.Apkg.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services;

/// <summary>
/// Manages the <see cref="Entities.DebContents"/> cache table. Provides read-through
/// caching of <c>dpkg-deb -c</c> output keyed by SHA256.
/// </summary>
public class DebContentsService(ApkgDbContext db)
{
    /// <summary>
    /// Returns cached file paths for a SHA256, or <c>null</c> if not cached.
    /// </summary>
    public async Task<List<string>?> GetAsync(string sha256)
    {
        var entry = await db.DebContents
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.SHA256 == sha256);

        if (entry == null)
            return null;

        return DeserializeContents(entry.ContentsJson);
    }

    /// <summary>
    /// Batch lookup. Returns a dictionary mapping each found SHA256 to its file paths.
    /// SHA256s without a cache entry are simply absent from the result (no error).
    /// </summary>
    public async Task<Dictionary<string, List<string>>> GetBatchAsync(
        IEnumerable<string> sha256s)
    {
        var sha256List = sha256s.Distinct().ToList();
        if (sha256List.Count == 0)
            return new Dictionary<string, List<string>>();

        var entries = await db.DebContents
            .AsNoTracking()
            .Where(c => sha256List.Contains(c.SHA256))
            .Select(c => new { c.SHA256, c.ContentsJson })
            .ToListAsync();

        var result = new Dictionary<string, List<string>>(entries.Count,
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var paths = DeserializeContents(entry.ContentsJson);
            if (paths != null)
                result[entry.SHA256] = paths;
        }

        return result;
    }

    /// <summary>
    /// Stores pre-parsed file paths for a SHA256. Creates a new row or updates
    /// an existing one (upsert). Thread-safe: if another thread already inserted
    /// this SHA256, the unique constraint violation is caught and we return.
    /// </summary>
    public async Task SetAsync(string sha256, List<string> paths)
    {
        var json = SerializeContents(paths);

        var existing = await db.DebContents.FindAsync(sha256);
        if (existing == null)
        {
            db.DebContents.Add(new Entities.DebContents
            {
                SHA256 = sha256,
                ContentsJson = json,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Race: another thread inserted the same SHA256 between our
                // FindAsync and SaveChangesAsync. Discard our insert — the
                // other thread's cache entry is equally valid.
                db.ChangeTracker.Clear();
            }
        }
        else
        {
            existing.ContentsJson = json;
            existing.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Computes <c>dpkg-deb -c</c> for the given CAS file path, caches the
    /// result keyed by <paramref name="sha256"/>, and returns the file paths.
    /// On cache hit, returns the cached value without shelling out.
    /// </summary>
    public async Task<List<string>> ComputeAndCacheAsync(string sha256, string casPath)
    {
        // Check cache first
        var cached = await GetAsync(sha256);
        if (cached != null)
            return cached;

        // Run dpkg-deb -c
        if (!File.Exists(casPath))
            throw new FileNotFoundException("Deb file not found for contents computation.", casPath);

        var files = await RunDpkgDebContentsAsync(casPath);

        // Store (best-effort; failure here is non-fatal — next sync will retry)
        try
        {
            await SetAsync(sha256, files);
        }
        catch
        {
            // Cache write failure should not block Contents generation
        }

        return files;
    }

    /// <summary>
    /// Returns a summary of the cache state.
    /// </summary>
    public async Task<DebContentsStats> GetStatsAsync()
    {
        var cached = await db.DebContents.CountAsync();

        // Count distinct SHA256s across both sources
        var localSha256s = await db.ApkgDebPackages
            .Select(p => p.SHA256)
            .Distinct()
            .CountAsync();

        // AptPackages SHA256 count would be huge (across all buckets), so we
        // only count the distinct SHA256s from local packages for practical UI.
        // The "hit rate" is a rough guide — mirror packages are counted when
        // they first go through Contents generation.

        return new DebContentsStats(
            TotalCached: cached,
            TotalLocalPackages: localSha256s
        );
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Private helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Shells out to <c>dpkg-deb -c</c> and parses the output.
    /// Identical logic to <see cref="Contents.ContentsGeneratorService.GetDebContentsAsync"/>,
    /// consolidated here to avoid code duplication.
    /// </summary>
    private static async Task<List<string>> RunDpkgDebContentsAsync(string debPath)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dpkg-deb",
            ArgumentList = { "-c", debPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                $"dpkg-deb -c failed (exit {process.ExitCode}): {err}");
        }

        return Contents.ContentsGeneratorService.ParseDpkgDebContents(output);
    }

    private static List<string>? DeserializeContents(string json)
    {
        try
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string SerializeContents(List<string> paths)
    {
        return Newtonsoft.Json.JsonConvert.SerializeObject(paths);
    }
}

/// <summary>
/// Summary statistics for the Contents cache.
/// </summary>
public record DebContentsStats(int TotalCached, int TotalLocalPackages);
