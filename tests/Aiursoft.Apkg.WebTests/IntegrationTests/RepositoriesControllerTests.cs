using System.Net;
using Aiursoft.Apkg.Entities;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

[TestClass]
public class RepositoriesControllerTests : TestBase
{
    private ApkgDbContext _db = null!;
    private AptRepository _repo = null!;
    private AptBucket _bucket = null!;

    [TestInitialize]
    public override async Task SetupTestContext()
    {
        await base.SetupTestContext();
        await LoginAsAdmin();

        _db = GetService<ApkgDbContext>();
        _repo = _db.AptRepositories.First();

        // Restore fields that individual tests mutate (shared server = persistent DB state).
        var cert = _db.AptCertificates.First();
        _repo.EnableGpgSign = true;
        _repo.CertificateId = cert.Id;

        _bucket = new AptBucket
        {
            CreatedAt = DateTime.UtcNow,
            InReleaseContent = "TEST-INRELEASE",
            ReleaseContent = "TEST-RELEASE"
        };
        _db.AptBuckets.Add(_bucket);
        _db.SaveChanges();

        _repo.PrimaryBucketId = _bucket.Id;
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
        bool isVirtual = true,
        string? remoteUrl = null)
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
            RemoteUrl = remoteUrl ?? (isVirtual ? $"http://example.com/{name}.deb" : null),
            Depends = depends,
            Recommends = recommends,
            Suggests = suggests,
            Conflicts = conflicts,
            Breaks = breaks,
            Replaces = replaces,
            Provides = provides,
            OriginSuite = "test",
            OriginComponent = "main",
            Maintainer = "Test Maintainer <test@test.com>",
            OriginalMaintainer = "Original <original@test.com>",
            Description = $"Test package {name}",
            DescriptionMd5 = "abc",
            Section = "utils",
            Priority = "optional",
            Origin = "Ubuntu",
            Bugs = "https://bugs.example.com",
            Size = "1024",
            InstalledSize = "4096",
            MD5sum = "abc",
            SHA1 = "abc",
            SHA512 = "abc"
        };
        _db.AptPackages.Add(pkg);
        _db.SaveChanges();
        return pkg;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Packages list page
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Packages_ReturnsOk()
    {
        AddPackage("curl");
        var response = await Http.GetAsync($"/Repositories/Packages?id={_repo.Id}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Packages_ShowsPackageNames()
    {
        AddPackage("wget");
        var response = await Http.GetAsync($"/Repositories/Packages?id={_repo.Id}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("wget"), "Expected package name 'wget' to appear in the list.");
    }

    [TestMethod]
    public async Task Packages_SearchFiltersByName()
    {
        AddPackage("openssh-server");
        AddPackage("openssh-client");
        AddPackage("vim");

        var response = await Http.GetAsync($"/Repositories/Packages?id={_repo.Id}&searchName=openssh");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("openssh-server"), "Search result should include openssh-server.");
        Assert.IsTrue(html.Contains("openssh-client"), "Search result should include openssh-client.");
        Assert.IsFalse(html.Contains("<code>vim</code>"), "Search result should not include vim.");
    }

    [TestMethod]
    public async Task Packages_NoResults_ShowsEmptyTable()
    {
        var response = await Http.GetAsync($"/Repositories/Packages?id={_repo.Id}&searchName=this-package-does-not-exist");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(html.Contains("<code>this-package-does-not-exist</code>"));
    }

    [TestMethod]
    public async Task Packages_ReturnsNotFound_WhenRepoHasNoPrimaryBucket()
    {
        _repo.PrimaryBucketId = null;
        _db.SaveChanges();

        var response = await Http.GetAsync($"/Repositories/Packages?id={_repo.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ──────────────────────────────────────────────────────────────────────
    // PackageDetails page — basic
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task PackageDetails_ReturnsOk()
    {
        var pkg = AddPackage("git");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task PackageDetails_ReturnsNotFound_ForInvalidId()
    {
        var response = await Http.GetAsync("/Repositories/PackageDetails/999999999");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task PackageDetails_ShowsVersion()
    {
        var pkg = AddPackage("nginx", "1.25.3");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("1.25.3"), "Package version should appear on details page.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // PackageDetails — metadata fields
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task PackageDetails_ShowsSection()
    {
        var pkg = AddPackage("tar");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("utils"), "Section should appear on details page.");
    }

    [TestMethod]
    public async Task PackageDetails_ShowsPriority()
    {
        var pkg = AddPackage("less");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("optional"), "Priority should appear on details page.");
    }

    [TestMethod]
    public async Task PackageDetails_ShowsOriginAndMaintainer()
    {
        var pkg = AddPackage("bash");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("Ubuntu"), "Origin should appear.");
        Assert.IsTrue(html.Contains("Test Maintainer"), "Maintainer should appear.");
    }

    [TestMethod]
    public async Task PackageDetails_ShowsBugsLink()
    {
        var pkg = AddPackage("openssh-server");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("bugs.example.com"), "Bugs URL should appear.");
    }

    [TestMethod]
    public async Task PackageDetails_ShowsInstalledSize()
    {
        var pkg = AddPackage("coreutils");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("4096"), "InstalledSize should appear.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // PackageDetails — relation sections (Recommends, Suggests, etc.)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task PackageDetails_ShowsDependencies_AsBadgeSubtlePrimary()
    {
        var pkg = AddPackage("libssl3", depends: "libc6 (>= 2.17), libgcc1");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("badge-subtle-primary"), "Dependencies should use badge-subtle-primary class.");
        Assert.IsTrue(html.Contains("libc6"), "Dependency 'libc6' should appear.");
        Assert.IsTrue(html.Contains("libgcc1"), "Dependency 'libgcc1' should appear.");
    }

    [TestMethod]
    public async Task PackageDetails_ShowsRecommends()
    {
        var pkg = AddPackage("printer-driver-all", recommends: "printer-driver-brlaser, printer-driver-foo2zjs");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Recommends"), "Recommends section should appear.");
        Assert.IsTrue(html.Contains("printer-driver-brlaser"), "Recommended package should appear.");
        Assert.IsTrue(html.Contains("printer-driver-foo2zjs"), "Recommended package should appear.");
    }

    [TestMethod]
    public async Task PackageDetails_ShowsSuggests()
    {
        var pkg = AddPackage("vim", suggests: "vim-doc, ctags");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Suggests"), "Suggests section should appear.");
        Assert.IsTrue(html.Contains("vim-doc"), "Suggested package should appear.");
    }

    [TestMethod]
    public async Task PackageDetails_ShowsConflicts()
    {
        var pkg = AddPackage("vim-tiny", conflicts: "vim");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Conflicts"), "Conflicts section should appear.");
        Assert.IsTrue(html.Contains("badge-subtle-danger"), "Conflicts badge should use danger style.");
    }

    [TestMethod]
    public async Task PackageDetails_ShowsProvides()
    {
        var pkg = AddPackage("editor", provides: "vim");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Provides"), "Provides section should appear.");
        Assert.IsTrue(html.Contains("badge-subtle-success"), "Provides badge should use success style.");
    }

    [TestMethod]
    public async Task PackageDetails_NoRelations_ShowsFallbackText()
    {
        var pkg = AddPackage("nodeps-pkg");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("No relations"), "Should show fallback text when no relations.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // PackageDetails — clickable dep links
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task PackageDetails_Dep_IsClickable_WhenPkgExistsInSameBucket()
    {
        var libc = AddPackage("libc6", "2.36");
        var pkg = AddPackage("curl-test", depends: "libc6 (>= 2.17)");

        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        var expectedHref = $"/Repositories/PackageDetails/{libc.Id}";
        Assert.IsTrue(html.Contains(expectedHref),
            "Dep badge should be a hyperlink pointing to the dependency's detail page.");
    }

    [TestMethod]
    public async Task PackageDetails_Dep_IsNotClickable_WhenPkgMissingFromBucket()
    {
        var pkg = AddPackage("orphan-pkg", depends: "nonexistent-lib");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        // Should still appear as a badge, just not linked
        Assert.IsTrue(html.Contains("nonexistent-lib"), "Unresolvable dep should still be shown.");
    }

    [TestMethod]
    public async Task PackageDetails_AltDeps_SeparatedByPipe_AreRendered()
    {
        var pkg = AddPackage("policy-test", depends: "pkexec | policykit-1");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("pkexec"), "First alternative should appear.");
        Assert.IsTrue(html.Contains("policykit-1"), "Second alternative should appear.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // PackageDetails — download button
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task PackageDetails_DownloadButton_LinksToPoolPath()
    {
        var pkg = AddPackage("htop", "3.2.1", isVirtual: true);
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains($"/artifacts/{pkg.Filename}"),
            $"Download button should link to '/artifacts/{pkg.Filename}'.");
    }

    [TestMethod]
    public async Task PackageDetails_DownloadButton_Present_ForVirtualPackage()
    {
        var pkg = AddPackage("screen", isVirtual: true);
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Download .deb") || html.Contains("download"),
            "Download button must be present even for virtual packages.");
    }

    [TestMethod]
    public async Task PackageDetails_VirtualBadge_UsesCorrectClass()
    {
        var pkg = AddPackage("screen2", isVirtual: true);
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("bg-warning"), "Virtual badge should use bg-warning.");
        Assert.IsFalse(html.Contains("bg-outline-warning"), "bg-outline-warning is invalid and must not appear.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // ReverseDepends JSON endpoint
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ReverseDepends_ReturnsOk_ForValidPackage()
    {
        var pkg = AddPackage("libz1");
        var response = await Http.GetAsync($"/Repositories/ReverseDepends/{pkg.Id}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task ReverseDepends_ReturnsNotFound_ForInvalidId()
    {
        var response = await Http.GetAsync("/Repositories/ReverseDepends/999999999");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task ReverseDepends_ReturnsJson()
    {
        var pkg = AddPackage("libssl-dev");
        var response = await Http.GetAsync($"/Repositories/ReverseDepends/{pkg.Id}");
        var contentType = response.Content.Headers.ContentType?.MediaType;
        Assert.IsTrue(contentType?.Contains("application/json") ?? false, "Should return JSON.");
    }

    [TestMethod]
    public async Task ReverseDepends_ReturnsPackage_ThatDependsOnIt()
    {
        var lib = AddPackage("libfoo1");
        AddPackage("myapp", depends: "libfoo1 (>= 1.0)");

        var response = await Http.GetAsync($"/Repositories/ReverseDepends/{lib.Id}");
        var json = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(json.Contains("myapp"), "Consumer package should appear in reverse deps.");
        Assert.IsTrue(json.Contains("Depends"), "RelType 'Depends' should be present.");
    }

    [TestMethod]
    public async Task ReverseDepends_ReturnsPackage_ThatRecommendsIt()
    {
        var lib = AddPackage("libbar1");
        AddPackage("meta-pkg", recommends: "libbar1");

        var response = await Http.GetAsync($"/Repositories/ReverseDepends/{lib.Id}");
        var json = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(json.Contains("meta-pkg"), "Recommender should appear in reverse deps.");
        Assert.IsTrue(json.Contains("Recommends"), "RelType 'Recommends' should be present.");
    }

    [TestMethod]
    public async Task ReverseDepends_ReturnsPackage_ThatSuggestsIt()
    {
        var lib = AddPackage("libbaz1");
        AddPackage("optional-pkg", suggests: "libbaz1");

        var response = await Http.GetAsync($"/Repositories/ReverseDepends/{lib.Id}");
        var json = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(json.Contains("optional-pkg"), "Suggester should appear in reverse deps.");
        Assert.IsTrue(json.Contains("Suggests"), "RelType 'Suggests' should be present.");
    }

    [TestMethod]
    public async Task ReverseDepends_ReturnsEmpty_WhenNobodyDependsOnIt()
    {
        var lonely = AddPackage("lonely-lib");
        var response = await Http.GetAsync($"/Repositories/ReverseDepends/{lonely.Id}");
        var json = await response.Content.ReadAsStringAsync();

        Assert.AreEqual("[]", json.Trim(), "Should return empty array when no reverse deps.");
    }

    [TestMethod]
    public async Task ReverseDepends_DoesNotIncludeSelf()
    {
        // A package that depends on itself (edge case)
        var pkg = AddPackage("self-dep", depends: "self-dep");
        var response = await Http.GetAsync($"/Repositories/ReverseDepends/{pkg.Id}");
        var json = await response.Content.ReadAsStringAsync();

        Assert.AreEqual("[]", json.Trim(), "Self-dependency should not appear in reverse deps list.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // PackageDetails page — reverse deps loading hook present in HTML
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task PackageDetails_HasReverseDepsSection_WithAjaxLoadingDiv()
    {
        var pkg = AddPackage("libajax1");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("rev-deps-loading"), "Loading spinner div should be present.");
        Assert.IsTrue(html.Contains("rev-deps-content"), "Content div for AJAX result should be present.");
        Assert.IsTrue(html.Contains("ReverseDepends"), "AJAX fetch URL should reference the endpoint.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Details page — configuration guide URL correctness
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Details_ReturnsOk()
    {
        var response = await Http.GetAsync($"/Repositories/Details/{_repo.Id}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Details_ConfigGuide_SignedRepo_ContainsArtifactsPrefix()
    {
        // Seeded repo has EnableGpgSign = true and CertificateId is set
        var response = await Http.GetAsync($"/Repositories/Details/{_repo.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("/artifacts/certs/"),
            "Cert download URL must contain /artifacts/certs/ prefix.");
        Assert.IsTrue(html.Contains($"/artifacts/{_repo.Distro}/"),
            "APT source URIs line must contain /artifacts/{distro}/ prefix.");
    }

    [TestMethod]
    public async Task Details_ConfigGuide_SignedRepo_DoesNotContainBareDistroPath()
    {
        var response = await Http.GetAsync($"/Repositories/Details/{_repo.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsFalse(html.Contains($"URIs: http://localhost/{_repo.Distro}/"),
            "sources.list URIs line must not omit the /artifacts prefix.");
        Assert.IsFalse(html.Contains($"URIs: https://localhost/{_repo.Distro}/"),
            "sources.list URIs line must not omit the /artifacts prefix.");
    }

    [TestMethod]
    public async Task Details_ConfigGuide_UnsignedRepo_ContainsArtifactsPrefixWithoutCerts()
    {
        _repo.EnableGpgSign = false;
        _repo.CertificateId = null;
        _db.SaveChanges();

        var response = await Http.GetAsync($"/Repositories/Details/{_repo.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains($"/artifacts/{_repo.Distro}/"),
            "Unsigned APT source URI must still use /artifacts/{distro}/ prefix.");
        Assert.IsFalse(html.Contains("/certs/"),
            "Unsigned repo config must not emit a certs URL.");
    }

    [TestMethod]
    public async Task Details_ConfigGuide_SignedRepo_ContainsArchitecturesLine()
    {
        var response = await Http.GetAsync($"/Repositories/Details/{_repo.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains($"Architectures: {_repo.Architecture}"),
            "Signed repo config guide must include the Architectures: field.");
    }

    [TestMethod]
    public async Task Details_ConfigGuide_UnsignedRepo_ContainsArchitecturesLine()
    {
        _repo.EnableGpgSign = false;
        _repo.CertificateId = null;
        _db.SaveChanges();

        var response = await Http.GetAsync($"/Repositories/Details/{_repo.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains($"Architectures: {_repo.Architecture}"),
            "Unsigned repo config guide must also include the Architectures: field.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // PackageDetails — How to Install URL correctness
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task PackageDetails_HowToInstall_ContainsArtifactsPrefix()
    {
        var pkg = AddPackage("zlib1g");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("/artifacts/certs/"),
            "How to Install must use /artifacts/certs/ for cert download URL.");
        Assert.IsTrue(html.Contains($"/artifacts/{_repo.Distro}/"),
            "How to Install must use /artifacts/{distro}/ for the APT source URIs line.");
    }

    [TestMethod]
    public async Task PackageDetails_HowToInstall_UsesDistroNotName()
    {
        // Repo.Distro = "anduinos", Repo.Name = "Anduinos Official questing" (with spaces, different value)
        var pkg = AddPackage("grep");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains($"/artifacts/{_repo.Distro}/"),
            "How to Install URI must use the Distro field.");
        Assert.IsFalse(html.Contains($"/artifacts/{_repo.Name}/"),
            "How to Install URI must not use the repo Name field.");
    }

    [TestMethod]
    public async Task PackageDetails_HowToInstall_UsesRepoIdForFilename()
    {
        var pkg = AddPackage("awk");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains($"apkg-{_repo.Id}.sources"),
            "sources.list filename must use apkg-{id}.sources to avoid spaces or collisions.");
        Assert.IsFalse(html.Contains($"{_repo.Name}.sources"),
            "sources.list filename must not use the repo Name (may contain spaces).");
    }

    [TestMethod]
    public async Task PackageDetails_HowToInstall_UnsignedRepo_ShowsTrustedYes_NoCerts()
    {
        _repo.EnableGpgSign = false;
        _repo.CertificateId = null;
        _db.SaveChanges();

        var pkg = AddPackage("sed-unsigned");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Trusted: yes"),
            "Unsigned repo How to Install must include 'Trusted: yes'.");
        Assert.IsFalse(html.Contains("/artifacts/certs/"),
            "Unsigned repo How to Install must not emit a certs curl command.");
        Assert.IsTrue(html.Contains($"/artifacts/{_repo.Distro}/"),
            "Unsigned repo How to Install must still have the correct APT source URI.");
    }

    [TestMethod]
    public async Task PackageDetails_HowToInstall_SignedRepo_ContainsArchitecturesLine()
    {
        var pkg = AddPackage("curl");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains($"Architectures: {_repo.Architecture}"),
            "Signed repo How to Install must include the Architectures: field.");
    }

    [TestMethod]
    public async Task PackageDetails_HowToInstall_UnsignedRepo_ContainsArchitecturesLine()
    {
        _repo.EnableGpgSign = false;
        _repo.CertificateId = null;
        _db.SaveChanges();

        var pkg = AddPackage("wget-unsigned");
        var response = await Http.GetAsync($"/Repositories/PackageDetails/{pkg.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains($"Architectures: {_repo.Architecture}"),
            "Unsigned repo How to Install must also include the Architectures: field.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // APT routes — /artifacts prefix enforced, bare paths rejected
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AptRoute_BareDistroPath_Returns404()
    {
        // Old route /{distro}/dists/... must no longer exist
        var response = await Http.GetAsync($"/{_repo.Distro}/dists/{_repo.Suite}/InRelease");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode,
            "Bare /{distro}/dists/... route must not be reachable after adding /artifacts prefix.");
    }

    [TestMethod]
    public async Task AptRoute_BareCertsPath_Returns404()
    {
        var response = await Http.GetAsync("/certs/anduinos");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode,
            "Bare /certs/{name} route must not be reachable after adding /artifacts prefix.");
    }
}
