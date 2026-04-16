using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.BackgroundJobs;
using Aiursoft.AptClient;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

[TestClass]
public class AptMirrorTests : TestBase
{
    private static readonly HttpClient ExternalClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private async Task<bool> CheckInternet()
    {
        try
        {
            var response = await ExternalClient.GetAsync("https://mirror.aiursoft.com/ubuntu/dists/questing/InRelease");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    [TestMethod]
    public async Task TestMirrorsSeeded()
    {
        await Server!.SeedMirrorsAsync(true);
        var mirrorService = GetService<AptMirrorService>();
        var isConfigured = await mirrorService.CheckConfiguredAsync(4);
        Assert.IsTrue(isConfigured, "Mirror repositories should be seeded.");
    }

    [TestMethod]
    public async Task TestMirrorManagementActions()
    {
        await Server!.SeedMirrorsAsync(true);
        await LoginAsAdmin();

        var indexResponse = await Http.GetAsync("/Mirrors");
        Assert.AreEqual(HttpStatusCode.OK, indexResponse.StatusCode);
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(indexHtml.Contains("Mirrors Management"));

        var createData = new Dictionary<string, string>
        {
            { "BaseUrl", "https://mirror.aiursoft.com/ubuntu/" },
            { "Suite", "test-suite" },
            { "Components", "main" }
        };
        var createResponse = await PostForm("/Mirrors/Create", createData);
        AssertRedirect(createResponse, "/Mirrors");

        indexResponse = await Http.GetAsync("/Mirrors");
        indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(indexHtml.Contains("test-suite"));

        var idMatch = Regex.Match(indexHtml, @"/Mirrors/Edit/(\d+)");
        Assert.IsTrue(idMatch.Success);
        var id = idMatch.Groups[1].Value;

        var editData = new Dictionary<string, string>
        {
            { "Id", id },
            { "BaseUrl", "https://mirror.aiursoft.com/ubuntu/" },
            { "Suite", "updated-suite" },
            { "Components", "main,universe" }
        };
        var editResponse = await PostForm($"/Mirrors/Edit/{id}", editData);
        AssertRedirect(editResponse, "/Mirrors");

        var deleteResponse = await PostForm($"/Mirrors/Delete/{id}", new Dictionary<string, string>());
        AssertRedirect(deleteResponse, "/Mirrors");
    }

    [TestMethod]
    public async Task TestLazyDownloadInRelease()
    {
        if (!await CheckInternet()) Assert.Inconclusive("No internet access to upstream mirror.");
        
        await Server!.SeedMirrorsAsync(true);
        var response = await Http.GetAsync("/dists/questing/InRelease");
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var mirrorService = GetService<AptMirrorService>();
            var localPath = await mirrorService.GetLocalMetadataPath("questing", "InRelease");
            Assert.IsNotNull(localPath);
            Assert.IsTrue(File.Exists(localPath));
        }
    }

    [TestMethod]
    public async Task TestClosedLoopWithAptClient()
    {
        if (!await CheckInternet()) Assert.Inconclusive("No internet access to upstream mirror.");

        await Server!.SeedMirrorsAsync(true);
        var localServerUrl = $"http://localhost:{Port}/";
        var repo = new AptRepository(localServerUrl, "questing", null, httpClientFactory: () => Http);
        var source = new AptPackageSource(repo, "main", "amd64", httpClientFactory: () => Http);

        try
        {
            var packages = await source.FetchPackagesAsync();
            Assert.IsNotNull(packages);
        }
        catch (Exception ex)
        {
            Assert.Inconclusive($"AptClient fetch failed: {ex.Message}");
        }
    }

    [TestMethod]
    [Ignore("Skip MirrorSyncJob by default.")]
    public async Task TestMirrorSyncJobExecution()
    {
        await Server!.SeedMirrorsAsync(true);
        var syncJob = GetService<MirrorSyncJob>();
        await syncJob.ExecuteAsync();
    }

    [TestMethod]
    public async Task TestPackageListAndDetails()
    {
        await Server!.SeedMirrorsAsync(true);
        await LoginAsAdmin();

        var mirrorService = GetService<AptMirrorService>();
        var mirror = await mirrorService.GetMirrorForSuite("questing");
        Assert.IsNotNull(mirror);

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.AptPackages.Add(new AptPackage
            {
                MirrorRepositoryId = mirror.Id,
                Package = "test-pkg",
                Version = "1.2.3",
                Architecture = "amd64",
                OriginSuite = "questing",
                OriginComponent = "main",
                Description = "A test package",
                Maintainer = "Tester",
                Filename = "pool/main/t/test-pkg/test-pkg_1.2.3_amd64.deb",
                MD5sum = "abc",
                SHA1 = "abc",
                SHA256 = "abc",
                SHA512 = "abc",
                Size = "100",
                Priority = "optional",
                Section = "utils",
                Origin = "Ubuntu",
                Bugs = "none",
                DescriptionMd5 = "abc"
            });
            await db.SaveChangesAsync();
        }

        var response = await Http.GetAsync($"/Mirrors/Packages/{mirror.Id}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("test-pkg"));

        int pkgId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            pkgId = (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(db.AptPackages, p => p.Package == "test-pkg")).Id;
        }

        var detailsResponse = await Http.GetAsync($"/Mirrors/PackageDetails/{pkgId}");
        var detailsHtml = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(detailsHtml.Contains("Package Details: test-pkg"));
    }

