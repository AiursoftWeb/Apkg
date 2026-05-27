using Aiursoft.Apkg.Sdk.Services;

namespace Aiursoft.Apkg.Sdk.Tests;

[TestClass]
public class SystemInfoProviderTests
{
    private readonly SystemInfoProvider _provider = new();

    // ── Unquote ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Unquote_QuotedString_RemovesQuotes()
    {
        var result = SystemInfoProvider.Unquote("\"ubuntu\"");
        Assert.AreEqual("ubuntu", result);
    }

    [TestMethod]
    public void Unquote_UnquotedString_ReturnsAsIs()
    {
        var result = SystemInfoProvider.Unquote("ubuntu");
        Assert.AreEqual("ubuntu", result);
    }

    [TestMethod]
    public void Unquote_SingleChar_ReturnsAsIs()
    {
        var result = SystemInfoProvider.Unquote("x");
        Assert.AreEqual("x", result);
    }

    [TestMethod]
    public void Unquote_EmptyString_ReturnsEmpty()
    {
        var result = SystemInfoProvider.Unquote("");
        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void Unquote_OnlyOneQuote_ReturnsAsIs()
    {
        var result = SystemInfoProvider.Unquote("\"ubuntu");
        Assert.AreEqual("\"ubuntu", result);
    }

    // ── GetOsInfo ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetOsInfo_ReturnsDistroAndSuite()
    {
        // Should work on any Debian/Ubuntu system (this is Linux)
        var (distro, suite) = _provider.GetOsInfo();

        Assert.IsFalse(string.IsNullOrWhiteSpace(distro), "Distro should be non-empty.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(suite), "Suite should be non-empty.");
    }

    [TestMethod]
    public void GetOsInfo_DistroIsKnown()
    {
        var (distro, _) = _provider.GetOsInfo();
        Assert.IsTrue(distro is "ubuntu" or "debian" or "anduinos",
            $"Expected a Debian-family distro, got '{distro}'.");
    }

    // ── GetArchitectureAsync ──────────────────────────────────────────────────

    [TestMethod]
    public async Task GetArchitectureAsync_ReturnsNonEmpty()
    {
        var arch = await _provider.GetArchitectureAsync();

        Assert.IsFalse(string.IsNullOrWhiteSpace(arch), "Architecture should be non-empty.");
    }

    [TestMethod]
    public async Task GetArchitectureAsync_IsKnownArch()
    {
        var arch = await _provider.GetArchitectureAsync();

        Assert.IsTrue(
            arch is "amd64" or "arm64" or "riscv64" or "i386" or "armhf" or "s390x" or "ppc64el",
            $"Expected a known Debian arch, got '{arch}'.");
    }
}
