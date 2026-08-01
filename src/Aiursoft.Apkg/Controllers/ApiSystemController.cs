using Aiursoft.Apkg.Sdk.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.Apkg.Controllers;

[ApiController]
[Route("api/system")]
[AllowAnonymous]
public sealed class ApiSystemController : ControllerBase
{
    [HttpGet("capabilities")]
    public ActionResult<ServerCapabilities> Capabilities() => new ServerCapabilities
    {
        ApkgManifestVersions = [2, 3],
        Features = ["appstream-assets-v1", "appstream-catalog-v1"]
    };
}
