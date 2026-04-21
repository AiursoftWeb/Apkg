using Aiursoft.Apkg.Services;
// ReSharper disable once RedundantUsingDirective
using Aiursoft.WebTools;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;

using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.Authentication;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Aiursoft.Apkg.Controllers;

[LimitPerMin]
public class AptMirrorController(
    AptMirrorService aptMirrorService,
    AptMetadataService metadataService,
    IGpgSigningService signingService,
    TemplateDbContext dbContext,
    ILogger<AptMirrorController> logger) : ControllerBase
{
    [HttpGet]
    [Route("ubuntu/dists/{suite}/{**path}")]
    [Route("dists/{suite}/{**path}")]
    public async Task<IActionResult> GetDists([FromRoute] string suite, [FromRoute] string path)
    {
        logger.LogInformation("APT dists request for suite {Suite}, path {Path}", suite, path);

        // Path might be: "InRelease", "Release", "main/binary-amd64/Packages.gz"
        var parts = path.Split('/');
        var isPackages = path.EndsWith("Packages") || path.EndsWith("Packages.gz") || path.EndsWith("Packages.xz");
        var isRelease = path.EndsWith("InRelease") || path.EndsWith("Release") || path.EndsWith("Release.gpg");

        if (parts.Length >= 2 && isPackages)
        {
            var component = parts[0];
            var arch = parts[1].Replace("binary-", "");
            var mirror = await dbContext.MirrorRepositories
                .Include(m => m.Certificate)
                .FirstOrDefaultAsync(m => m.Suite == suite && m.Component == component && m.Architecture == arch);

            if (mirror?.Certificate != null)
            {
                var packages = await dbContext.AptPackages
                    .Where(p => p.MirrorRepositoryId == mirror.Id)
                    .ToListAsync();

                var content = metadataService.GeneratePackagesFile(packages);
                if (path.EndsWith(".gz"))
                {
                    using var ms = new MemoryStream();
                    await using (var gs = new GZipStream(ms, CompressionMode.Compress))
                    {
                        await gs.WriteAsync(Encoding.UTF8.GetBytes(content));
                    }
                    return File(ms.ToArray(), "application/x-gzip");
                }
                return Content(content, "text/plain");
            }
        }

        if (isRelease)
        {
            // For simplicity in this stage, we pick any component from this suite to get the certificate.
            // In the future architecture, the Repository (Suite level) will have the certificate.
            var mirror = await dbContext.MirrorRepositories
                .Include(m => m.Certificate)
                .FirstOrDefaultAsync(m => m.Suite == suite && m.CertificateId != null);

            if (mirror?.Certificate != null)
            {
                // Generate index for all components and architectures in this suite
                var allInSuite = await dbContext.MirrorRepositories
                    .Where(m => m.Suite == suite)
                    .ToListAsync();

                var sb = new StringBuilder();
                sb.AppendLine($"Origin: Aiursoft Apkg");
                sb.AppendLine($"Label: Aiursoft Apkg");
                sb.AppendLine($"Suite: {suite}");
                sb.AppendLine($"Codename: {suite}");
                sb.AppendLine($"Date: {DateTime.UtcNow:R}");
                sb.AppendLine($"Architectures: {string.Join(" ", allInSuite.Select(m => m.Architecture).Distinct())}");
                sb.AppendLine($"Components: {string.Join(" ", allInSuite.Select(m => m.Component).Distinct())}");
                sb.AppendLine("SHA256:");

                foreach (var m in allInSuite)
                {
                    var pkgs = await dbContext.AptPackages.Where(p => p.MirrorRepositoryId == m.Id).ToListAsync();
                    var pkgsContent = metadataService.GeneratePackagesFile(pkgs);
                    var pkgsBytes = Encoding.UTF8.GetBytes(pkgsContent);
                    var sha256 = BitConverter.ToString(SHA256.HashData(pkgsBytes)).Replace("-", "").ToLower();
                    sb.AppendLine($" {sha256} {pkgsBytes.Length} {m.Component}/binary-{m.Architecture}/Packages");
                    
                    // Also add .gz entry
                    using var ms = new MemoryStream();
                    await using (var gs = new GZipStream(ms, CompressionLevel.Optimal))
                    {
                        await gs.WriteAsync(pkgsBytes);
                    }
                    var gzBytes = ms.ToArray();
                    var gzSha256 = BitConverter.ToString(SHA256.HashData(gzBytes)).Replace("-", "").ToLower();
                    sb.AppendLine($" {gzSha256} {gzBytes.Length} {m.Component}/binary-{m.Architecture}/Packages.gz");
                }

                var releaseContent = sb.ToString();
                if (path.EndsWith("InRelease"))
                {
                    var signed = await signingService.SignClearsignAsync(releaseContent, mirror.Certificate.PrivateKey);
                    return Content(signed, "text/plain");
                }
                return Content(releaseContent, "text/plain");
            }
        }

        var localPath = await aptMirrorService.GetLocalMetadataPath(suite, path);
        if (localPath == null)
        {
            return NotFound($"Suite {suite} or metadata {path} not found in configured mirrors.");
        }

        return this.WebFile(localPath);
    }

    [HttpGet]
    [Route("ubuntu/pool/{**path}")]
    [Route("pool/{**path}")]
    public async Task<IActionResult> GetPool([FromRoute] string path)
    {
        var localPath = await aptMirrorService.GetLocalPoolPath("pool/" + path);
        if (localPath == null)
        {
            return NotFound("Failed to fetch pool file from any configured mirror.");
        }

        logger.LogInformation("Serving pool file from local cache or lazy sync: {Path}", localPath);
        if (localPath.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
        {
            // Explicitly set the MIME type for Debian packages
            return this.PhysicalFile(localPath, "application/vnd.debian.binary-package", true);
        }
        return this.WebFile(localPath);
    }
}
