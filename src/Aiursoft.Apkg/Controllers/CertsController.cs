using Aiursoft.Apkg.Entities;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.Apkg.Controllers;

[LimitPerMin]
public class CertsController(TemplateDbContext dbContext) : ControllerBase
{
    [HttpGet]
    [Route("certs/{id:int}.asc")]
    [Route("certs/{id:int}.gpg")]
    public async Task<IActionResult> GetCert([FromRoute] int id)
    {
        var cert = await dbContext.AptCertificates.FindAsync(id);
        if (cert == null) return NotFound();

        // Standard GPG public key content type
        return Content(cert.PublicKey, "application/pgp-keys");
    }
}
