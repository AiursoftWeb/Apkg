using System.Net;
using Aiursoft.Apkg.Configuration;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.FileStorage;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

[TestClass]
public class MoreApkgTests : TestBase
{
    [TestMethod]
    public async Task TestProgramExtends()
    {
        await Server!.CopyAvatarFileAsync();
        await Server!.CopyAvatarFileAsync();
    }

    [TestMethod]
    public async Task TestHomeControllerExtra()
    {
        var respHome = await Http.GetAsync("/");
        Assert.AreEqual(HttpStatusCode.OK, respHome.StatusCode);

        var resp = await Http.GetAsync("/Home/SelfHost");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    [TestMethod]
    public async Task TestApkgPackagesExtra()
    {
        await LoginAsAdmin();

        var uploadHistory = await Http.GetAsync("/ApkgPackages/UploadHistory");
        Assert.AreEqual(HttpStatusCode.OK, uploadHistory.StatusCode);

        var uploadGet = await Http.GetAsync("/ApkgPackages/Upload");
        Assert.AreEqual(HttpStatusCode.OK, uploadGet.StatusCode);

        var nfDetails = await Http.GetAsync("/ApkgPackages/Details/999999");
        Assert.AreEqual(HttpStatusCode.NotFound, nfDetails.StatusCode);

        var nfUnlist = await Http.GetAsync("/ApkgPackages/Unlist/999999");
        Assert.AreEqual(HttpStatusCode.NotFound, nfUnlist.StatusCode);

        var nfRelist = await PostForm("/ApkgPackages/Relist/999999", new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.NotFound, nfRelist.StatusCode);

        var nfDelete = await PostForm("/ApkgPackages/Delete/999999", new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.NotFound, nfDelete.StatusCode);

        var nfDeletePost = await PostForm("/ApkgPackages/DeletePackage/999999", new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.NotFound, nfDeletePost.StatusCode);
    }

    [TestMethod]
    public async Task TestUserApiKeysFullFlow()
    {
        await LoginAsAdmin();

        var indexResp = await Http.GetAsync("/UserApiKeys");
        Assert.AreEqual(HttpStatusCode.OK, indexResp.StatusCode);

        var createGetResp = await Http.GetAsync("/UserApiKeys/Create");
        Assert.AreEqual(HttpStatusCode.OK, createGetResp.StatusCode);

        var invalidKey = await PostForm("/UserApiKeys/Create", new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.OK, invalidKey.StatusCode);

        var createPostResp = await PostForm("/UserApiKeys/Create", new Dictionary<string, string>
        {
            { "Name", "Test Api Key" },
            { "ExpirationDays", "30" }
        });
        Assert.AreEqual(HttpStatusCode.Found, createPostResp.StatusCode);

        int keyId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApkgDbContext>();
            var key = await db.UserApiKeys.OrderByDescending(k => k.CreatedAt).FirstAsync();
            keyId = key.Id;
        }

        var usageResp = await Http.GetAsync($"/UserApiKeys/Usage/{keyId}");
        Assert.AreEqual(HttpStatusCode.OK, usageResp.StatusCode);

        var deleteResp = await PostForm($"/UserApiKeys/Delete/{keyId}", new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.Found, deleteResp.StatusCode);

        var notFoundUsage = await Http.GetAsync("/UserApiKeys/Usage/999999");
        Assert.AreEqual(HttpStatusCode.NotFound, notFoundUsage.StatusCode);

        var notFoundDelete = await PostForm("/UserApiKeys/Delete/999999", new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.NotFound, notFoundDelete.StatusCode);
    }

    [TestMethod]
    public async Task TestBucketsControllerFlow()
    {
        await LoginAsAdmin();

        var indexResp = await Http.GetAsync("/Buckets");
        Assert.AreEqual(HttpStatusCode.OK, indexResp.StatusCode);

        int bucketId;
        int packageId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApkgDbContext>();
            var bucket = await db.AptBuckets.FirstOrDefaultAsync();
            if (bucket == null)
            {
                bucket = new AptBucket { CreatedAt = DateTime.UtcNow };
                db.AptBuckets.Add(bucket);
                await db.SaveChangesAsync();
            }
            bucketId = bucket.Id;

            var pkg = new AptPackage
            {
                BucketId = bucketId,
                Component = "main",
                Architecture = "amd64",
                Package = "test-coverage-pkg",
                Version = "1.0.0",
                Filename = "pool/main/t/test/test_1.0.0_amd64.deb",
                SHA256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                IsVirtual = true,
                OriginSuite = "test",
                OriginComponent = "main",
                Maintainer = "Test Maintainer <test@test.com>",
                Description = "Test package",
                Section = "utils",
                Priority = "optional",
                Origin = "Ubuntu",
                Bugs = "https://bugs.example.com",
                Size = "1024",
                MD5sum = "abc",
                SHA1 = "abc",
                SHA512 = "abc"
            };
            db.AptPackages.Add(pkg);
            await db.SaveChangesAsync();
            packageId = pkg.Id;
        }

