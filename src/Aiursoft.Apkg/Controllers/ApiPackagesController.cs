using System.Security.Claims;
using System.Security.Cryptography;
using Aiursoft.Apkg.Authorization;
using Aiursoft.Apkg.Entities;
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
    FeatureFoldersProvider folders) : ControllerBase
{
    private string ObjectsRoot => folders.GetObjectsFolder();

    /// <summary>
    /// Upload a .deb package to the specified repository.
    /// </summary>
    /// <param name="repositoryId">Target repository ID.</param>
    /// <param name="component">APT component, e.g. "main".</param>
    /// <param name="deb">The .deb file.</param>
    [HttpPost("upload")]
    [RequestSizeLimit(500 * 1024 * 1024)] // 500 MB
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

        // Resolve repository and verify the caller has permission to upload to it.
        var repo = await db.AptRepositories.FindAsync(repositoryId);
        if (repo == null)
            return NotFound(new { error = $"Repository {repositoryId} not found." });

        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);
        var canUploadRestricted = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanUploadToRestrictedRepositories);
        if (!repo.AllowAnyoneToUpload && !isAdmin && !canUploadRestricted)
            return StatusCode(403, new { error = "You do not have permission to upload to this restricted repository." });

        // Save the upload to a temp file for hash computation and .deb parsing.
        var tempPath = Path.Combine(folders.GetWorkspaceFolder(), $"api-upload-{Guid.NewGuid()}.deb");
        try
        {
            await using (var fs = System.IO.File.Create(tempPath))
                await deb.CopyToAsync(fs);

            // Compute all hashes in a single pass.
            string sha256, sha1, md5sum, sha512;
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

            // 409 if exact same file content already exists in this repository.
            var sha256Conflict = await db.LocalPackages
                .AnyAsync(lp => lp.RepositoryId == repositoryId && lp.SHA256 == sha256);
            if (sha256Conflict)
                return Conflict(new { error = "A package with this exact file content (SHA256) already exists in this repository." });

            // Parse control metadata from the .deb file.
            Dictionary<string, string> control;
            try
            {
                control = await debParser.ParseControlAsync(tempPath);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = $"Failed to parse .deb control file: {ex.Message}" });
            }

            if (!control.TryGetValue("Package", out var pkgName) ||
                !control.TryGetValue("Version", out var pkgVersion) ||
                !control.TryGetValue("Architecture", out var pkgArch))
            {
                return BadRequest(new { error = "The .deb control file is missing required fields (Package, Version, Architecture)." });
            }

            // 409 if the same package slot already exists (must delete the old one first).
            var slotConflict = await db.LocalPackages
                .AnyAsync(lp => lp.RepositoryId == repositoryId
                                && lp.Package == pkgName
                                && lp.Version == pkgVersion
                                && lp.Architecture == pkgArch
                                && lp.Component == component
                                && lp.IsEnabled);
            if (slotConflict)
                return Conflict(new { error = $"Package {pkgName} {pkgVersion} ({pkgArch}) in component '{component}' already exists. Delete or disable the existing entry before re-uploading." });

            // Move the temp file into CAS storage (Objects/<sha256[0..1]>/<sha256>.deb).
            var hashPrefix = sha256[..2];
            var casPath = Path.Combine(ObjectsRoot, hashPrefix, $"{sha256}.deb");
            Directory.CreateDirectory(Path.GetDirectoryName(casPath)!);

            if (System.IO.File.Exists(casPath))
            {
                // Deduplicate: file with matching hash already in CAS; discard the temp copy.
                System.IO.File.Delete(tempPath);
            }
            else
            {
                System.IO.File.Move(tempPath, casPath);
            }
            tempPath = null; // prevent deletion in finally block

            var pkgFirstChar = pkgName[0].ToString();
            var filename = $"pool/{component}/{pkgFirstChar}/{pkgName}/{pkgName}_{pkgVersion}_{pkgArch}.deb";

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var lp = new LocalPackage
            {
                UploadedByUserId = userId,
                RepositoryId = repositoryId,
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
            if (tempPath != null && System.IO.File.Exists(tempPath))
                System.IO.File.Delete(tempPath);
        }
    }
}
