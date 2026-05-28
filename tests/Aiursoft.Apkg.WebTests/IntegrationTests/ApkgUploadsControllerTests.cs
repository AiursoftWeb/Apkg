using System.Formats.Tar;
using System.Net;
using System.Net.Http.Headers;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.FileStorage;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

/// <summary>
/// Integration tests for the <see cref="Aiursoft.Apkg.Controllers.ApkgUploadsController"/>.
///
/// Covers: Index, Upload (GET/POST), Preview, Publish, Details, Unlist, Relist, Delete.
/// </summary>
[TestClass]
public class ApkgUploadsControllerTests : TestBase
{
    private ApkgDbContext _db = null!;
    private string _adminUserId = null!;
    private HttpClient _anonHttp = null!;

    [TestInitialize]
    public override async Task SetupTestContext()
    {
        await base.SetupTestContext();
        await LoginAsAdmin();

        _db = GetService<ApkgDbContext>();

        var userManager = GetService<UserManager<User>>();
        var admin = await userManager.FindByEmailAsync("admin@default.com");
        _adminUserId = admin!.Id;

        _anonHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = Http.BaseAddress
        };
    }

    public override void CleanTestContext()
    {
        _anonHttp.Dispose();
        base.CleanTestContext();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers — DB records
    // ──────────────────────────────────────────────────────────────────────

    private ApkgUpload AddApkgUpload(
        string? userId = null,
        bool isPublished = true,
        bool isListed = true,
        string? vaultPath = null)
    {
        var upload = new ApkgUpload
        {
            UploadedByUserId = userId ?? _adminUserId,
            FileName = "test.apkg",
            Package = $"test-pkg-{Guid.NewGuid():N}",
            Version = "1.0.0",
            Component = "main",
            Description = "Test package",
            IsPublished = isPublished,
            IsListed = isListed,
            VaultPath = vaultPath
        };
        _db.ApkgUploads.Add(upload);
        _db.SaveChanges();
        return upload;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers — .apkg vault file creation
    // ──────────────────────────────────────────────────────────────────────

    private static byte[] CreateApkgArchive(string manifestXml,
        params (string fileName, byte[] content)[] files)
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms,
                   System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        {
            using var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: true);

            var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestXml);
            var manifestEntry = new PaxTarEntry(TarEntryType.RegularFile, "manifest.xml")
            {
                DataStream = new MemoryStream(manifestBytes)
            };
            tar.WriteEntryAsync(manifestEntry).GetAwaiter().GetResult();

            foreach (var (name, data) in files)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(data)
                };
                tar.WriteEntryAsync(entry).GetAwaiter().GetResult();
            }
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Writes a .apkg to the Vault folder and returns its logical path (relative to Vault root).
    /// </summary>
    private string SaveApkgToVault(byte[] apkgBytes)
    {
        var folders = GetService<FeatureFoldersProvider>();
        var vaultDir = folders.GetVaultFolder();
        var fileName = $"test-{Guid.NewGuid():N}.apkg";
        var physicalPath = Path.Combine(vaultDir, fileName);
        File.WriteAllBytes(physicalPath, apkgBytes);
        return fileName;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Index
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Index_Anonymous_RedirectsToLogin()
    {
        var response = await _anonHttp.GetAsync("/ApkgUploads");

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous access to Index must redirect (to login).");
        Assert.IsTrue(
            response.Headers.Location?.OriginalString.Contains("Account/Login", StringComparison.OrdinalIgnoreCase) == true
            || response.Headers.Location?.OriginalString.Contains("login", StringComparison.OrdinalIgnoreCase) == true,
            $"Redirect should point to login, but was: {response.Headers.Location}");
    }

    [TestMethod]
    public async Task Index_Authenticated_Returns200()
    {
        var response = await Http.GetAsync("/ApkgUploads");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "An authenticated user must be able to access the APKG uploads index.");
    }

    [TestMethod]
    public async Task Index_Authenticated_ShowsOwnUploads()
    {
        var upload = AddApkgUpload();
        var response = await Http.GetAsync("/ApkgUploads");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains(upload.Package),
            "Index should list the admin's own packages.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Upload (GET)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task UploadGet_Anonymous_RedirectsToLogin()
    {
        var response = await _anonHttp.GetAsync("/ApkgUploads/Upload");

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous access to Upload page must redirect to login.");
    }

    [TestMethod]
    public async Task UploadGet_Authenticated_Returns200()
    {
        var response = await Http.GetAsync("/ApkgUploads/Upload");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "An authenticated user must see the Upload page.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Details
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Details_Anonymous_RedirectsToLogin()
    {
        var upload = AddApkgUpload();
        var response = await _anonHttp.GetAsync($"/ApkgUploads/Details/{upload.Id}");

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous access to Details must redirect to login.");
    }

    [TestMethod]
    public async Task Details_NonExistentUpload_Returns404()
    {
        var response = await Http.GetAsync("/ApkgUploads/Details/999999");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode,
            "Details for a non-existent upload must return 404.");
    }

    [TestMethod]
    public async Task Details_AdminCanViewAnyUpload_Returns200()
    {
        var upload = AddApkgUpload();
        var response = await Http.GetAsync($"/ApkgUploads/Details/{upload.Id}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "An admin must be able to view any upload's Details.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Unlist
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Unlist_Anonymous_RedirectsToLogin()
    {
        var upload = AddApkgUpload();
        var response = await _anonHttp.PostAsync($"/ApkgUploads/Unlist/{upload.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous POST to Unlist must redirect to login.");
    }

    [TestMethod]
    public async Task Unlist_NonExistentUpload_Returns404()
    {
        var token = await GetAntiCsrfToken("/ApkgUploads");
        var response = await Http.PostAsync("/ApkgUploads/Unlist/999999",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode,
            "Unlisting a non-existent upload must return 404.");
    }

    [TestMethod]
    public async Task Unlist_AdminCanUnlistOwnUpload_RedirectsToIndex()
    {
        var upload = AddApkgUpload(isPublished: true, isListed: true);

        var token = await GetAntiCsrfToken("/ApkgUploads");
        var response = await Http.PostAsync($"/ApkgUploads/Unlist/{upload.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        AssertRedirect(response, "/ApkgUploads");

        // Reload from DB and verify
        _db.Entry(upload).Reload();
        Assert.IsFalse(upload.IsListed, "Upload must be unlisted after the action.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Relist (admin-only)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Relist_Anonymous_RedirectsToLogin()
    {
        var upload = AddApkgUpload(isPublished: true, isListed: false);
        var response = await _anonHttp.PostAsync($"/ApkgUploads/Relist/{upload.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous relisting must redirect to login.");
    }

    [TestMethod]
    public async Task Relist_Admin_RelistsUploadAndRedirectsToDetails()
    {
        var upload = AddApkgUpload(isPublished: true, isListed: false);

        var token = await GetAntiCsrfToken("/ApkgUploads");
        var response = await Http.PostAsync($"/ApkgUploads/Relist/{upload.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        AssertRedirect(response, $"/ApkgUploads/Details/{upload.Id}");

        _db.Entry(upload).Reload();
        Assert.IsTrue(upload.IsListed, "Upload must be listed again after Relist.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Delete (admin-only)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Delete_Anonymous_RedirectsToLogin()
    {
        var upload = AddApkgUpload();
        var response = await _anonHttp.PostAsync($"/ApkgUploads/Delete/{upload.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous delete must redirect to login.");

        // Verify the record still exists
        var stillExists = _db.ApkgUploads.Any(u => u.Id == upload.Id);
        Assert.IsTrue(stillExists, "Upload must NOT be deleted when the requester is anonymous.");
    }

    [TestMethod]
    public async Task Delete_Admin_DeletesUploadAndRedirectsToIndex()
    {
        var upload = AddApkgUpload();
        var uploadId = upload.Id;

        var token = await GetAntiCsrfToken("/ApkgUploads");
        var response = await Http.PostAsync($"/ApkgUploads/Delete/{uploadId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        AssertRedirect(response, "/ApkgUploads");

        var deleted = _db.ApkgUploads.Any(u => u.Id == uploadId);
        Assert.IsFalse(deleted, "Upload must be deleted from the database after admin Delete.");
    }

    [TestMethod]
    public async Task Delete_NonExistentUpload_Returns404()
    {
        var token = await GetAntiCsrfToken("/ApkgUploads");
        var response = await Http.PostAsync("/ApkgUploads/Delete/999999",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode,
            "Deleting a non-existent upload must return 404.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Upload (POST) — requires a valid .apkg file in the Vault
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task UploadPost_Anonymous_RedirectsToLogin()
    {
        var response = await _anonHttp.PostAsync("/ApkgUploads/Upload",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "ApkgFilePath", "some-file.apkg" }
            }));

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous POST to Upload must redirect to login.");
    }

    [TestMethod]
    public async Task UploadPost_MissingFile_ReturnsModelError()
    {
        var token = await GetAntiCsrfToken("/ApkgUploads/Upload");
        var response = await Http.PostAsync("/ApkgUploads/Upload",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "ApkgFilePath", "nonexistent.apkg" },
                { "__RequestVerificationToken", token }
            }));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Upload with missing vault file should re-render the form (200).");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("File upload failed or missing"),
            "Page should show the missing-file error.");
    }

    [TestMethod]
    public async Task UploadPost_ValidApkg_CreatesRecordAndRedirectsToPreview()
    {
        var manifestXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>test-pkg</Name>
              <Version>1.0.0</Version>
              <Entries>
                <Entry>
                  <DebFile>test-pkg_1.0.0_amd64.deb</DebFile>
                  <Distro>anduinos</Distro>
                  <Suite>questing</Suite>
                  <Component>main</Component>
                  <Architecture>amd64</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        var apkgBytes = CreateApkgArchive(manifestXml,
            ("test-pkg_1.0.0_amd64.deb", new byte[64]));
        var vaultPath = SaveApkgToVault(apkgBytes);

        var uploadsBefore = _db.ApkgUploads.Count();

        var token = await GetAntiCsrfToken("/ApkgUploads/Upload");
        var response = await Http.PostAsync("/ApkgUploads/Upload",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "ApkgFilePath", vaultPath },
                { "__RequestVerificationToken", token }
            }));

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Upload POST with a valid .apkg must redirect.");
        Assert.IsTrue(
            response.Headers.Location?.OriginalString.Contains("Preview", StringComparison.OrdinalIgnoreCase) == true,
            $"Expected redirect to Preview, got: {response.Headers.Location}");

        var uploadsAfter = _db.ApkgUploads.Count();
        Assert.AreEqual(uploadsBefore + 1, uploadsAfter,
            "A new ApkgUpload record must be created.");
    }

    [TestMethod]
    public async Task UploadPost_ReUpload_UpdatesRecordNotDuplicates()
    {
        var manifestXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>test-pkg</Name>
              <Version>2.0.0</Version>
              <Entries>
                <Entry>
                  <DebFile>test-pkg_2.0.0_amd64.deb</DebFile>
                  <Distro>anduinos</Distro>
                  <Suite>questing</Suite>
                  <Component>main</Component>
                  <Architecture>amd64</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        var apkgBytes = CreateApkgArchive(manifestXml,
            ("test-pkg_2.0.0_amd64.deb", new byte[64]));
        var vaultPath = SaveApkgToVault(apkgBytes);

        // First upload
        var token = await GetAntiCsrfToken("/ApkgUploads/Upload");
        var response1 = await Http.PostAsync("/ApkgUploads/Upload",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "ApkgFilePath", vaultPath },
                { "__RequestVerificationToken", token }
            }));
        Assert.AreEqual(HttpStatusCode.Found, response1.StatusCode);

        var uploadsAfterFirst = _db.ApkgUploads.Count();

        // Second upload with same vault path
        token = await GetAntiCsrfToken("/ApkgUploads/Upload");
        var response2 = await Http.PostAsync("/ApkgUploads/Upload",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "ApkgFilePath", vaultPath },
                { "__RequestVerificationToken", token }
            }));
        Assert.AreEqual(HttpStatusCode.Found, response2.StatusCode);

        var uploadsAfterSecond = _db.ApkgUploads.Count();
        Assert.AreEqual(uploadsAfterFirst, uploadsAfterSecond,
            "Re-uploading the same vault file must update the existing record, not create a duplicate.");
    }

    [TestMethod]
    public async Task UploadPost_InvalidApkg_ReturnsModelError()
    {
        // Write garbage bytes to the vault
        var garbage = new byte[64];
        new Random(42).NextBytes(garbage);
        var vaultPath = SaveApkgToVault(garbage);

        var token = await GetAntiCsrfToken("/ApkgUploads/Upload");
        var response = await Http.PostAsync("/ApkgUploads/Upload",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "ApkgFilePath", vaultPath },
                { "__RequestVerificationToken", token }
            }));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Upload with invalid .apkg must re-render the form with error.");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("Failed to parse"),
            "Page must show parse error for invalid .apkg file.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Preview
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Preview_Anonymous_RedirectsToLogin()
    {
        var response = await _anonHttp.GetAsync("/ApkgUploads/Preview?vaultPath=some-file.apkg");

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous access to Preview must redirect to login.");
    }

    [TestMethod]
    public async Task Preview_MissingVaultPath_ReturnsBadRequest()
    {
        var response = await Http.GetAsync("/ApkgUploads/Preview?vaultPath=");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode,
            "Preview with empty vault path must return 400.");
    }

    [TestMethod]
    public async Task Preview_NonexistentFile_ReturnsNotFound()
    {
        var response = await Http.GetAsync("/ApkgUploads/Preview?vaultPath=nonexistent.apkg");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode,
            "Preview for a non-existent vault file must return 404.");
    }

    [TestMethod]
    public async Task Preview_ValidApkg_RendersPageWithTargets()
    {
        var manifestXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>preview-pkg</Name>
              <Version>1.0.0</Version>
              <Entries>
                <Entry>
                  <DebFile>pkg_1.0.0_amd64.deb</DebFile>
                  <Distro>anduinos</Distro>
                  <Suite>questing</Suite>
                  <Component>main</Component>
                  <Architecture>amd64</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        var apkgBytes = CreateApkgArchive(manifestXml,
            ("pkg_1.0.0_amd64.deb", new byte[64]));
        var vaultPath = SaveApkgToVault(apkgBytes);

        // First create the upload record
        var token = await GetAntiCsrfToken("/ApkgUploads/Upload");
        await Http.PostAsync("/ApkgUploads/Upload",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "ApkgFilePath", vaultPath },
                { "__RequestVerificationToken", token }
            }));

        var response = await Http.GetAsync($"/ApkgUploads/Preview?vaultPath={Uri.EscapeDataString(vaultPath)}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Preview of a valid .apkg must return 200.");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("preview-pkg"),
            "Preview must show the package name from manifest.");
        Assert.IsTrue(html.Contains("1.0.0"),
            "Preview must show the version from manifest.");
        Assert.IsTrue(html.Contains("anduinos"),
            "Preview must show the target distro.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Publish
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Publish_Anonymous_RedirectsToLogin()
    {
        var response = await _anonHttp.PostAsync("/ApkgUploads/Publish",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "vaultPath", "some-file.apkg" },
                { "fileName", "some-file.apkg" }
            }));

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous POST to Publish must redirect to login.");
    }

    [TestMethod]
    public async Task Publish_MissingVaultPath_ReturnsBadRequest()
    {
        var token = await GetAntiCsrfToken("/ApkgUploads");
        var response = await Http.PostAsync("/ApkgUploads/Publish",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "vaultPath", "" },
                { "fileName", "test.apkg" },
                { "__RequestVerificationToken", token }
            }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode,
            "Publish with empty vault path must return 400.");
    }

    [TestMethod]
    public async Task Publish_NoMatchingRepo_MarksPublished()
    {
        // Create .apkg targeting a distro/suite that has NO matching repo
        var manifestXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>orphan-pkg</Name>
              <Version>1.0.0</Version>
              <Entries>
                <Entry>
                  <DebFile>orphan.deb</DebFile>
                  <Distro>nonexistent</Distro>
                  <Suite>nonexistent</Suite>
                  <Component>main</Component>
                  <Architecture>amd64</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        var apkgBytes = CreateApkgArchive(manifestXml,
            ("orphan.deb", new byte[64]));
        var vaultPath = SaveApkgToVault(apkgBytes);

        // Create the upload record via Upload POST
        var token = await GetAntiCsrfToken("/ApkgUploads/Upload");
        await Http.PostAsync("/ApkgUploads/Upload",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "ApkgFilePath", vaultPath },
                { "__RequestVerificationToken", token }
            }));

        var upload = _db.ApkgUploads
            .First(u => u.VaultPath == vaultPath && !u.IsPublished);

        token = await GetAntiCsrfToken("/ApkgUploads");
        var response = await Http.PostAsync("/ApkgUploads/Publish",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "vaultPath", vaultPath },
                { "fileName", "orphan.apkg" },
                { "__RequestVerificationToken", token }
            }));

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Publish with no matching repo must still redirect (to Details).");

        _db.Entry(upload).Reload();
        Assert.IsTrue(upload.IsPublished,
            "Upload must be marked as published even when no entries matched a repo.");
        Assert.IsNull(upload.VaultPath,
            "Vault path must be cleared after successful publish.");
    }
}
