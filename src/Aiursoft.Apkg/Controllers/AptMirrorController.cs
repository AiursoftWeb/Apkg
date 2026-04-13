using Aiursoft.Apkg.Services;
// ReSharper disable once RedundantUsingDirective
using Aiursoft.WebTools;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.Apkg.Controllers;

[LimitPerMin]
public class AptMirrorController(
    AptMirrorService aptMirrorService,
    ILogger<AptMirrorController> logger) : ControllerBase
{
    [HttpGet]
    [Route("ubuntu/dists/{suite}/{**path}")]
    [Route("dists/{suite}/{**path}")]
    public async Task<IActionResult> GetDists([FromRoute] string suite, [FromRoute] string path)
    {
        logger.LogInformation("APT dists request for suite {Suite}, path {Path}", suite, path);
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
