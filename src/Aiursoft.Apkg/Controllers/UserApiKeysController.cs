using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models.UserApiKeysViewModels;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.Authentication;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Controllers;

[Authorize]
public class UserApiKeysController(
    ApkgDbContext db,
    UserManager<User> userManager) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Settings",
        NavGroupOrder = 9998,
        CascadedLinksGroupName = "Personal",
        CascadedLinksIcon = "user-circle",
        CascadedLinksOrder = 10,
        LinkText = "API Keys",
        LinkOrder = 10)]
    public async Task<IActionResult> Index()
    {
        var userId = userManager.GetUserId(User)!;
        var keys = await db.UserApiKeys
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync();

        var model = new UserApiKeysIndexViewModel
        {
            Keys = keys,
            PageTitle = "API Keys"
        };
        return this.StackView(model);
    }

    [HttpGet]
    public async Task<IActionResult> Usage(int id)
    {
        var userId = userManager.GetUserId(User)!;
        var key = await db.UserApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId);
        if (key == null) return NotFound();

        var model = new UsageViewModel
        {
            PageTitle = "API Key Usage",
            KeyDisplay = key.KeyPrefix + "...",
            KeyName = key.Name,
            RawKey = TempData["NewApiKey"] as string,
            BaseUrl = $"{Request.Scheme}://{Request.Host}"
        };
        return this.StackView(model);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return this.StackView(new UserApiKeysCreateViewModel { PageTitle = "New API Key" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserApiKeysCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.PageTitle = "New API Key";
            return this.StackView(model);
        }

        var userId = userManager.GetUserId(User)!;

        // Generate a cryptographically random 32-byte key and encode as URL-safe base64.
        var rawKeyBytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(rawKeyBytes);
        var rawKey = Convert.ToBase64String(rawKeyBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var keyHash   = ApiKeyAuthenticationHandler.ComputeSha256Hex(rawKey);
        var keyPrefix = rawKey[..8];

        var apiKey = new UserApiKey
        {
            UserId    = userId,
            Name      = model.Name.Trim(),
            KeyHash   = keyHash,
            KeyPrefix = keyPrefix
        };
        db.UserApiKeys.Add(apiKey);
        await db.SaveChangesAsync();

        // Pass the raw key to the Usage page through TempData (survives one redirect).
        TempData["NewApiKey"] = rawKey;
        return RedirectToAction(nameof(Usage), new { id = apiKey.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = userManager.GetUserId(User)!;
        var key = await db.UserApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId);
        if (key == null) return NotFound();

        db.UserApiKeys.Remove(key);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}

