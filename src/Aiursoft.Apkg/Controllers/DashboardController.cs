using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models.DashboardViewModels;
using Aiursoft.Apkg.Services;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Controllers;

[LimitPerMin]
public class DashboardController(ApkgDbContext db) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Home",
        CascadedLinksIcon = "home",
        CascadedLinksOrder = 1,
        LinkText = "Index",
        LinkOrder = 1)]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        page = Math.Max(1, page);

        // Gather all active repo buckets: repoId -> currentBucketId
        var activeRepos = await db.AptRepositories
            .Where(r => r.PrimaryBucketId != null)
            .Select(r => new { r.Id, r.Name, r.PrimaryBucketId })
            .ToListAsync();

        var activeBucketIds = activeRepos
            .Where(r => r.PrimaryBucketId != null)
            .Select(r => r.PrimaryBucketId!.Value)
            .Distinct()
            .ToList();

        // Build a lookup from bucketId → (repoId, repoName)
        var bucketToRepo = activeRepos
            .Where(r => r.PrimaryBucketId != null)
            .ToDictionary(r => r.PrimaryBucketId!.Value, r => new { r.Id, r.Name });

        var baseQuery = db.AptPackages.AsNoTracking()
            .Where(p => activeBucketIds.Contains(p.BucketId));

        var totalPackages = await baseQuery.CountAsync();

        List<AptPackage> packages;
        int totalResults;

        if (!string.IsNullOrWhiteSpace(q))
        {
            (packages, totalResults) = await PackageSearchService.SearchAsync(
                baseQuery, q, page, IndexViewModel.PageSize);
        }
        else
        {
            totalResults = totalPackages;
            packages = await baseQuery
                .OrderBy(p => p.Package)
                .ThenBy(p => p.Version)
                .Skip((page - 1) * IndexViewModel.PageSize)
                .Take(IndexViewModel.PageSize)
                .ToListAsync();
        }

        var results = packages.Select(pkg => new PackageSearchResult
        {
            Package = pkg,
            RepoId = bucketToRepo.TryGetValue(pkg.BucketId, out var r) ? r.Id : 0,
            RepoName = bucketToRepo.TryGetValue(pkg.BucketId, out var r2) ? r2.Name : "Unknown",
        }).ToList();

        return this.StackView(new IndexViewModel
        {
            Query = q,
            Results = results,
            TotalResults = totalResults,
            TotalPackages = totalPackages,
            TotalRepos = activeRepos.Count,
            Page = page,
        });
    }
}
