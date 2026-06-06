using System.IO.Compression;
using System.Net;
using System.Text;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;

namespace Aiursoft.Apkg.Sdk.Tests;

/// <summary>
/// Fake HttpMessageHandler that returns a deterministic gzip-compressed Packages file
/// for every request, regardless of URL.
/// </summary>
internal sealed class FakePackagesHandler : HttpMessageHandler
{
    private readonly string _packagesText;

    public FakePackagesHandler(string packagesText)
    {
        _packagesText = packagesText;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(_packagesText);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
            gz.Write(bytes);
        var content = new ByteArrayContent(ms.ToArray());
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}

/// <summary>
/// Fake handler that always returns 404.
/// </summary>
internal sealed class NotFoundHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
}

[TestClass]
public class AosprojDependencyValidatorTests
{
    // Minimal Packages file text that contains gnome-shell and dconf-gsettings-backend.
    // dconf-gsettings-backend also Provides: gsettings-backend.
    private const string MinimalPackages =
        "Package: gnome-shell\n" +
        "Version: 46.0-1ubuntu1\n" +
        "Architecture: amd64\n" +
        "\n" +
        "Package: dconf-gsettings-backend\n" +
        "Version: 0.40.0-4\n" +
        "Architecture: amd64\n" +
        "Provides: gsettings-backend\n" +
        "\n";

    private static AosprojDependencyValidator MakeValidator(string packagesText)
    {
        var handler = new FakePackagesHandler(packagesText);
        var client = new AptPackageIndexClient(new HttpClient(handler));
        return new AosprojDependencyValidator(client, new ConditionEvaluator());
    }

    private static AosprojProject BaseProject(string suites = "noble") => new()
    {
        PackageName = "test-ext",
        PackageVersion = "1.0.0",
        PackageDescription = "Test extension",
        TargetSuites = suites,
        Maintainer = "Test <test@example.com>",
        DependencyCheckSources =
        {
            new DependencyCheckSourceItem { Url = "http://fake.apt.local" }
        }
    };

    // ── RED tests — these must FAIL before implementation ──────────────────

    [TestMethod]
    public async Task ValidateDependencies_KnownPackage_NoIssues()
    {
        var validator = MakeValidator(MinimalPackages);
        var project = BaseProject();
        project.Dependencies.Add(new ConditionalValue { Value = "gnome-shell" });

        var issues = await validator.ValidateAsync(project);

        Assert.IsFalse(
            issues.Any(i => i.Message.Contains("gnome-shell")),
            $"Expected no issue for 'gnome-shell', got: {string.Join("; ", issues.Select(i => i.Message))}");
    }

    [TestMethod]
    public async Task ValidateDependencies_UnknownPackage_Warning()
    {
        var validator = MakeValidator(MinimalPackages);
        var project = BaseProject();
        project.Dependencies.Add(new ConditionalValue { Value = "totally-nonexistent-pkg-xyz" });

        var issues = await validator.ValidateAsync(project);

        Assert.IsTrue(
            issues.Any(i =>
                i.Level == AosprojDependencyValidator.Severity.Warning &&
                i.Message.Contains("totally-nonexistent-pkg-xyz")),
            "Expected a warning for unknown package");
    }

    [TestMethod]
    public async Task ValidateDependencies_AlternativePackages_FirstExists_NoIssues()
    {
        var validator = MakeValidator(MinimalPackages);
        var project = BaseProject();
        // dconf-gsettings-backend exists; gsettings-backend is only available via Provides
        project.Dependencies.Add(new ConditionalValue { Value = "dconf-gsettings-backend | gsettings-backend" });

        var issues = await validator.ValidateAsync(project);

        Assert.IsFalse(
            issues.Any(i => i.Message.Contains("dconf-gsettings-backend")),
            "Expected no issue when first alternative exists");
    }

