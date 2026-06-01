using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Aiursoft.Apkg.Authorization;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

/// <summary>
/// Integration tests for POST /api/packages/apkg-upload.
///
/// This endpoint accepts a full .apkg bundle (tar.gz containing manifest.xml + .deb files)
/// uploaded by an authenticated client (typically the APKG CLI).
/// Tests cover authentication, missing-file, and invalid-payload paths.
/// </summary>
[TestClass]
public class ApiPackagesApkgUploadTests : TestBase
{
    private ApkgDbContext _db = null!;

    [TestInitialize]
    public override async Task SetupTestContext()
    {
        await base.SetupTestContext();
        _db = GetService<ApkgDbContext>();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helper
    // ──────────────────────────────────────────────────────────────────────

    private async Task<string> CreateApiKeyAsync(bool withManageRepos = false)
    {
        var userManager = GetService<UserManager<User>>();
        var email = $"apkgupload-{Guid.NewGuid():N}@test.com";
        var user = new User
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "APKG Upload Test User"
        };
        var result = await userManager.CreateAsync(user, "Test@123456!");
        Assert.IsTrue(result.Succeeded, $"Failed to create user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        if (withManageRepos)
        {
            await userManager.AddClaimAsync(user,
                new Claim(AppPermissions.Type, AppPermissionNames.CanManageRepositories));
            await userManager.AddClaimAsync(user,
                new Claim(AppPermissions.Type, AppPermissionNames.CanUploadToRestrictedRepositories));
        }

        var rawKey = $"apkgkey{Guid.NewGuid():N}";
        _db.UserApiKeys.Add(new UserApiKey
        {
            UserId = user.Id,
            Name = "APKG Upload Test Key",
            KeyHash = ApiKeyAuthenticationHandler.ComputeSha256Hex(rawKey),
            KeyPrefix = rawKey[..8]
        });
        await _db.SaveChangesAsync();
        return rawKey;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Authentication
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ApkgUpload_NoAuth_Returns401()
    {
        using var form = new MultipartFormDataContent();
        var response = await Http.PostAsync("/api/packages/apkg-upload", form);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode,
            "An unauthenticated request to apkg-upload must be rejected with 401.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Payload validation
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ApkgUpload_Authenticated_NoFile_Returns400()
    {
        var rawKey = await CreateApiKeyAsync();

        // POST with authentication but no multipart content at all
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/packages/apkg-upload");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawKey);
        request.Content = new MultipartFormDataContent();

        var response = await Http.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode,
            "Uploading with no file attached must return 400.");
    }

    [TestMethod]
    public async Task ApkgUpload_Authenticated_RandomBytes_Returns400()
    {
        var rawKey = await CreateApiKeyAsync();

        // Send random bytes that are not a valid .apkg (gzipped tar)
        var garbage = new byte[128];
        new Random(42).NextBytes(garbage);

        using var fileContent = CreateOctetStreamContent(garbage);
        using var form = new MultipartFormDataContent();
        form.Add(fileContent, "apkg", "garbage.apkg");

        using var request = CreateAuthedUploadRequest(rawKey, form);

        var response = await Http.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode,
            "An invalid (non-tar.gz) .apkg payload must return 400.");
    }

    [TestMethod]
    public async Task ApkgUpload_Authenticated_EmptyTarGz_Returns400()
    {
        var rawKey = await CreateApiKeyAsync();

        // Create a minimal valid gzip stream (empty tar archive: 2 × 512-byte EOF blocks)
        var emptyTarGz = CreateEmptyTarGz();
        using var fileContent = CreateOctetStreamContent(emptyTarGz);
        using var form = new MultipartFormDataContent();
        form.Add(fileContent, "apkg", "empty.apkg");

        using var request = CreateAuthedUploadRequest(rawKey, form);

        var response = await Http.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode,
            "An .apkg archive with no manifest.xml must return 400.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Record lifecycle tests
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ApkgUpload_NoMatchingRepo_RecordDeleted()
    {
        var rawKey = await CreateApiKeyAsync(withManageRepos: true);

        var manifestXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>test-pkg</Name>
              <Distro>anduinos</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>test-pkg_1.0.0_noble_amd64.deb</DebFile>
                  <Suite>noble-addon</Suite>
                  <Architecture>all</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        // No matching repo for anduinos/noble-addon → record should be cleaned up
        var apkgBytes = CreateApkgArchive(manifestXml,
            ("test-pkg_1.0.0_noble_amd64.deb", new byte[64]));

        using var apkgContent = CreateOctetStreamContent(apkgBytes);
        using var form = new MultipartFormDataContent();
        form.Add(apkgContent, "apkg", "test-pkg.apkg");
        using var request = CreateAuthedUploadRequest(rawKey, form);

        var response = await Http.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Upload with no matching repo should return OK (warnings only).");

        var uploadCount = _db.ApkgRevisions.Count();
        Assert.AreEqual(0, uploadCount,
            "Upload record should be deleted when nothing was uploaded.");
    }

    [TestMethod]
    public async Task ApkgUpload_PreflightMissingEntry_Returns400_BeforeRecordCreated()
    {
        var rawKey = await CreateApiKeyAsync(withManageRepos: true);

        // manifest references a deb that's NOT in the archive
        var manifestXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>preflight-test-pkg</Name>
              <Distro>anduinos</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>missing.deb</DebFile>
                  <Suite>noble</Suite>
                  <Architecture>amd64</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        // Include a different file — NOT the one the manifest references
        var apkgBytes = CreateApkgArchive(manifestXml,
            ("wrong-name.deb", new byte[64]));

        using var apkgContent = CreateOctetStreamContent(apkgBytes);
        using var form = new MultipartFormDataContent();
        form.Add(apkgContent, "apkg", "test.apkg");
        using var request = CreateAuthedUploadRequest(rawKey, form);

        var response = await Http.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode,
            "Pre-flight should reject missing entries before creating a record.");

        var uploadCount = _db.ApkgRevisions.Count();
        Assert.AreEqual(0, uploadCount,
            "No upload record should exist when pre-flight validation fails.");
    }

    [TestMethod]
    public async Task ApkgUpload_ArchAll_MatchesAnyArchitectureRepo()
    {
        var rawKey = await CreateApiKeyAsync(withManageRepos: true);

        // Set up a repo with amd64 architecture — arch:all entries should match it
        var repo = new AptRepository
        {
            Name = "test-repo",
            Distro = "anduinos",
            Suite = "noble",
            Components = "main",
            Architecture = "amd64"
        };
        _db.AptRepositories.Add(repo);
        await _db.SaveChangesAsync();

        var manifestXml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>test-pkg</Name>
              <Distro>anduinos</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>test-pkg_1.0.0_noble_all.deb</DebFile>
                  <Suite>noble</Suite>
                  <Architecture>all</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        var apkgBytes = CreateApkgArchive(manifestXml,
            ("test-pkg_1.0.0_noble_all.deb", new byte[64]));

        using var apkgContent = CreateOctetStreamContent(apkgBytes);
        using var form = new MultipartFormDataContent();
        form.Add(apkgContent, "apkg", "test-pkg.apkg");
        using var request = CreateAuthedUploadRequest(rawKey, form);

        var response = await Http.SendAsync(request);

        // The deb upload will fail (not a real .deb), but we're verifying
        // the arch:all matching → repo IS found (no "No repository found" warning)
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(responseJson.Contains("No repository found"),
            "arch:all entry should match an amd64 repo — no warning about missing repo.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Ownership (Name, Distro, Component triplet)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ApkgUpload_Ownership_SameTripletDifferentUser_Returns403()
    {
        // Arrange: create User A who "owns" the (Name, Distro, Component) triplet
        var userManager = GetService<UserManager<User>>();

        var emailB = $"apkgupload-{Guid.NewGuid():N}@test.com";
        var userB = new User
        {
            UserName = emailB,
            Email = emailB,
            EmailConfirmed = true,
            DisplayName = "User B"
        };
        await userManager.CreateAsync(userB, "Test@123456!");

        // Persist a package record for User A via a direct DB insert
        var manifestXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>owned-pkg</Name>
              <Distro>anduinos</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>owned-pkg.deb</DebFile>
                  <Suite>noble</Suite>
                  <Architecture>amd64</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        // Insert a package record directly into the DB for User A
        // The ownership check now looks at ApkgPackages table, not ApkgUploads
        var userARecord = _db.Users.First(u => u.Email!.StartsWith("apkgupload-"));
        _db.ApkgPackages.Add(new ApkgPackage
        {
            Name = "owned-pkg",
            Distro = "anduinos",
            Component = "main",
            OwnerUserId = userARecord.Id
        });
        await _db.SaveChangesAsync();

        // Act: User B tries to upload the same (Name, Distro, Component) triplet
        var rawKeyB = $"apkgkey{Guid.NewGuid():N}";
        _db.UserApiKeys.Add(new UserApiKey
        {
            UserId = userB.Id,
            Name = "User B Key",
            KeyHash = ApiKeyAuthenticationHandler.ComputeSha256Hex(rawKeyB),
            KeyPrefix = rawKeyB[..8]
        });
        await _db.SaveChangesAsync();

        var apkgBytes = CreateApkgArchive(manifestXml,
            ("owned-pkg.deb", new byte[64]));
        using var apkgContent = CreateOctetStreamContent(apkgBytes);
        using var form = new MultipartFormDataContent();
        form.Add(apkgContent, "apkg", "owned-pkg.apkg");
        using var request = CreateAuthedUploadRequest(rawKeyB, form);

        var response = await Http.SendAsync(request);

        // Assert
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode,
            "User B must be rejected with 403 when (Name, Distro, Component) is owned by another user.");
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(responseJson.Contains("already owned"),
            "Response must mention the triplet is already owned.");
    }

    [TestMethod]
    public async Task ApkgUpload_Ownership_SameTripletSameUser_Allowed()
    {
        // Same user uploading the same triplet should succeed (not be rejected)

        // First, directly insert a package record in the DB
        var userEmail = $"apkgupload-{Guid.NewGuid():N}@test.com";
        var userManager = GetService<UserManager<User>>();
        var user = new User
        {
            UserName = userEmail,
            Email = userEmail,
            EmailConfirmed = true,
            DisplayName = "Test Owner"
        };
        await userManager.CreateAsync(user, "Test@123456!");

        _db.ApkgPackages.Add(new ApkgPackage
        {
            Name = "mypkg",
            Distro = "anduinos",
            Component = "main",
            OwnerUserId = user.Id
        });
        await _db.SaveChangesAsync();

        // Give the user an API key
        var rawKeyB = $"apkgkey{Guid.NewGuid():N}";
        _db.UserApiKeys.Add(new UserApiKey
        {
            UserId = user.Id,
            Name = "Owner Key",
            KeyHash = ApiKeyAuthenticationHandler.ComputeSha256Hex(rawKeyB),
            KeyPrefix = rawKeyB[..8]
        });
        await _db.SaveChangesAsync();

        var manifestXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>mypkg</Name>
              <Distro>anduinos</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>mypkg.deb</DebFile>
                  <Suite>noble</Suite>
                  <Architecture>amd64</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;

        var apkgBytes = CreateApkgArchive(manifestXml,
            ("mypkg.deb", new byte[64]));
        using var apkgContent = CreateOctetStreamContent(apkgBytes);
        using var form = new MultipartFormDataContent();
        form.Add(apkgContent, "apkg", "mypkg.apkg");
        using var request = CreateAuthedUploadRequest(rawKeyB, form);

        var response = await Http.SendAsync(request);

        // Should NOT be 403. May be 400 (fake deb) or OK — not 403 and not "already owned"
        Assert.AreNotEqual(HttpStatusCode.Forbidden, response.StatusCode,
            "The same user re-uploading the same triplet must NOT get 403.");
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(responseJson.Contains("already owned"),
            "Must not mention 'already owned' when the user owns the triplet.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Ownership: different triplet = no conflict
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ApkgUpload_Ownership_DifferentDistro_NoConflict()
    {
        var rawKeyA = await CreateApiKeyAsync(withManageRepos: true);
        var userManager = GetService<UserManager<User>>();

        // User A creates package "a1" for distro "anduinos"
        var manifestA = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>a1</Name>
              <Distro>anduinos</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>a1.deb</DebFile>
                  <Suite>noble</Suite>
                  <Architecture>amd64</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;
        var apkgA = CreateApkgArchive(manifestA, ("a1.deb", new byte[64]));
        using var formA = new MultipartFormDataContent();
        formA.Add(CreateOctetStreamContent(apkgA), "apkg", "a1.apkg");
        using var reqA = CreateAuthedUploadRequest(rawKeyA, formA);
        await Http.SendAsync(reqA);

        // User B tries same name "a1" but DIFFERENT distro "ubuntu"
        var rawKeyB = await CreateApiKeyAsync(withManageRepos: true);
        var manifestB = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>a1</Name>
              <Distro>ubuntu</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>a1.deb</DebFile>
                  <Suite>noble</Suite>
                  <Architecture>amd64</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;
        var apkgB = CreateApkgArchive(manifestB, ("a1.deb", new byte[64]));
        using var formB = new MultipartFormDataContent();
        formB.Add(CreateOctetStreamContent(apkgB), "apkg", "a1-ubuntu.apkg");
        using var reqB = CreateAuthedUploadRequest(rawKeyB, formB);

        var response = await Http.SendAsync(reqB);

        // Must NOT be 403 — different distro = different triplet = different namespace
        Assert.AreNotEqual(HttpStatusCode.Forbidden, response.StatusCode,
            "Different distro means different triplet; must not return 403.");
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(responseJson.Contains("already owned"),
            "Must not mention 'already owned' when triplet differs by distro.");

        // Both packages should exist in DB as separate families
        var packageCount = _db.ApkgPackages.Count(p => p.Name == "a1");
        Assert.AreEqual(2, packageCount,
            "Two separate ApkgPackages should exist for the same name with different distros.");
    }

    [TestMethod]
    public async Task ApkgUpload_Ownership_DifferentComponent_NoConflict()
    {
        var rawKeyA = await CreateApiKeyAsync(withManageRepos: true);

        // User A creates "a1" for "anduinos/main"
        var manifestA = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>a1</Name>
              <Distro>anduinos</Distro>
              <Component>main</Component>
              <Entries>
                <Entry>
                  <DebFile>a1.deb</DebFile>
                  <Suite>noble</Suite>
                  <Architecture>amd64</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;
        var apkgA = CreateApkgArchive(manifestA, ("a1.deb", new byte[64]));
        using var formA = new MultipartFormDataContent();
        formA.Add(CreateOctetStreamContent(apkgA), "apkg", "a1.apkg");
        using var reqA = CreateAuthedUploadRequest(rawKeyA, formA);
        await Http.SendAsync(reqA);

        // User B tries same name + same distro but DIFFERENT component
        var rawKeyB = await CreateApiKeyAsync(withManageRepos: true);
        var manifestB = """
            <?xml version="1.0" encoding="utf-8"?>
            <ApkgPackage>
              <Name>a1</Name>
              <Distro>anduinos</Distro>
              <Component>restricted</Component>
              <Entries>
                <Entry>
                  <DebFile>a1.deb</DebFile>
                  <Suite>noble</Suite>
                  <Architecture>amd64</Architecture>
                </Entry>
              </Entries>
            </ApkgPackage>
            """;
        var apkgB = CreateApkgArchive(manifestB, ("a1.deb", new byte[64]));
        using var formB = new MultipartFormDataContent();
        formB.Add(CreateOctetStreamContent(apkgB), "apkg", "a1-restricted.apkg");
        using var reqB = CreateAuthedUploadRequest(rawKeyB, formB);

        var response = await Http.SendAsync(reqB);

        // Must NOT be 403 — different component = different triplet
        Assert.AreNotEqual(HttpStatusCode.Forbidden, response.StatusCode,
            "Different component means different triplet; must not return 403.");
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(responseJson.Contains("already owned"),
            "Must not mention 'already owned' when triplet differs by component.");

        // Both packages should exist
        var packageCount = _db.ApkgPackages.Count(p => p.Name == "a1" && p.Distro == "anduinos");
        Assert.AreEqual(2, packageCount,
            "Two separate ApkgPackages should exist for same (name, distro) with different components.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static byte[] CreateApkgArchive(string manifestXml,
        params (string fileName, byte[] content)[] files)
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms,
                   System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        {
            using var tar = new System.Formats.Tar.TarWriter(gz,
                System.Formats.Tar.TarEntryFormat.Pax, leaveOpen: true);

            var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestXml);
            var manifestEntry = new System.Formats.Tar.PaxTarEntry(
                System.Formats.Tar.TarEntryType.RegularFile, "manifest.xml")
            {
                DataStream = new MemoryStream(manifestBytes)
            };
            tar.WriteEntryAsync(manifestEntry).GetAwaiter().GetResult();

            foreach (var (name, data) in files)
            {
                var entry = new System.Formats.Tar.PaxTarEntry(
                    System.Formats.Tar.TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(data)
                };
                tar.WriteEntryAsync(entry).GetAwaiter().GetResult();
            }
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Creates a minimal gzip-compressed tar archive that contains no entries
    /// (two 512-byte zero EOF blocks, which is the standard tar end-of-archive marker).
    /// </summary>
    private static byte[] CreateEmptyTarGz()
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        {
            // Standard tar EOF: two 512-byte blocks of zeros
            gz.Write(new byte[1024]);
        }
        return ms.ToArray();
    }

    private static ByteArrayContent CreateOctetStreamContent(byte[] data)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    private static HttpRequestMessage CreateAuthedUploadRequest(string apiKey, HttpContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/packages/apkg-upload")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }
}
