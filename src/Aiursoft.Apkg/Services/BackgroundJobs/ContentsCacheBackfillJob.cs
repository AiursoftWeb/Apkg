using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.FileStorage;
using Aiursoft.Canon.BackgroundJobs;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services.BackgroundJobs;

/// <summary>
/// One-shot (manually triggered) job that populates the <see cref="DebContents"/>
/// cache for all existing .deb packages whose SHA256 is not yet cached.
///
/// After initial deployment, an admin triggers this once from the Jobs dashboard.
/// Subsequent uploads are cached eagerly by <see cref="DebUploadService"/>;
/// subsequent syncs populate any remaining gaps lazily via
/// <see cref="RepositorySyncJob"/>.
/// </summary>
public class ContentsCacheBackfillJob(
    ApkgDbContext db,
    FeatureFoldersProvider folders,
    DebContentsService contentsCache,
    ILogger<ContentsCacheBackfillJob> logger) : IBackgroundJob
{
    public string Name => "Contents Cache Backfill";

    public string Description =>
        "One-time job: computes dpkg-deb -c output for all existing CAS .deb files " +
        "and stores the file listings in the DebContents cache table. " +
        "Run this once after upgrading an existing server to the Contents cache feature. " +
        "New server installations do not need to run this job.";

    public async Task ExecuteAsync()
    {
        logger.LogInformation("ContentsCacheBackfillJob started.");

        var objectsRoot = folders.GetObjectsFolder();

        // Collect all distinct SHA256s from local uploads that aren't yet cached
        var uncachedSha256s = await db.ApkgDebPackages
            .Where(p => !db.DebContents.Any(c => c.SHA256 == p.SHA256))
            .Select(p => p.SHA256)
            .Distinct()
            .ToListAsync();

        // Also include SHA256s from currently-active AptPackages
        var liveBuckets = await db.AptRepositories
            .Where(r => r.PrimaryBucketId != null)
            .Select(r => r.PrimaryBucketId!.Value)
            .ToListAsync();

        if (liveBuckets.Count > 0)
        {
            var liveSha256s = await db.AptPackages
                .Where(p => liveBuckets.Contains(p.BucketId)
                         && !db.DebContents.Any(c => c.SHA256 == p.SHA256))
                .Select(p => p.SHA256)
                .Distinct()
                .ToListAsync();

            uncachedSha256s = uncachedSha256s
                .Union(liveSha256s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        logger.LogInformation("Backfill: {Count} SHA256s need contents caching", uncachedSha256s.Count);

        if (uncachedSha256s.Count == 0)
        {
            logger.LogInformation("Backfill: nothing to do, exiting.");
            return;
        }

        int success = 0;
        int skipped = 0;
        int failed = 0;

        foreach (var sha256 in uncachedSha256s)
        {
            try
            {
                var casPath = Path.Combine(objectsRoot, sha256[..2], $"{sha256}.deb");
                if (!File.Exists(casPath))
                {
                    skipped++;
                    continue;
                }

                await contentsCache.ComputeAndCacheAsync(sha256, casPath);
                success++;

                if (success % 50 == 0)
                    logger.LogInformation("Backfill progress: {Success} cached, {Skipped} skipped, {Failed} failed",
                        success, skipped, failed);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to backfill contents for SHA256 {Sha256}", sha256);
                failed++;
            }
        }

        logger.LogInformation("Backfill complete: {Success} cached, {Skipped} skipped, {Failed} failed",
            success, skipped, failed);
    }
}
