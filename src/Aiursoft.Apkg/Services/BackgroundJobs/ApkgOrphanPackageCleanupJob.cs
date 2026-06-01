using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Apkg.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services.BackgroundJobs;

public class ApkgOrphanPackageCleanupJob(
    ApkgDbContext db,
    ILogger<ApkgOrphanPackageCleanupJob> logger) : IBackgroundJob
{
    public string Name => "APKG Orphan Package Cleanup";

    public string Description => "Deletes ApkgPackages with 0 revisions older than 2 hours, freeing the (Name, Distro, Component) triplet for other developers.";

    public async Task ExecuteAsync()
    {
        var cutoff = DateTime.UtcNow.AddHours(-2);
        var orphans = await db.ApkgPackages
            .Where(p => !p.Revisions.Any() && p.CreatedAt < cutoff)
            .ToListAsync();

        if (orphans.Count == 0)
            return;

        db.ApkgPackages.RemoveRange(orphans);
        await db.SaveChangesAsync();
        logger.LogInformation("ApkgOrphanPackageCleanupJob deleted {Count} orphan package(s).", orphans.Count);
    }
}
