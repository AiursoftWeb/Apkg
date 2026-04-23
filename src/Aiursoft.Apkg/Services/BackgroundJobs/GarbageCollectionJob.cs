using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.FileStorage;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services.BackgroundJobs;

public class GarbageCollectionJob(
    ApkgDbContext db,
    FeatureFoldersProvider folders,
    ILogger<GarbageCollectionJob> logger) : IBackgroundJob
{
    private string BucketsRoot => Path.Combine(folders.GetWorkspaceFolder(), "Buckets");
    private string ObjectsRoot => Path.Combine(folders.GetWorkspaceFolder(), "Objects");

    public string Name => "APT Garbage Collection";

    public string Description => "Cleans up orphaned buckets, packages, and physical files to free up disk and DB space.";

    public async Task ExecuteAsync()
    {
        logger.LogInformation("GarbageCollectionJob started.");

        // 1. Identify active buckets
        var activeMirrors = await db.AptMirrors
            .Where(m => m.CurrentBucketId != null)
            .Select(m => m.CurrentBucketId!.Value)
            .ToListAsync();
            
        var activeRepoCurrentBuckets = await db.AptRepositories
            .Where(r => r.CurrentBucketId != null)
            .Select(r => r.CurrentBucketId!.Value)
            .ToListAsync();

        // PendingBucketId buckets are being staged for signing — they must never be deleted,
        // even though they are not yet referenced by CurrentBucketId.
        var activeRepoPendingBuckets = await db.AptRepositories
            .Where(r => r.PendingBucketId != null)
            .Select(r => r.PendingBucketId!.Value)
            .ToListAsync();

        var activeBucketIds = activeMirrors
            .Union(activeRepoCurrentBuckets)
            .Union(activeRepoPendingBuckets)
            .Distinct()
            .ToList();

        // Only delete buckets that are fully built (BuildFinished = true) but not referenced.
        // Buckets with BuildFinished = false are still being constructed — don't touch them.
        // Safety net: also prune very old unfinished buckets (>2 hours) from crashed jobs.
        var crashThreshold = DateTime.UtcNow.AddHours(-2);
        
        var orphanedBuckets = await db.AptBuckets
            .Where(b => !activeBucketIds.Contains(b.Id) &&
                        (b.BuildFinished || b.CreatedAt < crashThreshold))
            .Select(b => b.Id)
            .ToListAsync();

        logger.LogInformation("Found {Count} orphaned buckets to delete.", orphanedBuckets.Count);

        foreach (var bucketId in orphanedBuckets)
        {
            // 1. Delete physical bucket directory (Packages, Packages.gz)
            var bucketDir = Path.Combine(BucketsRoot, bucketId.ToString());
            if (Directory.Exists(bucketDir))
            {
                Directory.Delete(bucketDir, true);
            }

            // 2. Delete packages from DB
            var packages = await db.AptPackages.Where(p => p.BucketId == bucketId).ToListAsync();
            db.AptPackages.RemoveRange(packages);

            // 3. Delete bucket from DB
            var bucket = await db.AptBuckets.FindAsync(bucketId);
            if (bucket != null)
            {
                db.AptBuckets.Remove(bucket);
            }

            await db.SaveChangesAsync();
        }

        // 4. Clean up orphaned CAS physical files (.deb)
        if (Directory.Exists(ObjectsRoot))
        {
            // Get all referenced hashes
            var referencedHashes = await db.AptPackages
                .Select(p => p.SHA256)
                .Distinct()
                .ToListAsync();

            var hashSet = new HashSet<string>(referencedHashes.Select(h => h.ToLowerInvariant()));

            var debFiles = Directory.GetFiles(ObjectsRoot, "*.deb", SearchOption.AllDirectories);
            int deletedFiles = 0;
            foreach (var file in debFiles)
            {
                var hash = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                if (!hashSet.Contains(hash))
                {
                    File.Delete(file);
                    deletedFiles++;
                }
            }
            
            logger.LogInformation("Deleted {Count} orphaned physical .deb files.", deletedFiles);
        }

        logger.LogInformation("GarbageCollectionJob finished.");
    }
}
