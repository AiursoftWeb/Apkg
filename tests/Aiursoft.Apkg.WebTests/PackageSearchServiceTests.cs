using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services;

namespace Aiursoft.Apkg.WebTests;

/// <summary>
/// Unit tests for PackageSearchService.ScoreAndRank.
///
/// Each test describes a real-world search intent and asserts
/// that the most relevant package surfaces to the top.
/// </summary>
[TestClass]
public class PackageSearchServiceTests
{
    // ──────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────

    private static AptPackage Pkg(string name, string description = "No description.") => new()
    {
        Package         = name,
        Version         = "1.0",
        Architecture    = "amd64",
        Maintainer      = "Test <t@example.com>",
        Description     = description,
        DescriptionMd5  = "abc",
        Section         = "utils",
        Priority        = "optional",
        Origin          = "Test",
        Bugs            = "none",
        Filename        = $"pool/main/{name}_1.0_amd64.deb",
        Size            = "1024",
        MD5sum          = "d41d8cd98f00b204e9800998ecf8427e",
        SHA1            = "da39a3ee5e6b4b0d3255bfef95601890afd80709",
        SHA256          = "e3b0c44298fc1c149afbf4c8996fb924",
        SHA512          = "cf83e1357eefb8bdf1542850d66d8007",
        OriginSuite     = "questing",
        OriginComponent = "main",
        Component       = "main",
        BucketId        = 1,
    };

    private static void AssertOrder(IList<AptPackage> results, params string[] expectedNames)
    {
        var actualNames = results.Select(p => p.Package).ToArray();
        CollectionAssert.AreEqual(
            expectedNames,
            actualNames,
            $"Expected order [{string.Join(", ", expectedNames)}] but got [{string.Join(", ", actualNames)}]");
    }

    // ──────────────────────────────────────────────────────────
    // SplitTerms
    // ──────────────────────────────────────────────────────────

    [TestMethod]
    public void SplitTerms_SingleWord_ReturnsSingleTerm()
    {
        var terms = PackageSearchService.SplitTerms("snap");
        CollectionAssert.AreEqual(new[] { "snap" }, terms);
    }

    [TestMethod]
    public void SplitTerms_MultipleWords_SplitsOnWhitespace()
    {
        var terms = PackageSearchService.SplitTerms("  snap  daemon  ");
        CollectionAssert.AreEqual(new[] { "snap", "daemon" }, terms);
    }

    [TestMethod]
    public void SplitTerms_EmptyString_ReturnsEmpty()
    {
        var terms = PackageSearchService.SplitTerms("   ");
        Assert.AreEqual(0, terms.Length);
    }

    // ──────────────────────────────────────────────────────────
    // Basic ranking
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// The original bug: searching "snap" should put "snapd" (prefix match)
    /// well before "aj-snapshot" (contains match, alphabetically first).
    /// </summary>
    [TestMethod]
    public void Search_Snap_SnapdBeforeAjSnapshot()
    {
        var packages = new[]
        {
            Pkg("aj-snapshot",  "make snapshots of JACK connections"),
            Pkg("gcc-snapshot", "SNAPSHOT of the GNU Compiler Collection"),
            Pkg("snapd",        "Daemon and tooling that enable snap packages"),
        };

        var results = PackageSearchService.ScoreAndRank(packages, "snap");

        // snapd is a prefix match (score 110) — must beat pure-contains matches (score 11)
        Assert.AreEqual("snapd", results[0].Package,
            "snapd (prefix of 'snap') must rank #1");
    }

    [TestMethod]
    public void Search_Snap_ExactMatchRanksFirst()
    {
        var packages = new[]
        {
            Pkg("snapd",        "Daemon for snap"),
            Pkg("snap",         "The snap tool"),          // exact match
            Pkg("aj-snapshot",  "Create snapshots"),
        };

        var results = PackageSearchService.ScoreAndRank(packages, "snap");

        Assert.AreEqual("snap", results[0].Package,
            "Exact package name match must always rank #1");
        Assert.AreEqual("snapd", results[1].Package,
            "Prefix match must rank #2");
    }

    [TestMethod]
    public void Search_ExactThenPrefixThenContains_CorrectOrder()
    {
        var packages = new[]
        {
            Pkg("libsnap-utils",  "Library for snap utilities"),  // contains
            Pkg("snap",           "The snap package manager"),    // exact
            Pkg("snapcraft",      "Create snaps"),                // prefix
        };

        var results = PackageSearchService.ScoreAndRank(packages, "snap");

        AssertOrder(results, "snap", "snapcraft", "libsnap-utils");
    }

    // ──────────────────────────────────────────────────────────
    // Case insensitivity
    // ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Search_UppercaseQuery_MatchesLowercasePackageNames()
    {
        var packages = new[]
        {
            Pkg("snapd"),
            Pkg("curl"),
        };

        var results = PackageSearchService.ScoreAndRank(packages, "SNAPD");

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("snapd", results[0].Package);
    }

    [TestMethod]
    public void Search_MixedCaseQuery_ExactMatchStillRanksFirst()
    {
        var packages = new[]
        {
            Pkg("snapd"),
            Pkg("snap"),
        };

        var results = PackageSearchService.ScoreAndRank(packages, "SNAP");

        Assert.AreEqual("snap", results[0].Package,
            "Case-insensitive exact match must still rank above prefix");
    }

    // ──────────────────────────────────────────────────────────
    // Description field
    // ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Search_TermInDescriptionOnly_PackageIsIncluded()
    {
        var packages = new[]
        {
            Pkg("curl",   "Command-line tool for snap operations"),  // description only
            Pkg("python", "The Python interpreter"),
        };

        var results = PackageSearchService.ScoreAndRank(packages, "snap");

        Assert.AreEqual(1, results.Count, "Package whose description contains 'snap' must be returned");
        Assert.AreEqual("curl", results[0].Package);
    }

