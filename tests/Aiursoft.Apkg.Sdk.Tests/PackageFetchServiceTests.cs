using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiursoft.Apkg.Sdk.Tests;

[TestClass]
public class PackageFetchServiceTests
{
    private string _root = null!;
    private string _home = null!;
    private string _key = null!;
    private readonly Dictionary<string, byte[]> _responses = [];
    private readonly List<string> _requests = [];
    private PackageFetchService Service => new(NullLogger<PackageFetchService>.Instance,
        () => new HttpClient(new FakeHttp(_responses, _requests)));
    private string Output => Path.Combine(_root, "output");

    [TestInitialize]
    public async Task Initialize()
    {
        _root = Directory.CreateTempSubdirectory("apkg-fetch-test-").FullName;
        _home = Directory.CreateDirectory(Path.Combine(_root, "gnupg")).FullName;
        _key = Path.Combine(_root, "archive.gpg");
        await Gpg("--passphrase", "", "--quick-generate-key", "Fetch Tests <fetch@example.invalid>", "ed25519", "sign", "0");
        await Gpg("--output", _key, "--export");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    private FetchSource Source(string suite) => new("https://repo.invalid/ubuntu", suite, "main", _key);
    private FetchInput Input(string name, params string[] suites) => new(name, name, "arm64", suites.Select(Source).ToArray());

    private async Task<string> Package(string name, string version, string arch = "arm64", bool realDeb = false, string algorithm = "SHA256")
    {
        var pool = $"pool/{name}_{version}_{arch}.deb";
        byte[] bytes;
        if (realDeb)
        {
            var root = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
            Directory.CreateDirectory(Path.Combine(root, "DEBIAN"));
            Directory.CreateDirectory(Path.Combine(root, "usr/share"));
            await File.WriteAllTextAsync(Path.Combine(root, "usr/share", name), "payload");
            await File.WriteAllTextAsync(Path.Combine(root, "DEBIAN/control"),
                $"Package: {name}\nVersion: {version}\nArchitecture: {arch}\nMaintainer: Test <t@example.invalid>\nDescription: test\nDepends: forbidden-dependency\n");
            var postinst = Path.Combine(root, "DEBIAN/postinst");
            await File.WriteAllTextAsync(postinst, "#!/bin/sh\nexit 91\n");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(postinst, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            var deb = root + ".deb";
            await Run("dpkg-deb", "--build", "--root-owner-group", root, deb);
            bytes = await File.ReadAllBytesAsync(deb);
        }
        else bytes = Encoding.UTF8.GetBytes($"{name} {version} {arch}");
        _responses["/ubuntu/" + pool] = bytes;
        return $"Package: {name}\nVersion: {version}\nArchitecture: {arch}\nMaintainer: Test\nDescription: Test\nSection: utils\nPriority: optional\nFilename: {pool}\nSize: {bytes.Length}\n{algorithm}: {(algorithm == "SHA512" ? Convert.ToHexStringLower(SHA512.HashData(bytes)) : Hash(bytes))}\n\n";
    }

    private async Task Repository(string suite, string packages, string arch = "arm64", string algorithm = "SHA256")
    {
        var bytes = Encoding.UTF8.GetBytes(packages);
        var index = $"main/binary-{arch}/Packages";
        _responses[$"/ubuntu/dists/{suite}/{index}"] = bytes;
        var release = Path.Combine(_root, "Release");
        await File.WriteAllTextAsync(release, $"Suite: {suite}\n{algorithm}:\n {(algorithm == "SHA512" ? Convert.ToHexStringLower(SHA512.HashData(bytes)) : Hash(bytes))} {bytes.Length} {index}\n");
        await Gpg("--output", release + ".asc", "--clearsign", release);
        _responses[$"/ubuntu/dists/{suite}/InRelease"] = await File.ReadAllBytesAsync(release + ".asc");
    }

    [TestMethod]
    [DataRow("SHA256", "SHA256")]
    [DataRow("SHA512", "SHA512")]
    [DataRow("SHA512", "SHA256")]
    [DataRow("SHA256", "SHA512")]
    public async Task SignedRepositorySupportsBothAlgorithmsAndPreservesManifest(string indexAlgorithm, string packageAlgorithm)
    {
        await Repository("base", await Package("tool", "1.0", algorithm: packageAlgorithm), algorithm: indexAlgorithm);
        var manifest = await Service.FetchAsync([Input("tool", "base")], Output, false);
        var expected = _responses["/ubuntu/pool/tool_1.0_arm64.deb"];
        Assert.AreEqual(Hash(expected), manifest.Inputs.Single().Sha256);
        var deb = Path.Combine(Output, manifest.Inputs.Single().DebPath);
        CollectionAssert.AreEqual(expected, await File.ReadAllBytesAsync(deb));
        _requests.Clear();
        await Service.FetchAsync([Input("tool", "base")], Output, false);
        Assert.AreEqual(0, _requests.Count(r => r.EndsWith(".deb")));
        await File.WriteAllTextAsync(deb, "broken cache");
        await Service.FetchAsync([Input("tool", "base")], Output, false);
        CollectionAssert.AreEqual(expected, await File.ReadAllBytesAsync(deb));
    }

    [TestMethod]
    public async Task BothAdvertisedAlgorithmsRequireValidSha512()
    {
        var stanza = await Package("tool", "1.0");
        // SHA-256 still matches, but the preferred SHA-512 digest does not.
        stanza = stanza.TrimEnd() + $"\nSHA512: {new string('0', 128)}\n\n";
        await Repository("base", stanza);
        await Assert.ThrowsAsync<Exception>(() => Service.FetchAsync([Input("tool", "base")], Output, false));
        Assert.IsFalse(Directory.Exists(Output));
    }

    [TestMethod]
    [DataRow("index")]
    [DataRow("package")]
    public async Task Sha512TamperingPreservesPreviousOutput(string target)
    {
        await Repository("base", await Package("tool", "1.0", algorithm: "SHA512"), algorithm: "SHA512");
        await Service.FetchAsync([Input("tool", "base")], Output, false);
        var previous = await File.ReadAllTextAsync(Path.Combine(Output, "manifest.json"));
        await Repository("base", await Package("tool", "2.0", algorithm: "SHA512"), algorithm: "SHA512");
        var path = target == "index" ? "/ubuntu/dists/base/main/binary-arm64/Packages" : "/ubuntu/pool/tool_2.0_arm64.deb";
        _responses[path] = Encoding.UTF8.GetBytes("tampered");
        await Assert.ThrowsAsync<Exception>(() => Service.FetchAsync([Input("tool", "base")], Output, false));
        Assert.AreEqual(previous, await File.ReadAllTextAsync(Path.Combine(Output, "manifest.json")));
    }

    [TestMethod]
    [DataRow("SHA512: broken")]
    [DataRow("SHA1: 0000000000000000000000000000000000000000")]
    public async Task MissingOrMalformedStrongPackageHashIsRejected(string field)
    {
        var stanza = await Package("tool", "1.0");
        stanza = System.Text.RegularExpressions.Regex.Replace(stanza, "SHA256: [a-f0-9]+", field);
        await Repository("base", stanza, algorithm: "SHA512");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Service.FetchAsync([Input("tool", "base")], Output, false));
        Assert.IsFalse(Directory.Exists(Output));
    }

    [TestMethod]
    public async Task SelectsLatestAcrossAllPockets_ReusesIndexesAndVerifiedDownloads()
    {
        await Repository("base", await Package("tool", "1.9") + await Package("data", "1.0", "all"));
        await Repository("updates", await Package("tool", "1.10"));
        await Repository("security", await Package("tool", "1:1.0"));
        var inputs = new[] { Input("tool", "base", "updates", "security"), Input("data", "base", "updates", "security") };
        var result = await Service.FetchAsync(inputs, Output, false);
        Assert.AreEqual("1:1.0", result.Inputs[0].Version);
        Assert.AreEqual("security", result.Inputs[0].Suite);
        Assert.AreEqual("all", result.Inputs[1].Architecture);
        Assert.AreEqual(3, _requests.Count(r => r.EndsWith("/Packages")));
        Assert.AreEqual(2, _requests.Count(r => r.EndsWith(".deb")));
        _requests.Clear();
        await Service.FetchAsync(inputs, Output, false);
        Assert.AreEqual(0, _requests.Count(r => r.EndsWith(".deb")));
        await File.WriteAllTextAsync(Path.Combine(Output, result.Inputs[0].DebPath), "corrupt cache");
        await Service.FetchAsync(inputs, Output, false);
        Assert.AreEqual(1, _requests.Count(r => r.EndsWith(".deb")));
    }

    [TestMethod]
    public async Task RequiredSourceFailureDoesNotFallBackOrReplacePreviousOutput()
    {
        await Repository("base", await Package("tool", "1.0"));
        await Service.FetchAsync([Input("tool", "base")], Output, false);
        var previous = await File.ReadAllTextAsync(Path.Combine(Output, "manifest.json"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            Service.FetchAsync([Input("tool", "base", "security")], Output, false));
        Assert.AreEqual(previous, await File.ReadAllTextAsync(Path.Combine(Output, "manifest.json")));
        Assert.AreEqual(0, Directory.GetDirectories(_root, ".apkg-fetch-*").Length);
    }

    [TestMethod]
    public async Task RejectsInvalidSignaturesIndexHashesAndPackageHashes()
    {
        var packages = await Package("tool", "1.0");
        await Repository("base", packages);
        var releasePath = "/ubuntu/dists/base/InRelease";
        var signed = _responses[releasePath];
        _responses[releasePath] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(signed).Replace("Suite: base", "Suite: evil"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Service.FetchAsync([Input("tool", "base")], Output, false));
        Assert.IsFalse(Directory.Exists(Output));
        _responses[releasePath] = signed;
        var indexPath = "/ubuntu/dists/base/main/binary-arm64/Packages";
        var index = _responses[indexPath];
        _responses[indexPath] = Encoding.UTF8.GetBytes("tampered index");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Service.FetchAsync([Input("tool", "base")], Output, false));
        _responses[indexPath] = index;
        _responses["/ubuntu/pool/tool_1.0_arm64.deb"] = Encoding.UTF8.GetBytes("tampered deb");
        await Assert.ThrowsExactlyAsync<System.Security.SecurityException>(() => Service.FetchAsync([Input("tool", "base")], Output, false));
        Assert.IsFalse(Directory.Exists(Output));
    }

    [TestMethod]
    public async Task ExtractionAndBuildKeepPayloadsAndMaintainerMetadataSeparate()
    {
        await Repository("base", await Package("tool", "1.0", realDeb: true));
        var project = Project();
        project.PrebuildCommands.Add(new PrebuildCommandItem
        {
            Run = "test -f \"$APKG_INPUTS_DIR/extracted/tool/usr/share/tool\" && " +
                  "test ! -f \"$APKG_STAGE_DIR/usr/share/tool\" && " +
                  "mkdir -p \"$APKG_STAGE_DIR/usr/share\" && " +
                  "cp \"$APKG_INPUTS_DIR/extracted/tool/usr/share/tool\" \"$APKG_STAGE_DIR/usr/share/selected\""
        });
        var builder = new DebBuilder(new ConditionEvaluator(), NullLogger<DebBuilder>.Instance,
            new AppStreamMetadataService(NullLogger<AppStreamMetadataService>.Instance), Service);
        await builder.BuildAsync(_root, project, "ubuntu", "base", "arm64", Path.Combine(_root, "bin"));
        var staging = Path.Combine(_root, "obj/base_arm64");
        Assert.AreEqual("payload", await File.ReadAllTextAsync(Path.Combine(staging, "usr/share/selected")));
        Assert.IsFalse(File.Exists(Path.Combine(staging, "usr/share/tool")));
        Assert.IsFalse((await File.ReadAllTextAsync(Path.Combine(staging, "DEBIAN/control"))).Contains("forbidden-dependency"));
        Assert.IsFalse(File.Exists(Path.Combine(staging, "DEBIAN/postinst")));
        var inputsDir = Path.Combine(_root, "obj/inputs/ubuntu/base/arm64");
        Assert.IsFalse(Directory.Exists(Path.Combine(inputsDir, "extracted/tool/DEBIAN")));
        var manifest = JsonSerializer.Deserialize<FetchManifest>(await File.ReadAllTextAsync(Path.Combine(inputsDir, "manifest.json")))!;
        Assert.AreEqual("arm64", manifest.Inputs.Single().Architecture);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(inputsDir, "manifest.json")));
        var entry = json.RootElement.GetProperty("Inputs")[0];
        Assert.AreEqual("tool", entry.GetProperty("Id").GetString());
        Assert.AreEqual("tool", entry.GetProperty("Package").GetString());
        Assert.AreEqual("1.0", entry.GetProperty("Version").GetString());
        Assert.AreEqual("arm64", entry.GetProperty("Architecture").GetString());
        Assert.AreEqual("arm64", entry.GetProperty("RequestedArchitecture").GetString());
        Assert.AreEqual("https://repo.invalid/ubuntu", entry.GetProperty("Repository").GetString());
        Assert.AreEqual("base", entry.GetProperty("Suite").GetString());
        Assert.AreEqual("main", entry.GetProperty("Component").GetString());
        var downloaded = await File.ReadAllBytesAsync(Path.Combine(inputsDir, entry.GetProperty("DebPath").GetString()!));
        Assert.AreEqual(Hash(downloaded), entry.GetProperty("Sha256").GetString());
        Assert.AreEqual("payload", await File.ReadAllTextAsync(Path.Combine(inputsDir,
            entry.GetProperty("ExtractedPath").GetString()!, "usr/share/tool")));
        _responses["/ubuntu/dists/base/InRelease"] = Encoding.UTF8.GetBytes("invalid signature");
        project.PrebuildCommands = [new PrebuildCommandItem { Run = "touch must-not-run" }];
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            builder.BuildAsync(_root, project, "ubuntu", "base", "arm64", Path.Combine(_root, "bin")));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "must-not-run")));
    }

    [TestMethod]
    public async Task RejectsWrongArchitectureMissingPackagesAndConflictingCandidates()
    {
        await Repository("base", await Package("tool", "1.0", "amd64"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Service.FetchAsync([Input("tool", "base")], Output, false));
        await Repository("base", await Package("tool", "1.0"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Service.FetchAsync([Input("missing", "base")], Output, false));
        var conflict = (await Package("tool", "1.0")).Replace(Hash(_responses["/ubuntu/pool/tool_1.0_arm64.deb"]), new string('a', 64));
        await Repository("updates", conflict);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Service.FetchAsync([Input("tool", "base", "updates")], Output, false));
        Assert.IsFalse(Directory.Exists(Output));
    }

    [TestMethod]
    public async Task OutputOwnershipAndMissingKeyFailBeforeNetworkAccess()
    {
        Directory.CreateDirectory(Output);
        await File.WriteAllTextAsync(Path.Combine(Output, "precious.txt"), "keep");
        await Assert.ThrowsExactlyAsync<IOException>(() => Service.FetchAsync([Input("tool", "base")], Output, false));
        File.Delete(_key);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Service.FetchAsync([Input("tool", "base")], Output, false));
        Assert.AreEqual(0, _requests.Count);
        Assert.AreEqual("keep", await File.ReadAllTextAsync(Path.Combine(Output, "precious.txt")));
    }

    [TestMethod]
    public void ProjectRoundTripConditionsAndExplicitArchitectures()
    {
        var project = Project();
        project.BuildSources[0].Suites = "$(Suite) $(Suite)-updates";
        project.BuildInputs.Add(new BuildInputItem { Package = "tool", Id = "host-tool", Sources = "ubuntu", Arch = "amd64" });
        project.BuildInputs.Add(new BuildInputItem { Package = "bios", Sources = "ubuntu", Arch = "amd64", Condition = "'$(Arch)' == 'amd64'" });
        var serializer = new AosprojSerializer();
        project = serializer.Deserialize(serializer.Serialize(project));
        var resolved = BuildInputResolver.Resolve(project, _root, "ubuntu", "base", "arm64");
        Assert.AreEqual(2, resolved.Count);
        Assert.AreEqual("arm64", resolved[0].Arch);
        Assert.AreEqual("amd64", resolved[1].Arch);
        Assert.AreEqual("base-updates", resolved[0].Sources[1].Suite);
        project.BuildInputs[1].Id = "tool";
        Assert.ThrowsExactly<InvalidDataException>(() => BuildInputResolver.Resolve(project, _root, "ubuntu", "base", "arm64"));
        Assert.ThrowsExactly<InvalidDataException>(() => serializer.Deserialize(XDocument.Parse(
            "<Project><ItemGroup><BuildInput Include='tool' Architecture='arm64'/></ItemGroup></Project>")));
        foreach (var item in new[] { "BuildSource", "BuildInput" })
            Assert.ThrowsExactly<InvalidDataException>(() => serializer.Deserialize(XDocument.Parse(
                $"<Project><ItemGroup Condition='false'><{item}/></ItemGroup></Project>")));
    }

    [TestMethod]
    public void SourcesFilePreservesRepositoryPocketPairingAndRejectsImplicitTrust()
    {
        var path = Path.Combine(_root, "ubuntu.sources");
        var content = "Types: deb\nURIs: https://archive.invalid/ubuntu\nSuites: base base-updates\nComponents: main\nSigned-By: archive.gpg\n\n" +
                      "Types: deb\nURIs: https://security.invalid/ubuntu\nSuites: base-security\nComponents: main\nSigned-By: archive.gpg\n";
        File.WriteAllText(path, content);
        var sources = BuildInputResolver.ReadSourcesFile(path, "arm64");
        Assert.AreEqual(3, sources.Count);
        Assert.AreEqual("https://security.invalid/ubuntu", sources[2].Url);
        Assert.AreEqual("base-security", sources[2].Suite);
        Assert.AreEqual(_key, sources[2].SignedBy);
        File.WriteAllText(path, content + "Trusted: yes\n");
        Assert.ThrowsExactly<InvalidDataException>(() => BuildInputResolver.ReadSourcesFile(path, "arm64"));
        File.WriteAllText(path, content.Replace("Signed-By: archive.gpg\n", ""));
        Assert.ThrowsExactly<ArgumentException>(() => BuildInputResolver.ReadSourcesFile(path, "arm64"));
        File.WriteAllText(path, content + "Signed-By: other.gpg\n");
        Assert.ThrowsExactly<InvalidDataException>(() => BuildInputResolver.ReadSourcesFile(path, "arm64"));
    }

    [TestMethod]
    public async Task SuiteShortNameFetchesUbuntuPocketsWhilePreservingAddonBuildTarget()
    {
        await Repository("resolute", await Package("tool", "1.0", realDeb: true));
        await Repository("resolute-updates", await Package("tool", "1.1", realDeb: true));
        await Repository("resolute-security", await Package("tool", "1.2", realDeb: true));
        var project = Project();
        project.TargetSuites = "resolute-addon";
        project.SuiteShortNameMap = "resolute-addon=resolute";
        project.PackageVersion = "1.0+$(SuiteShortName)";
        project.BuildSources[0].Suites = "$(SuiteShortName) $(SuiteShortName)-updates $(SuiteShortName)-security";
        project.BuildSources[0].Condition = "'$(Suite)' == 'resolute-addon'";
        project.BuildInputs[0].Id = "$(SuiteShortName)-tool";
        project.BuildInputs[0].Condition = "'$(Suite)' == 'resolute-addon'";
        project.PrebuildCommands.Add(new PrebuildCommandItem
        {
            Run = "mkdir -p \"$APKG_STAGE_DIR/usr/share\" && " +
                  "cp \"$APKG_INPUTS_DIR/extracted/resolute-tool/usr/share/tool\" \"$APKG_STAGE_DIR/usr/share/selected\""
        });
        var serializer = new AosprojSerializer();
        project = serializer.Deserialize(serializer.Serialize(project));

        // Standalone fetch and build use the same resolved source selection.
        var inputs = BuildInputResolver.Resolve(project, _root, "ubuntu", "resolute-addon", "arm64");
        var fetched = await Service.FetchAsync(inputs, Output, extract: true);
        Assert.AreEqual("resolute-security", fetched.Inputs.Single().Suite);
        Assert.AreEqual("1.2", fetched.Inputs.Single().Version);

        var builder = new DebBuilder(new ConditionEvaluator(), NullLogger<DebBuilder>.Instance,
            new AppStreamMetadataService(NullLogger<AppStreamMetadataService>.Instance), Service);
        var built = await builder.BuildAsync(_root, project, "ubuntu", "resolute-addon", "arm64", Path.Combine(_root, "bin"));
        Assert.AreEqual("resolute-addon", built.Suite);
        Assert.AreEqual("1.0+resolute", built.Version);
        Assert.AreEqual("payload", await File.ReadAllTextAsync(Path.Combine(_root, "obj/resolute-addon_arm64/usr/share/selected")));
        Assert.IsTrue(File.Exists(Path.Combine(_root, "obj/inputs/ubuntu/resolute-addon/arm64/manifest.json")));
        Assert.IsFalse(_requests.Any(r => r.Contains("resolute-addon", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ExtractionFailureAndRemovedInputsDoNotLeaveStalePayloads()
    {
        await Repository("base", await Package("first", "1.0", realDeb: true) + await Package("second", "1.0", realDeb: true));
        await Service.FetchAsync([Input("first", "base"), Input("second", "base")], Output, true);
        await File.WriteAllTextAsync(Path.Combine(Output, "extracted/first/usr/share/first"), "modified by script");
        await Service.FetchAsync([Input("first", "base")], Output, true);
        Assert.AreEqual("payload", await File.ReadAllTextAsync(Path.Combine(Output, "extracted/first/usr/share/first")));
        Assert.IsFalse(Directory.Exists(Path.Combine(Output, "extracted/second")));
        var previous = await File.ReadAllTextAsync(Path.Combine(Output, "manifest.json"));
        await Repository("base", await Package("first", "2.0")); // Hash-valid but not a deb.
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Service.FetchAsync([Input("first", "base")], Output, true));
        Assert.AreEqual(previous, await File.ReadAllTextAsync(Path.Combine(Output, "manifest.json")));
    }

    [TestMethod]
    public void LintRejectsMissingSourceAndArchitectureDeclarations()
    {
        var project = Project();
        project.BuildSources[0].Components = "";
        project.BuildInputs[0].Arch = "";
        project.BuildInputs[0].Sources = "unknown";
        var errors = new AosprojLinter(new ConditionEvaluator()).Lint(project, _root)
            .Where(i => i.Level == AosprojLinter.Severity.Error).Select(i => i.Message).ToArray();
        Assert.IsTrue(errors.Any(e => e.Contains("Components")));
        Assert.IsTrue(errors.Any(e => e.Contains("Arch")));
        Assert.IsTrue(errors.Any(e => e.Contains("unknown")));
    }

    private AosprojProject Project() => new()
    {
        PackageName = "assembled", PackageVersion = "1.0", PackageDescription = "Test", Maintainer = "Test <t@example.invalid>",
        TargetSuites = "base", TargetArchitectures = "arm64",
        BuildSources = [new BuildSourceItem { Name = "ubuntu", Url = "https://repo.invalid/ubuntu", Suites = "$(Suite)", Components = "main", SignedBy = "archive.gpg" }],
        BuildInputs = [new BuildInputItem { Package = "tool", Sources = "ubuntu", Arch = "$(Arch)" }]
    };

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private Task<string> Gpg(params string[] args) => Run("gpg",
        ["--no-options", "--homedir", _home, "--batch", "--yes", "--pinentry-mode", "loopback", .. args]);
    private static async Task<string> Run(string command, params string[] args)
    {
        var start = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.AreEqual(0, process.ExitCode, await stderr);
        return await stdout;
    }

    private sealed class FakeHttp(Dictionary<string, byte[]> responses, List<string> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            requests.Add(path);
            return Task.FromResult(responses.TryGetValue(path, out var content)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
