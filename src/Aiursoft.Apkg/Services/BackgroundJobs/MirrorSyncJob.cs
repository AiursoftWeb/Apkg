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
    public string Name => "APT Mirror Sync";

    public string Description => "Synchronizes metadata for all configured APT mirror repositories.";

    public async Task ExecuteAsync()
    {
        logger.LogInformation("MirrorSyncJob started.");
        var mirrors = await db.MirrorRepositories.ToListAsync();

        foreach (var mirror in mirrors)
        {
            try
            {
                await SyncMirrorAsync(mirror);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync mirror {BaseUrl} {Suite}", mirror.BaseUrl, mirror.Suite);
            }
        }

        logger.LogInformation("MirrorSyncJob finished.");
    }

    private async Task SyncMirrorAsync(MirrorRepository mirror)
    {
        logger.LogInformation("Syncing mirror {BaseUrl} {Suite}...", mirror.BaseUrl, mirror.Suite);
        
        // Construct a sources.list style content to use AptSourceExtractor
        var components = mirror.Components.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var architectures = new[] { "amd64", "i386" }; // We can make this configurable later

        foreach (var arch in architectures)
        {
            foreach (var component in components)
            {
                var repo = new AptRepository(mirror.BaseUrl, mirror.Suite, mirror.SignedBy, () => httpClientFactory.CreateClient());
                var source = new AptPackageSource(repo, component, arch, () => httpClientFactory.CreateClient());
                
                logger.LogInformation("Fetching packages for {Component} {Arch}...", component, arch);
                var packages = await source.FetchPackagesAsync();
                
                logger.LogInformation("Updating {Count} packages in database...", packages.Count);
                
                // Use a transaction or batch update for performance if needed. 
                // For MVP, we'll do a simple upsert logic or clear and insert.
                // Clear existing for this specific mirror/component/arch to avoid duplicates
                var existing = db.AptPackages.Where(p => 
                    p.MirrorRepositoryId == mirror.Id && 
                    p.OriginComponent == component && 
                    p.Architecture == arch);
                
                db.AptPackages.RemoveRange(existing);
                await db.SaveChangesAsync();

                foreach (var pkgFromApt in packages)
                {
                    var pkg = pkgFromApt.Package;
                    var entity = new AptPackage
                    {
                        MirrorRepositoryId = mirror.Id,
                        OriginSuite = mirror.Suite,
                        OriginComponent = component,
                        
                        Package = pkg.Package,
                        Version = pkg.Version,
                        Architecture = pkg.Architecture,
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
                }
                await db.SaveChangesAsync();
            }
        }
    }
}
