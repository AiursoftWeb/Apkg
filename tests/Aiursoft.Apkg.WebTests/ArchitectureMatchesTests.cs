using Aiursoft.Apkg.Controllers;

namespace Aiursoft.Apkg.WebTests;

[TestClass]
public class ArchitectureMatchesTests
{
    [TestMethod]
    public void EntryAll_MatchesAnyArchitecture()
    {
        Assert.IsTrue(ApkgUploadsController.ArchitectureMatches("amd64", "all"));
        Assert.IsTrue(ApkgUploadsController.ArchitectureMatches("arm64", "all"));
        Assert.IsTrue(ApkgUploadsController.ArchitectureMatches("i386", "all"));
        Assert.IsTrue(ApkgUploadsController.ArchitectureMatches("mips64el", "all"));
    }

    [TestMethod]
    public void EntryAll_CaseInsensitive()
    {
        Assert.IsTrue(ApkgUploadsController.ArchitectureMatches("amd64", "ALL"));
        Assert.IsTrue(ApkgUploadsController.ArchitectureMatches("amd64", "All"));
        Assert.IsTrue(ApkgUploadsController.ArchitectureMatches("amd64", "all"));
    }

    [TestMethod]
    public void EntrySpecific_MatchesExactArchitecture()
    {
        Assert.IsTrue(ApkgUploadsController.ArchitectureMatches("amd64", "amd64"));
        Assert.IsTrue(ApkgUploadsController.ArchitectureMatches("arm64", "arm64"));
    }

    [TestMethod]
    public void EntrySpecific_DoesNotMatchDifferentArchitecture()
    {
        Assert.IsFalse(ApkgUploadsController.ArchitectureMatches("amd64", "arm64"));
        Assert.IsFalse(ApkgUploadsController.ArchitectureMatches("arm64", "amd64"));
        Assert.IsFalse(ApkgUploadsController.ArchitectureMatches("i386", "amd64"));
    }

    [TestMethod]
    public void EntrySpecific_CaseInsensitive()
    {
        Assert.IsTrue(ApkgUploadsController.ArchitectureMatches("AMD64", "amd64"));
        Assert.IsTrue(ApkgUploadsController.ArchitectureMatches("amd64", "AMD64"));
    }

    [TestMethod]
    public void EntryAll_MatchesSourceArchitecture()
    {
        // "all" packages can also have repos with architecture "all" (less common but valid)
        Assert.IsTrue(ApkgUploadsController.ArchitectureMatches("all", "all"));
    }

    [TestMethod]
    public void RepoAll_OnlyMatchesEntryAll()
    {
        // repo arch "all" with entry arch "amd64" → no match
        // The repo declares what arch it serves; "all" is only for source repos
        Assert.IsTrue(ApkgUploadsController.ArchitectureMatches("all", "all"));
        Assert.IsFalse(ApkgUploadsController.ArchitectureMatches("all", "amd64"));
    }

    [TestMethod]
    public void BothControllers_Helpers_AreIdentical()
    {
        string[] testPairs = ["amd64", "arm64", "i386", "all", "ALL", "mips64el"];
        foreach (var entry in testPairs)
        foreach (var repo in testPairs)
        {
            var a = ApiPackagesController.ArchitectureMatches(repo, entry);
            var b = ApkgUploadsController.ArchitectureMatches(repo, entry);
            Assert.AreEqual(a, b, $"Mismatch: repo={repo} entry={entry}");
        }
    }
}
