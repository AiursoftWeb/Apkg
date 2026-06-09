using System.Security.Claims;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.FileStorage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.Apkg.Controllers;

/// <summary>
/// Provides a machine-friendly REST API for uploading .deb packages.
/// Authentication: Authorization: Bearer &lt;api_key&gt;
/// </summary>
[ApiController]
[Route("api/packages")]
[Authorize(AuthenticationSchemes = "ApiKey,Identity.Application")]
public class ApiPackagesController(
    ApkgUploadProcessor uploadProcessor,
    FeatureFoldersProvider folders) : ControllerBase
{
    [HttpPost("apkg-upload")]
    [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> UploadApkg(
        [FromQuery] bool skipDuplicate = false,
        [FromQuery] bool allowDowngrade = false,
        IFormFile? apkg = null)
    {
        if (apkg == null || apkg.Length == 0)
        {
            var errorSummary = new ApkgUploadSummary();
            errorSummary.Errors.Add("No file provided. Send the .apkg as a multipart/form-data field named 'apkg'.");
            return BadRequest(errorSummary);
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var apkgTempPath = CreateWorkspaceTempFilePath(".apkg");
        try
        {
            await using (var fs = System.IO.File.Create(apkgTempPath))
                await apkg.CopyToAsync(fs);

            var result = await uploadProcessor.ProcessApkgFileAsync(
                apkgTempPath,
                apkg.FileName,
                userId,
                User,
                skipDuplicate,
                allowDowngrade);

            return StatusCode(result.StatusCode, result.Summary);
        }
        finally
        {
            DeleteIfExists(apkgTempPath);
        }
    }

    private string CreateWorkspaceTempFilePath(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".bin";
        if (!extension.StartsWith('.'))
            extension = $".{extension}";

        return Path.Combine(folders.GetWorkspaceFolder(), $"api-upload-{Guid.NewGuid()}{extension}");
    }

    private static void DeleteIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
            System.IO.File.Delete(path);
    }

    // KEEP IN SYNC with inline condition and ApkgPackagesController.ArchitectureMatches.
    // EF can't translate this to SQL, so queries duplicate the logic inline.
    // Any change to the inline condition must be mirrored here.
    internal static bool ArchitectureMatches(string repoArchitecture, string entryArchitecture)
    {
        return ApkgUploadProcessor.ArchitectureMatches(repoArchitecture, entryArchitecture);
    }
}

