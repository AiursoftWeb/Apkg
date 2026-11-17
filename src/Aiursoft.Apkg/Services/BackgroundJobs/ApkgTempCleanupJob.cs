using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.FileStorage;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services.BackgroundJobs;

public class ApkgTempCleanupJob(
    ApkgDbContext db,
    StorageService storageService,
    ChunkedUploadService uploads,
    ILogger<ApkgTempCleanupJob> logger) : IBackgroundJob
{
    public string Name => "APKG Temp Cleanup";

    public string Description => "Deletes stale unpublished APKG upload files and their pending records, and cleans up abandoned chunked upload sessions.";

    public async Task ExecuteAsync()
    {
        await CleanupStaleRevisionsAsync();
        await uploads.CleanupExpiredAsync();
    }

    private async Task CleanupStaleRevisionsAsync()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        var expiredUploads = await db.ApkgRevisions
            .Where(u => u.TempApkgFileInVaultPath != null && !u.ApkgDebPackages.Any() && u.UploadedAt < cutoff)
            .ToListAsync();

        if (expiredUploads.Count == 0)
        {
            logger.LogInformation("ApkgTempCleanupJob finished. No stale pending uploads found.");
        }
        else
        {
            foreach (var upload in expiredUploads)
            {
                if (!string.IsNullOrWhiteSpace(upload.TempApkgFileInVaultPath))
                {
                    try
                    {
                        var physicalPath = storageService.GetFilePhysicalPath(upload.TempApkgFileInVaultPath, isVault: true);
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

}
