using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models.MirrorsViewModels;
using Aiursoft.Apkg.Models.SharedViewModels;
using Aiursoft.Apkg.Services;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Aiursoft.Apkg.Authorization;
using Aiursoft.Apkg.Configuration;

namespace Aiursoft.Apkg.Controllers;

[Authorize(Policy = AppPermissionNames.CanManageRepositories)]
public class RepositoriesController(
    ApkgDbContext dbContext,
    IAuthorizationService authorizationService,
    GlobalSettingsService globalSettingsService) : Controller
{
    private async Task<bool> CheckAccessAsync(string settingKey)
    {
        var auth = await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageRepositories);
        if (auth.Succeeded) return true;
        return await globalSettingsService.GetBoolSettingAsync(settingKey);
    }

    [AllowAnonymous]
    [RenderInNavBar(
        NavGroupName = "Package Engine",
        NavGroupOrder = 50,
        CascadedLinksGroupName = "Engine",
        CascadedLinksIcon = "package",
        CascadedLinksOrder = 10,
        LinkText = "Public Repositories",
        LinkOrder = 2)]
    public async Task<IActionResult> Index()
    {
        var repos = await dbContext.AptRepositories
            .Include(r => r.PrimaryBucket)
            .Include(r => r.Certificate)
            .ToListAsync();

        var packageCounts = await dbContext.AptPackages
            .GroupBy(p => p.BucketId)
            .Select(g => new { BucketId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BucketId, x => x.Count);

        var model = new RepoIndexViewModel
        {
            Repositories = repos,
            PackageCounts = packageCounts,
            PageTitle = "Public Repositories"
        };
        return this.StackView(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Packages(int id, string? searchName, string? sortOrder, int page = 1)
    {
        if (!await CheckAccessAsync(SettingsMap.AllowAnonymousBrowseRepository))
            return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

        var repo = await dbContext.AptRepositories.FindAsync(id);
        if (repo == null) return NotFound();
        if (repo.PrimaryBucketId == null)
        {
            var modelMissing = new PrimaryBucketMissingViewModel
            {
                TargetName = repo.Name,
                RequiredJobs = ["RepositorySyncJob", "RepositorySignJob"]
            };
            return this.StackView(modelMissing, "PrimaryBucketMissing");
        }

        var baseQuery = dbContext.AptPackages
            .Where(p => p.BucketId == repo.PrimaryBucketId);

        const int pageSize = 100;
        List<AptPackage> items;
        int totalCount;

        if (!string.IsNullOrWhiteSpace(searchName))
        {
            (items, totalCount) = await PackageSearchService.SearchAsync(baseQuery, searchName, page, pageSize, sortOrder);
        }
        else
        {
            totalCount = await baseQuery.CountAsync();
            var query = baseQuery;
            query = sortOrder switch
            {
                "name_desc" => query.OrderByDescending(p => p.Package),
                "size_asc" => query.OrderBy(p => p.Size.Length).ThenBy(p => p.Size),
                "size_desc" => query.OrderByDescending(p => p.Size.Length).ThenByDescending(p => p.Size),
                "component_asc" => query.OrderBy(p => p.Component),
                "component_desc" => query.OrderByDescending(p => p.Component),
                "status_asc" => query.OrderBy(p => p.IsVirtual),
                "status_desc" => query.OrderByDescending(p => p.IsVirtual),
                _ => query.OrderBy(p => p.Package)
            };

            items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        var model = new RepoPackagesViewModel
        {
            Repo = repo,
            Packages = items,
            SearchName = searchName,
            SortOrder = sortOrder,
            Page = page,
            TotalCount = totalCount,
            PageSize = pageSize,
            PageTitle = $"Packages in {repo.Suite}"
        };
        return this.StackView(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> PackageDetails(int id)
    {
        if (!await CheckAccessAsync(SettingsMap.AllowAnonymousViewPackageDetails))
            return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

        var package = await dbContext.AptPackages
            .Include(p => p.Bucket)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (package == null) return NotFound();

        var repo = await dbContext.AptRepositories
            .Include(r => r.Certificate)
            .FirstOrDefaultAsync(r => r.PrimaryBucketId == package.BucketId);

        var allRelNames = new[]
            {
                package.Depends, package.Recommends, package.Suggests,
                package.Conflicts, package.Breaks, package.Replaces, package.Provides
            }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .SelectMany(s => ParsePackageNames(s!))
            .Distinct()
            .ToList();

        var depLookup = allRelNames.Count > 0
            ? await dbContext.AptPackages
                .Where(p => p.BucketId == package.BucketId && allRelNames.Contains(p.Package))
                .GroupBy(p => p.Package)
                .Select(g => new { Name = g.Key, Id = g.Min(p => p.Id) })
                .ToDictionaryAsync(x => x.Name, x => x.Id)
            : [];

        var model = new RepoPackageDetailsViewModel
        {
            Package = package,
            Repo = repo,
            BaseUrl = $"{Request.Scheme}://{Request.Host}",
            DepLookup = depLookup,
            PageTitle = $"Package - {package.Package}"
        };
        return this.StackView(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ReverseDepends(int id)
    {
        if (!await CheckAccessAsync(SettingsMap.AllowAnonymousViewPackageDetails))
            return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

        var package = await dbContext.AptPackages.FindAsync(id);
        if (package == null) return NotFound();

        var name = package.Package;
        var candidates = await dbContext.AptPackages
            .Where(p => p.BucketId == package.BucketId && p.Id != id &&
                        ((p.Depends != null && p.Depends.Contains(name)) ||
                         (p.Recommends != null && p.Recommends.Contains(name)) ||
                         (p.Suggests != null && p.Suggests.Contains(name))))
            .Select(p => new { p.Id, p.Package, p.Version, p.Depends, p.Recommends, p.Suggests })
            .ToListAsync();

        var result = candidates
            .Select(p => new
            {
                id = p.Id,
                package = p.Package,
                version = p.Version,
                relTypes = new[]
                {
                    p.Depends != null && ParsePackageNames(p.Depends).Contains(name) ? "Depends" : null,
                    p.Recommends != null && ParsePackageNames(p.Recommends).Contains(name) ? "Recommends" : null,
                    p.Suggests != null && ParsePackageNames(p.Suggests).Contains(name) ? "Suggests" : null
                }.Where(r => r != null).ToArray()
            })
            .Where(p => p.relTypes.Length > 0)
            .ToList();

        return Json(result);
    }

    private static IEnumerable<string> ParsePackageNames(string depString) =>
        depString.Split(',')
            .SelectMany(entry => entry.Split('|'))
            .Select(part => part.Trim().Split([' ', '\t'], 2)[0].Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n));

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Details(int id)
    {
        if (!await CheckAccessAsync(SettingsMap.AllowAnonymousBrowseRepository))
            return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();

        var repo = await dbContext.AptRepositories
            .Include(r => r.PrimaryBucket)
            .Include(r => r.Certificate)
            .Include(r => r.Mirror)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (repo == null) return NotFound();

        var model = new RepoDetailsViewModel
        {
            Repo = repo,
            PageTitle = $"Repository - {repo.Name}"
        };
        return this.StackView(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var mirrors = await dbContext.AptMirrors.ToListAsync();
        var certs = await dbContext.AptCertificates.ToListAsync();

        var model = new RepoEditViewModel
        {
            AvailableMirrors = mirrors.Select(m => new SelectListItem(m.Suite, m.Id.ToString())).ToList(),
            AvailableCertificates = certs.Select(c => new SelectListItem(c.FriendlyName, c.Id.ToString())).ToList(),
            PageTitle = "Create Repository"
        };
        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RepoEditViewModel model)
    {
        if (ModelState.IsValid)
        {
            var repo = new AptRepository
            {
                Distro = model.Distro,
                Name = model.Name,
                Suite = model.Suite,
                Components = model.Components,
                Architecture = model.Architecture,
                MirrorId = model.MirrorId,
                EnableGpgSign = model.EnableGpgSign,
                CertificateId = model.EnableGpgSign ? model.CertificateId : null,
                AllowAnyoneToUpload = model.AllowAnyoneToUpload
            };
            dbContext.AptRepositories.Add(repo);
            await dbContext.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        var mirrors = await dbContext.AptMirrors.ToListAsync();
        var certs = await dbContext.AptCertificates.ToListAsync();
        model.AvailableMirrors = mirrors.Select(m => new SelectListItem(m.Suite, m.Id.ToString())).ToList();
        model.AvailableCertificates = certs.Select(c => new SelectListItem(c.FriendlyName, c.Id.ToString())).ToList();
        model.PageTitle = "Create Repository";
        return this.StackView(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var repo = await dbContext.AptRepositories.FindAsync(id);
        if (repo == null) return NotFound();

        var mirrors = await dbContext.AptMirrors.ToListAsync();
        var certs = await dbContext.AptCertificates.ToListAsync();

        var model = new RepoEditViewModel
        {
            Id = repo.Id,
            Distro = repo.Distro,
            Name = repo.Name,
            Suite = repo.Suite,
            Components = repo.Components,
            Architecture = repo.Architecture,
            MirrorId = repo.MirrorId,
            EnableGpgSign = repo.EnableGpgSign,
            CertificateId = repo.CertificateId,
            AllowAnyoneToUpload = repo.AllowAnyoneToUpload,
            AvailableMirrors = mirrors.Select(m => new SelectListItem(m.Suite, m.Id.ToString())).ToList(),
            AvailableCertificates = certs.Select(c => new SelectListItem(c.FriendlyName, c.Id.ToString())).ToList(),
            PageTitle = "Edit Repository"
        };
        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(RepoEditViewModel model)
    {
        if (ModelState.IsValid)
        {
            var repo = await dbContext.AptRepositories.FindAsync(model.Id);
            if (repo == null) return NotFound();

            repo.Distro = model.Distro;
            repo.Name = model.Name;
            repo.Suite = model.Suite;
            repo.Components = model.Components;
            repo.Architecture = model.Architecture;
            repo.MirrorId = model.MirrorId;
            repo.EnableGpgSign = model.EnableGpgSign;
            repo.CertificateId = model.EnableGpgSign ? model.CertificateId : null;
            repo.AllowAnyoneToUpload = model.AllowAnyoneToUpload;

            dbContext.Update(repo);
            await dbContext.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        var mirrors = await dbContext.AptMirrors.ToListAsync();
        var certs = await dbContext.AptCertificates.ToListAsync();
        model.AvailableMirrors = mirrors.Select(m => new SelectListItem(m.Suite, m.Id.ToString())).ToList();
        model.AvailableCertificates = certs.Select(c => new SelectListItem(c.FriendlyName, c.Id.ToString())).ToList();
        model.PageTitle = "Edit Repository";
        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var repo = await dbContext.AptRepositories.FindAsync(id);
        if (repo != null)
        {
            dbContext.AptRepositories.Remove(repo);
            await dbContext.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}
