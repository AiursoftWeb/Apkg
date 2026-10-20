using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO.Compression;
using System.Net;
using System.Text;

namespace Aiursoft.Apkg.Sdk.Tests;

[TestClass]
public class PackageBuildPlanResolverTests
{
    [TestMethod]
    public async Task ResolveAsync_FixedVersion_ResolvesCompleteMatrix()
    {
        var resolver = CreateResolver();
        var project = new AosprojProject
        {
            PackageName = "sample",
            PackageVersion = "2.3.4+$(SuiteShortName)",
            TargetDistro = "anduinos",
            TargetSuites = "noble-addon resolute-addon",
            TargetArchitectures = "amd64 arm64",
            SuiteShortNameMap = "noble-addon=noble resolute-addon=resolute"
        };

        var plan = await resolver.ResolveAsync(".", project);

        Assert.AreEqual("sample", plan.Name);
        Assert.AreEqual("anduinos", plan.Distro);
        Assert.AreEqual(4, plan.Targets.Count);
        Assert.AreEqual(
            "2.3.4+noble",
            plan.Targets.Single(target =>
                target.Suite == "noble-addon"
                && target.Architecture == "amd64").Version);
        Assert.AreEqual(
            "2.3.4+resolute",
            plan.Targets.Single(target =>
                target.Suite == "resolute-addon"
                && target.Architecture == "arm64").Version);
    }

    [TestMethod]
    public async Task ResolveAsync_UpstreamVersion_ReadsIndexWithoutDebPayload()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(), $"apkg-plan-{Guid.NewGuid():N}");
        var indexDir = Path.Combine(
            tempDir, "repo", "dists", "stable", "main", "binary-amd64");
        Directory.CreateDirectory(indexDir);
        await File.WriteAllTextAsync(
            Path.Combine(indexDir, "Packages"),
            PackageParagraph("upstream-sample", "1.9", "amd64")
            + PackageParagraph("upstream-sample", "1.10", "amd64"));

        try
        {
            var resolver = CreateResolver();
            var project = new AosprojProject
            {
                PackageName = "sample",
                PackageVersion = "2.0+$(UpstreamVersion)-1+$(SuiteShortName)",
                TargetDistro = "anduinos",
                TargetSuites = "stable-addon",
                TargetArchitectures = "amd64",
                SuiteShortNameMap = "stable-addon=stable",
                UpstreamPackage = "upstream-sample",
                UpstreamSuite = "stable",
                UpstreamComponent = "main",
                UpstreamArch = "amd64",
                UpstreamUrls =
                [
                    new ConditionalValue
                    {
                        Value = $"file://{Path.Combine(tempDir, "repo")}"
                    }
                ]
            };

            var plan = await resolver.ResolveAsync(tempDir, project);

            Assert.AreEqual("2.0+1.10-1+stable", plan.Targets.Single().Version);
            Assert.IsFalse(
                Directory.EnumerateFiles(
                    Path.Combine(tempDir, "repo"), "*.deb", SearchOption.AllDirectories).Any(),
                "Version resolution must not require a .deb payload.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task ResolveAsync_UnresolvedVersionVariable_FailsClosed()
    {
        var resolver = CreateResolver();
        var project = new AosprojProject
        {
            PackageName = "sample",
            PackageVersion = "1.0+$(Unknown)",
            TargetDistro = "anduinos",
            TargetSuites = "stable",
            TargetArchitectures = "amd64"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync(".", project));
    }

    [TestMethod]
    [DataRow("amd64", "1.0", "amd64", true)]
    [DataRow("amd64", "2.0", "amd64", false)]
    [DataRow("amd64", "1.0", "arm64", false)]
    [DataRow("all", "1.0", "all", true)]
    [DataRow("all", "1.0", "amd64", false)]
    public async Task PublishedVersions_RequireExactVersionAndArchitecture(
        string architecture, string publishedVersion, string publishedArchitecture, bool expected)
    {
        var indexes = new Dictionary<string, string>
        {
            ["/artifacts/anduinos/dists/stable/Release"] = "Architectures: amd64 arm64\n",
            ["/artifacts/anduinos/dists/stable/main/binary-amd64/Packages.gz"] =
                PackageParagraph("sample", publishedVersion, publishedArchitecture),
            ["/artifacts/anduinos/dists/stable/main/binary-arm64/Packages.gz"] =
                PackageParagraph("sample", publishedVersion, publishedArchitecture)
        };
        using var http = new HttpClient(new PublishedIndexHandler(indexes));
        var plan = new PackageBuildPlan
        {
            Name = "sample", Distro = "anduinos", Component = "main",
            Targets = [new() { Suite = "stable", Architecture = architecture, Version = "1.0" }]
        };

        Assert.AreEqual(expected, await new AptPackageIndexClient(http)
            .IsPublishedAsync(plan, "https://apkg.example.com/"));
    }

    [TestMethod]
    [DataRow("amd64 arm64")]
    [DataRow("all")]
    public async Task PublishedVersions_RequireEverySuiteAndArchitecture(string architectures)
    {
        var project = new AosprojProject
        {
            PackageName = "sample", PackageVersion = "1.0+$(SuiteShortName)",
            TargetDistro = "anduinos", TargetSuites = "noble-addon resolute-addon",
            TargetArchitectures = architectures, Component = "main",
            SuiteShortNameMap = "noble-addon=noble resolute-addon=resolute"
        };
        var plan = await CreateResolver().ResolveAsync(".", project);
        var indexes = new Dictionary<string, string>();
        foreach (var suite in project.SuiteList)
            indexes[$"/artifacts/anduinos/dists/{suite}/Release"] = "Architectures: amd64 arm64\n";
        using var http = new HttpClient(new PublishedIndexHandler(indexes));
        var client = new AptPackageIndexClient(http);
        foreach (var target in plan.Targets)
        foreach (var architecture in target.Architecture == "all" ? new[] { "amd64", "arm64" } : new[] { target.Architecture })
        {
            Assert.IsFalse(await client.IsPublishedAsync(plan, "https://apkg.example.com"));
            indexes[$"/artifacts/anduinos/dists/{target.Suite}/main/binary-{architecture}/Packages.gz"] =
                PackageParagraph("sample", target.Version, target.Architecture);
        }
        Assert.IsTrue(await client.IsPublishedAsync(plan, "https://apkg.example.com"));
    }

    private sealed class PublishedIndexHandler(Dictionary<string, string> indexes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.IsNull(request.Headers.Authorization);
            var path = request.RequestUri!.AbsolutePath;
            if (!indexes.TryGetValue(path, out var text))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (path.EndsWith("/Release", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) });
            using var buffer = new MemoryStream();
            using (var gzip = new GZipStream(buffer, CompressionMode.Compress, leaveOpen: true))
                gzip.Write(Encoding.UTF8.GetBytes(text));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(buffer.ToArray())
            });
        }
    }

    private static PackageBuildPlanResolver CreateResolver()
    {
        var evaluator = new ConditionEvaluator();
        var builder = new DebBuilder(
            evaluator,
            NullLogger<DebBuilder>.Instance);
        return new PackageBuildPlanResolver(builder);
    }

    private static string PackageParagraph(
        string package,
        string version,
        string architecture) => $"""
        Package: {package}
        Version: {version}
        Architecture: {architecture}
        Maintainer: Test <test@example.com>
        Description: test
        Section: utils
        Priority: optional
        Filename: pool/{package}_{version}_{architecture}.deb
        Size: 1
        SHA256: {new string('a', 64)}

        """;
}