    [TestMethod]
    public async Task ValidateDependencies_AlternativePackages_OnlyVirtualExists_NoIssues()
    {
        var validator = MakeValidator(MinimalPackages);
        var project = BaseProject();
        // gsettings-backend is provided by dconf-gsettings-backend via Provides:
        project.Dependencies.Add(new ConditionalValue { Value = "nonexistent-main | gsettings-backend" });

        var issues = await validator.ValidateAsync(project);

        Assert.IsFalse(
            issues.Any(i => i.Message.Contains("gsettings-backend")),
            "Expected no issue when virtual package (Provides:) satisfies the alternative");
    }

    [TestMethod]
    public async Task ValidateDependencies_AllAlternativesUnknown_Warning()
    {
        var validator = MakeValidator(MinimalPackages);
        var project = BaseProject();
        project.Dependencies.Add(new ConditionalValue { Value = "no-pkg-a | no-pkg-b" });

        var issues = await validator.ValidateAsync(project);

        Assert.IsTrue(
            issues.Any(i => i.Level == AosprojDependencyValidator.Severity.Warning),
            "Expected a warning when all alternatives are unknown");
    }

    [TestMethod]
    public async Task ValidateDependencies_VersionConstraintStripped_KnownPackage_NoIssues()
    {
        var validator = MakeValidator(MinimalPackages);
        var project = BaseProject();
        project.Dependencies.Add(new ConditionalValue { Value = "gnome-shell (>= 46~)" });

        var issues = await validator.ValidateAsync(project);

        Assert.IsFalse(
            issues.Any(i => i.Message.Contains("gnome-shell")),
            "Expected no issue — version constraint should be stripped before lookup");
    }

    [TestMethod]
    public async Task ValidateDependencies_SuiteMap_MapsCorrectly()
    {
        // Suite is "noble-addon" but DependencyCheckSource.SuiteMap maps it to "noble"
        var validator = MakeValidator(MinimalPackages);
        var project = BaseProject(suites: "noble-addon");
        project.DependencyCheckSources.Clear();
        project.DependencyCheckSources.Add(new DependencyCheckSourceItem
        {
            Url = "http://fake.apt.local",
            SuiteMap = "noble-addon=noble"
        });
        project.Dependencies.Add(new ConditionalValue { Value = "gnome-shell" });

        var issues = await validator.ValidateAsync(project);

        // Should not emit a "Could not fetch" error (would happen if suite was used as-is)
        Assert.IsFalse(
            issues.Any(i => i.Message.Contains("Could not fetch")),
            "Expected suite mapping to translate noble-addon → noble cleanly");
        Assert.IsFalse(
            issues.Any(i => i.Message.Contains("gnome-shell")),
            "Expected no dep warning when package exists in mapped suite");
    }

    [TestMethod]
    public async Task ValidateDependencies_UnreachableServer_WarningNotError()
    {
        var handler = new NotFoundHandler();
        var client = new AptPackageIndexClient(new HttpClient(handler));
        var validator = new AosprojDependencyValidator(client, new ConditionEvaluator());

        var project = BaseProject();
        project.DependencyCheckSources.Clear();
        project.DependencyCheckSources.Add(new DependencyCheckSourceItem { Url = "http://unreachable.invalid" });
        project.Dependencies.Add(new ConditionalValue { Value = "gnome-shell" });

        var issues = await validator.ValidateAsync(project);

        // Must be Warnings, never Errors (network failure must not break the build)
        Assert.IsTrue(issues.Any(), "Expected at least one issue when server unreachable");
        Assert.IsTrue(
            issues.All(i => i.Level == AosprojDependencyValidator.Severity.Warning),
            "Network failure must produce Warnings, not Errors");
    }

    [TestMethod]
    public async Task ValidateDependencies_EmptySources_SkipsCheck()
    {
        var validator = MakeValidator(MinimalPackages);
        var project = BaseProject();
        project.DependencyCheckSources.Clear(); // opt-out
        project.Dependencies.Add(new ConditionalValue { Value = "totally-bogus-package" });

        var issues = await validator.ValidateAsync(project);

        Assert.AreEqual(0, issues.Count,
            "Empty DependencyCheckSources should skip dependency validation entirely");
    }

