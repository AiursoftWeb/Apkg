using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models.MirrorsViewModels;
using Aiursoft.Apkg.Services;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Aiursoft.Apkg.Authorization;

namespace Aiursoft.Apkg.Controllers;

[Authorize(Policy = AppPermissionNames.CanManageRepositories)]
public class RepositoriesController(TemplateDbContext dbContext) : Controller
{
    [Authorize(Policy = AppPermissionNames.CanManageRepositories)]
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
            .Include(r => r.CurrentBucket)
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
    public async Task<IActionResult> Packages(int id, string? searchName, int page = 1)
    {
        var repo = await dbContext.AptRepositories.FindAsync(id);
        if (repo?.CurrentBucketId == null) return NotFound();

        var query = dbContext.AptPackages
            .Where(p => p.BucketId == repo.CurrentBucketId);

        if (!string.IsNullOrWhiteSpace(searchName))
        {
            query = query.Where(p => p.Package.Contains(searchName));
        }

        const int pageSize = 100;
        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(p => p.Package)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var model = new RepoPackagesViewModel
        {
            Repo = repo,
            Packages = items,
            SearchName = searchName,
            Page = page,
            TotalCount = totalCount,
            PageSize = pageSize,
            PageTitle = $"Packages in {repo.Suite}"
        };
        return this.StackView(model);
    }

    [HttpGet]
    public async Task<IActionResult> PackageDetails(int id)
    {
        var package = await dbContext.AptPackages
            .Include(p => p.Bucket)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (package == null) return NotFound();

        var repo = await dbContext.AptRepositories
            .FirstOrDefaultAsync(r => r.CurrentBucketId == package.BucketId);

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
            DepLookup = depLookup,
            PageTitle = $"Package - {package.Package}"
        };
        return this.StackView(model);
    }

    [HttpGet]
    public async Task<IActionResult> ReverseDepends(int id)
    {
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
    public async Task<IActionResult> Details(int id)
    {
        var repo = await dbContext.AptRepositories
            .Include(r => r.CurrentBucket)
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
                CertificateId = model.CertificateId
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
            CertificateId = repo.CertificateId,
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
            repo.CertificateId = model.CertificateId;

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