        var orders = new[]
        {
            "name_desc", "size_asc", "size_desc",
            "component_asc", "component_desc", "status_asc", "status_desc"
        };
        foreach (var order in orders)
        {
            var r = await Http.GetAsync($"/Buckets/Packages/{bucketId}?sortOrder={order}");
            Assert.AreEqual(HttpStatusCode.OK, r.StatusCode);
        }

        var page2 = await Http.GetAsync($"/Buckets/Packages/{bucketId}?page=2");
        Assert.AreEqual(HttpStatusCode.OK, page2.StatusCode);

        var notFoundResp = await Http.GetAsync("/Buckets/Packages/999999");
        Assert.AreEqual(HttpStatusCode.NotFound, notFoundResp.StatusCode);

        var mirrorsReverseDepends = await Http.GetAsync($"/Mirrors/ReverseDepends/{packageId}");
        Assert.AreEqual(HttpStatusCode.OK, mirrorsReverseDepends.StatusCode);

        var mirrorsPkgDetails = await Http.GetAsync($"/Mirrors/PackageDetails/{packageId}");
        Assert.AreEqual(HttpStatusCode.OK, mirrorsPkgDetails.StatusCode);

        var nfReverseDepends = await Http.GetAsync("/Mirrors/ReverseDepends/999999");
        Assert.AreEqual(HttpStatusCode.NotFound, nfReverseDepends.StatusCode);

