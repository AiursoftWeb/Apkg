using System.Text;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services;

namespace Aiursoft.Apkg.WebTests;

[TestClass]
public class AptMetadataServiceTests
{
    private AptMetadataService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = new AptMetadataService();
    }

    private static AptPackage MakePackage(string name = "test-pkg", string version = "1.0") => new()
    {
        Package = name,
        Version = version,
        Architecture = "amd64",
        Maintainer = "Test <test@example.com>",
        Description = "A test package",
        DescriptionMd5 = "abc123",
        Section = "utils",
        Priority = "optional",
        Origin = "Test",
        Bugs = "https://bugs.example.com",
        Filename = $"pool/main/t/test-pkg/{name}_{version}_amd64.deb",
        Size = "12345",
        MD5sum = "d41d8cd98f00b204e9800998ecf8427e",
        SHA1 = "da39a3ee5e6b4b0d3255bfef95601890afd80709",
        SHA256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        SHA512 = "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e",
        InstalledSize = "48",
        OriginSuite = "questing",
        OriginComponent = "main",
        Component = "main",
    };

    /// <summary>
    /// Regression test: the Packages file must NOT start with a UTF-8 BOM.
    /// A BOM causes apt to fail with "Encountered a section with no Package: header".
    /// </summary>
    [TestMethod]
    public async Task WritePackageEntry_DoesNotEmitUtf8Bom()
    {
        await using var ms = new MemoryStream();
        await using (var writer = new StreamWriter(ms, leaveOpen: true))
        {
            await _service.WritePackageEntryAsync(writer, MakePackage());
        }

        var bytes = ms.ToArray();
        Assert.IsTrue(bytes.Length > 3, "Output should not be empty.");

        // UTF-8 BOM is 0xEF 0xBB 0xBF
        var hasBom = bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        Assert.IsFalse(hasBom, "Packages file must not start with a UTF-8 BOM. " +
            "Use new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) instead of Encoding.UTF8.");
    }

    /// <summary>
    /// The first bytes of a Packages file must be exactly "Package: ".
    /// apt's RFC 822 parser requires this as the section header.
    /// </summary>
    [TestMethod]
    public async Task WritePackageEntry_FirstLineIsPackageHeader()
    {
        await using var ms = new MemoryStream();
        await using (var writer = new StreamWriter(ms, leaveOpen: true))
        {
            await _service.WritePackageEntryAsync(writer, MakePackage());
        }

        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.IsTrue(text.StartsWith("Package: ", StringComparison.Ordinal),
            $"First line must start with 'Package: ' (no BOM). Actual start: '{text[..Math.Min(20, text.Length)]}'");
    }

    /// <summary>
    /// Every stanza in a multi-package Packages file must start with "Package: ".
    /// This mirrors how apt splits the file on blank lines.
    /// </summary>
    [TestMethod]
    public async Task WritePackageEntry_MultipleEntries_EachSectionStartsWithPackageHeader()
    {
        await using var ms = new MemoryStream();
        await using (var writer = new StreamWriter(ms, leaveOpen: true))
        {
            await _service.WritePackageEntryAsync(writer, MakePackage("alpha"));
            await _service.WritePackageEntryAsync(writer, MakePackage("beta", "2.0"));
            await _service.WritePackageEntryAsync(writer, MakePackage("gamma", "3.0"));
        }

        var text = Encoding.UTF8.GetString(ms.ToArray());
        var sections = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.AreEqual(3, sections.Length, "Expected exactly 3 stanzas.");
        foreach (var section in sections)
        {
            var trimmed = section.TrimStart('\r', '\n');
            Assert.IsTrue(trimmed.StartsWith("Package: ", StringComparison.Ordinal),
                $"Stanza must begin with 'Package: ', got: {trimmed[..Math.Min(30, trimmed.Length)]}");
        }
    }

    /// <summary>
    /// Each stanza must end with a blank line so apt can correctly delimit entries.
    /// </summary>
    [TestMethod]
    public async Task WritePackageEntry_EndsWithBlankLine()
    {
        await using var ms = new MemoryStream();
        await using (var writer = new StreamWriter(ms, leaveOpen: true))
        {
            await _service.WritePackageEntryAsync(writer, MakePackage());
        }

        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.IsTrue(text.EndsWith("\n\n") || text.EndsWith("\r\n\r\n"),
            "Each package stanza must be terminated by a blank line.");
    }

    [TestMethod]
    public async Task WritePackageEntry_MultilineDescription()
    {
        var pkg = MakePackage();
        pkg.Description = "Summary\nLine 1\nLine 2";

        await using var ms = new MemoryStream();
        await using (var writer = new StreamWriter(ms, leaveOpen: true))
        {
            await _service.WritePackageEntryAsync(writer, pkg);
        }

        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.IsTrue(text.Contains("Description: Summary\n Line 1\n Line 2"),
            "Multiline description should be written with continuation spaces added. Actual:\n" + text);
    }
}