    [TestMethod]
    public void Search_NameMatchRanksHigherThanDescriptionOnly()
    {
        var packages = new[]
        {
            Pkg("curl",   "Snap-based downloader"),   // description only (score 1)
            Pkg("snapd",  "Daemon for snap"),          // prefix + name contains + desc (score 111)
        };

        var results = PackageSearchService.ScoreAndRank(packages, "snap");

        Assert.AreEqual("snapd", results[0].Package,
            "Name match must outrank a description-only match");
    }

    // ──────────────────────────────────────────────────────────
    // Multi-term searches
    // ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Search_TwoTerms_PackageMatchingBothTermsRanksHigher()
    {
        // When no exact/prefix advantage exists, matching both terms doubles the score
        var packages = new[]
        {
            Pkg("snapd",          "The snap daemon"),                   // matches "snap" only → 111
            Pkg("snap-installer", "Install snap packages on the system"), // matches "snap"(111) + "install"(111) → 222
        };

        var results = PackageSearchService.ScoreAndRank(packages, "snap install");

        Assert.AreEqual("snap-installer", results[0].Package,
            "snap-installer matches both 'snap' and 'install' so must outrank snapd (matches 'snap' only)");
    }

    [TestMethod]
    public void Search_TwoTerms_ExactMatchBeatsMultiTermPartialMatch()
    {
        // "git" has exact match on the first term (1110), which dominates
        // "git-extras" has prefix+contains for "git" and contains for "extras" (122)
        // This is correct: exact match is the strongest signal
        var packages = new[]
        {
            Pkg("git-extras", "Extra git commands"),   // prefix+contains "git" + contains "extras" → 122
            Pkg("git",        "The git VCS tool"),     // exact+prefix+contains "git" → 1110
        };

        var results = PackageSearchService.ScoreAndRank(packages, "git extras");

        Assert.AreEqual("git", results[0].Package,
            "Exact package name match (1110 pts) overrides multi-term partial match (122 pts)");
    }

    // ──────────────────────────────────────────────────────────
    // Edge cases
    // ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Search_EmptyKeyword_ReturnsEmpty()
    {
        var packages = new[] { Pkg("snapd"), Pkg("curl") };

        var results = PackageSearchService.ScoreAndRank(packages, "");

        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var packages = new[] { Pkg("curl"), Pkg("wget") };

        var results = PackageSearchService.ScoreAndRank(packages, "snap");

        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void Search_TiedScore_TiebreakerIsAlphabetical()
    {
        // All three are prefix matches of "lib" → same score
        var packages = new[]
        {
            Pkg("libz-dev"),
            Pkg("liba-dev"),
            Pkg("libm-dev"),
        };

        var results = PackageSearchService.ScoreAndRank(packages, "lib");

        AssertOrder(results, "liba-dev", "libm-dev", "libz-dev");
    }

    [TestMethod]
    public void Search_SinglePackage_ExactMatch_Score1110()
    {
        // exact(1000) + prefix(100) + contains(10) = 1110
        // description does NOT contain "snap" so no +1
        var packages = new[] { Pkg("snap", "The snap command-line tool") };

        var results = PackageSearchService.ScoreAndRank(packages, "snap");

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("snap", results[0].Package);
    }

    [TestMethod]
    public void Search_WhitespaceOnlyQuery_ReturnsEmpty()
    {
        var packages = new[] { Pkg("snapd"), Pkg("curl") };

        var results = PackageSearchService.ScoreAndRank(packages, "   \t  ");

        Assert.AreEqual(0, results.Count);
    }

    // ──────────────────────────────────────────────────────────
    // Real-world scenario: the original bug report
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Reproduces the exact bug: searching "snap" returned aj-snapshot on page 1
    /// while snapd was buried on page 2+. The new algorithm must surface snapd
    /// as the very first result when "snap" is the exact package name of
    /// the most-relevant package.
    /// </summary>
    [TestMethod]
    public void RealWorld_SnapSearch_SnapdAppearsBeforeAllSnapshotPackages()
    {
        var packages = new[]
        {
            Pkg("aj-snapshot",                          "make snapshots of JACK connections"),
            Pkg("apt-btrfs-snapshot",                   "Automatically create snapshot on apt operations"),
            Pkg("boot-managed-by-snapd",                "Package marking the system boot as managed by snapd"),
            Pkg("fwupd-snap",                           "Transitional package - fwupd snap"),
            Pkg("gcc-snapshot",                         "SNAPSHOT of the GNU Compiler Collection"),
            Pkg("gir1.2-snapd-2",                       "Typelib file for libsnapd-glib1"),
            Pkg("gnome-snapshot",                       "Take pictures and videos from your webcam"),
            Pkg("gnome-software-plugin-snap",           "Snap support for GNOME Software"),
            Pkg("golang-github-snapcore-snapd-dev",     "snappy development go packages"),
            Pkg("snapd",                                "Daemon and tooling that enable snap packages"),
        };

        var results = PackageSearchService.ScoreAndRank(packages, "snap");

        var topThree = results.Take(3).Select(p => p.Package).ToArray();
        Assert.IsTrue(topThree.Contains("snapd"),
            $"'snapd' must appear in the top 3 results. Actual top 3: [{string.Join(", ", topThree)}]");
        Assert.AreEqual("snapd", results[0].Package,
            $"'snapd' must be the #1 result. Actual #1: '{results[0].Package}'");
    }
}