        var nfPkgDetails = await Http.GetAsync("/Mirrors/PackageDetails/999999");
        Assert.AreEqual(HttpStatusCode.NotFound, nfPkgDetails.StatusCode);
    }

    [TestMethod]
    public async Task TestRepositoriesAndMirrorsFlow()
    {
        await LoginAsAdmin();

        var reposResp = await Http.GetAsync("/Repositories");
        Assert.AreEqual(HttpStatusCode.OK, reposResp.StatusCode);

        int reportId;
        int mirrorId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApkgDbContext>();
            var repo = await db.AptRepositories.FirstAsync();
            var report = new DependencyCheckReport
            {
                RepositoryId = repo.Id,
                CreatedAt = DateTime.UtcNow,
                ExpireAt = DateTime.UtcNow.AddDays(3),
                Status = "Completed",
                TotalPackages = 10,
                ProblematicPackages = 0,
                DetailsJson = "[]"
            };
            db.DependencyCheckReports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;

            var mirror = await db.AptMirrors.FirstOrDefaultAsync();
            if (mirror == null)
            {
                mirror = new AptMirror
                {
                    Distro = "debian",
                    BaseUrl = "https://deb.debian.org/debian",
                    Suite = "bookworm",
                    Components = "main",
                    Architecture = "amd64"
                };
                db.AptMirrors.Add(mirror);
                await db.SaveChangesAsync();
            }
            mirrorId = mirror.Id;
        }

        var checkReportResp = await Http.GetAsync($"/Repositories/CheckReport/{reportId}");
        Assert.AreEqual(HttpStatusCode.OK, checkReportResp.StatusCode);

        var mirrorsIndex = await Http.GetAsync("/Mirrors");
        Assert.AreEqual(HttpStatusCode.OK, mirrorsIndex.StatusCode);

        var mirrorsCreateGet = await Http.GetAsync("/Mirrors/Create");
        Assert.AreEqual(HttpStatusCode.OK, mirrorsCreateGet.StatusCode);

        var invalidCreateMirror = await PostForm("/Mirrors/Create", new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.OK, invalidCreateMirror.StatusCode);

        var createMirrorResp = await PostForm("/Mirrors/Create", new Dictionary<string, string>
        {
            { "Distro", "ubuntu" },
            { "BaseUrl", "http://archive.ubuntu.com/ubuntu" },
            { "Suite", "noble-test" },
            { "Components", "main" },
            { "Architecture", "amd64" }
        });
        Assert.AreEqual(HttpStatusCode.Found, createMirrorResp.StatusCode);

        var mirrorsEditGet = await Http.GetAsync($"/Mirrors/Edit/{mirrorId}");
        Assert.AreEqual(HttpStatusCode.OK, mirrorsEditGet.StatusCode);

        var notFoundEditGet = await Http.GetAsync("/Mirrors/Edit/999999");
        Assert.AreEqual(HttpStatusCode.NotFound, notFoundEditGet.StatusCode);

        var invalidEditMirror = await PostForm($"/Mirrors/Edit/{mirrorId}", new Dictionary<string, string>
        {
            { "Id", mirrorId.ToString() }
        });
        Assert.AreEqual(HttpStatusCode.OK, invalidEditMirror.StatusCode);

        var notFoundEditPost = await PostForm("/Mirrors/Edit/999999", new Dictionary<string, string>
        {
            { "Id", "999999" },
            { "Distro", "debian" },
            { "BaseUrl", "https://deb.debian.org/debian" },
            { "Suite", "bookworm" },
            { "Components", "main" },
            { "Architecture", "amd64" }
        });
        Assert.AreEqual(HttpStatusCode.NotFound, notFoundEditPost.StatusCode);

        var editMirrorResp = await PostForm($"/Mirrors/Edit/{mirrorId}", new Dictionary<string, string>
        {
            { "Id", mirrorId.ToString() },
            { "Distro", "debian" },
            { "BaseUrl", "https://deb.debian.org/debian" },
            { "Suite", "bookworm" },
            { "Components", "main" },
            { "Architecture", "amd64" }
        });
        Assert.AreEqual(HttpStatusCode.Found, editMirrorResp.StatusCode);

        var mirrorsPackages = await Http.GetAsync($"/Mirrors/Packages/{mirrorId}");
        Assert.AreEqual(HttpStatusCode.OK, mirrorsPackages.StatusCode);

        var mirrorsPackagesSearch = await Http.GetAsync($"/Mirrors/Packages/{mirrorId}?searchName=test");
        Assert.AreEqual(HttpStatusCode.OK, mirrorsPackagesSearch.StatusCode);

        var mirrorsPackagesSort = await Http.GetAsync($"/Mirrors/Packages/{mirrorId}?sortOrder=name_desc");
        Assert.AreEqual(HttpStatusCode.OK, mirrorsPackagesSort.StatusCode);

        var mirrorsPackagesP2 = await Http.GetAsync($"/Mirrors/Packages/{mirrorId}?page=2");
        Assert.AreEqual(HttpStatusCode.OK, mirrorsPackagesP2.StatusCode);

        var mirrorsPackagesNf = await Http.GetAsync("/Mirrors/Packages/999999");
        Assert.AreEqual(HttpStatusCode.NotFound, mirrorsPackagesNf.StatusCode);

        var mirrorsVerify = await Http.GetAsync($"/Mirrors/VerifyDetails/{mirrorId}");
        Assert.AreEqual(HttpStatusCode.OK, mirrorsVerify.StatusCode);

        var notFoundVerify = await Http.GetAsync("/Mirrors/VerifyDetails/999999");
        Assert.AreEqual(HttpStatusCode.NotFound, notFoundVerify.StatusCode);

        var deleteNonExistent = await PostForm("/Mirrors/Delete/999999", new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.Found, deleteNonExistent.StatusCode);
    }

    [TestMethod]
    public async Task TestViewComponentsAndGlobalSettings()
    {
        using var scope = Server!.Services.CreateScope();
        var globalSettings = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
        var storage = scope.ServiceProvider.GetRequiredService<StorageService>();

        var navbar = new Views.Shared.Components.MarketingNavbar.MarketingNavbar(globalSettings, storage)
        {
            ViewComponentContext = new ViewComponentContext
            {
                ViewContext = new ViewContext { HttpContext = new DefaultHttpContext() }
            }
        };
        var navRes = await navbar.InvokeAsync();
        Assert.IsNotNull(navRes);

        var footer = new Views.Shared.Components.MarketingFooter.MarketingFooter(globalSettings, storage)
        {
            ViewComponentContext = new ViewComponentContext
            {
                ViewContext = new ViewContext { HttpContext = new DefaultHttpContext() }
            }
        };
        var footRes = await footer.InvokeAsync();
        Assert.IsNotNull(footRes);

        await globalSettings.UpdateSettingAsync(SettingsMap.AllowUserAdjustNickname, "False");
        Assert.IsFalse(await globalSettings.GetBoolSettingAsync(SettingsMap.AllowUserAdjustNickname));
        await globalSettings.UpdateSettingAsync(SettingsMap.AllowUserAdjustNickname, "True");
        Assert.IsTrue(await globalSettings.GetBoolSettingAsync(SettingsMap.AllowUserAdjustNickname));
    }
}
