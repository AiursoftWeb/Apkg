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

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var result = await debUploadService.UploadDebToRepositoryAsync(repo, component, tempPath, userId);
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

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
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

            var uploadRecord = new ApkgUpload
            {
                UploadedByUserId = userId,
                FileName = Path.GetFileName(apkg.FileName),
                Package = manifest.Package,
                Version = manifest.Version,
                Component = component,
                Description = NullIfEmpty(manifest.Description),
                Maintainer = NullIfEmpty(manifest.Maintainer),
                Homepage = NullIfEmpty(manifest.Homepage),
                VaultPath = null,
                IsPublished = false,
                IsListed = true
            };
            db.ApkgUploads.Add(uploadRecord);
            await db.SaveChangesAsync();
            summary.UploadId = uploadRecord.Id;

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

                        var result = await debUploadService.UploadDebToRepositoryAsync(repo, component, uploadTempPath, userId, uploadRecord.Id);
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

                        summary.Errors.Add(result.Error ?? $"Upload failed for repository {DebUploadService.GetRepositoryDisplayName(repo)}.");
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

            uploadRecord.IsPublished = true;
            await db.SaveChangesAsync();
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
}
