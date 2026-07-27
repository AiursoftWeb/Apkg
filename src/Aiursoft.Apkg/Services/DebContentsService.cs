
using Aiursoft.Apkg.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aiursoft.Apkg.Services;

/// <summary>
/// Manages the <see cref="Entities.DebContents"/> cache table. Provides read-through
/// caching of <c>dpkg-deb -c</c> output keyed by SHA256.
/// </summary>
public class DebContentsService(
    ApkgDbContext db,
    ILogger<DebContentsService> logger)
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

        return DeserializeContents(entry.ContentsJson, entry.SHA256);
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

        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in sha256List.Chunk(150))
        {
            var chunkList = chunk.ToList();
            var entries = await db.DebContents
                .AsNoTracking()
                .Where(c => chunkList.Contains(c.SHA256))
                .Select(c => new { c.SHA256, c.ContentsJson })
                .ToListAsync();

            foreach (var entry in entries)
            {
                var paths = DeserializeContents(entry.ContentsJson, entry.SHA256);
                if (paths != null)
                    result[entry.SHA256] = paths;
            }
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
            db.DebContents.Add(new DebContents
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

        var files = await Contents.ContentsGeneratorService.GetDebContentsAsync(casPath);

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


    // ═══════════════════════════════════════════════════════════════════════
    // Private helpers
    // ═══════════════════════════════════════════════════════════════════════


    private List<string>? DeserializeContents(string json, string sha256)
    {
        try
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deserialize cache entry for SHA256 {SHA256}. It will be recomputed.", sha256);
            return null;
        }
    }

    private static string SerializeContents(List<string> paths)
    {
        return Newtonsoft.Json.JsonConvert.SerializeObject(paths);
    }
}