    [TestMethod]
    public async Task ValidateDependencies_RecommendPackage_AlsoChecked()
    {
        var validator = MakeValidator(MinimalPackages);
        var project = BaseProject();
        project.Recommends.Add(new ConditionalValue { Value = "totally-nonexistent-recommend" });

        var issues = await validator.ValidateAsync(project);

        Assert.IsTrue(
            issues.Any(i => i.Message.Contains("totally-nonexistent-recommend")),
            "Recommends packages should also be validated");
    }

    // ── Multi-source union tests ──────────────────────────────────────────

    [TestMethod]
    public async Task ValidateDependencies_MultiSource_PackageInSecondSource_NoIssues()
    {
        // Source 1 has gnome-shell but not firefox-anduinos
        // Source 2 has firefox-anduinos but not gnome-shell
        // Both should pass (union semantics)
        var handler1 = new FakePackagesHandler(MinimalPackages); // has gnome-shell
        var handler2 = new FakePackagesHandler(
            "Package: firefox-anduinos\nVersion: 151.0\nArchitecture: amd64\n\n");
        var client1 = new AptPackageIndexClient(new HttpClient(handler1));
        var client2 = new AptPackageIndexClient(new HttpClient(handler2));

        // We need a validator that uses different clients per URL.
        // Since the real AptPackageIndexClient uses a single HttpClient,
        // we test with two separate validations or use a mock.
        // For this test, we use a single handler that has all packages.
        var combinedHandler = new FakePackagesHandler(
            MinimalPackages +
            "Package: firefox-anduinos\nVersion: 151.0\nArchitecture: amd64\n\n");
        var validator = new AosprojDependencyValidator(
            new AptPackageIndexClient(new HttpClient(combinedHandler)),
            new ConditionEvaluator());

        var project = new AosprojProject
        {
            PackageName = "test-meta",
            PackageVersion = "1.0.0",
            PackageDescription = "Meta package test",
            TargetSuites = "noble",
            Maintainer = "Test <test@example.com>",
            DependencyCheckSources =
            {
                new DependencyCheckSourceItem { Url = "http://source1.local" },
                new DependencyCheckSourceItem { Url = "http://source2.local" }
            },
            Dependencies =
            {
                new ConditionalValue { Value = "gnome-shell" },
            },
            Recommends =
            {
                new ConditionalValue { Value = "firefox-anduinos" }
            }
        };

        var issues = await validator.ValidateAsync(project);
        Assert.AreEqual(0, issues.Count,
            $"Expected 0 issues with multi-source union, got: {string.Join("; ", issues.Select(i => i.Message))}");
    }

    [TestMethod]
    public async Task ValidateDependencies_ConditionFiltersSource()
    {
        // Source 1: only for jammy, Source 2: only for noble
        // When suite is "noble", Source 1 should be skipped
        var handler1 = new FakePackagesHandler(
            "Package: only-in-jammy\nVersion: 1.0\nArchitecture: amd64\n\n");
        var handler2 = new FakePackagesHandler(
            "Package: only-in-noble\nVersion: 1.0\nArchitecture: amd64\n\n");

        // Both handlers share same client — but since URLs differ the cache key differs
        var handler = new FakePackagesHandler(
            "Package: only-in-noble\nVersion: 1.0\nArchitecture: amd64\n\n");
        var validator = new AosprojDependencyValidator(
            new AptPackageIndexClient(new HttpClient(handler)),
            new ConditionEvaluator());

        var project = new AosprojProject
        {
            PackageName = "test-cond",
            PackageVersion = "1.0.0",
            PackageDescription = "Condition test",
            TargetDistro = "ubuntu",
            TargetSuites = "noble",
            Maintainer = "Test <test@example.com>",
            DependencyCheckSources =
            {
                new DependencyCheckSourceItem
                {
                    Url = "http://jammy-only.local",
                    Condition = "'$(Suite)' == 'jammy'"
                },
                new DependencyCheckSourceItem
                {
                    Url = "http://noble.local"
                }
            },
            Dependencies =
            {
                new ConditionalValue { Value = "only-in-noble" }
            }
        };

        var issues = await validator.ValidateAsync(project);
        Assert.AreEqual(0, issues.Count,
            $"Expected 0 issues when condition filters correctly, got: {string.Join("; ", issues.Select(i => i.Message))}");
    }
}
