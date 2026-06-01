using System.Formats.Tar;
using System.IO.Compression;
using Aiursoft.Apkg.Authorization;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models.ApkgPackagesViewModels;
using Aiursoft.Apkg.Models.LocalPackagesViewModels;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.FileStorage;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Controllers;

[Authorize]
public class ApkgPackagesController(
    ApkgDbContext db,
    ManifestSerializer manifestSerializer,
    StorageService storageService,
    DebUploadService debUploadService,
    FeatureFoldersProvider folders,
    UserManager<User> userManager,
    AptVersionComparisonService versionComparer,
    ILogger<ApkgPackagesController> logger) : Controller
{
    [HttpGet]
    [RenderInNavBar(
        NavGroupName = "MyPackages",
        NavGroupOrder = 25,
        CascadedLinksGroupName = "MyPackagesSub",
        CascadedLinksIcon = "package",
        CascadedLinksOrder = 10,
        LinkText = "Apkg Hybrid",
        LinkOrder = 2)]
    public async Task<IActionResult> Index()
    {
        var userId = userManager.GetUserId(User)!;
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);

        var query = db.ApkgPackages
            .Include(p => p.Revisions).ThenInclude(r => r.LocalPackages)
            .Include(p => p.OwnerUser)
            .AsQueryable();

        if (!isAdmin)
            query = query.Where(p => p.OwnerUserId == userId);

        // Exclude packages with no revisions (created by ownership check but never pushed)
        query = query.Where(p => p.Revisions.Any());

        var packages = await query
            .OrderByDescending(p => p.Revisions.Max(r => r.UploadedAt))
            .ToListAsync();

        // Collect all LocalPackages across all revisions for live-status computation
        var allLocalPackages = packages
            .SelectMany(p => p.Revisions)
            .SelectMany(r => r.LocalPackages)
            .ToList();

        var allRepoIds = allLocalPackages.Select(lp => lp.RepositoryId).Distinct().ToList();

        var repoPrimaryBuckets = await db.AptRepositories
            .Where(r => allRepoIds.Contains(r.Id) && r.PrimaryBucketId != null)
            .Select(r => new { r.Id, r.PrimaryBucketId })
            .ToListAsync();
        var repoPrimaryMap = repoPrimaryBuckets.ToDictionary(r => r.Id, r => r.PrimaryBucketId!.Value);

        var allPrimaryBucketIds = repoPrimaryBuckets
            .Select(r => r.PrimaryBucketId!.Value)
            .Distinct()
            .ToList();

        var liveKeySet = new HashSet<(int BucketId, string Package, string Version, string Architecture)>();
        if (allPrimaryBucketIds.Count > 0)
        {
            var aptPackages = await db.AptPackages
                .Where(p => allPrimaryBucketIds.Contains(p.BucketId))
                .Select(p => new { p.BucketId, p.Package, p.Version, p.Architecture })
                .ToListAsync();
            foreach (var ap in aptPackages)
                liveKeySet.Add((ap.BucketId, ap.Package, ap.Version, ap.Architecture));
        }

        var indexItems = new List<ApkgPackageIndexItem>();

        foreach (var pkg in packages)
        {
            var orderedRevisions = pkg.Revisions.OrderByDescending(r => r.UploadedAt).ToList();
            var latestRevision = orderedRevisions.First();

            // Find the latest published revision that has live packages
            ApkgRevision? liveRevision = null;
            foreach (var revision in orderedRevisions.Where(r => r.IsPublished))
            {
                var hasLive = revision.LocalPackages.Any(lp =>
                    lp.IsEnabled &&
                    repoPrimaryMap.TryGetValue(lp.RepositoryId, out var bucketId) &&
                    liveKeySet.Contains((bucketId, lp.Package, lp.Version, lp.Architecture)));
                if (hasLive)
                {
                    liveRevision = revision;
                    break;
                }
            }

            // If no live revision, use the latest published revision (or the latest draft for the owner)
            var displayRevision = liveRevision ?? orderedRevisions.FirstOrDefault(r => r.IsPublished) ?? latestRevision;

            var totalCount = displayRevision.LocalPackages.Count;
            var livePackages = displayRevision.LocalPackages
                .Where(lp =>
                    lp.IsEnabled &&
                    repoPrimaryMap.TryGetValue(lp.RepositoryId, out var bucketId) &&
                    liveKeySet.Contains((bucketId, lp.Package, lp.Version, lp.Architecture)))
                .ToList();

            int? nextVersionRevisionId = null;
            string? nextVersionSummary = null;
            if (liveRevision != null && latestRevision.Id != liveRevision.Id && latestRevision.IsPublished)
            {
                nextVersionRevisionId = latestRevision.Id;
                nextVersionSummary = latestRevision.LocalPackages
                    .Select(lp => lp.Version)
                    .Distinct()
                    .FirstOrDefault();
            }

            indexItems.Add(new ApkgPackageIndexItem
            {
                Package = pkg,
                DisplayRevision = displayRevision,
                PublishedCount = livePackages.Count,
                TotalPackageCount = totalCount,
                LiveVersions = livePackages.Select(lp => lp.Version).Distinct().ToList(),
                SyncStatus = liveRevision != null ? UploadSyncStatus.Live
                    : displayRevision.IsPublished ? UploadSyncStatus.Syncing
                    : UploadSyncStatus.Draft,
                NextVersionRevisionId = nextVersionRevisionId,
                NextVersionSummary = nextVersionSummary
            });
        }

        return this.StackView(new ApkgPackagesIndexViewModel
        {
            Packages = indexItems,
            IsAdmin = isAdmin
        });
    }

    [HttpGet]
    [RenderInNavBar(
        NavGroupName = "MyPackages",
        NavGroupOrder = 25,
        CascadedLinksGroupName = "MyPackagesSub",
        CascadedLinksIcon = "history",
        CascadedLinksOrder = 20,
        LinkText = "Upload History",
        LinkOrder = 3)]
    public async Task<IActionResult> UploadHistory()
    {
        var userId = userManager.GetUserId(User)!;
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);

        var query = db.ApkgRevisions
            .Include(r => r.ApkgPackage)
            .Include(r => r.LocalPackages)
            .AsQueryable();

        if (!isAdmin)
            query = query.Where(r => r.UploadedByUserId == userId);

        var revisions = await query
            .OrderByDescending(r => r.UploadedAt)
            .ToListAsync();

        var items = revisions.Select(r => new ApkgUploadHistoryItem
        {
            Revision = r,
            PackageName = r.ApkgPackage?.Name ?? "(deleted)",
            Distro = r.ApkgPackage?.Distro ?? "—",
            Component = r.ApkgPackage?.Component ?? "—",
            PackageCount = r.LocalPackages.Count
        }).ToList();

        return this.StackView(new ApkgUploadHistoryViewModel
        {
            Uploads = items
        });
    }

    [HttpGet]
    public async Task<IActionResult> PackageHistory(string name, string? distro = null, string? component = null, string? versionsFilter = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest();

        var userId = userManager.GetUserId(User)!;
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);

        var query = db.ApkgPackages
            .Include(p => p.Revisions).ThenInclude(r => r.UploadedByUser)
            .Include(p => p.Revisions).ThenInclude(r => r.LocalPackages).ThenInclude(lp => lp.Repository)
            .Where(p => p.Name == name)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(distro))
            query = query.Where(p => p.Distro == distro);
        if (!string.IsNullOrWhiteSpace(component))
            query = query.Where(p => p.Component == component);

        if (!isAdmin)
            query = query.Where(p => p.OwnerUserId == userId);

        var package = await query.FirstOrDefaultAsync();

        if (package == null)
            return NotFound();

        var revisions = package.Revisions.OrderByDescending(r => r.UploadedAt).ToList();
        var allLocalPackages = revisions.SelectMany(r => r.LocalPackages).ToList();
        var allPackageStatuses = await BuildPackageStatusAsync(allLocalPackages);

        int? latestVersionId = revisions.FirstOrDefault(r => r.IsPublished)?.Id;
        var normalizedFilter = versionsFilter?.ToLowerInvariant() switch
        {
            "live" => "live",
            "all"  => "all",
            _      => "latest"
        };

        return this.StackView(new ApkgPackagesPackageHistoryViewModel
        {
            PackageName = name,
            Revisions = revisions,
            AllPackageStatuses = allPackageStatuses,
            IsAdmin = isAdmin,
            LatestVersionId = latestVersionId,
            VersionsFilter = normalizedFilter
        });
    }

    [HttpGet]
    public IActionResult Upload()
    {
        return this.StackView(new ApkgPackagesUploadViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(ApkgPackagesUploadViewModel model)
    {
        if (!ModelState.IsValid)
            return this.StackView(model);

        string physicalPath;
        try
        {
            physicalPath = storageService.GetFilePhysicalPath(model.ApkgFilePath!, isVault: true);
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }

        if (!System.IO.File.Exists(physicalPath))
        {
            ModelState.AddModelError(nameof(model.ApkgFilePath), "File upload failed or missing. Please re-upload.");
            return this.StackView(model);
        }

        ApkgPackageManifest manifest;
        try
        {
            manifest = await ReadManifestAsync(physicalPath)
                ?? throw new InvalidOperationException("manifest.xml was not found in the .apkg archive.");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(nameof(model.ApkgFilePath), $"Failed to parse .apkg file: {ex.Message}");
            return this.StackView(model);
        }

        if (manifest.Entries.Count == 0)
            ModelState.AddModelError(nameof(model.ApkgFilePath), "manifest.xml: at least one <Entry> is required.");

        var distro = manifest.Distro.Trim().ToLowerInvariant();
        var component = manifest.Component.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(distro))
            ModelState.AddModelError(nameof(model.ApkgFilePath), "manifest.xml: <Distro> is required.");
        if (string.IsNullOrWhiteSpace(component))
            ModelState.AddModelError(nameof(model.ApkgFilePath), "manifest.xml: <Component> is required.");

        if (!ModelState.IsValid)
            return this.StackView(model);

        var userId = userManager.GetUserId(User)!;
        var package = await EnsurePackageOwnershipAsync(manifest.Name, distro, component, userId);

        var pendingRevision = await db.ApkgRevisions
            .FirstOrDefaultAsync(r => r.ApkgPackageId == package.Id
                                     && r.VaultPath == model.ApkgFilePath
                                     && !r.IsPublished);

        var fileName = Path.GetFileName(model.ApkgFilePath!);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "upload.apkg";

        if (pendingRevision == null)
        {
            pendingRevision = new ApkgRevision
            {
                ApkgPackageId = package.Id,
                UploadedByUserId = userId,
                FileName = fileName,
                VaultPath = model.ApkgFilePath,
                IsPublished = false,
                IsListed = true
            };
            db.ApkgRevisions.Add(pendingRevision);
        }
        else
        {
            pendingRevision.FileName = fileName;
            pendingRevision.UploadedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        return RedirectToAction(nameof(Preview), new { vaultPath = model.ApkgFilePath });
    }

    [HttpGet]
    public async Task<IActionResult> Preview(string vaultPath)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
            return BadRequest();

        string physicalPath;
        try
        {
            physicalPath = storageService.GetFilePhysicalPath(vaultPath, isVault: true);
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }

        if (!System.IO.File.Exists(physicalPath))
            return NotFound();

        ApkgPackageManifest manifest;
        try
        {
            manifest = await ReadManifestAsync(physicalPath)
                ?? throw new InvalidOperationException("manifest.xml was not found in the .apkg archive.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load preview for APKG {VaultPath}.", vaultPath);
            return BadRequest();
        }

        var userId = userManager.GetUserId(User)!;
        var revision = await db.ApkgRevisions
            .FirstOrDefaultAsync(r => r.UploadedByUserId == userId
                                      && r.VaultPath == vaultPath
                                      && !r.IsPublished);

        var fileName = revision?.FileName;
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = Path.GetFileName(vaultPath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "upload.apkg";

        var previewModel = await BuildPreviewModelAsync(manifest, vaultPath, fileName);
        return this.StackView(previewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(string vaultPath, string fileName)
    {
        fileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(vaultPath))
            return BadRequest();
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = Path.GetFileName(vaultPath);

        string physicalPath;
        try
        {
            physicalPath = storageService.GetFilePhysicalPath(vaultPath, isVault: true);
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }

        if (!System.IO.File.Exists(physicalPath))
            return NotFound();

        var extractedEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ApkgPackageManifest manifest;
        try
        {
            manifest = await ExtractApkgAsync(physicalPath, extractedEntries)
                ?? throw new InvalidOperationException("manifest.xml was not found in the .apkg archive.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish APKG from {VaultPath}.", vaultPath);
            return BadRequest();
        }

        var distro = manifest.Distro.Trim().ToLowerInvariant();
        var component = manifest.Component.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(distro) || string.IsNullOrWhiteSpace(component) || manifest.Entries.Count == 0)
            return BadRequest();

        var userId = userManager.GetUserId(User)!;
        var package = await EnsurePackageOwnershipAsync(manifest.Name, distro, component, userId);

        // Update package metadata from manifest
        package.Description = NullIfEmpty(manifest.Description) ?? package.Description;
        package.Maintainer = NullIfEmpty(manifest.Maintainer) ?? package.Maintainer;
        package.Homepage = NullIfEmpty(manifest.Homepage) ?? package.Homepage;

        var revision = await db.ApkgRevisions
            .Include(r => r.LocalPackages)
            .FirstOrDefaultAsync(r => r.ApkgPackageId == package.Id
                                      && r.VaultPath == vaultPath
                                      && !r.IsPublished);

        if (revision == null)
        {
            revision = new ApkgRevision
            {
                ApkgPackageId = package.Id,
                UploadedByUserId = userId,
                FileName = fileName,
                VaultPath = vaultPath,
                IsPublished = false,
                IsListed = true
            };
            db.ApkgRevisions.Add(revision);
            await db.SaveChangesAsync();
        }

        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);
        var canUploadRestricted = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanUploadToRestrictedRepositories);
        var skippedRepos = new List<string>();

        try
        {
            foreach (var entry in manifest.Entries)
            {
                var archiveDebPath = NormalizeArchiveEntryName(entry.DebFile);
                if (!extractedEntries.TryGetValue(archiveDebPath, out var extractedDebSource))
                {
                    ModelState.AddModelError(string.Empty, $"Archive entry '{entry.DebFile}' was not found for target {distro} {entry.Suite} {entry.Architecture}.");
                    var previewModel = await BuildPreviewModelAsync(manifest, vaultPath, fileName);
                    return this.StackView(previewModel, "Preview");
                }

                // KEEP IN SYNC with ArchitectureMatches helper below and ApiPackagesController line 165
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
                    logger.LogWarning(
                        "No repository found for {Distro} {Suite} {Architecture} with component {Component}.",
                        distro,
                        entry.Suite,
                        entry.Architecture,
                        component);
                    continue;
                }

                foreach (var repo in matchingRepositories)
                {
                    if (!CanUploadToRepository(repo, isAdmin, canUploadRestricted))
                    {
                        var repoName = DebUploadService.GetRepositoryDisplayName(repo);
                        logger.LogWarning(
                            "Skipping repository {Repository} because user {UserId} cannot upload to it.",
                            repoName,
                            userId);
                        skippedRepos.Add(repoName);
                        continue;
                    }

                    var uploadTempPath = CreateWorkspaceTempFilePath(".deb");
                    try
                    {
                        await using (var source = new FileStream(extractedDebSource, FileMode.Open, FileAccess.Read, FileShare.Read))
                        await using (var destination = System.IO.File.Create(uploadTempPath))
                            await source.CopyToAsync(destination);

                        var result = await debUploadService.UploadDebToRepositoryAsync(repo, component, uploadTempPath, userId, revision.Id);
                        if (result.Package != null)
                            continue;

                        ModelState.AddModelError(string.Empty, result.Error ?? $"Upload failed for repository {DebUploadService.GetRepositoryDisplayName(repo)}.");
                        var previewModel = await BuildPreviewModelAsync(manifest, vaultPath, fileName);
                        return this.StackView(previewModel, "Preview");
                    }
                    finally
                    {
                        DeleteIfExists(uploadTempPath);
                    }
                }
            }

            DeleteIfExists(physicalPath);

            revision.FileName = fileName;
            revision.VaultPath = null;
            revision.IsPublished = true;
            revision.IsListed = true;
            await db.SaveChangesAsync();

            if (skippedRepos.Count > 0)
                TempData["SkippedRepoWarnings"] = string.Join("|", skippedRepos.Distinct());

            return RedirectToAction(nameof(Details), new { id = revision.Id });
        }
        finally
        {
            foreach (var extractedEntry in extractedEntries.Values)
                DeleteIfExists(extractedEntry);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, string? tab = null, string? versionsFilter = null)
    {
        var revision = await db.ApkgRevisions
            .Include(r => r.ApkgPackage).ThenInclude(p => p!.OwnerUser)
            .Include(r => r.UploadedByUser)
            .Include(r => r.LocalPackages)
                .ThenInclude(lp => lp.Repository)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (revision == null)
            return NotFound();

        var userId = userManager.GetUserId(User)!;
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);
        var isOwner = revision.UploadedByUserId == userId;
        if (!isAdmin && !isOwner)
            return Forbid();

        var package = revision.ApkgPackage!;

        var historyQuery = db.ApkgRevisions
            .Include(r => r.UploadedByUser)
            .Include(r => r.LocalPackages)
                .ThenInclude(lp => lp.Repository)
            .Where(r => r.ApkgPackageId == package.Id)
            .AsQueryable();

        if (!isAdmin)
            historyQuery = historyQuery.Where(r => r.UploadedByUserId == userId && r.IsListed);

        var versionHistory = await historyQuery
            .OrderByDescending(r => r.UploadedAt)
            .ToListAsync();

        // Use the most recent *published* revision as "latest"
        int? latestVersionId = versionHistory.FirstOrDefault(r => r.IsPublished)?.Id;

        // Gather ALL packages across all revisions for the Versions tab
        var allHistoryPackages = versionHistory.SelectMany(r => r.LocalPackages).ToList();

        // Always include current revision's packages
        var historyPackageIds = allHistoryPackages.Select(p => p.Id).ToHashSet();
        foreach (var pkg in revision.LocalPackages)
        {
            if (!historyPackageIds.Contains(pkg.Id))
                allHistoryPackages.Add(pkg);
        }

        var allPackageStatuses = await BuildPackageStatusAsync(allHistoryPackages);

        // Overview tab needs only this revision's packages
        var currentPackageIds = revision.LocalPackages.Select(p => p.Id).ToHashSet();
        var packages = allPackageStatuses
            .Where(ps => currentPackageIds.Contains(ps.Package.Id))
            .ToList();

        var normalizedFilter = versionsFilter?.ToLowerInvariant() switch
        {
            "live" => "live",
            "all"  => "all",
            _      => "latest"
        };

        var activeTab = tab == "versions" ? "versions" : "overview";

        return this.StackView(new ApkgPackagesDetailsViewModel
        {
            Revision = revision,
            Packages = packages,
            AllPackageStatuses = allPackageStatuses,
            VersionHistory = versionHistory,
            LatestVersionId = latestVersionId,
            ActiveTab = activeTab,
            IsAdmin = isAdmin,
            IsOwner = isOwner,
            VersionsFilter = normalizedFilter
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlist(int id)
    {
        var revision = await db.ApkgRevisions
            .Include(r => r.LocalPackages)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (revision == null)
            return NotFound();

        var userId = userManager.GetUserId(User)!;
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);
        if (!isAdmin && revision.UploadedByUserId != userId)
            return Forbid();

        revision.IsListed = false;
        foreach (var lp in revision.LocalPackages)
            lp.IsEnabled = false;

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Relist(int id)
    {
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);
        if (!isAdmin)
            return Forbid();

        var revision = await db.ApkgRevisions
            .Include(r => r.LocalPackages)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (revision == null)
            return NotFound();

        revision.IsListed = true;
        foreach (var lp in revision.LocalPackages)
            lp.IsEnabled = true;

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DiscardDraft(string vaultPath)
    {
        var userId = userManager.GetUserId(User)!;
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);

        var draft = await db.ApkgRevisions
            .Include(r => r.LocalPackages)
            .FirstOrDefaultAsync(r => r.VaultPath == vaultPath && !r.IsPublished);

        if (draft == null)
            return RedirectToAction(nameof(Upload));

        if (draft.UploadedByUserId != userId && !isAdmin)
            return Forbid();

        if (!string.IsNullOrWhiteSpace(draft.VaultPath))
        {
            try
            {
                var physicalPath = storageService.GetFilePhysicalPath(draft.VaultPath, isVault: true);
                DeleteIfExists(physicalPath);
            }
            catch (ArgumentException) { }
        }

        db.LocalPackages.RemoveRange(draft.LocalPackages);
        db.ApkgRevisions.Remove(draft);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Upload));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var isAdmin = User.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories);
        if (!isAdmin)
            return Forbid();

        var revision = await db.ApkgRevisions
            .Include(r => r.LocalPackages)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (revision == null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(revision.VaultPath))
        {
            try
            {
                var physicalPath = storageService.GetFilePhysicalPath(revision.VaultPath, isVault: true);
                DeleteIfExists(physicalPath);
            }
            catch (ArgumentException)
            {
            }
        }

        db.LocalPackages.RemoveRange(revision.LocalPackages);
        db.ApkgRevisions.Remove(revision);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task<ApkgPackagesPreviewViewModel> BuildPreviewModelAsync(ApkgPackageManifest manifest, string vaultPath, string fileName)
    {
        var targets = await BuildTargetInfosAsync(manifest);
        return new ApkgPackagesPreviewViewModel
        {
            VaultPath = vaultPath,
            FileName = fileName,
            Manifest = manifest,
            Targets = targets
        };
    }

    private async Task<List<ApkgPreviewTargetInfo>> BuildTargetInfosAsync(ApkgPackageManifest manifest)
    {
        var distro = manifest.Distro.Trim().ToLowerInvariant();
        var component = manifest.Component.Trim().ToLowerInvariant();
        var targets = new List<ApkgPreviewTargetInfo>();
        foreach (var entry in manifest.Entries)
        {
            var matchingRepos = (await db.AptRepositories
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

            targets.Add(new ApkgPreviewTargetInfo
            {
                Entry = entry,
                MatchingRepositories = matchingRepos
            });
        }

        return targets;
    }

    private static bool CanUploadToRepository(AptRepository repo, bool isAdmin, bool canUploadRestricted)
    {
        return repo.AllowAnyoneToUpload || isAdmin || canUploadRestricted;
    }

    private async Task<ApkgPackageManifest?> ReadManifestAsync(string apkgPath)
    {
        await using var fileStream = new FileStream(apkgPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var tarReader = new TarReader(gzipStream);

        TarEntry? entry;
        while ((entry = await tarReader.GetNextEntryAsync()) != null)
        {
            if (entry.DataStream == null)
                continue;

            var entryName = NormalizeArchiveEntryName(entry.Name);
            if (!string.Equals(entryName, "manifest.xml", StringComparison.OrdinalIgnoreCase))
                continue;

            using var reader = new StreamReader(entry.DataStream);
            var manifestXml = await reader.ReadToEndAsync();
            return manifestSerializer.DeserializePackageManifest(manifestXml);
        }

        return null;
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

    private string CreateWorkspaceTempFilePath(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".bin";
        if (!extension.StartsWith('.'))
            extension = $".{extension}";

        return Path.Combine(folders.GetWorkspaceFolder(), $"apkg-upload-{Guid.NewGuid()}{extension}");
    }

    private async Task<List<PackageStatusInfo>> BuildPackageStatusAsync(List<LocalPackage> packages)
    {
        if (packages.Count == 0)
            return [];

        var repoIds = packages.Select(p => p.RepositoryId).Distinct().ToList();
        var repoBuckets = await db.AptRepositories
            .Where(r => repoIds.Contains(r.Id))
            .Select(r => new { r.Id, r.PrimaryBucketId, r.SecondaryBucketId })
            .ToListAsync();

        var allRelevantBucketIds = repoBuckets
            .SelectMany(r => new[] { r.PrimaryBucketId, r.SecondaryBucketId })
            .Where(b => b.HasValue)
            .Select(b => b!.Value)
            .Distinct()
            .ToList();

        var existingInBuckets = await db.AptPackages
            .Where(ap => allRelevantBucketIds.Contains(ap.BucketId))
            .Select(ap => new { ap.BucketId, ap.Package, ap.Version, ap.Architecture, ap.Id })
            .ToListAsync();

        var bucketLookup = existingInBuckets
            .GroupBy(x => x.BucketId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => (x.Package, x.Version, x.Architecture))
                    .ToDictionary(x => x.Key, x => x.First().Id));

        var anyVersionLookup = existingInBuckets
            .GroupBy(x => x.BucketId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => (x.Package, x.Architecture))
                    .ToDictionary(x => x.Key, x => x.First().Version));

        var repoLookup = repoBuckets.ToDictionary(r => r.Id, r => r);

        return packages.Select(lp =>
        {
            var status = LocalPackageStatus.PendingSync;
            var message = "Waiting for the next Repository Sync job (runs every 20 minutes).";
            int? liveId = null;

            if (!lp.IsEnabled)
            {
                status = LocalPackageStatus.Disabled;
                message = "This package is disabled and will not be included in future syncs.";
            }
            else
            {
                var repoInfo = repoLookup.GetValueOrDefault(lp.RepositoryId);
                var primaryBucketPackages = repoInfo?.PrimaryBucketId != null
                    ? bucketLookup.GetValueOrDefault(repoInfo.PrimaryBucketId.Value)
                    : null;

                if (primaryBucketPackages?.TryGetValue((lp.Package, lp.Version, lp.Architecture), out var foundId) == true)
                {
                    status = LocalPackageStatus.Live;
                    message = "Package is live and available for APT clients.";
                    liveId = foundId;
                }
                else
                {
                    var secondaryBucketPackages = repoInfo?.SecondaryBucketId != null
                        ? bucketLookup.GetValueOrDefault(repoInfo.SecondaryBucketId.Value)
                        : null;

                    if (secondaryBucketPackages?.ContainsKey((lp.Package, lp.Version, lp.Architecture)) == true)
                    {
                        status = LocalPackageStatus.StagedForSigning;
                        message = "Included in a pending bucket. Waiting for signing (up to 5 minutes).";
                    }
                    else if (repoInfo?.PrimaryBucketId != null
                             && anyVersionLookup.TryGetValue(repoInfo.PrimaryBucketId.Value, out var primaryByArch)
                             && primaryByArch.TryGetValue((lp.Package, lp.Architecture), out var liveVersion))
                    {
                        var cmp = versionComparer.Compare(lp.Version, liveVersion);
                        if (cmp > 0)
                        {
                            status = LocalPackageStatus.PendingSync;
                            message = $"Newer version waiting to replace {liveVersion}. Waiting for the next Repository Sync job (runs every 20 minutes).";
                        }
                        else
                        {
                            status = LocalPackageStatus.Superseded;
                            message = $"A newer version ({liveVersion}) is live. This version will not be synced.";
                        }
                    }
                }
            }

            return new PackageStatusInfo
            {
                Package = lp,
                Status = status,
                StatusMessage = message,
                LivePackageId = liveId
            };
        }).ToList();
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

    private async Task<ApkgPackage> EnsurePackageOwnershipAsync(string name, string distro, string component, string userId)
    {
        var package = await db.ApkgPackages
            .FirstOrDefaultAsync(p => p.Name == name && p.Distro == distro && p.Component == component);

        if (package != null)
        {
            if (package.OwnerUserId != userId)
                throw new InvalidOperationException(
                    $"Package '{name}' for distro '{distro}' component '{component}' is already owned by another user. Only the original uploader may add versions to this package.");
            return package;
        }

        package = new ApkgPackage
        {
            Name = name,
            Distro = distro,
            Component = component,
            OwnerUserId = userId
        };
        db.ApkgPackages.Add(package);
        await db.SaveChangesAsync();
        return package;
    }

    // KEEP IN SYNC with inline conditions and ApiPackagesController.ArchitectureMatches.
    internal static bool ArchitectureMatches(string repoArchitecture, string entryArchitecture)
    {
        if (string.Equals(entryArchitecture, "all", StringComparison.OrdinalIgnoreCase))
            return true;

        return repoArchitecture
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(a => string.Equals(a, entryArchitecture, StringComparison.OrdinalIgnoreCase));
    }
}
