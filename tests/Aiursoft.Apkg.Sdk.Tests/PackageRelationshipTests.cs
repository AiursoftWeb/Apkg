using System.Diagnostics;
using System.Xml.Linq;
using Aiursoft.Apkg.Sdk.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiursoft.Apkg.Sdk.Tests;

[TestClass]
public class PackageRelationshipTests
{
    private static readonly string[] Fields = ["Provides", "Conflicts", "Replaces", "Breaks"];

    private static XDocument CreateProjectXml(bool includeDefault)
    {
        return new XDocument(new XElement("Project",
            new XElement("PropertyGroup",
                new XElement("PackageName", "boot-relationships-test"),
                new XElement("PackageVersion", "1.0"),
                new XElement("PackageDescription", "Conditional boot package relationships"),
                new XElement("Maintainer", "Test <test@example.com>"),
                new XElement("TargetSuites", "noble resolute"),
                new XElement("TargetArchitectures", "amd64 arm64"),
                includeDefault ? Fields.Select(field => new XElement(field, "grub-common (= 2.14)")) : null),
            new XElement("PropertyGroup", Fields.SelectMany(field => new[]
            {
                new XElement(field, new XAttribute("Condition", "'$(Arch)' == 'amd64' and '$(Suite)' == 'resolute'"),
                    "grub-common (= 2.14), grub-efi-amd64 (= 2.14), grub-pc"),
                new XElement(field, new XAttribute("Condition", "'$(Architecture)' == 'arm64' and '$(Suite)' == 'resolute'"),
                    "grub-common (= 2.14), grub-efi-arm64 (= 2.14)")
            }))));
    }

    private static string BuildControl(XDocument xml, string arch, string suite, bool withUpstream = false)
    {
        var serializer = new AosprojSerializer();
        // CLI commands can rewrite projects, so evaluate after a round trip.
        var project = serializer.Deserialize(serializer.Serialize(serializer.Deserialize(xml)));
        var upstream = withUpstream ? Fields.ToDictionary(field => field, _ => "upstream-boot") : null;
        return DebBuilder.BuildControl(project, "1.0", arch, [], "", "", upstream,
            ConditionEvaluator.BuildContext("ubuntu", suite, arch));
    }

    private static void AssertRelationships(string control, string? expected)
    {
        foreach (var field in Fields)
        {
            if (expected == null)
                Assert.IsFalse(control.Contains($"{field}:"));
            else
                StringAssert.Contains(control, $"{field}: {expected}\n");
        }
    }

    [TestMethod]
    [DataRow("amd64", "resolute", "grub-common (= 2.14), grub-efi-amd64 (= 2.14), grub-pc")]
    [DataRow("arm64", "resolute", "grub-common (= 2.14), grub-efi-arm64 (= 2.14)")]
    [DataRow("arm64", "noble", "grub-common (= 2.14)")]
    public void ConditionalRelationships_SelectTargetAndReplaceWholeField(string arch, string suite, string expected)
    {
        AssertRelationships(BuildControl(CreateProjectXml(includeDefault: true), arch, suite, withUpstream: true), expected);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ConditionalRelationships_NoMatchUsesUpstreamOrOmitsField(bool withUpstream)
    {
        AssertRelationships(BuildControl(CreateProjectXml(includeDefault: false), "arm64", "noble", withUpstream),
            withUpstream ? "upstream-boot" : null);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    [DataRow("'$(Suite)' == 'resolute'")]
    public void ConditionalRelationships_LastMatchingDeclarationWinsAcrossGroups(string? condition)
    {
        var xml = CreateProjectXml(includeDefault: true);
        xml.Root!.Add(new XElement("PropertyGroup", Fields.Select(field => new XElement(field,
            condition == null ? null : new XAttribute("Condition", condition), "replacement-boot"))));
        AssertRelationships(BuildControl(xml, "arm64", "resolute"), "replacement-boot");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("  ")]
    public void ConditionalRelationships_EmptyOverrideClearsEarlierValueAndSurvivesRoundTrip(string value)
    {
        var xml = CreateProjectXml(includeDefault: true);
        xml.Root!.Add(new XElement("PropertyGroup", Fields.Select(field => new XElement(field,
            new XAttribute("Condition", "'$(Arch)' == 'arm64'"), value))));
        AssertRelationships(BuildControl(xml, "arm64", "resolute"), null);
        AssertRelationships(BuildControl(xml, "arm64", "resolute", withUpstream: true), "upstream-boot");
        AssertRelationships(BuildControl(xml, "amd64", "resolute"), "grub-common (= 2.14), grub-efi-amd64 (= 2.14), grub-pc");
    }

    [TestMethod]
    [DataRow("Provides")]
    [DataRow("Conflicts")]
    [DataRow("Replaces")]
    [DataRow("Breaks")]
    public void ConditionalRelationships_LintRejectsInvalidCondition(string field)
    {
        var xml = CreateProjectXml(includeDefault: false);
        xml.Descendants(field).First().SetAttributeValue("Condition", "invalid");
        var project = new AosprojSerializer().Deserialize(xml);
        var issues = new AosprojLinter(new ConditionEvaluator()).Lint(project, Path.GetTempPath());
        Assert.IsTrue(issues.Any(issue => issue.Level == AosprojLinter.Severity.Error &&
                                        issue.Message.Contains("Invalid condition 'invalid'")));
    }

    [TestMethod]
    [DataRow("  legacy (= 1),\n   legacy-helper  ", "  legacy (= 1),\n   legacy-helper  ")]
    [DataRow("", "upstream-boot")]
    [DataRow("  ", "upstream-boot")]
    public void ConditionalRelationships_LegacyPropertiesPreserveLastValueAndFormatting(string property, string expected)
    {
        var xml = CreateProjectXml(includeDefault: true);
        xml.Root!.Elements("PropertyGroup").Last().Remove();
        xml.Root.Add(new XElement("PropertyGroup", Fields.Select(field => new XElement(field, property))));
        AssertRelationships(BuildControl(xml, "amd64", "resolute", withUpstream: true), expected);
    }

    [TestMethod]
    public async Task ConditionalRelationships_BuildsBothArchitecturesWithoutLeakingMetadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"apkg-relationships-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var serializer = new AosprojSerializer();
            var path = Path.Combine(directory, "boot.aosproj");
            CreateProjectXml(includeDefault: true).Save(path);
            var project = await serializer.DeserializeFromFileAsync(path);
            var builder = new DebBuilder(new ConditionEvaluator(), NullLogger<DebBuilder>.Instance);
            foreach (var arch in new[] { "amd64", "arm64", "amd64" })
            {
                var output = Path.Combine(directory, "bin");
                await builder.BuildAsync(directory, project, "ubuntu", "resolute", arch, output);
                var deb = Path.Combine(output, $"boot-relationships-test_1.0_resolute_{arch}.deb");
                var start = new ProcessStartInfo("dpkg-deb") { RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add("--field");
                start.ArgumentList.Add(deb);
                using var process = Process.Start(start)!;
                var control = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                Assert.AreEqual(0, process.ExitCode, error);
                StringAssert.Contains(control, $"Architecture: {arch}\n");
                var expected = $"grub-common (= 2.14), grub-efi-{arch} (= 2.14)" + (arch == "amd64" ? ", grub-pc" : "");
                foreach (var field in Fields)
                    StringAssert.Contains(control, $"{field}: {expected}\n");
                Assert.IsFalse(control.Contains(arch == "amd64" ? "grub-efi-arm64" : "grub-efi-amd64"));
                if (arch == "arm64")
                    Assert.IsFalse(control.Contains("grub-pc"));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
