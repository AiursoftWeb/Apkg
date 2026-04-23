using System.Net;
using Aiursoft.Apkg.Entities;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

[TestClass]
public class MirrorsPackageDetailsTests : TestBase
{
    private ApkgDbContext _db = null!;
    private AptMirror _mirror = null!;
    private AptBucket _bucket = null!;

    [TestInitialize]
    public override async Task CreateServer()
    {
        await base.CreateServer();
        await LoginAsAdmin();

        _db = GetService<ApkgDbContext>();
        _mirror = _db.AptMirrors.First();

        _bucket = new AptBucket
        {
            CreatedAt = DateTime.UtcNow,
            InReleaseContent = "TEST-INRELEASE",
            ReleaseContent = "TEST-RELEASE"
        };
        _db.AptBuckets.Add(_bucket);
        _db.SaveChanges();

        _mirror.PrimaryBucketId = _bucket.Id;
        _db.SaveChanges();
    }

    private AptPackage AddPackage(
        string name,
        string version = "1.0",
        string? depends = null,
        string? recommends = null,
        string? suggests = null,
        string? conflicts = null,
        string? breaks = null,
        string? replaces = null,
        string? provides = null,
        bool isVirtual = true)
    {
        var pkg = new AptPackage
        {
            BucketId = _bucket.Id,
            Component = "main",
            Architecture = "amd64",
            Package = name,
            Version = version,
            Filename = $"pool/main/{name[0]}/{name}/{name}_{version}_amd64.deb",
            SHA256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            IsVirtual = isVirtual,
            RemoteUrl = isVirtual ? $"http://example.com/{name}.deb" : null,
            Depends = depends,
            Recommends = recommends,
            Suggests = suggests,
            Conflicts = conflicts,
            Breaks = breaks,
            Replaces = replaces,
            Provides = provides,
            OriginSuite = "questing",
            OriginComponent = "main",
            Maintainer = "Mirror Maintainer <maint@example.com>",
            Description = $"Test mirror package {name}",
            DescriptionMd5 = "abc",
            Section = "utils",
            Priority = "optional",
            Origin = "Ubuntu",
            Bugs = "https://bugs.example.com",
            Size = "2048",
            InstalledSize = "8192",
            MD5sum = "abc",
            SHA1 = "abc",
            SHA512 = "abc"
        };
        _db.AptPackages.Add(pkg);
        _db.SaveChanges();
        return pkg;
    }

