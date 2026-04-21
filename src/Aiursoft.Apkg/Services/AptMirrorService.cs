using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.FileStorage;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Aiursoft.Apkg.Services;

public class AptMirrorService(
    TemplateDbContext dbContext,
    FeatureFoldersProvider folders,
    FileLockProvider fileLockProvider,
    IHttpClientFactory httpClientFactory,
    ILogger<AptMirrorService> logger) : ITransientDependency
{
    private string ObjectsRoot => Path.Combine(folders.GetWorkspaceFolder(), "Objects");

    public async Task<string?> GetLocalPoolPath(string path)
    {
        logger.LogInformation("Lazy Sync requested for path: {Path}", path);
        
        // 1. Try to find the exact filename
        var package = await dbContext.AptPackages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Filename == path);

        if (package == null)
        {
            // 2. Try with trailing slash or without
            var alternativePath = path.StartsWith("/") ? path.TrimStart('/') : "/" + path;
            package = await dbContext.AptPackages
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Filename == alternativePath);
        }

        if (package == null)
        {
            var sampleFiles = await dbContext.AptPackages.Take(3).Select(p => p.Filename).ToListAsync();
            logger.LogWarning("Package not found! Database contains files like: {Samples}. We searched for: {Path}", string.Join(", ", sampleFiles), path);
            return null;
        }

        logger.LogInformation("Found package {PackageName} in DB. ID: {Id}, Virtual: {IsVirtual}", package.Package, package.Id, package.IsVirtual);
        
        // Use Content-Addressable Storage (CAS) logic for physical path
        var hash = package.SHA256.ToLowerInvariant();
        var hashPrefix = hash.Substring(0, 2);
        var localPath = Path.Combine(ObjectsRoot, hashPrefix, $"{hash}.deb");

        if (!package.IsVirtual && File.Exists(localPath))
        {
            logger.LogInformation("Package {PackageName} already physical. Serving from {LocalPath}", package.Package, localPath);
            return localPath;
        }

        if (string.IsNullOrWhiteSpace(package.RemoteUrl))
        {
            logger.LogError("Package {Package} is virtual but has no RemoteUrl!", package.Package);
            return null;
        }

        var lockObj = fileLockProvider.GetLock(localPath);
        await lockObj.WaitAsync();
        try
        {
            if (!File.Exists(localPath))
            {
                await DownloadAndVerifyAsync(package.RemoteUrl, localPath, package.SHA256);
            }

            // Always ensure DB is synced if the file exists physically
            logger.LogInformation("Updating DB status for all packages named {Filename}...", package.Filename);

            var affectedRows = await dbContext.AptPackages
                .Where(p => p.Filename == package.Filename)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsVirtual, false));

            logger.LogInformation("DB update complete. Affected rows: {Count}.", affectedRows);
            return localPath;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "CRITICAL: Lazy Sync failed for {Path} due to an exception!", path);
            throw;
        }
    }

    private async Task DownloadAndVerifyAsync(string url, string localPath, string expectedHash)
    {
        var directory = Path.GetDirectoryName(localPath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = localPath + ".downloading";
        try
        {
            logger.LogInformation("Downloading virtual package from {Url}...", url);
            using var client = httpClientFactory.CreateClient();
            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs);
            }

            // Verify SHA256
            logger.LogInformation("Verifying SHA256 for {Path}...", Path.GetFileName(localPath));
            await using (var fs = File.OpenRead(tempPath))
            {
                var hashBytes = await SHA256.HashDataAsync(fs);
                var actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"Hash mismatch for {url}! Expected: {expectedHash}, Actual: {actualHash}");
                }
            }

            if (File.Exists(localPath)) File.Delete(localPath);
            File.Move(tempPath, localPath);
            logger.LogInformation("Package {Path} is now physical and verified.", Path.GetFileName(localPath));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download or verify package from {Url}", url);
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }
}
