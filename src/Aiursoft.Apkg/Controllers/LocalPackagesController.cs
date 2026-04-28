using System.Security.Cryptography;
using Aiursoft.Apkg.Authorization;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models.LocalPackagesViewModels;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.FileStorage;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Controllers;

[Authorize]
public class LocalPackagesController(
    ApkgDbContext db,
    DebPackageParserService debParser,
    FeatureFoldersProvider folders,
    UserManager<User> userManager,
    StorageService storageService) : Controller
{
    private string ObjectsRoot => folders.GetObjectsFolder();

    [HttpGet]
    [RenderInNavBar(
        NavGroupName = "Package Engine",
        NavGroupOrder = 50,
        CascadedLinksGroupName = "Engine",
        CascadedLinksIcon = "package",
        CascadedLinksOrder = 10,
        LinkText = "My Packages",
        LinkOrder = 3)]
    public async Task<IActionResult> Index()
    {
        var userId = userManager.GetUserId(User)!;
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);

        var query = db.LocalPackages
            .Include(lp => lp.Repository)
            .Include(lp => lp.UploadedByUser)
            .AsQueryable();

        if (!isAdmin)
            query = query.Where(lp => lp.UploadedByUserId == userId);

        var packages = await query.OrderByDescending(lp => lp.CreatedAt).ToListAsync();

        var model = new LocalPackagesIndexViewModel
        {
            Packages = packages,
            IsAdmin = isAdmin,
            PageTitle = "My Packages"
        };
        return this.StackView(model);
    }

    [HttpGet]
    public async Task<IActionResult> Upload()
    {
        var repos = await GetUploadableRepositoriesAsync();
        if (repos.Count == 0)
            return Forbid();

        var model = new LocalPackagesUploadViewModel
        {
            AvailableRepositories = repos.Select(r => new SelectListItem($"{r.Suite} ({r.Distro})", r.Id.ToString())).ToList(),
            PageTitle = "Upload Package"
        };
        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(LocalPackagesUploadViewModel model)
    {
        var repos = await GetUploadableRepositoriesAsync();
        model.AvailableRepositories = repos.Select(r => new SelectListItem($"{r.Suite} ({r.Distro})", r.Id.ToString())).ToList();

        var repo = repos.FirstOrDefault(r => r.Id == model.RepositoryId);
        if (repo == null)
        {
            ModelState.AddModelError(nameof(model.RepositoryId), "Repository not found or you do not have permission to upload to it.");
        }

        if (!ModelState.IsValid)
        {
            model.PageTitle = "Upload Package";
            return this.StackView(model);
        }

        string? physicalPath;
        try
        {
            physicalPath = storageService.GetFilePhysicalPath(model.DebFilePath!, isVault: false);
            if (!System.IO.File.Exists(physicalPath))
            {
                ModelState.AddModelError(nameof(model.DebFilePath), "File upload failed or missing. Please re-upload.");
                model.PageTitle = "Upload Package";
                return this.StackView(model);
            }
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }

        // 1. Compute hashes and save to CAS storage
        string sha256, sha1, md5sum, sha512;
        long fileSize;
        string casPath;

        try
        {
            // Compute all hashes from the saved file
            await using (var fs = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
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

            // Parse control fields
            Dictionary<string, string> control;
            try
            {
                control = await debParser.ParseControlAsync(physicalPath);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(nameof(model.DebFilePath), $"Failed to parse .deb file: {ex.Message}");
                model.PageTitle = "Upload Package";
                return this.StackView(model);
            }

            if (!control.TryGetValue("Package", out var pkgName) ||
                !control.TryGetValue("Version", out var pkgVersion) ||
                !control.TryGetValue("Architecture", out var pkgArch))
            {
                ModelState.AddModelError(nameof(model.DebFilePath), "The .deb control file is missing required fields (Package, Version, Architecture).");
                model.PageTitle = "Upload Package";
                return this.StackView(model);
            }

            // 2. Save to ObjectsRoot (CAS)
            var hashPrefix = sha256.Substring(0, 2);
            casPath = Path.Combine(ObjectsRoot, hashPrefix, $"{sha256}.deb");
            Directory.CreateDirectory(Path.GetDirectoryName(casPath)!);

            if (System.IO.File.Exists(casPath))
            {
                // File already exists in CAS. Let's verify it matches our expected size as a basic integrity check.
                var existingFileInfo = new FileInfo(casPath);
                if (existingFileInfo.Length != fileSize)
                {
                    // Collision or corrupted file in CAS! Overwrite with our newly verified one.
                    System.IO.File.Delete(casPath);
                    System.IO.File.Move(physicalPath, casPath);
                }
                else
                {
                    // Sizes match, we trust our CAS for deduplication.
                    System.IO.File.Delete(physicalPath);
                }
            }
            else
            {
                System.IO.File.Move(physicalPath, casPath);
            }
            physicalPath = null; // prevent delete in finally
            // 3. Disable previous enabled version for this (package, arch) in this repo
            var existing = await db.LocalPackages
                .Where(lp => lp.RepositoryId == model.RepositoryId
                             && lp.Package == pkgName
                             && lp.Architecture == pkgArch
                             && lp.IsEnabled)
                .ToListAsync();
            foreach (var e in existing) e.IsEnabled = false;

            // 4. Build Filename (APT pool path)
            var pkgFirstChar = pkgName[0].ToString();
            var filename = $"pool/{model.Component}/{pkgFirstChar}/{pkgName}/{pkgName}_{pkgVersion}_{pkgArch}.deb";

            // 5. Create LocalPackage
            var userId = userManager.GetUserId(User)!;
            var lp = new LocalPackage
            {
                UploadedByUserId = userId,
                RepositoryId = model.RepositoryId,
                Component = model.Component,
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

            return RedirectToAction(nameof(Index));
        }
        finally
        {
            if (physicalPath != null && System.IO.File.Exists(physicalPath))
                System.IO.File.Delete(physicalPath);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var userId = userManager.GetUserId(User)!;
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);

        var lp = await db.LocalPackages.FindAsync(id);
        if (lp == null) return NotFound();
        if (!isAdmin && lp.UploadedByUserId != userId) return Forbid();

        lp.IsEnabled = !lp.IsEnabled;

        // If re-enabling, disable any other enabled version for same (package, arch) in same repo
        if (lp.IsEnabled)
        {
            var conflicts = await db.LocalPackages
                .Where(x => x.RepositoryId == lp.RepositoryId
                            && x.Package == lp.Package
                            && x.Architecture == lp.Architecture
                            && x.IsEnabled
                            && x.Id != id)
                .ToListAsync();
            foreach (var c in conflicts) c.IsEnabled = false;
        }

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = userManager.GetUserId(User)!;
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);

        var lp = await db.LocalPackages.FindAsync(id);
        if (lp == null) return NotFound();
        if (!isAdmin && lp.UploadedByUserId != userId) return Forbid();

        db.LocalPackages.Remove(lp);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<AptRepository>> GetUploadableRepositoriesAsync()
    {
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);
        var canUploadRestricted = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanUploadToRestrictedRepositories);

        if (isAdmin || canUploadRestricted)
            return await db.AptRepositories.ToListAsync();

        return await db.AptRepositories.Where(r => r.AllowAnyoneToUpload).ToListAsync();
    }
}
