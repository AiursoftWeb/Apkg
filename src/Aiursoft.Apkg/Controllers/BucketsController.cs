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
            .Where(m => m.CurrentBucketId != null)
            .ToDictionaryAsync(m => m.CurrentBucketId!.Value, m => $"Mirror: {m.Suite}");

        var repoUsage = await dbContext.AptRepositories
            .Where(r => r.CurrentBucketId != null)
            .ToDictionaryAsync(r => r.CurrentBucketId!.Value, r => $"Repo: {r.Name}");

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
            PageTitle = "Bucket History"
        };
        return this.StackView(model);
    }
}
