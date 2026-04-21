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
public class MirrorsController(TemplateDbContext dbContext) : Controller
{
    [Authorize(Policy = AppPermissionNames.CanManageMirrors)]
    [RenderInNavBar(
        NavGroupName = "Package Engine",
        NavGroupOrder = 50,
        CascadedLinksGroupName = "Mirrors",
        CascadedLinksIcon = "server",
        CascadedLinksOrder = 10,
        LinkText = "Upstream Mirrors",
        LinkOrder = 1)]
    public async Task<IActionResult> Index()
    {
        var mirrors = await dbContext.AptMirrors
            .Include(m => m.CurrentBucket)
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
    public async Task<IActionResult> Packages(int id, string? searchName, int page = 1)
    {
        var mirror = await dbContext.AptMirrors.FindAsync(id);
        if (mirror?.CurrentBucketId == null) return NotFound();

        var query = dbContext.AptPackages
            .Where(p => p.BucketId == mirror.CurrentBucketId);

        if (!string.IsNullOrWhiteSpace(searchName))
        {
            query = query.Where(p => p.Package.Contains(searchName));
        }

        var pageSize = 100;
        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(p => p.Package)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var model = new PackagesViewModel
        {
            Mirror = mirror,
            Packages = items,
            SearchName = searchName,
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

        var model = new PackageDetailsViewModel
        {
            Package = package,
            PageTitle = $"Package - {package.Package}"
        };
        return this.StackView(model);
    }

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
                BaseUrl = model.BaseUrl,
                Suite = model.Suite,
                Components = model.Components,
                Architecture = model.Architecture,
                SignedBy = model.SignedBy
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
            BaseUrl = mirror.BaseUrl,
            Suite = mirror.Suite,
            Components = mirror.Components,
            Architecture = mirror.Architecture,
            SignedBy = mirror.SignedBy,
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

            mirror.BaseUrl = model.BaseUrl;
            mirror.Suite = model.Suite;
            mirror.Components = model.Components;
            mirror.Architecture = model.Architecture;
            mirror.SignedBy = model.SignedBy;

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
