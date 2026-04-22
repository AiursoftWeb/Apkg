using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models.MirrorsViewModels;
using Aiursoft.Apkg.Services.Authentication;
using Aiursoft.Apkg.Services;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Aiursoft.Apkg.Authorization;

namespace Aiursoft.Apkg.Controllers;

[Authorize(Policy = AppPermissionNames.CanManageCertificates)]
public class AptCertificatesController(
    TemplateDbContext dbContext,
    IGpgSigningService signingService) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Package Engine",
        NavGroupOrder = 50,
        CascadedLinksGroupName = "Engine",
        CascadedLinksIcon = "package",
        CascadedLinksOrder = 10,
        LinkText = "Certificates",
        LinkOrder = 3)]
    public async Task<IActionResult> Index()
    {
        var certs = await dbContext.AptCertificates.ToListAsync();
        var model = new CertIndexViewModel
        {
            Certificates = certs,
            PageTitle = "Signing Certificates"
        };
        return this.StackView(model);
    }

    [HttpGet]
    public IActionResult Create()
    {
        var model = new CertCreateViewModel
        {
            PageTitle = "Generate New Key"
        };
        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CertCreateViewModel model)
    {
        if (ModelState.IsValid)
        {
            var (pub, priv, fpr) = await signingService.GenerateKeyPairAsync(model.FriendlyName);
            var cert = new AptCertificate
            {
                Name = model.Name,
                FriendlyName = model.FriendlyName,
                PublicKey = pub,
                PrivateKey = priv,
                Fingerprint = fpr
            };

            dbContext.AptCertificates.Add(cert);
            await dbContext.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        model.PageTitle = "Generate New Key";
        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var cert = await dbContext.AptCertificates.FindAsync(id);
        if (cert != null)
        {
            dbContext.AptCertificates.Remove(cert);
            await dbContext.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}
