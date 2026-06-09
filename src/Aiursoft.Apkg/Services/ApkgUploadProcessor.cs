using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Claims;
using Aiursoft.Apkg.Authorization;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.Apkg.Services.FileStorage;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services;

/// <summary>
/// Result of processing an .apkg file.
/// Contains the HTTP status code and the upload summary.
/// </summary>
public sealed class ApkgProcessingResult
{
    public required int StatusCode { get; init; }
    public required ApkgUploadSummary Summary { get; init; }
}

/// <summary>
/// Summary of an .apkg upload operation.
/// Shared between single-upload and chunked-upload flows.
/// </summary>
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

/// <summary>
/// Extracts the core .apkg processing logic so it can be shared between
/// <see cref="Controllers.ApiPackagesController"/> (single upload) and
/// <see cref="Controllers.ChunkedUploadController"/> (chunked upload).
/// </summary>
public class ApkgUploadProcessor(
    ApkgDbContext db,
    DebUploadService debUploadService,
    FeatureFoldersProvider folders,
    ManifestSerializer manifestSerializer,
    ILogger<ApkgUploadProcessor> logger)
{
    /// <summary>
    /// Processes an .apkg file that has already been saved to disk.
    /// Extracts the tar.gz archive, reads the manifest, validates entries,
    /// and uploads matching .deb packages to repositories.
    /// </summary>
    /// <param name="apkgFilePath">Absolute path to the .apkg file on disk.</param>
    /// <param name="originalFileName">Original file name (for the revision record).</param>
    /// <param name="userId">ID of the authenticated user performing the upload.</param>
    /// <param name="user">The authenticated user's ClaimsPrincipal.</param>
    /// <param name="skipDuplicate">If true, treat duplicates as warnings instead of errors.</param>
    /// <param name="allowDowngrade">If true, allow uploading older versions.</param>
    /// <returns>Processing result containing the status code and summary.</returns>
    public async Task<ApkgProcessingResult> ProcessApkgFileAsync(
        string apkgFilePath,
        string originalFileName,
        string userId,
        ClaimsPrincipal user,
        bool skipDuplicate,
        bool allowDowngrade)
    {
        var summary = new ApkgUploadSummary();
        var extractedEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            ApkgPackageManifest manifest;
            try
            {
                manifest = await ExtractApkgAsync(apkgFilePath, extractedEntries)
                    ?? throw new InvalidOperationException("manifest.xml was not found in the .apkg archive.");
            }
            catch (Exception ex)
            {
                summary.Errors.Add($"Failed to read .apkg archive: {ex.Message}");
                return new ApkgProcessingResult { StatusCode = StatusCodes.Status400BadRequest, Summary = summary };
            }

            var distro = manifest.Distro.Trim().ToLowerInvariant();
            var component = manifest.Component.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(distro))
            {
                summary.Errors.Add("manifest.xml: <Distro> is required.");
                return new ApkgProcessingResult { StatusCode = StatusCodes.Status400BadRequest, Summary = summary };
            }
            if (string.IsNullOrWhiteSpace(component))
            {
                summary.Errors.Add("manifest.xml: <Component> is required.");
                return new ApkgProcessingResult { StatusCode = StatusCodes.Status400BadRequest, Summary = summary };
            }
            if (string.IsNullOrWhiteSpace(manifest.Name))
            {
                summary.Errors.Add("manifest.xml: <Name> is required.");
                return new ApkgProcessingResult { StatusCode = StatusCodes.Status400BadRequest, Summary = summary };
            }
            if (manifest.Entries.Count == 0)
            {
                summary.Errors.Add("manifest.xml: at least one <Entry> is required.");
                return new ApkgProcessingResult { StatusCode = StatusCodes.Status400BadRequest, Summary = summary };
            }

            // Ownership check: (Name, Distro, Component) triplet is unique
            var existingPackage = await db.ApkgPackages
                .FirstOrDefaultAsync(p => p.Name == manifest.Name && p.Distro == distro && p.Component == component);
            if (existingPackage != null && existingPackage.OwnerUserId != userId)
            {
                summary.Errors.Add(
                    $"Package '{manifest.Name}' for distro '{distro}' component '{component}' is already owned by another user.");
                return new ApkgProcessingResult { StatusCode = StatusCodes.Status403Forbidden, Summary = summary };
            }

            // ── Pre-flight: validate all entries exist before creating any record ──
            foreach (var entry in manifest.Entries)
            {
                var archiveDebPath = NormalizeArchiveEntryName(entry.DebFile);
                if (!extractedEntries.ContainsKey(archiveDebPath))
                {
                    summary.Errors.Add($"Archive entry '{entry.DebFile}' was not found for target {distro} {entry.Suite} {entry.Architecture}.");
                    return new ApkgProcessingResult { StatusCode = StatusCodes.Status400BadRequest, Summary = summary };
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
                FileName = Path.GetFileName(originalFileName),
                TempApkgFileInVaultPath = null,
                IsListed = true
            };
            db.ApkgRevisions.Add(revisionRecord);
            await db.SaveChangesAsync();
            summary.UploadId = revisionRecord.Id;

            var isAdmin = user.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);
            var canUploadRestricted = user.HasClaim(AppPermissions.Type, AppPermissionNames.CanUploadToRestrictedRepositories);

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
                        await using (var destination = File.Create(uploadTempPath))
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
                        return new ApkgProcessingResult { StatusCode = result.StatusCode, Summary = summary };
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
                    return new ApkgProcessingResult { StatusCode = StatusCodes.Status409Conflict, Summary = summary };

                await db.SaveChangesAsync();
                return new ApkgProcessingResult { StatusCode = StatusCodes.Status200OK, Summary = summary };
            }

            // Nothing was uploaded — clean up the record and any associated packages
            db.ApkgDebPackages.RemoveRange(revisionRecord.ApkgDebPackages);
            db.ApkgRevisions.Remove(revisionRecord);
            await db.SaveChangesAsync();

            if (summary.Errors.Count > 0 && !skipDuplicate)
                return new ApkgProcessingResult { StatusCode = StatusCodes.Status409Conflict, Summary = summary };

            return new ApkgProcessingResult { StatusCode = StatusCodes.Status200OK, Summary = summary };
        }
        finally
        {
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
            await using (var tempStream = File.Create(tempEntryPath))
                await entry.DataStream.CopyToAsync(tempStream);

            if (extractedEntries.Remove(entryName, out var oldEntryPath))
                DeleteIfExists(oldEntryPath);

            extractedEntries[entryName] = tempEntryPath;

            if (string.Equals(entryName, "manifest.xml", StringComparison.OrdinalIgnoreCase))
            {
                var manifestXml = await File.ReadAllTextAsync(tempEntryPath);
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

    internal static string NormalizeArchiveEntryName(string entryName)
    {
        var normalized = entryName.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized;
    }

    private static void DeleteIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            File.Delete(path);
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
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