    // ──────────────────────────────────────────────────────────────────────
    // PackageDetails page — basic
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task MirrorPackageDetails_ReturnsOk()
    {
        var pkg = AddPackage("curl");
        var response = await Http.GetAsync($"/Mirrors/PackageDetails/{pkg.Id}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task MirrorPackageDetails_ReturnsNotFound_ForInvalidId()
    {
        var response = await Http.GetAsync("/Mirrors/PackageDetails/999999999");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task MirrorPackageDetails_ShowsVersionAndArchitecture()
    {
        var pkg = AddPackage("wget", "2.1.0");
        var response = await Http.GetAsync($"/Mirrors/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("2.1.0"), "Version should appear on details page.");
        Assert.IsTrue(html.Contains("amd64"), "Architecture should appear.");
    }

    [TestMethod]
    public async Task MirrorPackageDetails_ShowsFullMetadata()
    {
        var pkg = AddPackage("bash");
        var response = await Http.GetAsync($"/Mirrors/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("Mirror Maintainer"), "Maintainer should appear.");
        Assert.IsTrue(html.Contains("Ubuntu"), "Origin should appear.");
        Assert.IsTrue(html.Contains("utils"), "Section should appear.");
        Assert.IsTrue(html.Contains("optional"), "Priority should appear.");
        Assert.IsTrue(html.Contains("8192"), "InstalledSize should appear.");
        Assert.IsTrue(html.Contains("bugs.example.com"), "Bugs URL should appear.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Relations — clickable deps
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task MirrorPackageDetails_Dep_IsClickable_WhenPkgExistsInSameBucket()
    {
        var libc = AddPackage("libc6", "2.36");
        var pkg = AddPackage("curl-mirror", depends: "libc6 (>= 2.17)");

        var response = await Http.GetAsync($"/Mirrors/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        var expectedHref = $"/Mirrors/PackageDetails/{libc.Id}";
        Assert.IsTrue(html.Contains(expectedHref),
            "Dep badge should be a hyperlink pointing to the dependency's detail page.");
    }

    [TestMethod]
    public async Task MirrorPackageDetails_Dep_IsNotClickable_WhenPkgMissingFromBucket()
    {
        var pkg = AddPackage("orphan-mirror-pkg", depends: "nonexistent-mirror-lib");
        var response = await Http.GetAsync($"/Mirrors/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("nonexistent-mirror-lib"), "Unresolvable dep should still be shown as badge.");
        // When not resolved, it should be a <span>, not an <a> pointing to PackageDetails
        Assert.IsFalse(html.Contains($"/Mirrors/PackageDetails/\"{pkg.Id}"),
            "No link should be generated for the current package itself.");
    }

    [TestMethod]
    public async Task MirrorPackageDetails_ShowsRecommends()
    {
        var pkg = AddPackage("linux-generic", recommends: "linux-firmware, amd64-microcode");
        var response = await Http.GetAsync($"/Mirrors/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Recommends"), "Recommends section should appear.");
        Assert.IsTrue(html.Contains("linux-firmware"), "Recommended package should appear.");
    }

    [TestMethod]
    public async Task MirrorPackageDetails_ShowsSuggests()
    {
        var pkg = AddPackage("vim-mirror", suggests: "vim-doc");
        var response = await Http.GetAsync($"/Mirrors/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Suggests"), "Suggests section should appear.");
        Assert.IsTrue(html.Contains("vim-doc"), "Suggested package should appear.");
    }

    [TestMethod]
    public async Task MirrorPackageDetails_ShowsConflicts_WithDangerBadge()
    {
        var pkg = AddPackage("vim-tiny-mirror", conflicts: "vim-full");
        var response = await Http.GetAsync($"/Mirrors/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Conflicts"), "Conflicts section should appear.");
        Assert.IsTrue(html.Contains("badge-subtle-danger"), "Conflicts badge should use danger style.");
    }

    [TestMethod]
    public async Task MirrorPackageDetails_NoRelations_ShowsFallbackText()
    {
        var pkg = AddPackage("nodeps-mirror-pkg");
        var response = await Http.GetAsync($"/Mirrors/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("No relations"), "Should show fallback text when no relations.");
    }

    [TestMethod]
    public async Task MirrorPackageDetails_AltDeps_SeparatedByPipe_AreRendered()
    {
        var pkg = AddPackage("policy-mirror", depends: "pkexec | policykit-1");
        var response = await Http.GetAsync($"/Mirrors/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("pkexec"), "First alternative should appear.");
        Assert.IsTrue(html.Contains("policykit-1"), "Second alternative should appear.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Download button
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task MirrorPackageDetails_DownloadButton_Present()
    {
        var pkg = AddPackage("htop-mirror", "3.2.1", isVirtual: true);
        var response = await Http.GetAsync($"/Mirrors/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains($"/{pkg.Filename}"),
            $"Download button should link to '/{pkg.Filename}'.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // ReverseDepends JSON endpoint
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task MirrorReverseDepends_ReturnsOk_ForValidPackage()
    {
        var pkg = AddPackage("libz-mirror");
        var response = await Http.GetAsync($"/Mirrors/ReverseDepends/{pkg.Id}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task MirrorReverseDepends_ReturnsNotFound_ForInvalidId()
    {
        var response = await Http.GetAsync("/Mirrors/ReverseDepends/999999999");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task MirrorReverseDepends_ReturnsJson()
    {
        var pkg = AddPackage("libssl-mirror");
        var response = await Http.GetAsync($"/Mirrors/ReverseDepends/{pkg.Id}");
        var contentType = response.Content.Headers.ContentType?.MediaType;
        Assert.IsTrue(contentType?.Contains("application/json") ?? false, "Should return JSON.");
    }

    [TestMethod]
    public async Task MirrorReverseDepends_ReturnsPackage_ThatDependsOnIt()
    {
        var lib = AddPackage("libfoo-mirror");
        AddPackage("myapp-mirror", depends: "libfoo-mirror (>= 1.0)");

        var response = await Http.GetAsync($"/Mirrors/ReverseDepends/{lib.Id}");
        var json = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(json.Contains("myapp-mirror"), "Consumer package should appear in reverse deps.");
        Assert.IsTrue(json.Contains("Depends"), "RelType 'Depends' should be present.");
    }

    [TestMethod]
    public async Task MirrorReverseDepends_ReturnsEmpty_WhenNobodyDependsOnIt()
    {
        var lonely = AddPackage("lonely-mirror-lib");
        var response = await Http.GetAsync($"/Mirrors/ReverseDepends/{lonely.Id}");
        var json = await response.Content.ReadAsStringAsync();

        Assert.AreEqual("[]", json.Trim(), "Should return empty array when no reverse deps.");
    }

    [TestMethod]
    public async Task MirrorReverseDepends_DoesNotIncludeSelf()
    {
        var pkg = AddPackage("self-dep-mirror", depends: "self-dep-mirror");
        var response = await Http.GetAsync($"/Mirrors/ReverseDepends/{pkg.Id}");
        var json = await response.Content.ReadAsStringAsync();

        Assert.AreEqual("[]", json.Trim(), "Self-dependency should not appear in reverse deps list.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Async reverse deps section present in HTML
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task MirrorPackageDetails_HasReverseDepsSection_WithAjaxLoadingDiv()
    {
        var pkg = AddPackage("libajax-mirror");
        var response = await Http.GetAsync($"/Mirrors/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("rev-deps-loading"), "Loading spinner div should be present.");
        Assert.IsTrue(html.Contains("rev-deps-content"), "Content div for AJAX result should be present.");
        Assert.IsTrue(html.Contains("ReverseDepends"), "AJAX fetch URL should reference the endpoint.");
    }
}
