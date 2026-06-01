using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.FileStorage;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services.BackgroundJobs;

public class ApkgTempCleanupJob(
    ApkgDbContext db,
    StorageService storageService,
    ILogger<ApkgTempCleanupJob> logger) : IBackgroundJob
{
    public string Name => "APKG Temp Cleanup";

    public string Description => "Deletes stale unpublished APKG upload files and their pending records.";

    public async Task ExecuteAsync()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        var expiredUploads = await db.ApkgRevisions
            .Where(u => !u.IsPublished && !u.LocalPackages.Any() && u.UploadedAt < cutoff)
            .ToListAsync();

        if (expiredUploads.Count == 0)
        {
            logger.LogInformation("ApkgTempCleanupJob finished. No stale pending uploads found.");
            return;
        }

        foreach (var upload in expiredUploads)
        {
            if (!string.IsNullOrWhiteSpace(upload.VaultPath))
            {
                try
                {
                    var physicalPath = storageService.GetFilePhysicalPath(upload.VaultPath, isVault: true);
                    if (File.Exists(physicalPath))
                        File.Delete(physicalPath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete APKG temp file for upload {UploadId}.", upload.Id);
                }
            }
        }

        db.ApkgRevisions.RemoveRange(expiredUploads);
        await db.SaveChangesAsync();
        logger.LogInformation("ApkgTempCleanupJob finished. Deleted {Count} stale pending upload record(s).", expiredUploads.Count);
    }
}