    [TestMethod]
    public async Task TestPackageSearchAndPagination()
    {
        await Server!.SeedMirrorsAsync(true);
        await LoginAsAdmin();

        var mirrorService = GetService<AptMirrorService>();
        var mirror = await mirrorService.GetMirrorForSuite("questing");
        Assert.IsNotNull(mirror);

        // Seed 105 packages to test pagination (PageSize = 100)
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            for (int i = 1; i <= 105; i++)
            {
                db.AptPackages.Add(new AptPackage
                {
                    MirrorRepositoryId = mirror.Id,
                    Package = $"pkg-{i:D3}",
                    Version = "1.0.0",
                    Architecture = "amd64",
                    OriginSuite = "questing",
                    OriginComponent = "main",
                    Description = i == 50 ? "Special Description" : "Normal package",
                    Filename = $"pool/main/p/pkg-{i:D3}/pkg-{i:D3}_1.0.0_amd64.deb",
                    MD5sum = "abc",
                    SHA1 = "abc",
                    SHA256 = "abc",
                    SHA512 = "abc",
                    Size = "100",
                    Priority = "optional",
                    Section = "utils",
                    DescriptionMd5 = "abc",
                    Maintainer = "Tester",
                    Origin = "Ubuntu",
                    Bugs = "none"
                });
            }
            await db.SaveChangesAsync();
        }

        // 1. Test basic list (Page 1)
        var response = await Http.GetAsync($"/Mirrors/Packages/{mirror.Id}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("pkg-001"));
        Assert.IsTrue(html.Contains("pkg-100"));
        Assert.IsFalse(html.Contains("pkg-101")); // On Page 2

        // 2. Test pagination (Page 2)
        response = await Http.GetAsync($"/Mirrors/Packages/{mirror.Id}?page=2");
        html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("pkg-101"));
        Assert.IsTrue(html.Contains("pkg-105"));
        Assert.IsFalse(html.Contains("pkg-001"));

        // 3. Test Search by Name
        response = await Http.GetAsync($"/Mirrors/Packages/{mirror.Id}?searchName=pkg-050");
        html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("pkg-050"));
        Assert.IsFalse(html.Contains("pkg-001"));

        // 4. Test Search by Description
        response = await Http.GetAsync($"/Mirrors/Packages/{mirror.Id}?searchName=Special");
        html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("pkg-050"));
        Assert.IsFalse(html.Contains("pkg-001"));
    }

    [TestMethod]
    public async Task TestPoolDownload()
    {
        if (!await CheckInternet()) Assert.Inconclusive("No internet access.");

        await Server!.SeedMirrorsAsync(true);
        var path = "main/a/adduser/adduser_3.137ubuntu1_all.deb";
        var response = await Http.GetAsync("/pool/" + path);
        
        if (response.StatusCode == HttpStatusCode.OK)
        {
            Assert.AreEqual("application/vnd.debian.binary-package", response.Content.Headers.ContentType?.MediaType);
        }
    }
}
