using System.Xml.Linq;
using Aiursoft.Apkg.Client.Handlers;
using Aiursoft.CommandFramework;
using Aiursoft.CommandFramework.Models;

namespace Aiursoft.Apkg.Client.Tests;

[TestClass]
public class TestHandlerTests
{
    private string _root = null!;
    private NestedCommandApp App => new NestedCommandApp()
        .WithGlobalOptions(CommonOptionsProvider.VerboseOption)
        .WithFeature(new TestHandler());

    [TestInitialize]
    public void Initialize() => _root = Directory.CreateTempSubdirectory("apkg-test-cli-").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    private void Package(string directory, string command, string profile = "release")
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, directory)).FullName;
        new XDocument(new XElement("Project",
            new XElement("ItemGroup", new XElement("TestCommand",
                new XAttribute("Name", "source"),
                new XAttribute("Profile", profile),
                new XAttribute("Run", command)))))
            .Save(Path.Combine(folder, "package.aosproj"));
    }

    [TestMethod]
    public async Task RecursiveFailureStillRunsOtherPackagesAndWritesReport()
    {
        Package("a", "exit 17");
        Package("b", "touch ran");
        Package("c", "touch must-not-run", "hardware");
        var report = Path.Combine(_root, "results.xml");
        var result = await App.TestRunAsync(
            ["test", "--path", _root, "--recursive", "--profile", "release", "--report", report]);
        Assert.AreNotEqual(0, result.ProgramReturn);
        Assert.IsTrue(File.Exists(Path.Combine(_root, "b", "ran")));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "c", "must-not-run")));
        var document = XDocument.Load(report);
        Assert.HasCount(1, document.Descendants("failure"));
        Assert.HasCount(1, document.Descendants("skipped"));
    }

    [TestMethod]
    public async Task ListingAndMissingProfileNeverExecuteEntries()
    {
        Package("a", "touch must-not-run");
        var result = await App.TestRunAsync(
            ["test", "--path", Path.Combine(_root, "a"), "--profile", "release", "--list"]);
        Assert.AreEqual(0, result.ProgramReturn);
        Assert.IsFalse(File.Exists(Path.Combine(_root, "a", "must-not-run")));
        var missing = await App.TestRunAsync(["test", "--path", Path.Combine(_root, "a")]);
        Assert.AreNotEqual(0, missing.ProgramReturn);
        Assert.IsFalse(File.Exists(Path.Combine(_root, "a", "must-not-run")));
    }

    [TestMethod]
    public async Task UnconfiguredPackageIsExplicitAndNotAFailedCommand()
    {
        Package("a", "exit 99", "hardware");
        var report = Path.Combine(_root, "results.xml");
        var result = await App.TestRunAsync(
            ["test", "--path", Path.Combine(_root, "a"), "--profile", "release", "--report", report]);
        Assert.AreEqual(0, result.ProgramReturn);
        var suite = XDocument.Load(report).Descendants("testsuite").Single();
        Assert.AreEqual(suite.Attribute("tests")!.Value, suite.Attribute("skipped")!.Value);
        Assert.AreEqual("Profile not configured", suite.Descendants("skipped").Single().Attribute("message")!.Value);
    }
}
