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
        logger.LogInformation("Syncing mirror {BaseUrl} {Suite} {Component} {Arch}...", mirror.BaseUrl, mirror.Suite, mirror.Component, mirror.Architecture);

        var repo = new AptRepository(mirror.BaseUrl, mirror.Suite, mirror.SignedBy, () => httpClientFactory.CreateClient());
        var source = new AptPackageSource(repo, mirror.Component, mirror.Architecture, () => httpClientFactory.CreateClient());

        logger.LogInformation("Fetching packages for {Component} {Arch}...", mirror.Component, mirror.Architecture);
        var packages = await source.FetchPackagesAsync();

        logger.LogInformation("Updating {Count} packages in database...", packages.Count);

        // Clear existing for this specific mirror
        var existing = db.AptPackages.Where(p => p.MirrorRepositoryId == mirror.Id);

        db.AptPackages.RemoveRange(existing);
        await db.SaveChangesAsync();

        foreach (var pkgFromApt in packages)
        {
            var pkg = pkgFromApt.Package;
            var entity = new AptPackage
            {
                MirrorRepositoryId = mirror.Id,
                OriginSuite = mirror.Suite,
                OriginComponent = mirror.Component,

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
