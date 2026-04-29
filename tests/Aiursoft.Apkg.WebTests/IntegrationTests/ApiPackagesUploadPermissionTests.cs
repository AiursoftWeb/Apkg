using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Aiursoft.Apkg.Authorization;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

/// <summary>
/// Integration tests for the permission model of POST /api/packages/upload:
///   - Any authenticated user may upload to an open repository (AllowAnyoneToUpload = true).
///   - Only users with CanUploadToRestrictedRepositories may upload to a restricted repository.
///
/// A fake (invalid) .deb binary is sent intentionally.  The permission gate runs BEFORE deb
/// parsing, so if a user is authorised, the server reaches the parse stage and returns 400 for
/// the bad file.  If a user is not authorised, the server returns 403 before touching the file.
/// This lets us distinguish "permission denied" (403) from "permission passed, bad payload" (400).
/// </summary>
[TestClass]
public class ApiPackagesUploadPermissionTests : TestBase
{
    private ApkgDbContext _db = null!;
    private AptRepository _openRepo = null!;
    private AptRepository _restrictedRepo = null!;

    [TestInitialize]
    public override async Task SetupTestContext()
    {
        await base.SetupTestContext();

        _db = GetService<ApkgDbContext>();

        // Use the seeded repository as the open one.
        _openRepo = _db.AptRepositories.First();
        _openRepo.AllowAnyoneToUpload = true;

        // Create a second repository that is restricted.
        _restrictedRepo = new AptRepository
        {
            Name = $"restricted-test-{Guid.NewGuid():N}",
            Suite = "teststrict",
            AllowAnyoneToUpload = false
        };
        _db.AptRepositories.Add(_restrictedRepo);
        _db.SaveChanges();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a fresh user, optionally grants CanUploadToRestrictedRepositories,
    /// inserts an API key for that user, and returns the raw key string.
    /// </summary>
    private async Task<string> CreateUserAndApiKey(bool withRestrictedUploadPermission = false)
    {
        var userManager = GetService<UserManager<User>>();

        var email = $"apiperm-{Guid.NewGuid():N}@test.com";
        var user = new User
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "API Perm Test User"
        };
        var result = await userManager.CreateAsync(user, "Test@123456!");
        Assert.IsTrue(result.Succeeded,
            $"Failed to create test user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        if (withRestrictedUploadPermission)
        {
            await userManager.AddClaimAsync(user,
                new Claim(AppPermissions.Type, AppPermissionNames.CanUploadToRestrictedRepositories));
        }

        // Insert the API key directly into the database so we know the raw key value.
        var rawKey = $"testkey{Guid.NewGuid():N}"; // 40 chars; first 8 used as KeyPrefix
        _db.UserApiKeys.Add(new UserApiKey
        {
            UserId = user.Id,
            Name = "Integration Test Key",
            KeyHash = ApiKeyAuthenticationHandler.ComputeSha256Hex(rawKey),
            KeyPrefix = rawKey[..8]
        });
        await _db.SaveChangesAsync();

        return rawKey;
    }

    /// <summary>
    /// Returns a multipart/form-data body containing a small, intentionally invalid .deb file.
    /// </summary>
    private static MultipartFormDataContent CreateFakeDebContent()
    {
        // "!<arch>" — the start of a valid ar archive, but the rest is truncated/invalid.
        var fileBytes = new byte[] { 0x21, 0x3C, 0x61, 0x72, 0x63, 0x68, 0x3E };
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var form = new MultipartFormDataContent();
        form.Add(fileContent, "deb", "fake.deb");
        return form;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test cases
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Upload_NoPermission_OpenRepo_PermissionPasses_Returns400NotForbidden()
    {
        // Arrange: plain user, no special permissions; repo is open
        var rawKey = await CreateUserAndApiKey(withRestrictedUploadPermission: false);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/packages/upload?repositoryId={_openRepo.Id}&component=main");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawKey);
        request.Content = CreateFakeDebContent();

        // Act
        var response = await Http.SendAsync(request);

        // Assert: permission check passes (AllowAnyoneToUpload = true); fake .deb fails parsing → 400
        Assert.AreNotEqual(HttpStatusCode.Unauthorized, response.StatusCode,
            "A valid API key must be accepted — not 401.");
        Assert.AreNotEqual(HttpStatusCode.Forbidden, response.StatusCode,
            "A user uploading to an open repo must NOT be blocked with 403.");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode,
            "The fake .deb should reach the parse stage and fail with 400, confirming permission passed.");
    }

    [TestMethod]
    public async Task Upload_NoPermission_RestrictedRepo_Returns403Forbidden()
    {
        // Arrange: plain user, no special permissions; repo is restricted
        var rawKey = await CreateUserAndApiKey(withRestrictedUploadPermission: false);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/packages/upload?repositoryId={_restrictedRepo.Id}&component=main");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawKey);
        request.Content = CreateFakeDebContent();

        // Act
        var response = await Http.SendAsync(request);

        // Assert: permission gate must reject the upload before the file is touched
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode,
            "A user without CanUploadToRestrictedRepositories must be rejected with 403 on a restricted repo.");
    }

    [TestMethod]
    public async Task Upload_WithRestrictedUploadPermission_OpenRepo_PermissionPasses_Returns400NotForbidden()
    {
        // Arrange: user has CanUploadToRestrictedRepositories; repo is open (should trivially pass)
        var rawKey = await CreateUserAndApiKey(withRestrictedUploadPermission: true);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/packages/upload?repositoryId={_openRepo.Id}&component=main");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawKey);
        request.Content = CreateFakeDebContent();

        // Act
        var response = await Http.SendAsync(request);

        // Assert
        Assert.AreNotEqual(HttpStatusCode.Forbidden, response.StatusCode,
            "A user with upload permission uploading to an open repo must NOT get 403.");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode,
            "The fake .deb should reach parse stage and fail with 400.");
    }

    [TestMethod]
    public async Task Upload_WithRestrictedUploadPermission_RestrictedRepo_PermissionPasses_Returns400NotForbidden()
    {
        // Arrange: user has CanUploadToRestrictedRepositories; repo is restricted
        var rawKey = await CreateUserAndApiKey(withRestrictedUploadPermission: true);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/packages/upload?repositoryId={_restrictedRepo.Id}&component=main");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawKey);
        request.Content = CreateFakeDebContent();

        // Act
        var response = await Http.SendAsync(request);

        // Assert: permission check passes (CanUploadToRestrictedRepositories present);
        // fake .deb fails parsing → 400
        Assert.AreNotEqual(HttpStatusCode.Forbidden, response.StatusCode,
            "A user with CanUploadToRestrictedRepositories must NOT be rejected with 403 on a restricted repo.");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode,
            "The fake .deb should reach parse stage and fail with 400, confirming permission check passed.");
    }
}
