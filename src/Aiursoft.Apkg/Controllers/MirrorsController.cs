using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models.MirrorsViewModels;
using Aiursoft.Apkg.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Aiursoft.Apkg.Authorization;

namespace Aiursoft.Apkg.Controllers;

[Authorize(Policy = AppPermissionNames.CanManageMirrors)]
public class MirrorsController(TemplateDbContext dbContext) : Controller
{
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
}
