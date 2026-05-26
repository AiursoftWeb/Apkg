using Aiursoft.Apkg.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Controllers;

[ApiController]
[Route("api/sources")]
[AllowAnonymous]
public class ApiSourcesController(ApkgDbContext db) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetSourceConfig(int id)
    {
        var repo = await db.AptRepositories
            .Include(r => r.Certificate)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (repo == null)
            return NotFound(new { error = "Repository not found." });

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var aptBaseUrl = $"{baseUrl}/artifacts/{repo.Distro}/";
        var components = string.IsNullOrWhiteSpace(repo.Components) ? "main" : repo.Components;

        return Ok(new
        {
            id = repo.Id,
            name = repo.Name,
            distro = repo.Distro,
            suite = repo.Suite,
            components,
            architecture = repo.Architecture,
            aptBaseUrl,
            enableGpgSign = repo.EnableGpgSign,
            keyUrl = repo.EnableGpgSign && repo.Certificate != null
                ? $"{baseUrl}/artifacts/certs/{repo.Certificate.Name}"
                : null,
            keyFileName = repo.EnableGpgSign && repo.Certificate != null
                ? $"{repo.Certificate.Name}-archive-keyring.gpg"
                : null,
            sourcesFileName = $"apkg-{repo.Id}.sources",
        });
    }
}
