using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.Apkg.Sdk.Tests;

[TestClass]
public class DebPackageValidatorTests
{
    // ── ParseRfc822 ───────────────────────────────────────────────────────────

    [TestMethod]
    public void ParseRfc822_BasicField()
    {
        var result = DebPackageValidator.ParseRfc822("Package: mypkg\nVersion: 1.0\n");

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("mypkg", result["Package"]);
        Assert.AreEqual("1.0", result["Version"]);
    }

    [TestMethod]
    public void ParseRfc822_CaseInsensitiveKeys()
    {
        var result = DebPackageValidator.ParseRfc822("Package: mypkg\nPACKAGE: other\n");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("other", result["package"]);
    }

    [TestMethod]
    public void ParseRfc822_ContinuationLine()
    {
        var input = "Description: This is a long description\n that continues\n on multiple lines\n";
        var result = DebPackageValidator.ParseRfc822(input);

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result["Description"].Contains("continues"));
        Assert.IsTrue(result["Description"].Contains("multiple lines"));
    }

    [TestMethod]
    public void ParseRfc822_MultipleFields()
    {
        var input = "Package: mypkg\nVersion: 2.0\nArchitecture: amd64\nMaintainer: Me <me@example.com>\n";
        var result = DebPackageValidator.ParseRfc822(input);

        Assert.AreEqual(4, result.Count);
        Assert.AreEqual("mypkg", result["Package"]);
        Assert.AreEqual("2.0", result["Version"]);
        Assert.AreEqual("amd64", result["Architecture"]);
        Assert.AreEqual("Me <me@example.com>", result["Maintainer"]);
    }

    [TestMethod]
    public void ParseRfc822_EmptyInput()
    {
        var result = DebPackageValidator.ParseRfc822("");
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ParseRfc822_WhitespaceInput()
    {
        var result = DebPackageValidator.ParseRfc822("   \n  \n");
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ParseRfc822_IgnoresLinesWithoutColon()
    {
        var result = DebPackageValidator.ParseRfc822("Package: test\nThis line has no colon\nVersion: 1.0\n");
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("test", result["Package"]);
        Assert.AreEqual("1.0", result["Version"]);
    }

    [TestMethod]
    public void ParseRfc822_BlankLinesBetweenRecords()
    {
        var input = "Package: first\n\nPackage: second\nVersion: 1.0\n";
        var result = DebPackageValidator.ParseRfc822(input);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("second", result["Package"]);
        Assert.AreEqual("1.0", result["Version"]);
    }

    [TestMethod]
    public void ParseRfc822_TabContinuationLine()
    {
        var input = "Description: First line\n\tcontinued with tab\n more continuation\n";
        var result = DebPackageValidator.ParseRfc822(input);

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result["Description"].Contains("continued with tab"));
    }

    [TestMethod]
    public void ParseRfc822_TrimsValues()
    {
        var result = DebPackageValidator.ParseRfc822("Package:   spaced-value   \n");

        Assert.AreEqual("spaced-value", result["Package"]);
    }

    [TestMethod]
    public void ParseRfc822_ColonInValue()
    {
        var result = DebPackageValidator.ParseRfc822("Url: https://example.com\n");

        Assert.AreEqual("https://example.com", result["Url"]);
    }

    [TestMethod]
    public void ParseRfc822_ColonAtStart()
    {
        var result = DebPackageValidator.ParseRfc822(": empty-key\nPackage: test\n");

        Assert.AreEqual(1, result.Count); // line with colon at index 0 is skipped
        Assert.AreEqual("test", result["Package"]);
    }

    // ── GetRequiredField ──────────────────────────────────────────────────────

    [TestMethod]
    public void GetRequiredField_ReturnsValue()
    {
        var control = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Package"] = "mypkg"
        };

        var result = DebPackageValidator.GetRequiredField(control, "Package", "test.deb");
        Assert.AreEqual("mypkg", result);
    }

    [TestMethod]
    public void GetRequiredField_TrimsValue()
    {
        var control = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Package"] = "  trimmed  "
        };

        var result = DebPackageValidator.GetRequiredField(control, "Package", "test.deb");
        Assert.AreEqual("trimmed", result);
    }

    [TestMethod]
    public void GetRequiredField_MissingField_Throws()
    {
        var control = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Package"] = "mypkg"
        };

        try
        {
            DebPackageValidator.GetRequiredField(control, "Version", "test.deb");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("Version"));
            Assert.IsTrue(ex.Message.Contains("test.deb"));
        }
    }

    [TestMethod]
    public void GetRequiredField_EmptyField_Throws()
    {
        var control = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Version"] = ""
        };

        try
        {
            DebPackageValidator.GetRequiredField(control, "Version", "pkg.deb");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException) { /* expected */ }
    }

    [TestMethod]
    public void GetRequiredField_WhitespaceOnlyField_Throws()
    {
        var control = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Version"] = "   "
        };

        try
        {
            DebPackageValidator.GetRequiredField(control, "Version", "pkg.deb");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException) { /* expected */ }
    }
}
