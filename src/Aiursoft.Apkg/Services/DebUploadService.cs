using System.Security.Cryptography;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.FileStorage;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services;

public sealed class DebUploadResult
{
    public int StatusCode { get; init; }
    public string? Error { get; init; }
    public LocalPackage? Package { get; init; }
}

public class DebUploadService(
    ApkgDbContext db,
    DebPackageParserService debParser,
    FeatureFoldersProvider folders,
    AptVersionComparisonService versionComparer)
{
    private string ObjectsRoot => folders.GetObjectsFolder();

    public async Task<DebUploadResult> UploadDebToRepositoryAsync(
        AptRepository repo,
        string component,
        string tempDebPath,
        string uploadedByUserId,
        int? apkgUploadId = null,
        bool allowDowngrade = false)
    {
        if (string.IsNullOrWhiteSpace(component))
        {
            return new DebUploadResult
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Error = "Component is required."
            };
        }

        component = component.Trim().ToLowerInvariant();

        string sha256;
        string sha1;
        string md5sum;
        string sha512;
        long fileSize;
        await using (var fs = new FileStream(tempDebPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            fileSize = fs.Length;
            using var sha256Hasher = SHA256.Create();
            using var sha1Hasher = SHA1.Create();
            using var md5Hasher = MD5.Create();
            using var sha512Hasher = SHA512.Create();
            var buffer = new byte[81920];
            int read;
            while ((read = await fs.ReadAsync(buffer)) > 0)
            {
                sha256Hasher.TransformBlock(buffer, 0, read, null, 0);
                sha1Hasher.TransformBlock(buffer, 0, read, null, 0);
                md5Hasher.TransformBlock(buffer, 0, read, null, 0);
                sha512Hasher.TransformBlock(buffer, 0, read, null, 0);
            }

            sha256Hasher.TransformFinalBlock([], 0, 0);
            sha1Hasher.TransformFinalBlock([], 0, 0);
            md5Hasher.TransformFinalBlock([], 0, 0);
            sha512Hasher.TransformFinalBlock([], 0, 0);
            sha256 = BitConverter.ToString(sha256Hasher.Hash!).Replace("-", "").ToLowerInvariant();
            sha1 = BitConverter.ToString(sha1Hasher.Hash!).Replace("-", "").ToLowerInvariant();
            md5sum = BitConverter.ToString(md5Hasher.Hash!).Replace("-", "").ToLowerInvariant();
            sha512 = BitConverter.ToString(sha512Hasher.Hash!).Replace("-", "").ToLowerInvariant();
        }

        var sha256Conflict = await db.LocalPackages
            .AnyAsync(lp => lp.RepositoryId == repo.Id && lp.SHA256 == sha256);
        if (sha256Conflict)
        {
            return new DebUploadResult
            {
                StatusCode = StatusCodes.Status409Conflict,
                Error = $"A package with this exact file content (SHA256) already exists in repository {GetRepositoryDisplayName(repo)}."
            };
        }

        Dictionary<string, string> control;
        try
        {
            control = await debParser.ParseControlAsync(tempDebPath);
        }
        catch (Exception ex)
        {
            return new DebUploadResult
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Error = $"Failed to parse .deb control file: {ex.Message}"
            };
        }

        if (!control.TryGetValue("Package", out var pkgName) || string.IsNullOrWhiteSpace(pkgName) ||
            !control.TryGetValue("Version", out var pkgVersion) || string.IsNullOrWhiteSpace(pkgVersion) ||
            !control.TryGetValue("Architecture", out var pkgArch) || string.IsNullOrWhiteSpace(pkgArch))
        {
            return new DebUploadResult
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Error = "The .deb control file is missing required fields (Package, Version, Architecture)."
            };
        }

        var slotConflict = await db.LocalPackages
            .AnyAsync(lp => lp.RepositoryId == repo.Id
                            && lp.Package == pkgName
                            && lp.Version == pkgVersion
                            && lp.Architecture == pkgArch
                            && lp.Component == component
                            && lp.IsEnabled);
        if (slotConflict)
        {
            return new DebUploadResult
            {
                StatusCode = StatusCodes.Status409Conflict,
                Error = $"Package {pkgName} {pkgVersion} ({pkgArch}) in repository {GetRepositoryDisplayName(repo)} and component '{component}' already exists. Use skip-duplicate to skip it."
            };
        }

        // Downgrade guard: check against the primary bucket's live version.
        if (!allowDowngrade && repo.PrimaryBucketId != null)
        {
            var primaryVersions = await db.AptPackages
                .Where(p => p.BucketId == repo.PrimaryBucketId
                         && p.Package == pkgName
                         && p.Architecture == pkgArch)
                .Select(p => p.Version)
                .ToListAsync();

            if (primaryVersions.Count > 0)
            {
                var cmp = Comparer<string>.Create(versionComparer.Compare);
                var latestPrimary = primaryVersions.OrderByDescending(v => v, cmp).First();
                if (versionComparer.Compare(pkgVersion, latestPrimary) < 0)
                {
                    return new DebUploadResult
                    {
                        StatusCode = StatusCodes.Status403Forbidden,
                        Error = $"Downgrade blocked: {pkgName} {pkgVersion} ({pkgArch}) is older than the currently published version {latestPrimary} in {GetRepositoryDisplayName(repo)}. Use --allow-downgrade to force the downgrade."
                    };
                }
            }
        }

        var hashPrefix = sha256[..2];
        var casPath = Path.Combine(ObjectsRoot, hashPrefix, $"{sha256}.deb");
        Directory.CreateDirectory(Path.GetDirectoryName(casPath)!);

        if (File.Exists(casPath))
            File.Delete(tempDebPath);
        else
            File.Move(tempDebPath, casPath);

        var pkgFirstChar = pkgName[0].ToString();
        var filename = $"pool/{component}/{pkgFirstChar}/{pkgName}/{pkgName}_{pkgVersion}_{pkgArch}.deb";
        var lp = new LocalPackage
        {
            UploadedByUserId = uploadedByUserId,
            RepositoryId = repo.Id,
            ApkgUploadId = apkgUploadId,
            Component = component,
            Package = pkgName,
            Version = pkgVersion,
            Architecture = pkgArch,
            Maintainer = control.GetValueOrDefault("Maintainer", "Unknown"),
            Description = control.GetValueOrDefault("Description"),
            Section = control.GetValueOrDefault("Section"),
            Priority = control.GetValueOrDefault("Priority"),
            Homepage = control.GetValueOrDefault("Homepage"),
            InstalledSize = control.GetValueOrDefault("Installed-Size"),
            Depends = control.GetValueOrDefault("Depends"),
            Recommends = control.GetValueOrDefault("Recommends"),
            Suggests = control.GetValueOrDefault("Suggests"),
            Conflicts = control.GetValueOrDefault("Conflicts"),
            Breaks = control.GetValueOrDefault("Breaks"),
            Replaces = control.GetValueOrDefault("Replaces"),
            Provides = control.GetValueOrDefault("Provides"),
            Source = control.GetValueOrDefault("Source"),
            MultiArch = control.GetValueOrDefault("Multi-Arch"),
            OriginalMaintainer = control.GetValueOrDefault("Original-Maintainer"),
            Filename = filename,
            Size = fileSize.ToString(),
            SHA256 = sha256,
            SHA1 = sha1,
            MD5sum = md5sum,
            SHA512 = sha512
        };
        db.LocalPackages.Add(lp);
        await db.SaveChangesAsync();

        return new DebUploadResult
        {
            StatusCode = StatusCodes.Status200OK,
            Package = lp
        };
    }

    public static string GetRepositoryDisplayName(AptRepository repo)
    {
        return $"{repo.Name} ({repo.Distro} {repo.Suite} {repo.Architecture})";
    }
}
