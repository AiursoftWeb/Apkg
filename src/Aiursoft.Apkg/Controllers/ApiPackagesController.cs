using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using Aiursoft.Apkg.Authorization;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.FileStorage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Controllers;

/// <summary>
/// Provides a machine-friendly REST API for uploading .deb packages.
/// Authentication: Authorization: Bearer &lt;api_key&gt;
/// </summary>
[ApiController]
[Route("api/packages")]
[Authorize(AuthenticationSchemes = "ApiKey,Identity.Application")]
public class ApiPackagesController(
    ApkgDbContext db,
    DebPackageParserService debParser,
    FeatureFoldersProvider folders,
    ManifestSerializer manifestSerializer,
    ILogger<ApiPackagesController> logger) : ControllerBase
{
    private string ObjectsRoot => folders.GetObjectsFolder();

    [HttpPost("upload")]
    [RequestSizeLimit(500 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        [FromQuery] int repositoryId,
        [FromQuery] string component,
        IFormFile? deb)
    {
        if (deb == null || deb.Length == 0)
            return BadRequest(new { error = "No file provided. Send the .deb as a multipart/form-data field named 'deb'." });

        if (string.IsNullOrWhiteSpace(component))
            return BadRequest(new { error = "Query parameter 'component' is required." });

        component = component.Trim().ToLowerInvariant();

        var repo = await db.AptRepositories.FindAsync(repositoryId);
        if (repo == null)
            return NotFound(new { error = $"Repository {repositoryId} not found." });

        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);
        var canUploadRestricted = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanUploadToRestrictedRepositories);
        if (!CanUploadToRepository(repo, isAdmin, canUploadRestricted))
            return StatusCode(403, new { error = "You do not have permission to upload to this restricted repository." });

        var tempPath = CreateWorkspaceTempFilePath(".deb");
        try
        {
            await using (var fs = System.IO.File.Create(tempPath))
                await deb.CopyToAsync(fs);

            var result = await UploadDebToRepositoryAsync(repo, component, tempPath);
            if (result.Package == null)
                return StatusCode(result.StatusCode, new { error = result.Error });

            var lp = result.Package;
            return Ok(new
            {
                lp.Id,
                lp.Package,
                lp.Version,
                lp.Architecture,
                lp.Component,
                lp.RepositoryId,
                lp.SHA256,
                lp.Size,
                lp.Filename,
                lp.CreatedAt
            });
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    [HttpPost("apkg-upload")]
    [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> UploadApkg([FromQuery] bool skipDuplicate = false, IFormFile? apkg = null)
    {
        var summary = new ApkgUploadSummary();
        if (apkg == null || apkg.Length == 0)
        {
            summary.Errors.Add("No file provided. Send the .apkg as a multipart/form-data field named 'apkg'.");
            return BadRequest(summary);
        }

        var apkgTempPath = CreateWorkspaceTempFilePath(".apkg");
        var extractedEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using (var fs = System.IO.File.Create(apkgTempPath))
                await apkg.CopyToAsync(fs);

            ApkgManifest manifest;
            try
            {
                manifest = await ExtractApkgAsync(apkgTempPath, extractedEntries)
                    ?? throw new InvalidOperationException("manifest.xml was not found in the .apkg archive.");
            }
            catch (Exception ex)
            {
                summary.Errors.Add($"Failed to read .apkg archive: {ex.Message}");
                return BadRequest(summary);
            }

            var component = manifest.Component.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(component))
            {
                summary.Errors.Add("manifest.xml: <Component> is required.");
                return BadRequest(summary);
            }

            if (manifest.Targets.Count == 0)
            {
                summary.Errors.Add("manifest.xml: at least one <Target> is required.");
                return BadRequest(summary);
            }

            var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);
            var canUploadRestricted = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanUploadToRestrictedRepositories);

            foreach (var target in manifest.Targets)
            {
                var archiveDebPath = NormalizeArchiveEntryName(target.DebFile);
                if (!extractedEntries.TryGetValue(archiveDebPath, out var extractedDebSource))
                {
                    summary.Errors.Add($"Archive entry '{target.DebFile}' was not found for target {target.Distro} {target.Suites} {target.Architecture}.");
                    return BadRequest(summary);
                }

                var suites = target.SuiteList;
                var candidateRepositories = await db.AptRepositories
                    .Where(r => r.Distro == target.Distro
                                && suites.Contains(r.Suite)
                                && r.Architecture == target.Architecture)
                    .ToListAsync();

                var matchingRepositories = candidateRepositories
                    .Where(r => r.Components
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Contains(component, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (matchingRepositories.Count == 0)
                {
                    var warning = $"No repository found for {target.Distro} {target.Suites} {target.Architecture} with component '{component}'.";
                    logger.LogWarning("{Warning}", warning);
                    summary.Warnings.Add(warning);
                    continue;
                }

                foreach (var repo in matchingRepositories)
                {
                    if (!CanUploadToRepository(repo, isAdmin, canUploadRestricted))
                    {
                        var warning = $"Skipping repository {GetRepositoryDisplayName(repo)} because you do not have permission to upload to it.";
                        logger.LogWarning("{Warning}", warning);
                        summary.Warnings.Add(warning);
                        continue;
                    }

                    var uploadTempPath = CreateWorkspaceTempFilePath(".deb");
                    try
                    {
                        await using (var source = new FileStream(extractedDebSource, FileMode.Open, FileAccess.Read, FileShare.Read))
                        await using (var destination = System.IO.File.Create(uploadTempPath))
                            await source.CopyToAsync(destination);

                        var result = await UploadDebToRepositoryAsync(repo, component, uploadTempPath);
                        if (result.Package != null)
                        {
                            summary.Uploaded.Add(new UploadedPackageSummary
                            {
                                Repository = GetRepositoryDisplayName(repo),
                                Package = result.Package.Package,
                                Version = result.Package.Version,
                                Arch = result.Package.Architecture
                            });
                            continue;
                        }

                        if (result.StatusCode == StatusCodes.Status409Conflict)
                        {
                            if (skipDuplicate)
                            {
                                var warning = result.Error ?? $"Package already exists in {GetRepositoryDisplayName(repo)}.";
                                logger.LogWarning("{Warning}", warning);
                                summary.Warnings.Add(warning);
                            }
                            else
                            {
                                summary.Errors.Add(result.Error ?? $"Package already exists in {GetRepositoryDisplayName(repo)}.");
                            }

                            continue;
                        }

                        summary.Errors.Add(result.Error ?? $"Upload failed for repository {GetRepositoryDisplayName(repo)}.");
                        return StatusCode(result.StatusCode, summary);
                    }
                    finally
                    {
                        DeleteIfExists(uploadTempPath);
                    }
                }
            }

            if (summary.Errors.Count > 0 && !skipDuplicate)
                return Conflict(summary);

            return Ok(summary);
        }
        finally
        {
            DeleteIfExists(apkgTempPath);
            foreach (var extractedEntry in extractedEntries.Values)
                DeleteIfExists(extractedEntry);
        }
    }

    private async Task<ApkgManifest?> ExtractApkgAsync(string apkgPath, Dictionary<string, string> extractedEntries)
    {
        await using var fileStream = new FileStream(apkgPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var tarReader = new TarReader(gzipStream);

        ApkgManifest? manifest = null;
        TarEntry? entry;
        while ((entry = await tarReader.GetNextEntryAsync()) != null)
        {
            if (entry.DataStream == null)
                continue;

            var entryName = NormalizeArchiveEntryName(entry.Name);
            if (string.IsNullOrWhiteSpace(entryName))
                continue;

            var tempEntryPath = CreateWorkspaceTempFilePath(Path.GetExtension(entryName));
            await using (var tempStream = System.IO.File.Create(tempEntryPath))
                await entry.DataStream.CopyToAsync(tempStream);

            if (extractedEntries.Remove(entryName, out var oldEntryPath))
                DeleteIfExists(oldEntryPath);

            extractedEntries[entryName] = tempEntryPath;

            if (string.Equals(entryName, "manifest.xml", StringComparison.OrdinalIgnoreCase))
            {
                var manifestXml = await System.IO.File.ReadAllTextAsync(tempEntryPath);
                manifest = manifestSerializer.Deserialize(manifestXml);
            }
        }

        return manifest;
    }

    private async Task<DebUploadResult> UploadDebToRepositoryAsync(AptRepository repo, string component, string tempPath)
    {
        string sha256;
        string sha1;
        string md5sum;
        string sha512;
        long fileSize;
        await using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
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
            control = await debParser.ParseControlAsync(tempPath);
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

        var hashPrefix = sha256[..2];
        var casPath = Path.Combine(ObjectsRoot, hashPrefix, $"{sha256}.deb");
        Directory.CreateDirectory(Path.GetDirectoryName(casPath)!);

        if (System.IO.File.Exists(casPath))
            System.IO.File.Delete(tempPath);
        else
            System.IO.File.Move(tempPath, casPath);

        var pkgFirstChar = pkgName[0].ToString();
        var filename = $"pool/{component}/{pkgFirstChar}/{pkgName}/{pkgName}_{pkgVersion}_{pkgArch}.deb";
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var lp = new LocalPackage
        {
            UploadedByUserId = userId,
            RepositoryId = repo.Id,
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

    private static bool CanUploadToRepository(AptRepository repo, bool isAdmin, bool canUploadRestricted)
    {
        return repo.AllowAnyoneToUpload || isAdmin || canUploadRestricted;
    }

    private string CreateWorkspaceTempFilePath(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".bin";
        if (!extension.StartsWith('.'))
            extension = $".{extension}";

        return Path.Combine(folders.GetWorkspaceFolder(), $"api-upload-{Guid.NewGuid()}{extension}");
    }

    private static string NormalizeArchiveEntryName(string entryName)
    {
        var normalized = entryName.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized;
    }

    private static void DeleteIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
            System.IO.File.Delete(path);
    }

    private static string GetRepositoryDisplayName(AptRepository repo)
    {
        return $"{repo.Name} ({repo.Distro} {repo.Suite} {repo.Architecture})";
    }

    private sealed class DebUploadResult
    {
        public int StatusCode { get; init; }
        public string? Error { get; init; }
        public LocalPackage? Package { get; init; }
    }

    public sealed class ApkgUploadSummary
    {
        public List<UploadedPackageSummary> Uploaded { get; } = [];
        public List<string> Warnings { get; } = [];
        public List<string> Errors { get; } = [];
    }

    public sealed class UploadedPackageSummary
    {
        public required string Repository { get; init; }
        public required string Package { get; init; }
        public required string Version { get; init; }
        public required string Arch { get; init; }
    }
}
