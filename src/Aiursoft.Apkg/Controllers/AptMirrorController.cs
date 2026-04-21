using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.FileStorage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Controllers;

[AllowAnonymous]
[ApiController]
public class AptMirrorController(
    AptMirrorService aptMirrorService,
    TemplateDbContext dbContext,
    FeatureFoldersProvider folders) : ControllerBase
{
    private string BucketsRoot => Path.Combine(folders.GetWorkspaceFolder(), "Buckets");

    [HttpGet]
    [Route("ubuntu/dists/{suite}/{**path}")]
    [Route("repo/{repoName}/dists/{suite}/{**path}")]
    [Route("dists/{suite}/{**path}")]
    public async Task<IActionResult> GetDists([FromRoute] string? repoName, [FromRoute] string suite, [FromRoute] string path)
    {
        repoName ??= suite; 
        path = path.TrimStart('/');

        var repo = await dbContext.AptRepositories
            .AsNoTracking()
            .Include(r => r.CurrentBucket)
            .FirstOrDefaultAsync(r => r.Name == repoName || r.Suite == suite);

        if (repo?.CurrentBucket == null) return NotFound();

        var bucket = repo.CurrentBucket;

        if (path.EndsWith("InRelease")) return Content(bucket.InReleaseContent ?? string.Empty, "text/plain");
        if (path.EndsWith("Release")) return Content(bucket.ReleaseContent ?? string.Empty, "text/plain");

        if (path.Contains("Packages"))
        {
            var localPath = Path.Combine(BucketsRoot, bucket.Id.ToString(), path);
            if (System.IO.File.Exists(localPath))
            {
                return PhysicalFile(localPath, localPath.EndsWith(".gz") ? "application/x-gzip" : "text/plain", true);
            }
        }

        return NotFound();
    }

    [HttpGet]
    [Route("certs/latest")]
    [Route("certs/{id:int}")]
    public async Task<IActionResult> GetCert([FromRoute] int? id)
    {
        var cert = id == null 
            ? await dbContext.AptCertificates.AsNoTracking().FirstOrDefaultAsync()
            : await dbContext.AptCertificates.FindAsync(id);
        
        if (cert == null) return NotFound();
        return Content(cert.PublicKey, "application/pgp-keys");
    }

    [HttpGet]
    [Route("ubuntu/pool/{**path}")]
    [Route("repo/{repoName}/pool/{**path}")]
    [Route("pool/{**path}")]
    public async Task<IActionResult> GetPool([FromRoute] string path)
    {
        var dbPath = "pool/" + path; 
        var localPath = await aptMirrorService.GetLocalPoolPath(dbPath);
        if (localPath == null) localPath = await aptMirrorService.GetLocalPoolPath(path);
        
        if (localPath == null) return NotFound();

        return PhysicalFile(localPath, "application/vnd.debian.binary-package", true);
    }
}
