using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.FileStorage;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Aiursoft.Apkg.Services;

public class AptMirrorService(
    ApkgDbContext dbContext,
    FeatureFoldersProvider folders,
    FileLockProvider fileLockProvider,
    IHttpClientFactory httpClientFactory,
    ILogger<AptMirrorService> logger) : ITransientDependency
{
    private string ObjectsRoot => folders.GetObjectsFolder();

    public async Task<string?> GetLocalPoolPath(string path, string? distro = null, string? repoName = null)
    {
        logger.LogInformation("Lazy Sync requested for path: {Path} (distro={Distro}, repo={Repo})", path, distro, repoName);

        // 1. Normalize path: ensure it starts with pool/
        if (!path.StartsWith("pool/") && !path.StartsWith("/pool/"))
        {
            path = "pool/" + path.TrimStart('/');
        }
        path = path.TrimStart('/'); // Standard: no leading slash for DB matching

        // 2. Try to find the package, scoped to the primary buckets of the requested distro/repo
        //    to avoid returning a stale record from an orphaned bucket whose SHA256 no longer
        //    matches the Packages index that was generated from the current primary bucket.
        AptPackage? package = null;

        if (!string.IsNullOrWhiteSpace(distro) || !string.IsNullOrWhiteSpace(repoName))
        {
            var repoQuery = dbContext.AptRepositories.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(distro))
                repoQuery = repoQuery.Where(r => r.Distro == distro);
            if (!string.IsNullOrWhiteSpace(repoName))
                repoQuery = repoQuery.Where(r => r.Name == repoName || r.Suite == repoName);

            var primaryBucketIds = await repoQuery
                .Where(r => r.PrimaryBucketId != null)
                .Select(r => r.PrimaryBucketId!.Value)
                .Distinct()
                .ToListAsync();

            if (primaryBucketIds.Count > 0)
            {
                package = await dbContext.AptPackages
                    .AsNoTracking()
                    .Where(p => primaryBucketIds.Contains(p.BucketId))
                    .FirstOrDefaultAsync(p => p.Filename == path || p.Filename == "/" + path);
            }
        }

        // Fallback: no distro/repo provided, or distro has no primary bucket yet — search globally.
        package ??= await dbContext.AptPackages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Filename == path || p.Filename == "/" + path);

        if (package == null)
        {
            logger.LogWarning("Package with path {Path} not found in database.", path);
            return null;
        }

        logger.LogInformation("Found package {PackageName} in DB. ID: {Id}, Virtual: {IsVirtual}", package.Package, package.Id, package.IsVirtual);

        var hash = package.SHA256.ToLowerInvariant();
        var hashPrefix = hash.Substring(0, 2);
        var localPath = Path.Combine(ObjectsRoot, hashPrefix, $"{hash}.deb");

        // 3. Fast path: file already exists
        if (File.Exists(localPath))
        {
            if (package.IsVirtual)
            {
                await SyncDbStateAsync(package.Filename);
            }
            return localPath;
        }

        if (string.IsNullOrWhiteSpace(package.RemoteUrl))
        {
            // Local packages (uploaded by users) have no RemoteUrl — the CAS file is the only copy.
            // If the file is missing here it means the storage was wiped (e.g. /tmp cleaned on reboot).
            logger.LogError(
                "CAS file for package {Package} (SHA256: {Hash}) is missing from disk and cannot be re-fetched " +
                "because it has no RemoteUrl. The package was likely a user-uploaded local package whose storage " +
                "path was wiped. Please re-upload the package.",
                package.Package, package.SHA256);
            return null;
        }

        // 4. Slow path: locked download
        var lockObj = fileLockProvider.GetLock(localPath);
        await lockObj.WaitAsync();
        try
        {
            if (File.Exists(localPath))
            {
                await SyncDbStateAsync(package.Filename);
                return localPath;
            }

            await DownloadAndVerifyAsync(package.RemoteUrl, localPath, package.SHA256);
            await SyncDbStateAsync(package.Filename);
            return localPath;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "CRITICAL: Lazy Sync failed for {Path} due to an exception!", path);
            throw;
        }
        finally
        {
            lockObj.Release();
        }
    }

    private async Task SyncDbStateAsync(string filename)
    {
        try
        {
            logger.LogInformation("Updating DB status for all packages named {Filename}...", filename);
            var count = await dbContext.AptPackages
                .Where(p => p.Filename == filename || p.Filename == "/" + filename)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsVirtual, false));
            logger.LogInformation("DB update complete. Affected rows: {Count}.", count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update IsVirtual state in DB for {Filename}.", filename);
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
            logger.LogInformation("Downloading from {Url}...", url);
            using var client = httpClientFactory.CreateClient();
            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs);
            }

            logger.LogInformation("Verifying SHA256...");
            await using (var fs = File.OpenRead(tempPath))
            {
                var hashBytes = await SHA256.HashDataAsync(fs);
                var actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"Hash mismatch! Expected: {expectedHash}, Actual: {actualHash}");
                }
            }

            if (File.Exists(localPath)) File.Delete(localPath);
            File.Move(tempPath, localPath);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }
}
