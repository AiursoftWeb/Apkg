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
                Name = model.Name,
                Suite = model.Suite,
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
            Name = repo.Name,
            Suite = repo.Suite,
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

            repo.Name = model.Name;
            repo.Suite = model.Suite;
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
