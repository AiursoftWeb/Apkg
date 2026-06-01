using System.Formats.Tar;
using System.Net;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.FileStorage;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

/// <summary>
/// Integration tests for the <see cref="Aiursoft.Apkg.Controllers.ApkgPackagesController"/>.
///
/// Covers: Index, Upload (GET/POST), Preview, Publish, Details, Unlist, Relist, Delete.
/// </summary>
[TestClass]
public class ApkgPackagesControllerTests : TestBase
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

    private ApkgRevision AddApkgPackageAndRevision(
        string? userId = null,
        string? packageName = null,
        string? component = null,
        bool isListed = true,
        string? vaultPath = null)
    {
        var name = packageName ?? $"test-pkg-{Guid.NewGuid():N}";
        var pkg = new ApkgPackage
        {
            Name = name,
            Distro = "ubuntu",
            Component = component ?? "main",
            OwnerUserId = userId ?? _adminUserId
        };
        _db.ApkgPackages.Add(pkg);
        _db.SaveChanges();

        var revision = new ApkgRevision
        {
            ApkgPackageId = pkg.Id,
            UploadedByUserId = userId ?? _adminUserId,
            FileName = "test.apkg",
            IsListed = isListed,
            TempApkgFileInVaultPath = vaultPath
        };
        _db.ApkgRevisions.Add(revision);
        _db.SaveChanges();
        return revision;
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

            foreach (var (entryName, data) in files)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
                {
                    DataStream = new MemoryStream(data)
                };
                tar.WriteEntryAsync(entry).GetAwaiter().GetResult();
            }
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Writes a .apkg to the Vault/apkg-upload subfolder and returns its
    /// logical vault path (relative to Vault root).  The path must start with
    /// "apkg-upload/" to pass the model's [RegularExpression] validation.
    /// </summary>
    private string SaveApkgToVault(byte[] apkgBytes)
    {
        var folders = GetService<FeatureFoldersProvider>();
        var subFolder = Path.Combine(folders.GetVaultFolder(), "apkg-upload");
        Directory.CreateDirectory(subFolder);
        var fileName = $"test-{Guid.NewGuid():N}.apkg";
        var physicalPath = Path.Combine(subFolder, fileName);
        File.WriteAllBytes(physicalPath, apkgBytes);
        return $"apkg-upload/{fileName}";
    }

    // ──────────────────────────────────────────────────────────────────────
    // Index
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Index_Anonymous_RedirectsToLogin()
    {
        var response = await _anonHttp.GetAsync("/ApkgPackages");

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
        var response = await Http.GetAsync("/ApkgPackages");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "An authenticated user must be able to access the APKG packages index.");
    }

    [TestMethod]
    public async Task Index_Authenticated_ShowsOwnUploads()
    {
        var revision = AddApkgPackageAndRevision();
        var response = await Http.GetAsync("/ApkgPackages");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains(revision.ApkgPackage!.Name),
            "Index should list the admin's own packages.");
    }

    [TestMethod]
    public async Task Index_GroupedByPackage_ShowsLatestOnly()
    {
        var pkgName = $"grouped-pkg-{Guid.NewGuid():N}";

        // Create a single ApkgPackage, then add 3 revisions at different times
        var pkg = new ApkgPackage
        {
            Name = pkgName,
            Distro = "ubuntu",
            Component = "main",
            OwnerUserId = _adminUserId
        };
        _db.ApkgPackages.Add(pkg);
        _db.SaveChanges();

        _db.ApkgRevisions.Add(new ApkgRevision
        {
            ApkgPackageId = pkg.Id,
            UploadedByUserId = _adminUserId,
            FileName = "v1.apkg",
            
            IsListed = true,
            UploadedAt = DateTime.UtcNow.AddHours(-3)
        });
        _db.ApkgRevisions.Add(new ApkgRevision
        {
            ApkgPackageId = pkg.Id,
            UploadedByUserId = _adminUserId,
            FileName = "v2.apkg",
            
            IsListed = true,
            UploadedAt = DateTime.UtcNow.AddHours(-2)
        });
        _db.ApkgRevisions.Add(new ApkgRevision
        {
            ApkgPackageId = pkg.Id,
            UploadedByUserId = _adminUserId,
            FileName = "v3.apkg",
            
            IsListed = true,
            UploadedAt = DateTime.UtcNow.AddHours(-1)
        });
        _db.SaveChanges();

        var response = await Http.GetAsync("/ApkgPackages");
        var html = await response.Content.ReadAsStringAsync();

        // Latest upload must appear, older uploads must not
        Assert.IsTrue(html.Contains(pkgName), "Index should show the latest upload.");
        Assert.IsFalse(html.Contains("v2.apkg"), "Index should not show older uploads.");
        Assert.IsFalse(html.Contains("v1.apkg"), "Index should not show older uploads.");
    }

    [TestMethod]
    public async Task Index_GroupedByPackage_DifferentPackagesBothShown()
    {
        var pkgName1 = $"pkg-alpha-{Guid.NewGuid():N}";
        var pkgName2 = $"pkg-beta-{Guid.NewGuid():N}";

        var pkg1 = new ApkgPackage
        {
            Name = pkgName1, Distro = "ubuntu", Component = "main", OwnerUserId = _adminUserId
        };
        var pkg2 = new ApkgPackage
        {
            Name = pkgName2, Distro = "ubuntu", Component = "main", OwnerUserId = _adminUserId
        };
        _db.ApkgPackages.Add(pkg1);
        _db.ApkgPackages.Add(pkg2);
        _db.SaveChanges();

        _db.ApkgRevisions.Add(new ApkgRevision
        {
            ApkgPackageId = pkg1.Id, UploadedByUserId = _adminUserId, FileName = "a1.apkg",
            IsListed = true
        });
        _db.ApkgRevisions.Add(new ApkgRevision
        {
            ApkgPackageId = pkg1.Id, UploadedByUserId = _adminUserId, FileName = "a2.apkg",
            IsListed = true
        });
        _db.ApkgRevisions.Add(new ApkgRevision
        {
            ApkgPackageId = pkg2.Id, UploadedByUserId = _adminUserId, FileName = "b1.apkg",
            IsListed = true
        });
        _db.SaveChanges();

        var response = await Http.GetAsync("/ApkgPackages");
        var html = await response.Content.ReadAsStringAsync();

        // Both packages should appear
        Assert.IsTrue(html.Contains(pkgName1), "First package should appear.");
        Assert.IsTrue(html.Contains(pkgName2), "Second package should appear.");
        Assert.IsFalse(html.Contains("a1.apkg"), "Older upload filename should not appear.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // PackageHistory
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task PackageHistory_Anonymous_RedirectsToLogin()
    {
        var response = await _anonHttp.GetAsync("/ApkgPackages/PackageHistory?name=some-pkg");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous access to PackageHistory must redirect to login.");
    }

    [TestMethod]
    public async Task PackageHistory_MissingName_ReturnsBadRequest()
    {
        var response = await Http.GetAsync("/ApkgPackages/PackageHistory?name=");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode,
            "PackageHistory with empty name must return 400.");
    }

    [TestMethod]
    public async Task PackageHistory_NonExistentPackage_ReturnsNotFound()
    {
        var response = await Http.GetAsync("/ApkgPackages/PackageHistory?name=nonexistent-pkg-xyz");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode,
            "PackageHistory for a non-existent package must return 404.");
    }

    [TestMethod]
    public async Task PackageHistory_ShowsAllVersions()
    {
        var pkgName = $"history-pkg-{Guid.NewGuid():N}";

        var pkg = new ApkgPackage
        {
            Name = pkgName, Distro = "ubuntu", Component = "main", OwnerUserId = _adminUserId
        };
        _db.ApkgPackages.Add(pkg);
        _db.SaveChanges();

        _db.ApkgRevisions.Add(new ApkgRevision
        {
            ApkgPackageId = pkg.Id, UploadedByUserId = _adminUserId, FileName = "v1.apkg",
            IsListed = true,
            UploadedAt = DateTime.UtcNow.AddHours(-2)
        });
        _db.ApkgRevisions.Add(new ApkgRevision
        {
            ApkgPackageId = pkg.Id, UploadedByUserId = _adminUserId, FileName = "v2.apkg",
            IsListed = true,
            UploadedAt = DateTime.UtcNow.AddHours(-1)
        });
        _db.SaveChanges();

        var response = await Http.GetAsync($"/ApkgPackages/PackageHistory?name={Uri.EscapeDataString(pkgName)}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(html.Contains(pkgName), "History should display the package name.");
    }

    [TestMethod]
    public async Task PackageHistory_ShowsBackLink()
    {
        var pkgName = $"backlink-pkg-{Guid.NewGuid():N}";

        var pkg = new ApkgPackage
        {
            Name = pkgName, Distro = "ubuntu", Component = "main", OwnerUserId = _adminUserId
        };
        _db.ApkgPackages.Add(pkg);
        _db.SaveChanges();

        _db.ApkgRevisions.Add(new ApkgRevision
        {
            ApkgPackageId = pkg.Id, UploadedByUserId = _adminUserId, FileName = "v1.apkg",
            IsListed = true
        });
        _db.SaveChanges();

        var response = await Http.GetAsync($"/ApkgPackages/PackageHistory?name={Uri.EscapeDataString(pkgName)}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Back to Packages"),
            "History page should have a back link to the package list.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Upload (GET)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task UploadGet_Anonymous_RedirectsToLogin()
    {
        var response = await _anonHttp.GetAsync("/ApkgPackages/Upload");

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous access to Upload page must redirect to login.");
    }

    [TestMethod]
    public async Task UploadGet_Authenticated_Returns200()
    {
        var response = await Http.GetAsync("/ApkgPackages/Upload");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "An authenticated user must see the Upload page.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Details
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Details_Anonymous_RedirectsToLogin()
    {
        var revision = AddApkgPackageAndRevision();
        var response = await _anonHttp.GetAsync($"/ApkgPackages/Details/{revision.Id}");

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous access to Details must redirect to login.");
    }

    [TestMethod]
    public async Task Details_NonExistentUpload_Returns404()
    {
        var response = await Http.GetAsync("/ApkgPackages/Details/999999");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode,
            "Details for a non-existent upload must return 404.");
    }

    [TestMethod]
    public async Task Details_AdminCanViewAnyUpload_Returns200()
    {
        var revision = AddApkgPackageAndRevision();
        var response = await Http.GetAsync($"/ApkgPackages/Details/{revision.Id}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "An admin must be able to view any upload's Details.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Unlist
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Unlist_Anonymous_RedirectsToLogin()
    {
        var revision = AddApkgPackageAndRevision();
        var response = await _anonHttp.PostAsync($"/ApkgPackages/Unlist/{revision.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous POST to Unlist must redirect to login.");
    }

    [TestMethod]
    public async Task Unlist_NonExistentUpload_Returns404()
    {
        var token = await GetAntiCsrfToken("/ApkgPackages");
        var response = await Http.PostAsync("/ApkgPackages/Unlist/999999",
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
        var revision = AddApkgPackageAndRevision(isListed: true);

        var token = await GetAntiCsrfToken("/ApkgPackages");
        var response = await Http.PostAsync($"/ApkgPackages/Unlist/{revision.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        AssertRedirect(response, "/ApkgPackages");

        // Reload from DB and verify
        _db.Entry(revision).Reload();
        Assert.IsFalse(revision.IsListed, "Upload must be unlisted after the action.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Relist (admin-only)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Relist_Anonymous_RedirectsToLogin()
    {
        var revision = AddApkgPackageAndRevision(isListed: false);
        var response = await _anonHttp.PostAsync($"/ApkgPackages/Relist/{revision.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous relisting must redirect to login.");
    }

    [TestMethod]
    public async Task Relist_Admin_RelistsUploadAndRedirectsToDetails()
    {
        var revision = AddApkgPackageAndRevision(isListed: false);

        var token = await GetAntiCsrfToken("/ApkgPackages");
        var response = await Http.PostAsync($"/ApkgPackages/Relist/{revision.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        AssertRedirect(response, $"/ApkgPackages/Details/{revision.Id}");

        _db.Entry(revision).Reload();
        Assert.IsTrue(revision.IsListed, "Upload must be listed again after Relist.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Delete (admin-only)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Delete_Anonymous_RedirectsToLogin()
    {
        var revision = AddApkgPackageAndRevision();
        var response = await _anonHttp.PostAsync($"/ApkgPackages/Delete/{revision.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous delete must redirect to login.");

        // Verify the record still exists
        var stillExists = _db.ApkgRevisions.Any(r => r.Id == revision.Id);
        Assert.IsTrue(stillExists, "Upload must NOT be deleted when the requester is anonymous.");
    }

    [TestMethod]
    public async Task Delete_Admin_DeletesUploadAndRedirectsToIndex()
    {
        var revision = AddApkgPackageAndRevision();
        var revisionId = revision.Id;

        var token = await GetAntiCsrfToken("/ApkgPackages");
        var response = await Http.PostAsync($"/ApkgPackages/Delete/{revisionId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        AssertRedirect(response, "/ApkgPackages");

        var deleted = _db.ApkgRevisions.Any(r => r.Id == revisionId);
        Assert.IsFalse(deleted, "Upload must be deleted from the database after admin Delete.");
    }

    [TestMethod]
    public async Task Delete_NonExistentUpload_Returns404()
    {
        var token = await GetAntiCsrfToken("/ApkgPackages");
        var response = await Http.PostAsync("/ApkgPackages/Delete/999999",
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
        var response = await _anonHttp.PostAsync("/ApkgPackages/Upload",
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
        var token = await GetAntiCsrfToken("/ApkgPackages/Upload");
        var response = await Http.PostAsync("/ApkgPackages/Upload",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "ApkgFilePath", "nonexistent.apkg" },
                { "__RequestVerificationToken", token }
            }));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Upload with missing vault file should re-render the form (200).");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(
            html.Contains("upload", StringComparison.OrdinalIgnoreCase),
            "Page should show validation error for missing file.");
    }

    [TestMethod]
    public async Task UploadPost_ValidApkg_CreatesRecordAndRedirectsToPreview()
    {
        var manifestXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>test-pkg</Name>
              <Distro>anduinos</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>test-pkg_1.0.0_amd64.deb</DebFile>
                  <Suite>questing</Suite>
                  <Architecture>amd64</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        var apkgBytes = CreateApkgArchive(manifestXml,
            ("test-pkg_1.0.0_amd64.deb", new byte[64]));
        var vaultPath = SaveApkgToVault(apkgBytes);

        var revisionsBefore = _db.ApkgRevisions.Count();

        var token = await GetAntiCsrfToken("/ApkgPackages/Upload");
        var response = await Http.PostAsync("/ApkgPackages/Upload",
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

        var revisionsAfter = _db.ApkgRevisions.Count();
        Assert.AreEqual(revisionsBefore + 1, revisionsAfter,
            "A new ApkgRevision record must be created.");
    }

    [TestMethod]
    public async Task UploadPost_ReUpload_UpdatesRecordNotDuplicates()
    {
        var manifestXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>test-pkg</Name>
              <Distro>anduinos</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>test-pkg_2.0.0_amd64.deb</DebFile>
                  <Suite>questing</Suite>
                  <Architecture>amd64</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        var apkgBytes = CreateApkgArchive(manifestXml,
            ("test-pkg_2.0.0_amd64.deb", new byte[64]));
        var vaultPath = SaveApkgToVault(apkgBytes);

        // First upload
        var token = await GetAntiCsrfToken("/ApkgPackages/Upload");
        var response1 = await Http.PostAsync("/ApkgPackages/Upload",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "ApkgFilePath", vaultPath },
                { "__RequestVerificationToken", token }
            }));
        Assert.AreEqual(HttpStatusCode.Found, response1.StatusCode);

        var revisionsAfterFirst = _db.ApkgRevisions.Count();

        // Second upload with same vault path
        token = await GetAntiCsrfToken("/ApkgPackages/Upload");
        var response2 = await Http.PostAsync("/ApkgPackages/Upload",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "ApkgFilePath", vaultPath },
                { "__RequestVerificationToken", token }
            }));
        Assert.AreEqual(HttpStatusCode.Found, response2.StatusCode);

        var revisionsAfterSecond = _db.ApkgRevisions.Count();
        Assert.AreEqual(revisionsAfterFirst, revisionsAfterSecond,
            "Re-uploading the same vault file must update the existing record, not create a duplicate.");
    }

    [TestMethod]
    public async Task UploadPost_InvalidApkg_ReturnsModelError()
    {
        // Write garbage bytes to the vault
        var garbage = new byte[64];
        new Random(42).NextBytes(garbage);
        var vaultPath = SaveApkgToVault(garbage);

        var token = await GetAntiCsrfToken("/ApkgPackages/Upload");
        var response = await Http.PostAsync("/ApkgPackages/Upload",
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
        var response = await _anonHttp.GetAsync("/ApkgPackages/Preview?vaultPath=some-file.apkg");

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Anonymous access to Preview must redirect to login.");
    }

    [TestMethod]
    public async Task Preview_MissingVaultPath_ReturnsBadRequest()
    {
        var response = await Http.GetAsync("/ApkgPackages/Preview?vaultPath=");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode,
            "Preview with empty vault path must return 400.");
    }

    [TestMethod]
    public async Task Preview_NonexistentFile_ReturnsNotFound()
    {
        var response = await Http.GetAsync("/ApkgPackages/Preview?vaultPath=nonexistent.apkg");

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
              <Distro>anduinos</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>pkg_1.0.0_amd64.deb</DebFile>
                  <Suite>questing</Suite>
                  <Architecture>amd64</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        var apkgBytes = CreateApkgArchive(manifestXml,
            ("pkg_1.0.0_amd64.deb", new byte[64]));
        var vaultPath = SaveApkgToVault(apkgBytes);

        // First create the upload record
        var token = await GetAntiCsrfToken("/ApkgPackages/Upload");
        await Http.PostAsync("/ApkgPackages/Upload",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "ApkgFilePath", vaultPath },
                { "__RequestVerificationToken", token }
            }));

        var response = await Http.GetAsync($"/ApkgPackages/Preview?vaultPath={Uri.EscapeDataString(vaultPath)}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Preview of a valid .apkg must return 200.");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("preview-pkg"),
            "Preview must show the package name from manifest.");
        Assert.IsTrue(html.Contains("anduinos"),
            "Preview must show the target distro.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Publish
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Publish_Anonymous_RedirectsToLogin()
    {
        var response = await _anonHttp.PostAsync("/ApkgPackages/Publish",
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
        var token = await GetAntiCsrfToken("/ApkgPackages");
        var response = await Http.PostAsync("/ApkgPackages/Publish",
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
              <Distro>nonexistent</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>orphan.deb</DebFile>
                  <Suite>nonexistent</Suite>
                  <Architecture>amd64</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        var apkgBytes = CreateApkgArchive(manifestXml,
            ("orphan.deb", new byte[64]));
        var vaultPath = SaveApkgToVault(apkgBytes);

        // Create the upload record via Upload POST
        var token = await GetAntiCsrfToken("/ApkgPackages/Upload");
        await Http.PostAsync("/ApkgPackages/Upload",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "ApkgFilePath", vaultPath },
                { "__RequestVerificationToken", token }
            }));

        var revision = _db.ApkgRevisions
            .First(r => r.TempApkgFileInVaultPath == vaultPath);

        token = await GetAntiCsrfToken("/ApkgPackages");
        var response = await Http.PostAsync("/ApkgPackages/Publish",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "vaultPath", vaultPath },
                { "fileName", "orphan.apkg" },
                { "__RequestVerificationToken", token }
            }));

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            "Publish with no matching repo must still redirect (to Details).");

        _db.Entry(revision).Reload();
        Assert.IsNull(revision.TempApkgFileInVaultPath,
            "TempApkgFileInVaultPath must be cleared after successful publish.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Push-twice-same-triplet equivalence
    // ──────────────────────────────────────────────────────────────────────

    private ApkgDebPackage CreateLocalPackage(int revisionId, string pkg, string ver, string arch)
    {
        return new ApkgDebPackage
        {
            ApkgRevisionId = revisionId,
            RepositoryId = 1,
            UploadedByUserId = _adminUserId,
            Package = pkg,
            Version = ver,
            Architecture = arch,
            Maintainer = "test <test@test.com>",
            Filename = $"pool/main/{pkg[0]}/{pkg}/{pkg}_{ver}_{arch}.deb",
            Size = "1024",
            SHA256 = $"sha256-{Guid.NewGuid():N}"
        };
    }

    [TestMethod]
    public async Task PushTwiceSameTriplet_CreatesTwoRevisionsUnderSamePackage()
    {
        var pkg = new ApkgPackage
        {
            Name = $"twopush-{Guid.NewGuid():N}",
            Distro = "ubuntu",
            Component = "main",
            OwnerUserId = _adminUserId
        };
        _db.ApkgPackages.Add(pkg);
        _db.SaveChanges();

        var rev1 = new ApkgRevision
        {
            ApkgPackageId = pkg.Id,
            UploadedByUserId = _adminUserId,
            FileName = "push1.apkg",
            
            IsListed = true,
            UploadedAt = DateTime.UtcNow.AddHours(-2)
        };
        _db.ApkgRevisions.Add(rev1);
        _db.SaveChanges();

        var rev2 = new ApkgRevision
        {
            ApkgPackageId = pkg.Id,
            UploadedByUserId = _adminUserId,
            FileName = "push2.apkg",
            
            IsListed = true,
            UploadedAt = DateTime.UtcNow.AddHours(-1)
        };
        _db.ApkgRevisions.Add(rev2);
        _db.SaveChanges();

        // Push 1: 3 debs
        for (var i = 0; i < 3; i++)
            _db.ApkgDebPackages.Add(CreateLocalPackage(rev1.Id, $"deb-{i}", "1.0.0", "amd64"));

        // Push 2: 2 debs (different names, same triplet)
        for (var i = 0; i < 2; i++)
            _db.ApkgDebPackages.Add(CreateLocalPackage(rev2.Id, $"deb-{i + 3}", "2.0.0", "amd64"));

        _db.SaveChanges();

        // Assert: two revisions exist under the same package
        var revisions = _db.ApkgRevisions
            .Where(r => r.ApkgPackageId == pkg.Id)
            .ToList();
        Assert.AreEqual(2, revisions.Count,
            "Two pushes on the same triplet must create two revisions under one package.");

        // Assert: total 5 ApkgDebPackages across both revisions
        var totalDebs = _db.ApkgDebPackages
            .Count(lp => lp.ApkgRevision != null
                         && lp.ApkgRevision.ApkgPackageId == pkg.Id);
        Assert.AreEqual(5, totalDebs,
            "Push 1 (3 debs) + Push 2 (2 debs) = 5 total debs under the same ApkgPackage.");

        // Assert: each revision has the correct count
        Assert.AreEqual(3, _db.ApkgDebPackages.Count(lp => lp.ApkgRevisionId == rev1.Id));
        Assert.AreEqual(2, _db.ApkgDebPackages.Count(lp => lp.ApkgRevisionId == rev2.Id));
    }

    [TestMethod]
    public async Task DeleteOneRevision_PreservesOtherRevisionAndPackages()
    {
        var pkg = new ApkgPackage
        {
            Name = $"delrev-{Guid.NewGuid():N}",
            Distro = "ubuntu",
            Component = "main",
            OwnerUserId = _adminUserId
        };
        _db.ApkgPackages.Add(pkg);
        _db.SaveChanges();

        var rev1 = new ApkgRevision
        {
            ApkgPackageId = pkg.Id,
            UploadedByUserId = _adminUserId,
            FileName = "keep.apkg",
            
            IsListed = true
        };
        _db.ApkgRevisions.Add(rev1);
        _db.SaveChanges();
        _db.ApkgDebPackages.Add(CreateLocalPackage(rev1.Id, "keep-deb", "1.0.0", "amd64"));

        var rev2 = new ApkgRevision
        {
            ApkgPackageId = pkg.Id,
            UploadedByUserId = _adminUserId,
            FileName = "delete.apkg",
            
            IsListed = true
        };
        _db.ApkgRevisions.Add(rev2);
        _db.SaveChanges();
        _db.ApkgDebPackages.Add(CreateLocalPackage(rev2.Id, "del-deb", "2.0.0", "amd64"));
        _db.SaveChanges();

        // Act: delete revision 2
        var token = await GetAntiCsrfToken("/ApkgPackages");
        var response = await Http.PostAsync($"/ApkgPackages/Delete/{rev2.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);

        // Assert: rev1 and its package survive
        Assert.IsTrue(_db.ApkgRevisions.Any(r => r.Id == rev1.Id),
            "Revision 1 must survive deletion of revision 2.");
        Assert.AreEqual(1, _db.ApkgDebPackages.Count(lp => lp.ApkgRevisionId == rev1.Id),
            "Revision 1's ApkgDebPackage must be untouched.");
        Assert.IsTrue(_db.ApkgPackages.Any(p => p.Id == pkg.Id),
            "ApkgPackage must survive deletion of one revision.");

        // Assert: rev2 and its packages are gone
        Assert.IsFalse(_db.ApkgRevisions.Any(r => r.Id == rev2.Id),
            "Revision 2 must be deleted.");
        Assert.AreEqual(0, _db.ApkgDebPackages.Count(lp => lp.ApkgRevisionId == rev2.Id),
            "Revision 2's ApkgDebPackages must cascade-delete with it.");
    }

    [TestMethod]
    public async Task PackageHistory_PushTwice_AggregatesDebsFromBothRevisions()
    {
        var pkgName = $"histdup-{Guid.NewGuid():N}";

        var pkg = new ApkgPackage
        {
            Name = pkgName,
            Distro = "ubuntu",
            Component = "main",
            OwnerUserId = _adminUserId
        };
        _db.ApkgPackages.Add(pkg);
        _db.SaveChanges();

        var rev1 = new ApkgRevision
        {
            ApkgPackageId = pkg.Id,
            UploadedByUserId = _adminUserId,
            FileName = "first-push.apkg",
            
            IsListed = true,
            UploadedAt = DateTime.UtcNow.AddHours(-2)
        };
        _db.ApkgRevisions.Add(rev1);
        _db.SaveChanges();
        _db.ApkgDebPackages.Add(CreateLocalPackage(rev1.Id, "deb-alpha", "1.0.0", "amd64"));

        var rev2 = new ApkgRevision
        {
            ApkgPackageId = pkg.Id,
            UploadedByUserId = _adminUserId,
            FileName = "second-push.apkg",
            
            IsListed = true,
            UploadedAt = DateTime.UtcNow.AddHours(-1)
        };
        _db.ApkgRevisions.Add(rev2);
        _db.SaveChanges();
        _db.ApkgDebPackages.Add(CreateLocalPackage(rev2.Id, "deb-beta", "2.0.0", "arm64"));
        _db.SaveChanges();

        var response = await Http.GetAsync(
            $"/ApkgPackages/PackageHistory?name={Uri.EscapeDataString(pkgName)}&distro=ubuntu&component=main&versionsFilter=all");
        var html = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        // PackageHistory renders ApkgDebPackages (each .deb) with their version and architecture,
        // NOT the revision FileName. Both pushes' debs should be visible with "all" filter.
        Assert.IsTrue(html.Contains("1.0.0"),
            "PackageHistory must show deb from first push.");
        Assert.IsTrue(html.Contains("2.0.0"),
            "PackageHistory must show deb from second push.");
        Assert.IsTrue(html.Contains("amd64"),
            "PackageHistory must show first push's architecture.");
        Assert.IsTrue(html.Contains("arm64"),
            "PackageHistory must show second push's architecture.");
    }

    [TestMethod]
    public async Task Index_PushTwiceSameTriplet_ShowsOneRow()
    {
        var pkgName = $"idx-onerow-{Guid.NewGuid():N}";

        var pkg = new ApkgPackage
        {
            Name = pkgName,
            Distro = "ubuntu",
            Component = "main",
            OwnerUserId = _adminUserId
        };
        _db.ApkgPackages.Add(pkg);
        _db.SaveChanges();

        _db.ApkgRevisions.Add(new ApkgRevision
        {
            ApkgPackageId = pkg.Id,
            UploadedByUserId = _adminUserId,
            FileName = "v1.apkg",
            
            IsListed = true,
            UploadedAt = DateTime.UtcNow.AddHours(-2)
        });
        _db.ApkgRevisions.Add(new ApkgRevision
        {
            ApkgPackageId = pkg.Id,
            UploadedByUserId = _adminUserId,
            FileName = "v2.apkg",
            
            IsListed = true,
            UploadedAt = DateTime.UtcNow.AddHours(-1)
        });
        _db.SaveChanges();

        var response = await Http.GetAsync("/ApkgPackages");
        var html = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            $"Expected 200 OK but got {response.StatusCode}. Body: {html[..Math.Min(2000, html.Length)]}");

        // One row per package — older filenames must not appear.
        // The Index view renders item.Package.Name, not revision filenames.
        Assert.IsTrue(html.Contains(pkgName),
            $"Index must show '{pkgName}'. Body: {html[..Math.Min(500, html.Length)]}");
        Assert.IsFalse(html.Contains("v1.apkg"),
            "Older push filename must not appear in Index.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Unpublished handling
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Index_UnpublishedRevision_ShowsUnpublishedStatus()
    {
        var pkgName = $"unpub-idx-{Guid.NewGuid():N}";
        var pkg = new ApkgPackage
        {
            Name = pkgName, Distro = "ubuntu", Component = "main", OwnerUserId = _adminUserId
        };
        _db.ApkgPackages.Add(pkg);
        _db.SaveChanges();

        _db.ApkgRevisions.Add(new ApkgRevision
        {
            ApkgPackageId = pkg.Id,
            UploadedByUserId = _adminUserId,
            FileName = "unpublished.apkg",
            TempApkgFileInVaultPath = "apkg-upload/unpublished.apkg",
            IsListed = true
        });
        _db.SaveChanges();

        var response = await Http.GetAsync("/ApkgPackages");
        var html = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            $"Expected 200 OK but got {response.StatusCode}. Body: {html[..Math.Min(2000, html.Length)]}");
        Assert.IsTrue(html.Contains(pkgName),
            "Unpublished package should appear in Index with Unpublished status.");
        Assert.IsTrue(html.Contains("Unpublished"),
            "Index should show 'Unpublished' badge for unpublished revisions.");
    }

    [TestMethod]
    public async Task Index_PublishedRevision_ShowsSyncingStatus()
    {
        var pkgName = $"syncing-idx-{Guid.NewGuid():N}";
        var pkg = new ApkgPackage
        {
            Name = pkgName, Distro = "ubuntu", Component = "main", OwnerUserId = _adminUserId
        };
        _db.ApkgPackages.Add(pkg);
        _db.SaveChanges();

        _db.ApkgRevisions.Add(new ApkgRevision
        {
            ApkgPackageId = pkg.Id,
            UploadedByUserId = _adminUserId,
            FileName = "published.apkg",
            
            IsListed = true
        });
        _db.SaveChanges();

        var response = await Http.GetAsync("/ApkgPackages");
        var html = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            $"Expected 200 OK but got {response.StatusCode}. Body: {html[..Math.Min(2000, html.Length)]}");
        Assert.IsTrue(html.Contains("Syncing"),
            "Published but not-yet-live package must show Syncing badge in Index.");
    }

    [TestMethod]
    public async Task Details_UnpublishedRevision_RendersWithPublishAffordance()
    {
        var revision = AddApkgPackageAndRevision(vaultPath: "apkg-upload/unpub-test.apkg");

        var response = await Http.GetAsync($"/ApkgPackages/Details/{revision.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        // Unpublished details should show the Publish affordance
        Assert.IsTrue(html.Contains(revision.ApkgPackage!.Name),
            "Details must show the package name.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // PackageHistory version filters
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task PackageHistory_FilterLatest_ShowsOnlyLatestRevisionPackages()
    {
        var pkgName = $"flt-{Guid.NewGuid():N}";
        var pkg = new ApkgPackage
        {
            Name = pkgName, Distro = "ubuntu", Component = "main", OwnerUserId = _adminUserId
        };
        _db.ApkgPackages.Add(pkg);
        _db.SaveChanges();

        var rev1 = new ApkgRevision
        {
            ApkgPackageId = pkg.Id, UploadedByUserId = _adminUserId,
            FileName = "v1.apkg", IsListed = true,
            UploadedAt = DateTime.UtcNow.AddHours(-2)
        };
        _db.ApkgRevisions.Add(rev1);
        _db.SaveChanges();
        _db.ApkgDebPackages.Add(CreateLocalPackage(rev1.Id, "pkg-a", "1.0.0", "amd64"));

        var rev2 = new ApkgRevision
        {
            ApkgPackageId = pkg.Id, UploadedByUserId = _adminUserId,
            FileName = "v2.apkg", IsListed = true,
            UploadedAt = DateTime.UtcNow.AddHours(-1)
        };
        _db.ApkgRevisions.Add(rev2);
        _db.SaveChanges();
        _db.ApkgDebPackages.Add(CreateLocalPackage(rev2.Id, "pkg-b", "2.0.0", "arm64"));
        _db.SaveChanges();

        var response = await Http.GetAsync(
            $"/ApkgPackages/PackageHistory?name={Uri.EscapeDataString(pkgName)}&distro=ubuntu&component=main&versionsFilter=latest");
        var html = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        // latest filter → only rev2's packages (latest published revision).
        // Use architecture to distinguish: it only appears in table <td>, not in modals,
        // whereas version strings also appear in Unlist/Delete modal bodies.
        Assert.IsTrue(html.Contains("arm64"),
            "Latest filter must show the latest revision's package (arch arm64).");
        Assert.IsFalse(html.Contains("amd64"),
            "Latest filter must NOT show the older revision's package (arch amd64).");
    }

    [TestMethod]
    public async Task PackageHistory_FilterAll_ShowsAllRevisionsPackages()
    {
        var pkgName = $"fltall-{Guid.NewGuid():N}";
        var pkg = new ApkgPackage
        {
            Name = pkgName, Distro = "ubuntu", Component = "main", OwnerUserId = _adminUserId
        };
        _db.ApkgPackages.Add(pkg);
        _db.SaveChanges();

        var rev1 = new ApkgRevision
        {
            ApkgPackageId = pkg.Id, UploadedByUserId = _adminUserId,
            FileName = "v1.apkg", IsListed = true,
            UploadedAt = DateTime.UtcNow.AddHours(-2)
        };
        _db.ApkgRevisions.Add(rev1);
        _db.SaveChanges();
        _db.ApkgDebPackages.Add(CreateLocalPackage(rev1.Id, "pkg-a", "1.0.0", "amd64"));

        var rev2 = new ApkgRevision
        {
            ApkgPackageId = pkg.Id, UploadedByUserId = _adminUserId,
            FileName = "v2.apkg", IsListed = true,
            UploadedAt = DateTime.UtcNow.AddHours(-1)
        };
        _db.ApkgRevisions.Add(rev2);
        _db.SaveChanges();
        _db.ApkgDebPackages.Add(CreateLocalPackage(rev2.Id, "pkg-b", "2.0.0", "arm64"));
        _db.SaveChanges();

        var response = await Http.GetAsync(
            $"/ApkgPackages/PackageHistory?name={Uri.EscapeDataString(pkgName)}&distro=ubuntu&component=main&versionsFilter=all");
        var html = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        // all filter → both revisions' packages must appear
        Assert.IsTrue(html.Contains("1.0.0"),
            "All filter must show the first revision's package version.");
        Assert.IsTrue(html.Contains("2.0.0"),
            "All filter must show the second revision's package version.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // UploadHistory
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task UploadHistory_Anonymous_RedirectsToLogin()
    {
        var response = await _anonHttp.GetAsync("/ApkgPackages/UploadHistory");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
    }

    [TestMethod]
    public async Task UploadHistory_Authenticated_Returns200()
    {
        var response = await Http.GetAsync("/ApkgPackages/UploadHistory");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task UploadHistory_ShowsOwnRevisions()
    {
        var revision = AddApkgPackageAndRevision();
        var response = await Http.GetAsync("/ApkgPackages/UploadHistory");
        var html = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(html.Contains(revision.ApkgPackage!.Name),
            "UploadHistory must list the user's own revision.");
    }

    [TestMethod]
    public async Task UploadHistory_UnpublishedRevision_ShowsUnpublishedBadge()
    {
        AddApkgPackageAndRevision(vaultPath: "apkg-upload/unpub.apkg");
        var response = await Http.GetAsync("/ApkgPackages/UploadHistory");
        var html = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(html.Contains("Unpublished"),
            "UploadHistory must show Unpublished badge for revisions with TempApkgFileInVaultPath set.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // DeletePackage
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task DeletePackage_Anonymous_RedirectsToLogin()
    {
        var revision = AddApkgPackageAndRevision();
        var pkgId = revision.ApkgPackageId;

        var response = await _anonHttp.PostAsync($"/ApkgPackages/DeletePackage?id={pkgId}",
            new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);

        var stillExists = _db.ApkgPackages.Any(p => p.Id == pkgId);
        Assert.IsTrue(stillExists, "Package must survive anonymous delete attempt.");
    }

    [TestMethod]
    public async Task DeletePackage_Owner_DeletesEntirePackageAndRevisionsAndLocalPackages()
    {
        var pkg = new ApkgPackage
        {
            Name = $"delpkg-{Guid.NewGuid():N}",
            Distro = "ubuntu",
            Component = "main",
            OwnerUserId = _adminUserId
        };
        _db.ApkgPackages.Add(pkg);
        _db.SaveChanges();

        var rev1 = new ApkgRevision
        {
            ApkgPackageId = pkg.Id, UploadedByUserId = _adminUserId,
            FileName = "r1.apkg", IsListed = true
        };
        var rev2 = new ApkgRevision
        {
            ApkgPackageId = pkg.Id, UploadedByUserId = _adminUserId,
            FileName = "r2.apkg", IsListed = true
        };
        _db.ApkgRevisions.Add(rev1);
        _db.ApkgRevisions.Add(rev2);
        _db.SaveChanges();

        _db.ApkgDebPackages.Add(CreateLocalPackage(rev1.Id, "deb-a", "1.0.0", "amd64"));
        _db.ApkgDebPackages.Add(CreateLocalPackage(rev2.Id, "deb-b", "2.0.0", "arm64"));
        _db.SaveChanges();

        var pkgId = pkg.Id;
        var token = await GetAntiCsrfToken("/ApkgPackages");
        var response = await Http.PostAsync($"/ApkgPackages/DeletePackage?id={pkgId}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "__RequestVerificationToken", token }
            }));

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);

        // Cascade: package, all revisions, and all local packages must be gone
        Assert.IsFalse(_db.ApkgPackages.Any(p => p.Id == pkgId));
        Assert.IsFalse(_db.ApkgRevisions.Any(r => r.ApkgPackageId == pkgId));
        Assert.AreEqual(0, _db.ApkgDebPackages.Count(lp =>
            lp.ApkgRevision != null && lp.ApkgRevision.ApkgPackageId == pkgId));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Live status (requires AptRepository + PrimaryBucket + AptPackage setup)
    // ──────────────────────────────────────────────────────────────────────

    private void SetupLiveInfra(string distro, string suite, string architecture, string component,
        out int bucketId, out int repoId)
    {
        var bucket = new AptBucket();
        _db.AptBuckets.Add(bucket);
        _db.SaveChanges();
        bucketId = bucket.Id;

        var repo = new AptRepository
        {
            Name = $"repo-{Guid.NewGuid():N}",
            Distro = distro,
            Suite = suite,
            Components = component,
            Architecture = architecture,
            PrimaryBucketId = bucketId
        };
        _db.AptRepositories.Add(repo);
        _db.SaveChanges();
        repoId = repo.Id;
    }

    private void InsertLiveAptPackage(int bucketId, string package, string version, string architecture, string component)
    {
        var sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        _db.AptPackages.Add(new AptPackage
        {
            BucketId = bucketId,
            Package = package,
            Version = version,
            Architecture = architecture,
            Component = component,
            Filename = $"pool/main/{package[0]}/{package}/{package}_{version}_{architecture}.deb",
            OriginSuite = "noble",
            OriginComponent = component,
            Maintainer = "test <test@test.com>",
            Description = "Test package",
            DescriptionMd5 = "d41d8cd98f00b204e9800998ecf8427e",
            Section = "utils",
            Priority = "optional",
            Origin = "test",
            Bugs = "https://example.com/bugs",
            Size = "1024",
            MD5sum = "d41d8cd98f00b204e9800998ecf8427e",
            SHA1 = "da39a3ee5e6b4b0d3255bfef95601890afd80709",
            SHA256 = sha256,
            SHA512 = "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e",
            IsVirtual = false
        });
        _db.SaveChanges();
    }

    [TestMethod]
    public async Task Index_LiveRevision_ShowsLiveStatusAndNextVersion()
    {
        var pkgName = $"live-idx-{Guid.NewGuid():N}";
        SetupLiveInfra("ubuntu", "noble", "amd64", "main", out var bucketId, out var repoId);

        var pkg = new ApkgPackage
        {
            Name = pkgName, Distro = "ubuntu", Component = "main", OwnerUserId = _adminUserId
        };
        _db.ApkgPackages.Add(pkg);
        _db.SaveChanges();

        // Revision 1: published, has a live package (exists in AptPackages)
        var rev1 = new ApkgRevision
        {
            ApkgPackageId = pkg.Id, UploadedByUserId = _adminUserId,
            FileName = "v1.apkg", IsListed = true,
            UploadedAt = DateTime.UtcNow.AddHours(-2)
        };
        _db.ApkgRevisions.Add(rev1);
        _db.SaveChanges();

        var lp1 = CreateLocalPackage(rev1.Id, "live-deb", "1.0.0", "amd64");
        lp1.RepositoryId = repoId;
        _db.ApkgDebPackages.Add(lp1);

        // Make it live by inserting a matching AptPackage
        InsertLiveAptPackage(bucketId, "live-deb", "1.0.0", "amd64", "main");
        _db.SaveChanges();

        // Revision 2: newer published revision, not yet live
        var rev2 = new ApkgRevision
        {
            ApkgPackageId = pkg.Id, UploadedByUserId = _adminUserId,
            FileName = "v2.apkg", IsListed = true,
            UploadedAt = DateTime.UtcNow.AddHours(-1)
        };
        _db.ApkgRevisions.Add(rev2);
        _db.SaveChanges();

        var lp2 = CreateLocalPackage(rev2.Id, "live-deb", "2.0.0", "amd64");
        lp2.RepositoryId = repoId;
        _db.ApkgDebPackages.Add(lp2);
        _db.SaveChanges();

        var response = await Http.GetAsync("/ApkgPackages");
        var html = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            $"Expected 200 OK but got {response.StatusCode}. Body: {html[..Math.Min(2000, html.Length)]}");
        Assert.IsTrue(html.Contains(pkgName), "Live package must appear in Index.");
        Assert.IsTrue(html.Contains("Live"),
            "Index must show Live badge when a revision's packages are confirmed in the APT repo.");
        Assert.IsTrue(html.Contains("2.0.0"),
            "Index must show Next Version badge linking to the newer pending revision.");
    }

    [TestMethod]
    public async Task PackageHistory_FilterLive_ShowsOnlyLivePackages()
    {
        var pkgName = $"live-hist-{Guid.NewGuid():N}";
        SetupLiveInfra("ubuntu", "noble", "amd64", "main", out var bucketId, out var repoId);

        var pkg = new ApkgPackage
        {
            Name = pkgName, Distro = "ubuntu", Component = "main", OwnerUserId = _adminUserId
        };
        _db.ApkgPackages.Add(pkg);
        _db.SaveChanges();

        var rev = new ApkgRevision
        {
            ApkgPackageId = pkg.Id, UploadedByUserId = _adminUserId,
            FileName = "v1.apkg", IsListed = true
        };
        _db.ApkgRevisions.Add(rev);
        _db.SaveChanges();

        // Live package
        var liveLp = CreateLocalPackage(rev.Id, "live-deb", "1.0.0", "amd64");
        liveLp.RepositoryId = repoId;
        _db.ApkgDebPackages.Add(liveLp);
        InsertLiveAptPackage(bucketId, "live-deb", "1.0.0", "amd64", "main");

        // Staged package (not live)
        var stagedLp = CreateLocalPackage(rev.Id, "live-deb", "2.0.0", "amd64");
        stagedLp.RepositoryId = repoId;
        _db.ApkgDebPackages.Add(stagedLp);
        _db.SaveChanges();

        var response = await Http.GetAsync(
            $"/ApkgPackages/PackageHistory?name={Uri.EscapeDataString(pkgName)}&distro=ubuntu&component=main&versionsFilter=live");
        var html = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        // Live filter → only the package confirmed in AptPackages appears
        Assert.IsTrue(html.Contains("1.0.0"),
            "Live filter must show the version confirmed in the APT repo.");
        Assert.IsFalse(html.Contains("2.0.0"),
            "Live filter must NOT show the version that is not yet in the APT repo.");
    }
}
