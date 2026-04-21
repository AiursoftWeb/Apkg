using Aiursoft.Apkg.Authorization;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models.MirrorsViewModels;
using Aiursoft.Apkg.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Controllers;

[Authorize(Policy = AppPermissionNames.CanManageMirrors)]
public class MirrorsController(TemplateDbContext dbContext) : Controller
{
    public async Task<IActionResult> Index()
    {
        var mirrors = await dbContext.MirrorRepositories
            .Include(m => m.Certificate)
            .ToListAsync();
        var counts = await dbContext.AptPackages
            .GroupBy(p => p.MirrorRepositoryId)
            .Select(g => new { MirrorId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.MirrorId, x => x.Count);

        var model = new IndexViewModel
        {
            Mirrors = mirrors,
            PackageCounts = counts,
            PageTitle = "Mirrors Management"
        };
        return this.StackView(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var mirror = await dbContext.MirrorRepositories
            .Include(m => m.Certificate)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (mirror == null) return NotFound();

        var model = new EditViewModel
        {
            Id = mirror.Id,
            BaseUrl = mirror.BaseUrl,
            Suite = mirror.Suite,
            Component = mirror.Component,
            Architecture = mirror.Architecture,
            SignedBy = mirror.SignedBy,
            PageTitle = $"Mirror Details - {mirror.Suite}/{mirror.Component}"
        };
        ViewData["Certificate"] = mirror.Certificate;
        return this.StackView(model);
    }

    [HttpGet]
    public IActionResult Create()
    {
        var model = new CreateViewModel
        {
            PageTitle = "Add New Mirror"
        };
        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        if (ModelState.IsValid)
        {
            var mirror = new MirrorRepository
            {
                BaseUrl = model.BaseUrl,
                Suite = model.Suite,
                Component = model.Component,
                Architecture = model.Architecture,
                SignedBy = model.SignedBy
            };
            dbContext.MirrorRepositories.Add(mirror);
            await dbContext.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        model.PageTitle = "Add New Mirror";
        return this.StackView(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var mirror = await dbContext.MirrorRepositories.FindAsync(id);
        if (mirror == null) return NotFound();

        var model = new EditViewModel
        {
            Id = mirror.Id,
            BaseUrl = mirror.BaseUrl,
            Suite = mirror.Suite,
            Component = mirror.Component,
            Architecture = mirror.Architecture,
            SignedBy = mirror.SignedBy,
            PageTitle = "Edit Mirror"
        };
        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditViewModel model)
    {
        if (ModelState.IsValid)
        {
            var mirror = await dbContext.MirrorRepositories.FindAsync(model.Id);
            if (mirror == null) return NotFound();

            mirror.BaseUrl = model.BaseUrl;
            mirror.Suite = model.Suite;
            mirror.Component = model.Component;
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
        var mirror = await dbContext.MirrorRepositories.FindAsync(id);
        if (mirror != null)
        {
            dbContext.MirrorRepositories.Remove(mirror);
            await dbContext.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Packages(int id, string? searchName = null, int page = 1)
    {
        var mirror = await dbContext.MirrorRepositories.FindAsync(id);
        if (mirror == null) return NotFound();

        page = Math.Max(1, page);
        var query = dbContext.AptPackages
            .Where(p => p.MirrorRepositoryId == id);

        if (!string.IsNullOrWhiteSpace(searchName))
        {
            query = query.Where(p => p.Package.Contains(searchName) || 
                                     p.Description.Contains(searchName) || 
                                     p.Filename.Contains(searchName));
        }

        var totalCount = await query.CountAsync();
        const int pageSize = 100;
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        if (totalPages > 0 && page > totalPages)
        {
            page = totalPages;
        }

        var packages = await query
            .OrderBy(p => p.Package)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var model = new PackagesViewModel
        {
            Mirror = mirror,
            Packages = packages,
            SearchName = searchName,
            Page = page,
            TotalCount = totalCount,
            PageSize = pageSize,
            PageTitle = $"Packages in {mirror.Suite}"
        };
        return this.StackView(model);
    }

    public async Task<IActionResult> PackageDetails(int id)
    {
        var package = await dbContext.AptPackages
            .Include(p => p.Mirror)
            .FirstOrDefaultAsync(p => p.Id == id);
        
        if (package == null) return NotFound();

        var model = new PackageDetailsViewModel
        {
            Package = package,
            PageTitle = $"Details for {package.Package}"
        };
        return this.StackView(model);
    }
}
