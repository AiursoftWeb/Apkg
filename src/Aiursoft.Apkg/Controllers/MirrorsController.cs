using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models.MirrorsViewModels;
using Aiursoft.Apkg.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Aiursoft.Apkg.Authorization;
using Aiursoft.UiStack.Navigation;

namespace Aiursoft.Apkg.Controllers;

[Authorize(Policy = AppPermissionNames.CanManageMirrors)]
public class MirrorsController(ApkgDbContext dbContext) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Package Engine",
        NavGroupOrder = 50,
        CascadedLinksGroupName = "Engine",
        CascadedLinksIcon = "package",
        CascadedLinksOrder = 10,
        LinkText = "Upstream Mirrors",
        LinkOrder = 1)]
    public async Task<IActionResult> Index()
    {
        var mirrors = await dbContext.AptMirrors
            .Include(m => m.PrimaryBucket)
            .Include(m => m.SecondaryBucket)
            .ToListAsync();
            
        var packageCounts = await dbContext.AptPackages
            .GroupBy(p => p.BucketId)
            .Select(g => new { BucketId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BucketId, x => x.Count);

        var model = new IndexViewModel
        {
            Mirrors = mirrors,
            PackageCounts = packageCounts,
            PageTitle = "Upstream Mirrors"
        };
        return this.StackView(model);
    }

    [HttpGet]
    public async Task<IActionResult> Packages(int id, string? searchName, string? sortOrder, int page = 1)
    {
        var mirror = await dbContext.AptMirrors.FindAsync(id);
        if (mirror?.PrimaryBucketId == null) return NotFound();

        var baseQuery = dbContext.AptPackages
            .Where(p => p.BucketId == mirror.PrimaryBucketId);

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

        var model = new PackagesViewModel
        {
            Mirror = mirror,
            Packages = items,
            SearchName = searchName,
            SortOrder = sortOrder,
            Page = page,
            TotalCount = totalCount,
            PageSize = pageSize,
            PageTitle = $"Packages in {mirror.Suite}"
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

        var model = new PackageDetailsViewModel
        {
            Package = package,
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
    public IActionResult Create()
    {
        var model = new MirrorEditViewModel
        {
            PageTitle = "Add New Mirror"
        };
        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MirrorEditViewModel model)
    {
        if (ModelState.IsValid)
        {
            var mirror = new AptMirror
            {
                Distro = model.Distro,
                BaseUrl = model.BaseUrl,
                Suite = model.Suite,
                Components = model.Components,
                Architecture = model.Architecture,
                SignedBy = model.SignedBy,
                AllowInsecure = model.AllowInsecure
            };
            dbContext.AptMirrors.Add(mirror);
            await dbContext.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        model.PageTitle = "Add New Mirror";
        return this.StackView(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var mirror = await dbContext.AptMirrors.FindAsync(id);
        if (mirror == null) return NotFound();
        
        var model = new MirrorEditViewModel
        {
            Id = mirror.Id,
            Distro = mirror.Distro,
            BaseUrl = mirror.BaseUrl,
            Suite = mirror.Suite,
            Components = mirror.Components,
            Architecture = mirror.Architecture,
            SignedBy = mirror.SignedBy,
            AllowInsecure = mirror.AllowInsecure,
            PageTitle = "Edit Mirror"
        };
        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(MirrorEditViewModel model)
    {
        if (ModelState.IsValid)
        {
            var mirror = await dbContext.AptMirrors.FindAsync(model.Id);
            if (mirror == null) return NotFound();

            mirror.Distro = model.Distro;
            mirror.BaseUrl = model.BaseUrl;
            mirror.Suite = model.Suite;
            mirror.Components = model.Components;
            mirror.Architecture = model.Architecture;
            mirror.SignedBy = model.SignedBy;
            mirror.AllowInsecure = model.AllowInsecure;

            dbContext.Update(mirror);
            await dbContext.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        model.PageTitle = "Edit Mirror";
        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var mirror = await dbContext.AptMirrors.FindAsync(id);
        if (mirror != null)
        {
            dbContext.AptMirrors.Remove(mirror);
            await dbContext.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}
