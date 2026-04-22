using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Apkg.Entities;
using Aiursoft.AptClient;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services.BackgroundJobs;

public class MirrorSyncJob(
    TemplateDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<MirrorSyncJob> logger) : IBackgroundJob
{
    public string Name => "APT Mirror Sync V2";

    public string Description => "Synchronizes entire suites (multiple components) into versioned buckets.";

    public async Task ExecuteAsync()
    {
        logger.LogInformation("MirrorSyncJob V2 started.");
        var mirrors = await db.AptMirrors.ToListAsync();

        foreach (var mirror in mirrors)
        {
            try
            {
                await SyncMirrorSuiteAsync(mirror);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync mirror suite {Suite} from {BaseUrl}", mirror.Suite, mirror.BaseUrl);
            }
        }

        logger.LogInformation("MirrorSyncJob V2 finished.");
    }

    private async Task SyncMirrorSuiteAsync(AptMirror mirror)
    {
        logger.LogInformation("Starting sync for suite {Suite} from {BaseUrl}...", mirror.Suite, mirror.BaseUrl);

        // 1. Create a new bucket for this sync session
        var bucket = new AptBucket
        {
            CreatedAt = DateTime.UtcNow
        };
        db.AptBuckets.Add(bucket);
        await db.SaveChangesAsync();

        var components = mirror.Components.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var architectures = mirror.Architecture.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var totalInserted = 0;
        var insertedKeys = new HashSet<string>();
        foreach (var arch in architectures)
        {
            foreach (var component in components)
            {
                try
                {
                    totalInserted += await FetchAndInsertComponentAsync(mirror, bucket, component, arch, insertedKeys);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch component {Component} [{Arch}] for suite {Suite}. Skipping...", component, arch, mirror.Suite);
                }
            }
        }

        if (totalInserted == 0)
        {
            logger.LogWarning("No packages were found for any component in suite {Suite}. The sync is considered failed and the bucket will not be swapped.", mirror.Suite);
            return;
        }

        // 2. Atomic swap: Update the mirror's current bucket pointer
        logger.LogInformation("Sync completed for suite {Suite}. Swapped {Count} packages to Bucket {BucketId}.", mirror.Suite, totalInserted, bucket.Id);
        
        // Re-attach the mirror entity because change tracker might have been cleared
        db.AptMirrors.Update(mirror);
        mirror.CurrentBucketId = bucket.Id;
        await db.SaveChangesAsync();
    }

    private async Task<int> FetchAndInsertComponentAsync(AptMirror mirror, AptBucket bucket, string component, string arch, HashSet<string> insertedKeys)
    {
        var upstreamRoot = $"{mirror.BaseUrl.TrimEnd('/')}/{mirror.Distro.TrimStart('/')}";
        logger.LogInformation("Fetching component {Component} [{Arch}] for suite {Suite} from {UpstreamRoot}...", component, arch, mirror.Suite, upstreamRoot);

        var repo = new AptClient.AptRepository(upstreamRoot, mirror.Suite, mirror.SignedBy, () => httpClientFactory.CreateClient());
        var source = new AptPackageSource(repo, component, arch, () => httpClientFactory.CreateClient());

        var count = 0;
        await foreach (var pkgFromApt in source.FetchPackagesAsync())
        {
            var pkg = pkgFromApt.Package;
            
            // Check for duplicates within the current bucket
            var key = $"{pkg.Package}|{pkg.Version}|{pkg.Architecture}|{component}";
            if (!insertedKeys.Add(key))
            {
                continue;
            }

            // Construct the upstream URL for lazy sync using the combined upstream root
            var remoteUrl = $"{upstreamRoot.TrimEnd('/')}/{pkg.Filename.TrimStart('/')}";

            var entity = new AptPackage
            {
                BucketId = bucket.Id,
                Component = component,
                Architecture = pkg.Architecture,
                IsVirtual = true,
                RemoteUrl = remoteUrl,
                OriginSuite = mirror.Suite,
                OriginComponent = component,

                Package = pkg.Package,
                Version = pkg.Version,
                Maintainer = pkg.Maintainer,
                Description = pkg.Description,
                DescriptionMd5 = pkg.DescriptionMd5,
                Section = pkg.Section,
                Priority = pkg.Priority,
                Origin = pkg.Origin,
                Bugs = pkg.Bugs,
                Filename = pkg.Filename,
                Size = pkg.Size,
                MD5sum = pkg.MD5sum,
                SHA1 = pkg.SHA1,
                SHA256 = pkg.SHA256,
                SHA512 = pkg.SHA512,
                InstalledSize = pkg.InstalledSize,
                OriginalMaintainer = pkg.OriginalMaintainer,
                Homepage = pkg.Homepage,
                Depends = pkg.Depends,
                Source = pkg.Source,
                MultiArch = pkg.MultiArch,
                Provides = pkg.Provides,
                Suggests = pkg.Suggests,
                Recommends = pkg.Recommends,
                Conflicts = pkg.Conflicts,
                Breaks = pkg.Breaks,
                Replaces = pkg.Replaces,
                Extras = pkg.Extras
            };
            db.AptPackages.Add(entity);
            count++;

            if (count % 1000 == 0)
            {
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                logger.LogInformation("Saved {Count} packages for {Component} [{Arch}] so far...", count, component, arch);
            }
        }
        
        // Save remaining packages
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        logger.LogInformation("Finished syncing {Count} packages for {Component} [{Arch}].", count, component, arch);
        return count;
    }
}
