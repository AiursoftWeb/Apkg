using System.Net;
using Aiursoft.Apkg.Entities;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

[TestClass]
public class LocalPackagesControllerTests : TestBase
{
    private ApkgDbContext _db = null!;
    private AptRepository _repo = null!;
    private string _adminUserId = null!;
    private HttpClient _anonHttp = null!;

    [TestInitialize]
    public override async Task SetupTestContext()
    {
        await base.SetupTestContext();
        await LoginAsAdmin();

        _db = GetService<ApkgDbContext>();
        _repo = _db.AptRepositories.First();

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

    private LocalPackage AddLocalPackage(
        string name = "testpkg",
        string version = "1.0.0",
        string arch = "amd64",
        bool isEnabled = true)
    {
        var lp = new LocalPackage
        {
            UploadedByUserId = _adminUserId,
            RepositoryId = _repo.Id,
            Component = "main",
            Package = name,
            Version = version,
            Architecture = arch,
            Maintainer = "Test <test@example.com>",
            Filename = $"pool/main/{name[0]}/{name}/{name}_{version}_{arch}.deb",
            Size = "1024",
            SHA256 = Guid.NewGuid().ToString("N").PadRight(64, '0')[..64],
            IsEnabled = isEnabled
        };
        _db.LocalPackages.Add(lp);
        _db.SaveChanges();
        return lp;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Index page
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Index_ReturnsOk_WhenLoggedIn()
    {
        var response = await Http.GetAsync("/LocalPackages/Index");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Index_RedirectsToLogin_WhenNotAuthenticated()
    {
        var response = await _anonHttp.GetAsync("/LocalPackages/Index");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.IsTrue(response.Headers.Location?.ToString().Contains("Login") ?? false, "Expected redirect to Login.");
    }

    [TestMethod]
    public async Task Index_ShowsSeededPackage()
    {
        AddLocalPackage("mypackage", "2.0.0");
        var response = await Http.GetAsync("/LocalPackages/Index");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("mypackage"), "Expected package name to appear in index.");
        Assert.IsTrue(html.Contains("2.0.0"), "Expected package version to appear in index.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Upload page
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Upload_ReturnsOk_WhenLoggedIn()
    {
        var response = await Http.GetAsync("/LocalPackages/Upload");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Upload_RedirectsToLogin_WhenNotAuthenticated()
    {
        var response = await _anonHttp.GetAsync("/LocalPackages/Upload");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.IsTrue(response.Headers.Location?.ToString().Contains("Login") ?? false, "Expected redirect to Login.");
    }

    [TestMethod]
    public async Task Upload_ShowsRepositoryDropdown()
    {
        var response = await Http.GetAsync("/LocalPackages/Upload");
        var html = await response.Content.ReadAsStringAsync();
        // The repo Suite should appear in dropdown
        Assert.IsTrue(html.Contains(_repo.Suite), "Expected repository suite in dropdown.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Toggle action
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Toggle_DisablesEnabledPackage()
    {
        var lp = AddLocalPackage("togglepkg", isEnabled: true);
        var response = await PostForm($"/LocalPackages/Toggle?id={lp.Id}", new Dictionary<string, string>(), tokenUrl: "/LocalPackages/Index");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);

        _db.Entry(lp).Reload();
        Assert.IsFalse(lp.IsEnabled, "Package should be disabled after toggle.");
    }

    [TestMethod]
    public async Task Toggle_EnablesDisabledPackage()
    {
        var lp = AddLocalPackage("enablepkg", isEnabled: false);
        var response = await PostForm($"/LocalPackages/Toggle?id={lp.Id}", new Dictionary<string, string>(), tokenUrl: "/LocalPackages/Index");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);

        _db.Entry(lp).Reload();
        Assert.IsTrue(lp.IsEnabled, "Package should be enabled after toggle.");
    }

    [TestMethod]
    public async Task Toggle_ReturnsNotFound_ForMissingId()
    {
        var response = await PostForm("/LocalPackages/Toggle?id=999999999", new Dictionary<string, string>(), tokenUrl: "/LocalPackages/Index");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Delete action
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Delete_RemovesPackage()
    {
        var lp = AddLocalPackage("deletepkg");
        var response = await PostForm($"/LocalPackages/Delete?id={lp.Id}", new Dictionary<string, string>(), tokenUrl: "/LocalPackages/Index");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);

        var stillExists = _db.LocalPackages.Any(x => x.Id == lp.Id);
        Assert.IsFalse(stillExists, "Package should be removed from database after delete.");
    }

    [TestMethod]
    public async Task Delete_ReturnsNotFound_ForMissingId()
    {
        var response = await PostForm("/LocalPackages/Delete?id=999999999", new Dictionary<string, string>(), tokenUrl: "/LocalPackages/Index");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
