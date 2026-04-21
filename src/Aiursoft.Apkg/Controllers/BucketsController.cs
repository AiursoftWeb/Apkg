using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models.MirrorsViewModels;
using Aiursoft.Apkg.Services;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Aiursoft.Apkg.Authorization;

namespace Aiursoft.Apkg.Controllers;

[Authorize(Policy = AppPermissionNames.CanManageMirrors)]
public class BucketsController(TemplateDbContext dbContext) : Controller
{
    [Authorize(Policy = AppPermissionNames.CanManageMirrors)]
    [RenderInNavBar(
        NavGroupName = "Package Engine",
        CascadedLinksGroupName = "Audit",
        CascadedLinksIcon = "history",
        CascadedLinksOrder = 40,
        LinkText = "Bucket Snapshots",
        LinkOrder = 1)]
    public async Task<IActionResult> Index()
    {
        var buckets = await dbContext.AptBuckets
            .OrderByDescending(b => b.CreatedAt)
            .Take(100)
            .ToListAsync();
            
        var model = new BucketsIndexViewModel
        {
            Buckets = buckets,
            PageTitle = "Bucket History"
        };
        return this.StackView(model);
    }
}
