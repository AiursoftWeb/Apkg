using System.Net;
using System.Net.Http.Headers;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Services.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

[TestClass]
public class ApiPackagesPreflightTests : TestBase
{
    private ApkgDbContext _db = null!;

    [TestInitialize]
    public override async Task SetupTestContext()
    {
        await base.SetupTestContext();
        _db = GetService<ApkgDbContext>();
    }

    [TestMethod]
    public async Task Preflight_NoAuthentication_Returns401()
    {
        var response = await Http.PostAsJsonAsync(
            "/api/packages/preflight",
            CreateRequest("unauthenticated"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Preflight_NoMatchingRepository_IsNotPresent()
    {
        var (apiKey, _) = await CreateApiKeyAsync();
        using var request = CreateAuthedRequest(
            apiKey,
            CreateRequest($"no-repo-{Guid.NewGuid():N}"));

        var response = await Http.SendAsync(request);
        var result = await response.Content.ReadFromJsonAsync<PackagePreflightResponse>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(result);
        Assert.IsFalse(result.AllPresent);
        Assert.AreEqual(PackagePreflightStatus.NoRepository, result.Targets.Single().Status);
    }

    [TestMethod]
    public async Task Preflight_RequiresPackageInEveryMatchingRepository()
    {
        var (apiKey, userId) = await CreateApiKeyAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var packageName = $"matrix-{suffix}";
        var suite = $"suite-{suffix}";
        var repositories = new[]
        {
            CreateRepository($"repo-a-{suffix}", suite),
            CreateRepository($"repo-b-{suffix}", suite)
        };
        _db.AptRepositories.AddRange(repositories);
        await _db.SaveChangesAsync();
        _db.ApkgDebPackages.Add(
            CreateDeb(repositories[0].Id, packageName, userId));
        await _db.SaveChangesAsync();

        using var firstRequest = CreateAuthedRequest(
            apiKey,
            CreateRequest(packageName, suite));
        var firstResponse = await Http.SendAsync(firstRequest);
        var firstResult =
            await firstResponse.Content.ReadFromJsonAsync<PackagePreflightResponse>();

        Assert.IsNotNull(firstResult);
        Assert.IsFalse(firstResult.AllPresent);
        Assert.AreEqual(PackagePreflightStatus.Missing, firstResult.Targets.Single().Status);
        Assert.AreEqual(1, firstResult.Targets.Single().Repositories.Count(repo => repo.Present));

        _db.ApkgDebPackages.Add(
            CreateDeb(repositories[1].Id, packageName, userId));
        await _db.SaveChangesAsync();

        using var secondRequest = CreateAuthedRequest(
            apiKey,
            CreateRequest(packageName, suite));
        var secondResponse = await Http.SendAsync(secondRequest);
        var secondResult =
            await secondResponse.Content.ReadFromJsonAsync<PackagePreflightResponse>();

        Assert.IsNotNull(secondResult);
        Assert.IsTrue(secondResult.AllPresent);
        Assert.AreEqual(PackagePreflightStatus.Present, secondResult.Targets.Single().Status);
        Assert.IsTrue(secondResult.Targets.Single().Repositories.All(repo => repo.Present));
    }

    private async Task<(string apiKey, string userId)> CreateApiKeyAsync()
    {
        var userManager = GetService<UserManager<User>>();
        var email = $"preflight-{Guid.NewGuid():N}@test.com";
        var user = new User
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Preflight Test User"
        };
        var createResult = await userManager.CreateAsync(user, "Test@123456!");
        Assert.IsTrue(createResult.Succeeded);

        var rawKey = $"apkgkey{Guid.NewGuid():N}";
        _db.UserApiKeys.Add(new UserApiKey
        {
            UserId = user.Id,
            Name = "Preflight key",
            KeyHash = ApiKeyAuthenticationHandler.ComputeSha256Hex(rawKey),
            KeyPrefix = rawKey[..8]
        });
        await _db.SaveChangesAsync();
        return (rawKey, user.Id);
    }

    private static HttpRequestMessage CreateAuthedRequest(
        string apiKey,
        PackagePreflightRequest body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/packages/preflight")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private static PackagePreflightRequest CreateRequest(
        string packageName,
        string suite = "resolute-addon") => new()
    {
        Name = packageName,
        Distro = "anduinos",
        Component = "main",
        Targets =
        [
            new PackagePreflightTarget
            {
                Suite = suite,
                Architecture = "amd64",
                Version = "1.0.0+resolute"
            }
        ]
    };

    private static AptRepository CreateRepository(string name, string suite) => new()
    {
        Name = name,
        Distro = "anduinos",
        Suite = suite,
        Components = "main",
        Architecture = "amd64",
        AllowAnyoneToUpload = true
    };

    private static ApkgDebPackage CreateDeb(
        int repositoryId,
        string packageName,
        string userId) => new()
    {
        RepositoryId = repositoryId,
        UploadedByUserId = userId,
        Package = packageName,
        Version = "1.0.0+resolute",
        Architecture = "amd64",
        Maintainer = "Test",
        Filename = $"pool/main/{packageName[0]}/{packageName}.deb",
        Size = "1",
        SHA256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
        IsEnabled = true
    };
}
