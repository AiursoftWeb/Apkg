using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models.MirrorsViewModels;
using Aiursoft.Apkg.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Aiursoft.Apkg.Authorization;

namespace Aiursoft.Apkg.Controllers;

[Authorize(Policy = AppPermissionNames.CanManageMirrors)]
public class RepositoriesController(TemplateDbContext dbContext) : Controller
{
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
}
