using System.Xml.Linq;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;

namespace Aiursoft.Apkg.Sdk.Tests;

[TestClass]
public class AosprojSerializerTests
{
    private readonly AosprojSerializer _serializer = new();

    // ── Commit c35e691: Elem returns null for empty values ────────────────────

    [TestMethod]
    public void Serialize_EmptyProperty_IsOmitted()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0.0",
            PackageDescription = "",
            PackageHomepage = "",
            RepositoryUrl = null!
        };

        var doc = _serializer.Serialize(project);
        var xml = doc.ToString();

        Assert.IsFalse(xml.Contains("<PackageDescription"), "Empty PackageDescription should be omitted.");
        Assert.IsFalse(xml.Contains("<PackageHomepage"), "Empty PackageHomepage should be omitted.");
    }

    [TestMethod]
    public void Serialize_WhitespaceOnlyProperty_IsOmitted()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0.0",
            PackageDescription = "   ",
            PackageHomepage = "\t\n"
        };

        var doc = _serializer.Serialize(project);
        var xml = doc.ToString();

        Assert.IsFalse(xml.Contains("<PackageDescription"), "Whitespace-only values should be omitted.");
        Assert.IsFalse(xml.Contains("<PackageHomepage"), "Whitespace-only values should be omitted.");
    }

    [TestMethod]
    public void Serialize_NonEmptyProperty_IsIncluded()
    {
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "2.0.0",
            PackageDescription = "A test package"
        };

        var doc = _serializer.Serialize(project);
        Assert.IsNotNull(doc.Root);
        var pg = doc.Root.Element("PropertyGroup");
        Assert.IsNotNull(pg);
        Assert.AreEqual("my-pkg", pg.Element("PackageName")?.Value);
        Assert.AreEqual("2.0.0", pg.Element("PackageVersion")?.Value);
        Assert.AreEqual("A test package", pg.Element("PackageDescription")?.Value);
    }

    // ── Commit 22ae3d2: Basic project round-trip ──────────────────────────────

    [TestMethod]
    public void RoundTrip_BasicProperties()
    {
        var original = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "3.1.0",
            PackageDescription = "Test package",
            PackageAuthors = "Alice <alice@example.com>",
            Maintainer = "Bob <bob@example.com>",
            PackageHomepage = "https://example.com",
            LicenseType = "Apache-2.0",
            Component = "universe",
            TargetDistro = "ubuntu",
            TargetSuites = "jammy noble",
            TargetArchitectures = "amd64 arm64"
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual(original.PackageName, roundTripped.PackageName);
        Assert.AreEqual(original.PackageVersion, roundTripped.PackageVersion);
        Assert.AreEqual(original.PackageDescription, roundTripped.PackageDescription);
        Assert.AreEqual(original.PackageAuthors, roundTripped.PackageAuthors);
        Assert.AreEqual(original.Maintainer, roundTripped.Maintainer);
        Assert.AreEqual(original.PackageHomepage, roundTripped.PackageHomepage);
        Assert.AreEqual(original.LicenseType, roundTripped.LicenseType);
        Assert.AreEqual(original.Component, roundTripped.Component);
        Assert.AreEqual(original.TargetDistro, roundTripped.TargetDistro);
        Assert.AreEqual(original.TargetSuites, roundTripped.TargetSuites);
        Assert.AreEqual(original.TargetArchitectures, roundTripped.TargetArchitectures);
    }

    // ── Commit 8ca0ae7: IncludeScript item type ───────────────────────────────

    [TestMethod]
    public void RoundTrip_IncludeScript()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            IncludeScripts =
            {
                new IncludeScriptItem
                {
                    Source = "scripts/start.sh",
                    Target = "/usr/bin/start",
                    Condition = "'$(Arch)' == 'amd64'"
                }
            }
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual(1, roundTripped.IncludeScripts.Count);
        Assert.AreEqual("scripts/start.sh", roundTripped.IncludeScripts[0].Source);
        Assert.AreEqual("/usr/bin/start", roundTripped.IncludeScripts[0].Target);
        Assert.AreEqual("'$(Arch)' == 'amd64'", roundTripped.IncludeScripts[0].Condition);
    }

    // ── Include= attribute ───────────────────────────────────────────────────

    [TestMethod]
    public void Deserialize_IncludeAttribute()
    {
        var xml = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <PackageName>test</PackageName>
                <PackageVersion>1.0</PackageVersion>
                <PackageDescription>desc</PackageDescription>
              </PropertyGroup>
              <ItemGroup>
                <IncludeFile Include="src/file.txt" Target="/opt/file.txt" />
              </ItemGroup>
            </Project>
            """);

        var project = _serializer.Deserialize(xml);
        Assert.AreEqual(1, project.IncludeFiles.Count);
        Assert.AreEqual("src/file.txt", project.IncludeFiles[0].Source);
    }

    // ── Commit ddb0331: TargetDistro (singular) ───────────────────────────────

    [TestMethod]
    public void RoundTrip_TargetDistro()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0.0",
            PackageDescription = "desc",
            TargetDistro = "anduinos"
        };

        var doc = _serializer.Serialize(original);
        var xml = doc.ToString();
        Assert.IsTrue(xml.Contains("<TargetDistro>anduinos</TargetDistro>"));

        var roundTripped = _serializer.Deserialize(doc);
        Assert.AreEqual("anduinos", roundTripped.TargetDistro);
    }

    // ── Commit e03a598: Systemd units, PostInstallScript, PreRemoveScript ─────

    [TestMethod]
    public void RoundTrip_SystemdUnit()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            SystemdUnits =
            {
                new SystemdUnitItem
                {
                    Source = "deploy/myapp.service",
                    AutoEnable = true,
                    Condition = "'$(Distro)' == 'ubuntu'"
                }
            }
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual(1, roundTripped.SystemdUnits.Count);
        Assert.AreEqual("deploy/myapp.service", roundTripped.SystemdUnits[0].Source);
        Assert.IsTrue(roundTripped.SystemdUnits[0].AutoEnable);
    }

    [TestMethod]
    public void RoundTrip_SystemdUnitAutoEnableFalse()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            SystemdUnits =
            {
                new SystemdUnitItem
                {
                    Source = "deploy/monitor.service",
                    AutoEnable = false
                }
            }
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.IsFalse(roundTripped.SystemdUnits[0].AutoEnable);
    }

    [TestMethod]
    public void RoundTrip_PostInstallScript()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            PostInstallScripts =
            {
                new PostInstallScriptItem { Source = "scripts/postinst.sh" }
            }
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual(1, roundTripped.PostInstallScripts.Count);
        Assert.AreEqual("scripts/postinst.sh", roundTripped.PostInstallScripts[0].Source);
    }

    [TestMethod]
    public void RoundTrip_PreRemoveScript()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            PreRemoveScripts =
            {
                new PreRemoveScriptItem { Source = "scripts/prerm.sh" }
            }
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual(1, roundTripped.PreRemoveScripts.Count);
        Assert.AreEqual("scripts/prerm.sh", roundTripped.PreRemoveScripts[0].Source);
    }

    // ── Round-trip: full project with all item types ──────────────────────────

    [TestMethod]
    public void RoundTrip_FullProject()
    {
        var original = new AosprojProject
        {
            PackageName = "full-pkg",
            PackageVersion = "5.0.0",
            PackageDescription = "Full-featured package",
            Maintainer = "Team <team@example.com>",
            TargetDistro = "ubuntu",
            TargetSuites = "jammy noble",
            TargetArchitectures = "amd64",
            Dependencies =
            {
                new ConditionalValue { Value = "libc6", Condition = null },
                new ConditionalValue { Value = "libssl3", Condition = "'$(Suite)' == 'jammy'" }
            },
            IncludeFiles =
            {
                new IncludeFileItem { Source = "src/app", Target = "/usr/bin/app" }
            },
            IncludeFolders =
            {
                new IncludeFolderItem { Source = "data/", Target = "/usr/share/app" }
            },
            IncludeScripts =
            {
                new IncludeScriptItem { Source = "scripts/setup.sh", Target = "/usr/lib/app/setup" }
            },
            ConfFiles =
            {
                new ConfFileItem { Source = "config/app.conf", Target = "/etc/app/app.conf" }
            },
            PostInstallScripts =
            {
                new PostInstallScriptItem { Source = "scripts/postinst.sh" }
            },
            PreRemoveScripts =
            {
                new PreRemoveScriptItem { Source = "scripts/prerm.sh" }
            },
            SystemdUnits =
            {
                new SystemdUnitItem { Source = "deploy/app.service", AutoEnable = true }
            }
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual(original.PackageName, roundTripped.PackageName);
        Assert.AreEqual(original.PackageVersion, roundTripped.PackageVersion);
        Assert.AreEqual(2, roundTripped.Dependencies.Count);
        Assert.AreEqual(1, roundTripped.IncludeFiles.Count);
        Assert.AreEqual(1, roundTripped.IncludeFolders.Count);
        Assert.AreEqual(1, roundTripped.IncludeScripts.Count);
        Assert.AreEqual(1, roundTripped.ConfFiles.Count);
        Assert.AreEqual(1, roundTripped.PostInstallScripts.Count);
        Assert.AreEqual(1, roundTripped.PreRemoveScripts.Count);
        Assert.AreEqual(1, roundTripped.SystemdUnits.Count);
    }

    // ── Commit de829ad: Dependency as ItemGroup item ──────────────────────────

    [TestMethod]
    public void Deserialize_DependencyItemGroup()
    {
        var xml = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <PackageName>test</PackageName>
                <PackageVersion>1.0</PackageVersion>
                <PackageDescription>desc</PackageDescription>
              </PropertyGroup>
              <ItemGroup>
                <Dependency Include="libc6" />
                <Dependency Include="libssl3" Condition="'$(Suite)' == 'jammy'" />
              </ItemGroup>
            </Project>
            """);

        var project = _serializer.Deserialize(xml);
        Assert.AreEqual(2, project.Dependencies.Count);
        Assert.AreEqual("libc6", project.Dependencies[0].Value);
        Assert.AreEqual("libssl3", project.Dependencies[1].Value);
        Assert.AreEqual("'$(Suite)' == 'jammy'", project.Dependencies[1].Condition);
    }

    // ── FindProjectFile ───────────────────────────────────────────────────────

    [TestMethod]
    public void FindProjectFile_SingleFile_ReturnsPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var expected = Path.Combine(dir, "test.aosproj");
        File.WriteAllText(expected, "<Project/>");
        try
        {
            var result = AosprojSerializer.FindProjectFile(dir);
            Assert.AreEqual(expected, result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void FindProjectFile_NoFile_Throws()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            try
            {
                AosprojSerializer.FindProjectFile(dir);
                Assert.Fail("Expected FileNotFoundException was not thrown.");
            }
            catch (FileNotFoundException) { /* expected */ }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void FindProjectFile_MultipleFiles_Throws()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.aosproj"), "<Project/>");
        File.WriteAllText(Path.Combine(dir, "b.aosproj"), "<Project/>");
        try
        {
            try
            {
                AosprojSerializer.FindProjectFile(dir);
                Assert.Fail("Expected InvalidOperationException was not thrown.");
            }
            catch (InvalidOperationException) { /* expected */ }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── Condition attributes on items ─────────────────────────────────────────

    [TestMethod]
    public void Serialize_ItemWithCondition_HasConditionAttribute()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            IncludeFiles =
            {
                new IncludeFileItem
                {
                    Source = "src/x86-lib.so",
                    Target = "/usr/lib/lib.so",
                    Condition = "'$(Arch)' == 'amd64'"
                }
            }
        };

        var doc = _serializer.Serialize(project);
        var xml = doc.ToString();

        Assert.IsTrue(xml.Contains("Condition="), "Item with condition should have a Condition attribute.");
    }

    [TestMethod]
    public void Serialize_ItemWithoutCondition_HasNoConditionAttribute()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            IncludeFiles =
            {
                new IncludeFileItem
                {
                    Source = "src/file.txt",
                    Target = "/opt/file.txt"
                }
            }
        };

        var doc = _serializer.Serialize(project);
        var element = doc.Descendants("IncludeFile").First();
        Assert.IsNull(element.Attribute("Condition"));
    }
}
