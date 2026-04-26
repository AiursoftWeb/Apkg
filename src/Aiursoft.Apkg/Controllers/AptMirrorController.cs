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
    ApkgDbContext dbContext,
    FeatureFoldersProvider folders) : ControllerBase
{
    private string BucketsRoot => Path.Combine(folders.GetWorkspaceFolder(), "Buckets");

    [HttpGet]
    [Route("artifacts/{distro}/dists/{suite}/{**path}")]
    [Route("artifacts/repo/{repoName}/dists/{suite}/{**path}")]
    [Route("artifacts/dists/{suite}/{**path}")]
    public async Task<IActionResult> GetDists(
        [FromRoute] string? distro,
        [FromRoute] string? repoName, 
        [FromRoute] string suite, 
        [FromRoute] string path)
    {
        repoName ??= suite; 
        path = path.TrimStart('/');

        var repoQuery = dbContext.AptRepositories
            .AsNoTracking()
            .Include(r => r.PrimaryBucket)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(distro))
        {
            repoQuery = repoQuery.Where(r => r.Distro == distro);
        }

        var repo = await repoQuery.FirstOrDefaultAsync(r => r.Name == repoName || r.Suite == suite);

        if (repo?.PrimaryBucket == null) return NotFound();

        var bucket = repo.PrimaryBucket;

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
    [Route("artifacts/certs/{name}")]
    public async Task<IActionResult> GetCert([FromRoute] string name)
    {
        var cert = await dbContext.AptCertificates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == name);
        
        if (cert == null) return NotFound();
        return Content(cert.PublicKey, "application/pgp-keys");
    }

    [HttpGet]
    [Route("artifacts/{distro}/pool/{**path}")]
    [Route("artifacts/repo/{repoName}/pool/{**path}")]
    [Route("artifacts/pool/{**path}")]
    public async Task<IActionResult> GetPool(
        [FromRoute] string? distro,
        [FromRoute] string path)
    {
        var dbPath = "pool/" + path; 
        var localPath = await aptMirrorService.GetLocalPoolPath(dbPath);
        if (localPath == null) localPath = await aptMirrorService.GetLocalPoolPath(path);
        
        if (localPath == null) return NotFound();

        return PhysicalFile(localPath, "application/vnd.debian.binary-package", true);
    }
}
