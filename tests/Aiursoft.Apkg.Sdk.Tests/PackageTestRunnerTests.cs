using System.Diagnostics;
using System.Xml.Linq;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;

namespace Aiursoft.Apkg.Sdk.Tests;

[TestClass]
public class PackageTestRunnerTests
{
    private string _root = null!;
    private readonly AosprojSerializer _serializer = new();
    private PackageTestRunner Runner => new(_serializer);

    [TestInitialize]
    public void Initialize() => _root = Directory.CreateTempSubdirectory("apkg-test-runner-").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    private string Project(params TestCommandItem[] commands)
    {
        var path = Path.Combine(_root, "example.aosproj");
        _serializer.Serialize(new AosprojProject
        {
            PackageName = "example",
            TestCommands = [.. commands],
            PrebuildCommands = [new() { Run = "touch forbidden-build" }]
        }).Save(path);
        return path;
    }

    private static TestCommandItem Entry(string name, string run, string profile = "release", int timeout = 5) =>
        new() { Name = name, Run = run, Profile = profile, TimeoutSeconds = timeout };

    [TestMethod]
    public async Task RunsOnlySelectedProfileInProjectDirectoryWithoutBuilding()
    {
        var path = Project(Entry("source", "printf '%s' \"$APKG_TEST_PROFILE\" > observed"),
            Entry("hardware", "touch forbidden-hardware", "hardware"));
        var results = await Runner.RunAsync(path, "release");
        Assert.AreEqual("release", File.ReadAllText(Path.Combine(_root, "observed")));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "forbidden-build")));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "forbidden-hardware")));
        Assert.AreEqual(PackageTestStatus.Passed, results.Single().Status);
    }

    [TestMethod]
    public async Task FailurePreservesExitCodeAndOutputAndDoesNotPreventLaterEntries()
    {
        var path = Project(Entry("fail", "printf '<failure>'; printf 'diagnostic' >&2; exit 23"),
            Entry("later", "touch later"));
        var results = await Runner.RunAsync(path, "release");
        Assert.AreEqual(PackageTestStatus.Failed, results[0].Status);
        Assert.AreEqual(23, results[0].ExitCode);
        Assert.AreEqual("<failure>", results[0].StandardOutput);
        Assert.AreEqual("diagnostic", results[0].StandardError);
        Assert.IsTrue(File.Exists(Path.Combine(_root, "later")));
        Assert.AreEqual(PackageTestStatus.Passed, results[1].Status);
        var report = XDocument.Parse(PackageTestRunner.CreateReport(results).ToString());
        Assert.AreEqual("1", report.Root!.Element("testsuite")!.Attribute("failures")!.Value);
        Assert.AreEqual("diagnostic", report.Descendants("failure").Single().Value);
    }

    [TestMethod]
    public async Task UnconfiguredProfileIsNotReportedAsPassed()
    {
        var results = await Runner.RunAsync(Project(Entry("hardware", "touch unexpected", "hardware")), "release");
        Assert.AreEqual(PackageTestStatus.NotConfigured, results.Single().Status);
        Assert.IsFalse(File.Exists(Path.Combine(_root, "unexpected")));
        Assert.IsNotNull(PackageTestRunner.CreateReport(results).Descendants("skipped").SingleOrDefault());
    }

    [TestMethod]
    public async Task InvalidConfigurationFailsBeforeAnyCommandRuns()
    {
        var path = Project(Entry("first", "touch unexpected"), Entry("bad", "true", timeout: 0));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Runner.RunAsync(path, "release"));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "unexpected")));
    }

    [TestMethod]
    public void RejectsDuplicateEntriesAndConditionalTestDeclarations()
    {
        Assert.IsTrue(PackageTestRunner.Validate([Entry("same", "true"), Entry("same", "false")]).Count > 0);
        Assert.HasCount(0, PackageTestRunner.Validate([Entry("same", "true"), Entry("same", "true", "gui")]));
        foreach (var xml in new[]
        {
            """<Project><ItemGroup><TestCommand Name="x" Profile="release" Run="true" Condition="false" /></ItemGroup></Project>""",
            """<Project><ItemGroup Condition="false"><TestCommand Name="x" Profile="release" Run="true" /></ItemGroup></Project>"""
        })
            Assert.ThrowsExactly<InvalidDataException>(() => _serializer.Deserialize(XDocument.Parse(xml)));
    }

    [TestMethod]
    public async Task TimeoutKillsChildrenEvenAfterShellExitsAndDoesNotHangOnInheritedPipes()
    {
        var path = Project(Entry("timeout", "(sleep 2; touch leaked-child) & exit 0", timeout: 1));
        var clock = Stopwatch.StartNew();
        var result = (await Runner.RunAsync(path, "release")).Single();
        Assert.AreEqual(PackageTestStatus.TimedOut, result.Status);
        Assert.IsLessThan(5.0, clock.Elapsed.TotalSeconds);
        await Task.Delay(1500);
        Assert.IsFalse(File.Exists(Path.Combine(_root, "leaked-child")));
    }

    [TestMethod]
    public async Task TimeoutReapsDescendantsWithTheirOwnSession()
    {
        var path = Project(Entry("pty-like", "setsid sh -c 'sleep 2; touch leaked-session' & wait", timeout: 1));
        var result = (await Runner.RunAsync(path, "release")).Single();
        Assert.AreEqual(PackageTestStatus.TimedOut, result.Status);
        await Task.Delay(1500);
        Assert.IsFalse(File.Exists(Path.Combine(_root, "leaked-session")));
    }

    [TestMethod]
    public async Task CancellationDoesNotRunSubsequentEntries()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var path = Project(Entry("slow", "sleep 10"), Entry("later", "touch unexpected"));
        var results = await Runner.RunAsync(path, "release", cancellationToken: cancel.Token);
        Assert.AreEqual(PackageTestStatus.Cancelled, results.Single().Status);
        Assert.IsFalse(File.Exists(Path.Combine(_root, "unexpected")));
    }

    [TestMethod]
    public async Task OutputIsBoundedAndReportRemainsValidXml()
    {
        var path = Project(Entry("loud", "head -c 100000 /dev/zero | tr '\\000' x; printf '\\001<&' >&2"));
        var result = (await Runner.RunAsync(path, "release")).Single();
        Assert.AreEqual(PackageTestStatus.Passed, result.Status);
        Assert.IsLessThanOrEqualTo(65536, result.StandardOutput.Length);
        var parsed = XDocument.Parse(PackageTestRunner.CreateReport([result]).ToString());
        Assert.AreEqual("<&", parsed.Descendants("system-err").Single().Value);
    }

    [TestMethod]
    public void DiscoverySkipsBuildOutputAndSymlinksAndStopsAtPackageBoundary()
    {
        foreach (var directory in new[] { "a", "b", "a/vendor", "obj", ".git", "target", ".venv" })
        {
            var path = Directory.CreateDirectory(Path.Combine(_root, directory)).FullName;
            File.WriteAllText(Path.Combine(path, "package.aosproj"), "<Project />");
        }
        Directory.CreateSymbolicLink(Path.Combine(_root, "alias"), Path.Combine(_root, "a"));
        var found = PackageTestRunner.Discover(_root, recursive: true);
        CollectionAssert.AreEqual(new[]
        {
            Path.Combine(_root, "a", "package.aosproj"),
            Path.Combine(_root, "b", "package.aosproj")
        }, found.ToArray());
    }

    [TestMethod]
    public async Task SerializationPreservesProfileCommandAndTimeout()
    {
        var path = Project(Entry("unicode", "printf '你好<&' > result", "custom-profile", 42));
        var entries = await Runner.ReadCommandsAsync(path, "custom-profile");
        Assert.AreEqual(42, entries.Single().TimeoutSeconds);
        var results = await Runner.RunAsync(path, "custom-profile");
        Assert.AreEqual(PackageTestStatus.Passed, results.Single().Status);
        Assert.AreEqual("你好<&", File.ReadAllText(Path.Combine(_root, "result")));
    }
}
