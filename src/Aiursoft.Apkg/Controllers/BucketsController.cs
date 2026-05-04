using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models.MirrorsViewModels;
using Aiursoft.Apkg.Services;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Aiursoft.Apkg.Authorization;

namespace Aiursoft.Apkg.Controllers;

[Authorize(Policy = AppPermissionNames.CanViewBuckets)]
public class BucketsController(ApkgDbContext dbContext) : Controller
{
    [Authorize(Policy = AppPermissionNames.CanViewBuckets)]
    [RenderInNavBar(
        NavGroupName = "Package Engine",
        NavGroupOrder = 50,
        CascadedLinksGroupName = "Engine",
        CascadedLinksIcon = "package",
        CascadedLinksOrder = 10,
        LinkText = "Snapshots history",
        LinkOrder = 4)]
    public async Task<IActionResult> Index()
    {
        var buckets = await dbContext.AptBuckets
            .OrderByDescending(b => b.CreatedAt)
            .Take(100)
            .ToListAsync();

        // Calculate package counts per bucket
        var rawData = await dbContext.AptPackages
            .GroupBy(p => p.BucketId)
            .Select(g => new { BucketId = g.Key, Count = g.Count(), Sizes = g.Select(p => p.Size).ToList() })
            .ToListAsync();

        var packageCounts = rawData.ToDictionary(
            x => x.BucketId,
            x => new { x.Count, TotalSize = x.Sizes.Sum(s => long.TryParse(s, out var l) ? l : 0) });

        // Find active usage
        var mirrorUsage = await dbContext.AptMirrors
            .Where(m => m.PrimaryBucketId != null)
            .ToDictionaryAsync(m => m.PrimaryBucketId!.Value, m => $"Mirror: {m.Suite}");

        var repoUsage = await dbContext.AptRepositories
            .Where(r => r.PrimaryBucketId != null)
            .ToDictionaryAsync(r => r.PrimaryBucketId!.Value, r => $"Repo: {r.Name}");

        var pendingRepoUsage = await dbContext.AptRepositories
            .Where(r => r.SecondaryBucketId != null)
            .ToDictionaryAsync(r => r.SecondaryBucketId!.Value, r => $"Repo: {r.Name}");

        var pendingMirrorUsage = await dbContext.AptMirrors
            .Where(m => m.SecondaryBucketId != null)
            .ToDictionaryAsync(m => m.SecondaryBucketId!.Value, m => $"Mirror: {m.Suite}");

        var pendingUsage = pendingRepoUsage
            .Concat(pendingMirrorUsage)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var model = new BucketsIndexViewModel
        {
            Buckets = buckets,
            PackageCounts = packageCounts.ToDictionary(k => k.Key, v => v.Value.Count),
            StorageUsage = packageCounts.ToDictionary(k => k.Key, v => v.Value.TotalSize),
            InUseBy = buckets.ToDictionary(b => b.Id, b =>
            {
                var usages = new List<string>();
                if (mirrorUsage.TryGetValue(b.Id, out var m)) usages.Add(m);
                if (repoUsage.TryGetValue(b.Id, out var r)) usages.Add(r);
                return string.Join(", ", usages);
            }),
            PendingUsage = pendingUsage,
            PageTitle = "Bucket History"
        };
        return this.StackView(model);
    }

    [HttpGet]
    public async Task<IActionResult> Packages(int id, string? sortOrder, int page = 1)
    {
        var bucket = await dbContext.AptBuckets.FindAsync(id);
        if (bucket == null) return NotFound();

        const int pageSize = 100;
        var baseQuery = dbContext.AptPackages.Where(p => p.BucketId == id);
        var totalCount = await baseQuery.CountAsync();

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

        var packages = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var model = new BucketPackagesViewModel
        {
            Bucket = bucket,
            Packages = packages,
            SortOrder = sortOrder,
            Page = page,
            TotalCount = totalCount,
            PageSize = pageSize,
            PageTitle = $"Packages in Bucket #{id}"
        };
        return this.StackView(model);
    }
}
