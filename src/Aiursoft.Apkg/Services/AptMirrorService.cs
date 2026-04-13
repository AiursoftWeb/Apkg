using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.FileStorage;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services;

public class AptMirrorService(
    TemplateDbContext dbContext,
    FeatureFoldersProvider folders,
    FileLockProvider lockProvider,
    IHttpClientFactory httpClientFactory,
    ILogger<AptMirrorService> logger) : ITransientDependency
{
    private string MirrorsRoot => folders.GetMirrorsFolder();

    public async Task<MirrorRepository?> GetMirrorForSuite(string suite)
    {
        return await dbContext.MirrorRepositories
            .FirstOrDefaultAsync(m => m.Suite == suite);
    }

    public async Task<bool> CheckConfiguredAsync(int expectedCount)
    {
        var count = await dbContext.MirrorRepositories.CountAsync();
        return count >= expectedCount;
    }

    public async Task<string?> GetLocalMetadataPath(string suite, string path)
    {
        var mirror = await GetMirrorForSuite(suite);
        if (mirror == null) return null;

        var localPath = Path.Combine(MirrorsRoot, suite, path);
        
        // Lock this file path to prevent concurrent syncs
        var fileLock = lockProvider.GetLock(localPath);
        await fileLock.WaitAsync();
        try
        {
            if (File.Exists(localPath))
            {
                var fileName = Path.GetFileName(path);
                if (fileName is "InRelease" or "Release" or "Packages.gz" or "Packages")
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(localPath) > TimeSpan.FromMinutes(30))
                    {
                        await SyncFromUpstream(mirror.BaseUrl, $"dists/{mirror.Suite}/{path}", localPath);
                    }
                }
                return localPath;
            }

            await SyncFromUpstream(mirror.BaseUrl, $"dists/{mirror.Suite}/{path}", localPath);
            return localPath;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to sync metadata {Path} for suite {Suite}", path, suite);
            return null;
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task<string?> GetLocalPoolPath(string path)
    {
        // path is like "pool/main/a/abc/abc_1.0_amd64.deb"
        // Try to find it in our database index first to know which upstream to use.
        var pkgRecord = await dbContext.AptPackages
            .Include(p => p.Mirror)
            .FirstOrDefaultAsync(p => p.Filename == path || ("pool/" + p.Filename) == path);

        var baseUrl = pkgRecord?.Mirror?.BaseUrl;
        if (string.IsNullOrEmpty(baseUrl))
        {
            // Fallback to first mirror if not in index yet (e.g. sync job hasn't run)
            var firstMirror = await dbContext.MirrorRepositories.FirstOrDefaultAsync();
            baseUrl = firstMirror?.BaseUrl;
        }

        if (string.IsNullOrEmpty(baseUrl)) return null;

        var localPath = Path.Combine(MirrorsRoot, path);
        
        var fileLock = lockProvider.GetLock(localPath);
        await fileLock.WaitAsync();
        try
        {
            if (File.Exists(localPath))
            {
                return localPath;
            }

            await SyncFromUpstream(baseUrl, path, localPath);
            return localPath;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to sync pool file {Path}", path);
            return null;
        }
        finally
        {
            fileLock.Release();
        }
    }

    private async Task SyncFromUpstream(string baseUrl, string relativePath, string localPath)
    {
        logger.LogInformation("Syncing {Path} from {BaseUrl} to {LocalPath}", relativePath, baseUrl, localPath);
        var dir = Path.GetDirectoryName(localPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

        using var client = httpClientFactory.CreateClient();
        var url = $"{baseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}";
        
        // Use a temporary file to avoid partial downloads being served
        var tempPath = localPath + ".tmp";
        try
        {
            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs);
            }

            if (File.Exists(localPath)) File.Delete(localPath);
            File.Move(tempPath, localPath);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
