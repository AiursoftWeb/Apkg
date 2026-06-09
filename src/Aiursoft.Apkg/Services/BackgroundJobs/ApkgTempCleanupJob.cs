using System.Text.Json;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models;
using Aiursoft.Apkg.Services.FileStorage;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services.BackgroundJobs;

public class ApkgTempCleanupJob(
    ApkgDbContext db,
    StorageService storageService,
    FeatureFoldersProvider folders,
    ILogger<ApkgTempCleanupJob> logger) : IBackgroundJob
{
    public string Name => "APKG Temp Cleanup";

    public string Description => "Deletes stale unpublished APKG upload files and their pending records, and cleans up abandoned chunked upload sessions.";

    public async Task ExecuteAsync()
    {
        await CleanupStaleRevisionsAsync();
        CleanupStaleChunkedUploadSessions();
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

    private void CleanupStaleChunkedUploadSessions()
    {
        var chunkedUploadsRoot = folders.GetChunkedUploadsFolder();
        if (!Directory.Exists(chunkedUploadsRoot))
            return;

        var cutoff = DateTime.UtcNow.AddHours(-24);
        var deletedCount = 0;

        foreach (var sessionDir in Directory.GetDirectories(chunkedUploadsRoot))
        {
            try
            {
                var sessionJsonPath = Path.Combine(sessionDir, "session.json");
                if (!File.Exists(sessionJsonPath))
                {
                    // No session.json — check directory creation time instead
                    var dirInfo = new DirectoryInfo(sessionDir);
                    if (dirInfo.CreationTimeUtc < cutoff)
                    {
                        Directory.Delete(sessionDir, recursive: true);
                        deletedCount++;
                    }
                    continue;
                }

                var json = File.ReadAllText(sessionJsonPath);
                var session = JsonSerializer.Deserialize<ChunkedUploadSession>(json,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                if (session == null || session.CreatedAtUtc < cutoff)
                {
                    Directory.Delete(sessionDir, recursive: true);
                    deletedCount++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean up chunked upload session directory: {SessionDir}", sessionDir);
            }
        }

        if (deletedCount > 0)
        {
            logger.LogInformation("ApkgTempCleanupJob: Deleted {Count} stale chunked upload session(s).", deletedCount);
        }
    }
}

