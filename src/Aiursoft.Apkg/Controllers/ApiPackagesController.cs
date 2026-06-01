using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Claims;
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
    DebUploadService debUploadService,
    FeatureFoldersProvider folders,
    ManifestSerializer manifestSerializer,
    ILogger<ApiPackagesController> logger) : ControllerBase
{
    [HttpPost("apkg-upload")]
    [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> UploadApkg(
        [FromQuery] bool skipDuplicate = false,
        [FromQuery] bool allowDowngrade = false,
        IFormFile? apkg = null)
    {
        var summary = new ApkgUploadSummary();
        if (apkg == null || apkg.Length == 0)
        {
            summary.Errors.Add("No file provided. Send the .apkg as a multipart/form-data field named 'apkg'.");
            return BadRequest(summary);
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var apkgTempPath = CreateWorkspaceTempFilePath(".apkg");
        var extractedEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using (var fs = System.IO.File.Create(apkgTempPath))
                await apkg.CopyToAsync(fs);

            ApkgPackageManifest manifest;
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

            var distro = manifest.Distro.Trim().ToLowerInvariant();
            var component = manifest.Component.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(distro))
            {
                summary.Errors.Add("manifest.xml: <Distro> is required.");
                return BadRequest(summary);
            }
            if (string.IsNullOrWhiteSpace(component))
            {
                summary.Errors.Add("manifest.xml: <Component> is required.");
                return BadRequest(summary);
            }
            if (string.IsNullOrWhiteSpace(manifest.Name))
            {
                summary.Errors.Add("manifest.xml: <Name> is required.");
                return BadRequest(summary);
            }

            if (manifest.Entries.Count == 0)
            {
                summary.Errors.Add("manifest.xml: at least one <Entry> is required.");
                return BadRequest(summary);
            }

            // Ownership check: (Name, Distro, Component) triplet is unique
            var existingPackage = await db.ApkgPackages
                .FirstOrDefaultAsync(p => p.Name == manifest.Name && p.Distro == distro && p.Component == component);
            if (existingPackage != null && existingPackage.OwnerUserId != userId)
            {
                summary.Errors.Add(
                    $"Package '{manifest.Name}' for distro '{distro}' component '{component}' is already owned by another user.");
                return StatusCode(StatusCodes.Status403Forbidden, summary);
            }

            // ── Pre-flight: validate all entries exist before creating any record ──
            foreach (var entry in manifest.Entries)
            {
                var archiveDebPath = NormalizeArchiveEntryName(entry.DebFile);
                if (!extractedEntries.ContainsKey(archiveDebPath))
                {
                    summary.Errors.Add($"Archive entry '{entry.DebFile}' was not found for target {distro} {entry.Suite} {entry.Architecture}.");
                    return BadRequest(summary);
                }
            }

            // Find or create ApkgPackage
            if (existingPackage == null)
            {
                existingPackage = new ApkgPackage
                {
                    Name = manifest.Name,
                    Distro = distro,
                    Component = component,
                    Description = NullIfEmpty(manifest.Description),
                    Maintainer = NullIfEmpty(manifest.Maintainer),
                    Homepage = NullIfEmpty(manifest.Homepage),
                    License = NullIfEmpty(manifest.License),
                    OwnerUserId = userId
                };
                db.ApkgPackages.Add(existingPackage);
                await db.SaveChangesAsync();
            }

            var revisionRecord = new ApkgRevision
            {
                ApkgPackageId = existingPackage.Id,
                UploadedByUserId = userId,
                FileName = Path.GetFileName(apkg.FileName),
                VaultPath = null,
                IsPublished = false,
                IsListed = true
            };
            db.ApkgRevisions.Add(revisionRecord);
            await db.SaveChangesAsync();
            summary.UploadId = revisionRecord.Id;

            var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);
            var canUploadRestricted = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanUploadToRestrictedRepositories);

            foreach (var entry in manifest.Entries)
            {
                var archiveDebPath = NormalizeArchiveEntryName(entry.DebFile);
                var extractedDebSource = extractedEntries[archiveDebPath];

                // KEEP IN SYNC with ArchitectureMatches helper below and ApkgPackagesController
                var matchingRepositories = (await db.AptRepositories
                        .Where(r => r.Distro == distro
                                    && r.Suite == entry.Suite)
                        .ToListAsync())
                    .Where(r => r.Components
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Contains(component, StringComparer.OrdinalIgnoreCase)
                        && (r.Architecture
                                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Any(a => string.Equals(a, entry.Architecture, StringComparison.OrdinalIgnoreCase))
                            || string.Equals(entry.Architecture, "all", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (matchingRepositories.Count == 0)
                {
                    var warning = $"No repository found for {distro} {entry.Suite} {entry.Architecture} with component '{component}'.";
                    logger.LogWarning("{Warning}", warning);
                    summary.Warnings.Add(warning);
                    continue;
                }

                foreach (var repo in matchingRepositories)
                {
                    if (!CanUploadToRepository(repo, isAdmin, canUploadRestricted))
                    {
                        var warning = $"Skipping repository {DebUploadService.GetRepositoryDisplayName(repo)} because you do not have permission to upload to it.";
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

                        var result = await debUploadService.UploadDebToRepositoryAsync(repo, component, uploadTempPath, userId, revisionRecord.Id,
                            allowDowngrade: allowDowngrade);
                        if (result.Package != null)
                        {
                            summary.Uploaded.Add(new UploadedPackageSummary
                            {
                                Repository = DebUploadService.GetRepositoryDisplayName(repo),
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
                                var warning = result.Error ?? $"Package already exists in {DebUploadService.GetRepositoryDisplayName(repo)}.";
                                logger.LogWarning("{Warning}", warning);
                                summary.Warnings.Add(warning);
                            }
                            else
                            {
                                summary.Errors.Add(result.Error ?? $"Package already exists in {DebUploadService.GetRepositoryDisplayName(repo)}.");
                            }

                            continue;
                        }

                        if (result.StatusCode == StatusCodes.Status403Forbidden)
                        {
                            summary.Errors.Add(result.Error ?? $"Downgrade blocked for {DebUploadService.GetRepositoryDisplayName(repo)}.");
                            continue;
                        }

                        summary.Errors.Add(result.Error ?? $"Upload failed for repository {DebUploadService.GetRepositoryDisplayName(repo)}.");
                        return StatusCode(result.StatusCode, summary);
                    }
                    finally
                    {
                        DeleteIfExists(uploadTempPath);
                    }
                }
            }

            if (summary.Uploaded.Count > 0)
            {
                if (summary.Errors.Count > 0 && !skipDuplicate)
                    return Conflict(summary);

                revisionRecord.IsPublished = true;
                await db.SaveChangesAsync();
                return Ok(summary);
            }

            // Nothing was uploaded — clean up the record and any associated packages
            db.LocalPackages.RemoveRange(revisionRecord.LocalPackages);
            db.ApkgRevisions.Remove(revisionRecord);
            await db.SaveChangesAsync();

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

    private async Task<ApkgPackageManifest?> ExtractApkgAsync(string apkgPath, Dictionary<string, string> extractedEntries)
    {
        await using var fileStream = new FileStream(apkgPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var tarReader = new TarReader(gzipStream);

        ApkgPackageManifest? manifest = null;
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
                manifest = manifestSerializer.DeserializePackageManifest(manifestXml);
            }
        }

        return manifest;
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

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public sealed class ApkgUploadSummary
    {
        public int? UploadId { get; set; }
        // ReSharper disable once CollectionNeverQueried.Global
        public List<UploadedPackageSummary> Uploaded { get; } = [];
        // ReSharper disable once CollectionNeverQueried.Global
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

    // KEEP IN SYNC with inline condition and ApkgPackagesController.ArchitectureMatches.
    // EF can't translate this to SQL, so queries duplicate the logic inline.
    // Any change to the inline condition must be mirrored here.
    internal static bool ArchitectureMatches(string repoArchitecture, string entryArchitecture)
    {
        if (string.Equals(entryArchitecture, "all", StringComparison.OrdinalIgnoreCase))
            return true;

        return repoArchitecture
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(a => string.Equals(a, entryArchitecture, StringComparison.OrdinalIgnoreCase));
    }
}
