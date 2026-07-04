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
    public void RoundTrip_SystemdUnitUsePreset()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            SystemdUnits =
            {
                new SystemdUnitItem
                {
                    Source = "deploy/zram.service",
                    AutoEnable = true,
                    UsePreset = true
                }
            }
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual(1, roundTripped.SystemdUnits.Count);
        Assert.AreEqual("deploy/zram.service", roundTripped.SystemdUnits[0].Source);
        Assert.IsTrue(roundTripped.SystemdUnits[0].UsePreset,
            "UsePreset=true should survive serializer round-trip");
    }

    [TestMethod]
    public void RoundTrip_SystemdUnitUsePresetDefaultFalse()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            SystemdUnits =
            {
                new SystemdUnitItem
                {
                    Source = "deploy/app.service",
                    AutoEnable = true
                    // UsePreset defaults to false
                }
            }
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.IsFalse(roundTripped.SystemdUnits[0].UsePreset,
            "UsePreset should default to false when not explicitly set");
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

    [TestMethod]
    public void RoundTrip_PreInstallScript()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            PreInstallScripts =
            {
                new PreInstallScriptItem { Source = "scripts/preinst.sh" }
            }
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual(1, roundTripped.PreInstallScripts.Count);
        Assert.AreEqual("scripts/preinst.sh", roundTripped.PreInstallScripts[0].Source);
    }

    [TestMethod]
    public void RoundTrip_PostRemoveScript()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            PostRemoveScripts =
            {
                new PostRemoveScriptItem { Source = "scripts/postrm.sh" }
            }
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual(1, roundTripped.PostRemoveScripts.Count);
        Assert.AreEqual("scripts/postrm.sh", roundTripped.PostRemoveScripts[0].Source);
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
            PreInstallScripts =
            {
                new PreInstallScriptItem { Source = "scripts/preinst.sh" }
            },
            PostRemoveScripts =
            {
                new PostRemoveScriptItem { Source = "scripts/postrm.sh" }
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
        Assert.AreEqual(1, roundTripped.PreInstallScripts.Count);
        Assert.AreEqual(1, roundTripped.PostRemoveScripts.Count);
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

    // ── UpstreamSource properties ─────────────────────────────────────────────

    [TestMethod]
    public void RoundTrip_UpstreamSourceProperties()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0.0",
            PackageDescription = "desc",
            UpstreamUrls = [new() { Value = "http://archive.ubuntu.com/ubuntu" }],
            UpstreamDistro = "ubuntu",
            UpstreamPackage = "base-files",
            UpstreamSuite = "$(Suite)",
            UpstreamComponent = "main",
            UpstreamArch = "all"
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual(original.UpstreamUrls.Single().Value, roundTripped.UpstreamUrls.Single().Value);
        Assert.AreEqual(original.UpstreamDistro, roundTripped.UpstreamDistro);
        Assert.AreEqual(original.UpstreamPackage, roundTripped.UpstreamPackage);
        Assert.AreEqual(original.UpstreamSuite, roundTripped.UpstreamSuite);
        Assert.AreEqual(original.UpstreamComponent, roundTripped.UpstreamComponent);
        Assert.AreEqual(original.UpstreamArch, roundTripped.UpstreamArch);
    }

    [TestMethod]
    public void Serialize_UpstreamFieldsOmittedWhenEmpty()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0.0",
            PackageDescription = "desc"
        };

        var doc = _serializer.Serialize(project);
        var xml = doc.ToString();

        Assert.IsFalse(xml.Contains("UpstreamUrl"), "Empty UpstreamUrl should be omitted.");
        Assert.IsFalse(xml.Contains("UpstreamPackage"), "Empty UpstreamPackage should be omitted.");
    }

    [TestMethod]
    public void Deserialize_UpstreamSourceFromXml()
    {
        var xml = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <PackageName>base-files</PackageName>
                <PackageVersion>13</PackageVersion>
                <PackageDescription>AnduinOS base files</PackageDescription>
                <UpstreamUrl>http://archive.ubuntu.com/ubuntu</UpstreamUrl>
                <UpstreamDistro>ubuntu</UpstreamDistro>
                <UpstreamPackage>base-files</UpstreamPackage>
                <UpstreamSuite>$(Suite)</UpstreamSuite>
                <UpstreamComponent>main</UpstreamComponent>
                <UpstreamArch>amd64</UpstreamArch>
              </PropertyGroup>
            </Project>
            """);

        var project = _serializer.Deserialize(xml);

        Assert.AreEqual("http://archive.ubuntu.com/ubuntu", project.UpstreamUrls.Single().Value);
        Assert.AreEqual("ubuntu", project.UpstreamDistro);
        Assert.AreEqual("base-files", project.UpstreamPackage);
        Assert.AreEqual("$(Suite)", project.UpstreamSuite);
        Assert.AreEqual("main", project.UpstreamComponent);
        Assert.AreEqual("amd64", project.UpstreamArch);
        Assert.IsTrue(project.HasUpstreamSource);
    }

    [TestMethod]
    public void Deserialize_MultipleUpstreamUrls_WithConditions()
    {
        var xml = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <PackageName>base-files</PackageName>
                <PackageVersion>13</PackageVersion>
                <PackageDescription>AnduinOS base files</PackageDescription>
                <UpstreamUrl Condition="'$(Arch)' == 'amd64'">http://archive.ubuntu.com/ubuntu</UpstreamUrl>
                <UpstreamUrl Condition="'$(Arch)' == 'arm64'">http://ports.ubuntu.com/ubuntu-ports</UpstreamUrl>
                <UpstreamDistro>ubuntu</UpstreamDistro>
                <UpstreamPackage>base-files</UpstreamPackage>
                <UpstreamSuite>$(Suite)</UpstreamSuite>
              </PropertyGroup>
            </Project>
            """);

        var project = _serializer.Deserialize(xml);

        Assert.AreEqual(2, project.UpstreamUrls.Count);
        Assert.AreEqual("http://archive.ubuntu.com/ubuntu", project.UpstreamUrls[0].Value);
        Assert.AreEqual("'$(Arch)' == 'amd64'", project.UpstreamUrls[0].Condition);
        Assert.AreEqual("http://ports.ubuntu.com/ubuntu-ports", project.UpstreamUrls[1].Value);
        Assert.AreEqual("'$(Arch)' == 'arm64'", project.UpstreamUrls[1].Condition);
    }

    [TestMethod]
    public void Serialize_MultipleUpstreamUrls_RoundTrip()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            UpstreamUrls =
            [
                new ConditionalValue { Value = "http://a", Condition = "A" },
                new ConditionalValue { Value = "http://b", Condition = "B" }
            ]
        };

        var doc = _serializer.Serialize(project);
        var xml = doc.ToString();
        Assert.IsTrue(xml.Contains("Condition=\"A\""));
        Assert.IsTrue(xml.Contains("Condition=\"B\""));

        var roundTripped = _serializer.Deserialize(doc);
        Assert.AreEqual(2, roundTripped.UpstreamUrls.Count);
        Assert.AreEqual("http://a", roundTripped.UpstreamUrls[0].Value);
        Assert.AreEqual("A", roundTripped.UpstreamUrls[0].Condition);
        Assert.AreEqual("http://b", roundTripped.UpstreamUrls[1].Value);
        Assert.AreEqual("B", roundTripped.UpstreamUrls[1].Condition);
    }

    [TestMethod]
    public void Serialize_UpstreamSuiteMapping_RoundTrip()
    {
        var xml = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <PackageName>test</PackageName>
                <PackageVersion>1.0</PackageVersion>
                <PackageDescription>desc</PackageDescription>
                <UpstreamSuiteMapping>noble-addon=noble questing-addon=questing</UpstreamSuiteMapping>
              </PropertyGroup>
            </Project>
            """);

        var project = _serializer.Deserialize(xml);
        Assert.AreEqual("noble-addon=noble questing-addon=questing", project.UpstreamSuiteMapping);

        var doc = _serializer.Serialize(project);
        var roundTripped = _serializer.Deserialize(doc);
        Assert.AreEqual("noble-addon=noble questing-addon=questing", roundTripped.UpstreamSuiteMapping);
    }

    [TestMethod]
    public void Deserialize_UpstreamSuiteMapping_DefaultsToEmpty()
    {
        var xml = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <PackageName>test</PackageName>
                <PackageVersion>1.0</PackageVersion>
                <PackageDescription>desc</PackageDescription>
              </PropertyGroup>
            </Project>
            """);

        var project = _serializer.Deserialize(xml);
        Assert.AreEqual("", project.UpstreamSuiteMapping);
    }

    // ── File API round-trip ──────────────────────────────────────────────────

    [TestMethod]
    public async Task DeserializeFromFileAsync_RoundTrip()
    {
        var original = new AosprojProject
        {
            PackageName = "file-read-pkg",
            PackageVersion = "2.0.0",
            PackageDescription = "File read test",
            Maintainer = "File <file@example.com>",
            TargetDistro = "debian",
            TargetSuites = "bookworm",
            IncludeFiles =
            {
                new IncludeFileItem { Source = "src/app", Target = "/usr/bin/app" }
            }
        };

        var path = Path.GetTempFileName();
        try
        {
            await _serializer.SerializeToFileAsync(original, path);
            var roundTripped = await _serializer.DeserializeFromFileAsync(path);

            Assert.AreEqual(original.PackageName, roundTripped.PackageName);
            Assert.AreEqual(original.PackageVersion, roundTripped.PackageVersion);
            Assert.AreEqual("debian", roundTripped.TargetDistro);
            Assert.AreEqual(1, roundTripped.IncludeFiles.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task DeserializeFromFileAsync_ReadsSavedXml()
    {
        var project = new AosprojProject
        {
            PackageName = "saved-pkg",
            PackageVersion = "3.0.0",
            PackageDescription = "Save test",
            TargetSuites = "jammy noble",
            Provides = "virtual-thing",
            PrebuildCommands =
            {
                new PrebuildCommandItem { Run = "make" }
            }
        };

        var path = Path.GetTempFileName();
        try
        {
            await _serializer.SerializeToFileAsync(project, path);
            var read = await _serializer.DeserializeFromFileAsync(path);

            Assert.AreEqual("saved-pkg", read.PackageName);
            Assert.AreEqual("3.0.0", read.PackageVersion);
            Assert.AreEqual("jammy noble", read.TargetSuites);
            Assert.AreEqual("virtual-thing", read.Provides);
            Assert.AreEqual(1, read.PrebuildCommands.Count);
            Assert.AreEqual("make", read.PrebuildCommands[0].Run);
        }
        finally
        {
            File.Delete(path);
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

    // ── Section / Priority / Breaks round-trip ──────────────────────────────

    [TestMethod]
    public void RoundTrip_SectionPriorityBreaks()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            Section = "admin",
            Priority = "required",
            Breaks = "old-pkg (<< 2.0)"
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual("admin", roundTripped.Section);
        Assert.AreEqual("required", roundTripped.Priority);
        Assert.AreEqual("old-pkg (<< 2.0)", roundTripped.Breaks);
    }

    [TestMethod]
    public void Deserialize_SectionPriorityBreaks_DefaultsWhenMissing()
    {
        var xml = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <PackageName>test</PackageName>
                <PackageVersion>1.0</PackageVersion>
                <PackageDescription>desc</PackageDescription>
              </PropertyGroup>
            </Project>
            """);

        var project = _serializer.Deserialize(xml);
        // Section/Priority default to empty (not set) — BuildControl applies
        // Debian-standard fallbacks "utils"/"optional" at build time.
        Assert.AreEqual("", project.Section);
        Assert.AreEqual("", project.Priority);
        Assert.AreEqual("", project.Breaks);
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

    // ── Mode attribute support ─────────────────────────────────────────────────

    [TestMethod]
    public void Deserialize_IncludeFile_WithMode()
    {
        var xml = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <PackageName>test</PackageName>
                <PackageVersion>1.0</PackageVersion>
                <PackageDescription>desc</PackageDescription>
              </PropertyGroup>
              <ItemGroup>
                <IncludeFile Include="src/app" Target="/usr/bin/app" Mode="755" />
              </ItemGroup>
            </Project>
            """);

        var project = _serializer.Deserialize(xml);
        Assert.AreEqual(1, project.IncludeFiles.Count);
        var item = project.IncludeFiles[0];
        Assert.IsTrue(item.Mode.HasValue);
        var mode = item.Mode!.Value;
        Assert.IsTrue(mode.HasFlag(UnixFileMode.UserRead));
        Assert.IsTrue(mode.HasFlag(UnixFileMode.UserWrite));
        Assert.IsTrue(mode.HasFlag(UnixFileMode.UserExecute));
        Assert.IsTrue(mode.HasFlag(UnixFileMode.GroupRead));
        Assert.IsTrue(mode.HasFlag(UnixFileMode.GroupExecute));
        Assert.IsTrue(mode.HasFlag(UnixFileMode.OtherRead));
        Assert.IsTrue(mode.HasFlag(UnixFileMode.OtherExecute));
        Assert.IsFalse(mode.HasFlag(UnixFileMode.GroupWrite));
        Assert.IsFalse(mode.HasFlag(UnixFileMode.OtherWrite));
    }

    [TestMethod]
    public void Deserialize_IncludeFile_WithoutMode_IsNull()
    {
        var xml = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <PackageName>test</PackageName>
                <PackageVersion>1.0</PackageVersion>
                <PackageDescription>desc</PackageDescription>
              </PropertyGroup>
              <ItemGroup>
                <IncludeFile Include="src/logo.svg" Target="/opt/logo.svg" />
              </ItemGroup>
            </Project>
            """);

        var project = _serializer.Deserialize(xml);
        Assert.AreEqual(1, project.IncludeFiles.Count);
        Assert.IsNull(project.IncludeFiles[0].Mode);
    }

    [TestMethod]
    public void Deserialize_IncludeFile_Mode600()
    {
        var xml = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <PackageName>test</PackageName>
                <PackageVersion>1.0</PackageVersion>
                <PackageDescription>desc</PackageDescription>
              </PropertyGroup>
              <ItemGroup>
                <IncludeFile Include="src/secret" Target="/etc/secret" Mode="600" />
              </ItemGroup>
            </Project>
            """);

        var project = _serializer.Deserialize(xml);
        var item = project.IncludeFiles[0];
        var mode = item.Mode!.Value;
        Assert.IsTrue(mode.HasFlag(UnixFileMode.UserRead));
        Assert.IsTrue(mode.HasFlag(UnixFileMode.UserWrite));
        Assert.IsFalse(mode.HasFlag(UnixFileMode.UserExecute));
        Assert.IsFalse(mode.HasFlag(UnixFileMode.GroupRead));
        Assert.IsFalse(mode.HasFlag(UnixFileMode.GroupWrite));
        Assert.IsFalse(mode.HasFlag(UnixFileMode.GroupExecute));
        Assert.IsFalse(mode.HasFlag(UnixFileMode.OtherRead));
        Assert.IsFalse(mode.HasFlag(UnixFileMode.OtherWrite));
        Assert.IsFalse(mode.HasFlag(UnixFileMode.OtherExecute));
    }

    [TestMethod]
    public void Serialize_IncludeFile_WithMode()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            IncludeFiles =
            {
                new IncludeFileItem
                {
                    Source = "src/app",
                    Target = "/usr/bin/app",
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                           UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                           UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                }
            }
        };

        var doc = _serializer.Serialize(project);
        var element = doc.Descendants("IncludeFile").First();
        Assert.AreEqual("755", element.Attribute("Mode")?.Value);
    }

    [TestMethod]
    public void Serialize_IncludeFile_WithoutMode_OmitsAttribute()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            IncludeFiles =
            {
                new IncludeFileItem
                {
                    Source = "src/app",
                    Target = "/usr/bin/app"
                }
            }
        };

        var doc = _serializer.Serialize(project);
        var element = doc.Descendants("IncludeFile").First();
        Assert.IsNull(element.Attribute("Mode"));
    }

    [TestMethod]
    public void RoundTrip_IncludeFile_WithMode()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            IncludeFiles =
            {
                new IncludeFileItem
                {
                    Source = "src/app",
                    Target = "/usr/bin/app",
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite |
                           UnixFileMode.GroupRead | UnixFileMode.OtherRead
                }
            }
        };

        var doc = _serializer.Serialize(project);
        var deserialized = _serializer.Deserialize(doc);
        var item = deserialized.IncludeFiles[0];
        Assert.IsTrue(item.Mode.HasValue);
        var mode = item.Mode!.Value;
        Assert.IsTrue(mode.HasFlag(UnixFileMode.UserRead));
        Assert.IsFalse(mode.HasFlag(UnixFileMode.UserExecute));
        Assert.IsFalse(mode.HasFlag(UnixFileMode.GroupExecute));
        Assert.IsFalse(mode.HasFlag(UnixFileMode.OtherExecute));
    }

    [TestMethod]
    public void Deserialize_ConfFile_WithMode()
    {
        var xml = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <PackageName>test</PackageName>
                <PackageVersion>1.0</PackageVersion>
                <PackageDescription>desc</PackageDescription>
              </PropertyGroup>
              <ItemGroup>
                <ConfFile Include="cfg.json" Target="/etc/app/cfg.json" Mode="644" />
              </ItemGroup>
            </Project>
            """);

        var project = _serializer.Deserialize(xml);
        var item = project.ConfFiles[0];
        Assert.IsTrue(item.Mode.HasValue);
        var mode = item.Mode!.Value;
        Assert.IsTrue(mode.HasFlag(UnixFileMode.UserRead));
        Assert.IsTrue(mode.HasFlag(UnixFileMode.UserWrite));
        Assert.IsFalse(mode.HasFlag(UnixFileMode.UserExecute));
        Assert.IsTrue(mode.HasFlag(UnixFileMode.GroupRead));
        Assert.IsTrue(mode.HasFlag(UnixFileMode.OtherRead));
    }

    // ── UpstreamSignedBy round-trip ───────────────────────────────────────────

    [TestMethod]
    public void RoundTrip_UpstreamSignedBy()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0.0",
            PackageDescription = "desc",
            UpstreamSignedBy = "keys/anduinos-archive-keyring.gpg"
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual("keys/anduinos-archive-keyring.gpg", roundTripped.UpstreamSignedBy);
    }

    [TestMethod]
    public void Serialize_UpstreamSignedBy_OmittedWhenEmpty()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0.0",
            PackageDescription = "desc"
        };

        var doc = _serializer.Serialize(project);
        var xml = doc.ToString();

        Assert.IsFalse(xml.Contains("UpstreamSignedBy"), "Empty UpstreamSignedBy should be omitted.");
    }

    [TestMethod]
    public void Deserialize_UpstreamSignedBy_FromXml()
    {
        var xml = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <PackageName>test</PackageName>
                <PackageVersion>1.0</PackageVersion>
                <PackageDescription>desc</PackageDescription>
                <UpstreamSignedBy>keys/anduinos-archive-keyring.gpg</UpstreamSignedBy>
              </PropertyGroup>
            </Project>
            """);

        var project = _serializer.Deserialize(xml);
        Assert.AreEqual("keys/anduinos-archive-keyring.gpg", project.UpstreamSignedBy);
    }

    // ── DependencyCheckSource round-trip ────────────────────────────────────────

    [TestMethod]
    public void RoundTrip_DependencyCheckSources()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0.0",
            PackageDescription = "desc",
            DependencyCheckSources =
            {
                new DependencyCheckSourceItem
                {
                    Url = "https://archive.ubuntu.com/ubuntu",
                    SuiteMap = "noble-addon=noble questing-addon=questing"
                },
                new DependencyCheckSourceItem
                {
                    Url = "https://apkg-dev.aiursoft.com/artifacts/anduinos",
                    SuiteMap = "noble-addon=noble-addon questing-addon=questing-addon",
                    Condition = "'$(Suite)' != 'jammy'"
                }
            }
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual(2, roundTripped.DependencyCheckSources.Count);
        Assert.AreEqual("https://archive.ubuntu.com/ubuntu", roundTripped.DependencyCheckSources[0].Url);
        Assert.AreEqual("noble-addon=noble questing-addon=questing", roundTripped.DependencyCheckSources[0].SuiteMap);
        Assert.IsNull(roundTripped.DependencyCheckSources[0].Condition);

        Assert.AreEqual("https://apkg-dev.aiursoft.com/artifacts/anduinos", roundTripped.DependencyCheckSources[1].Url);
        Assert.AreEqual("noble-addon=noble-addon questing-addon=questing-addon", roundTripped.DependencyCheckSources[1].SuiteMap);
        Assert.AreEqual("'$(Suite)' != 'jammy'", roundTripped.DependencyCheckSources[1].Condition);
    }

    [TestMethod]
    public void Serialize_EmptyDependencyCheckSources_Omitted()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0.0",
            PackageDescription = "desc"
        };

        var doc = _serializer.Serialize(project);
        var xml = doc.ToString();

        Assert.IsFalse(xml.Contains("DependencyCheckSource"), "Empty DependencyCheckSources should be omitted.");
    }

    [TestMethod]
    public void Deserialize_DependencyCheckSource_FromXml()
    {
        var xml = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <PackageName>test</PackageName>
                <PackageVersion>1.0</PackageVersion>
                <PackageDescription>desc</PackageDescription>
              </PropertyGroup>
              <ItemGroup>
                <DependencyCheckSource
                    Url="https://archive.ubuntu.com/ubuntu"
                    SuiteMap="noble-addon=noble questing-addon=questing" />
                <DependencyCheckSource
                    Url="https://apkg-dev.aiursoft.com/artifacts/anduinos"
                    SuiteMap="noble-addon=noble-addon questing-addon=questing-addon"
                    Condition="'$(Distro)' == 'anduinos'" />
              </ItemGroup>
            </Project>
            """);

        var project = _serializer.Deserialize(xml);

        Assert.AreEqual(2, project.DependencyCheckSources.Count);
        Assert.AreEqual("https://archive.ubuntu.com/ubuntu", project.DependencyCheckSources[0].Url);
        Assert.AreEqual("noble-addon=noble questing-addon=questing", project.DependencyCheckSources[0].SuiteMap);
        Assert.IsNull(project.DependencyCheckSources[0].Condition);

        Assert.AreEqual("https://apkg-dev.aiursoft.com/artifacts/anduinos", project.DependencyCheckSources[1].Url);
        Assert.AreEqual("noble-addon=noble-addon questing-addon=questing-addon", project.DependencyCheckSources[1].SuiteMap);
        Assert.AreEqual("'$(Distro)' == 'anduinos'", project.DependencyCheckSources[1].Condition);
    }

    // ── DpkgTrigger round-trip ──────────────────────────────────────────────

    [TestMethod]
    public void RoundTrip_DpkgTrigger_DefaultType()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            DpkgTriggers =
            {
                new DpkgTriggerItem
                {
                    Name = "/etc/dconf/db"
                    // Type omitted — should default to "interest-noawait"
                }
            }
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual(1, roundTripped.DpkgTriggers.Count);
        Assert.AreEqual("/etc/dconf/db", roundTripped.DpkgTriggers[0].Name);
        Assert.AreEqual("interest-noawait", roundTripped.DpkgTriggers[0].Type);
    }

    [TestMethod]
    public void RoundTrip_DpkgTrigger_ExplicitType()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            DpkgTriggers =
            {
                new DpkgTriggerItem
                {
                    Name = "/usr/share/glib-2.0/schemas",
                    Type = "interest"
                }
            }
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual(1, roundTripped.DpkgTriggers.Count);
        Assert.AreEqual("/usr/share/glib-2.0/schemas", roundTripped.DpkgTriggers[0].Name);
        Assert.AreEqual("interest", roundTripped.DpkgTriggers[0].Type);
    }

    [TestMethod]
    public void RoundTrip_DpkgTrigger_ActivateNoAwait()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            DpkgTriggers =
            {
                new DpkgTriggerItem
                {
                    Name = "/etc/ld.so.conf.d",
                    Type = "activate-noawait"
                }
            }
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual(1, roundTripped.DpkgTriggers.Count);
        Assert.AreEqual("/etc/ld.so.conf.d", roundTripped.DpkgTriggers[0].Name);
        Assert.AreEqual("activate-noawait", roundTripped.DpkgTriggers[0].Type);
    }

    [TestMethod]
    public void RoundTrip_DpkgTrigger_Multiple()
    {
        var original = new AosprojProject
        {
            PackageName = "test",
            DpkgTriggers =
            {
                new DpkgTriggerItem { Name = "/etc/dconf/db", Type = "interest-noawait" },
                new DpkgTriggerItem { Name = "/usr/share/glib-2.0/schemas", Type = "interest" },
                new DpkgTriggerItem { Name = "/etc/ld.so.conf.d", Type = "activate" }
            }
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual(3, roundTripped.DpkgTriggers.Count);
        Assert.AreEqual("/etc/dconf/db", roundTripped.DpkgTriggers[0].Name);
        Assert.AreEqual("interest-noawait", roundTripped.DpkgTriggers[0].Type);
        Assert.AreEqual("/usr/share/glib-2.0/schemas", roundTripped.DpkgTriggers[1].Name);
        Assert.AreEqual("interest", roundTripped.DpkgTriggers[1].Type);
        Assert.AreEqual("/etc/ld.so.conf.d", roundTripped.DpkgTriggers[2].Name);
        Assert.AreEqual("activate", roundTripped.DpkgTriggers[2].Type);
    }

    [TestMethod]
    public void Deserialize_DpkgTrigger_FromXml()
    {
        var xml = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <PackageName>test</PackageName>
                <PackageVersion>1.0</PackageVersion>
                <PackageDescription>desc</PackageDescription>
              </PropertyGroup>
              <ItemGroup>
                <DpkgTrigger Include="/etc/dconf/db" Type="interest-noawait" />
                <DpkgTrigger Include="/usr/share/glib-2.0/schemas" Type="interest" />
              </ItemGroup>
            </Project>
            """);

        var project = _serializer.Deserialize(xml);

        Assert.AreEqual(2, project.DpkgTriggers.Count);
        Assert.AreEqual("/etc/dconf/db", project.DpkgTriggers[0].Name);
        Assert.AreEqual("interest-noawait", project.DpkgTriggers[0].Type);
        Assert.AreEqual("/usr/share/glib-2.0/schemas", project.DpkgTriggers[1].Name);
        Assert.AreEqual("interest", project.DpkgTriggers[1].Type);
    }

    [TestMethod]
    public void Deserialize_DpkgTrigger_WithoutType_DefaultsToInterestNoAwait()
    {
        var xml = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <PackageName>test</PackageName>
                <PackageVersion>1.0</PackageVersion>
                <PackageDescription>desc</PackageDescription>
              </PropertyGroup>
              <ItemGroup>
                <DpkgTrigger Include="/etc/dconf/db" />
              </ItemGroup>
            </Project>
            """);

        var project = _serializer.Deserialize(xml);

        Assert.AreEqual(1, project.DpkgTriggers.Count);
        Assert.AreEqual("/etc/dconf/db", project.DpkgTriggers[0].Name);
        Assert.AreEqual("interest-noawait", project.DpkgTriggers[0].Type,
            "When Type attribute is omitted, it should default to 'interest-noawait'.");
    }

    [TestMethod]
    public void Serialize_EmptyDpkgTriggers_Omitted()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0.0",
            PackageDescription = "desc"
        };

        var doc = _serializer.Serialize(project);
        var xml = doc.ToString();

        Assert.IsFalse(xml.Contains("DpkgTrigger"), "Empty DpkgTriggers should be omitted.");
    }

    [TestMethod]
    public void RoundTrip_FullProject_WithDpkgTriggers()
    {
        var original = new AosprojProject
        {
            PackageName = "full-pkg",
            PackageVersion = "5.0.0",
            PackageDescription = "Full-featured package with triggers",
            Maintainer = "Team <team@example.com>",
            TargetDistro = "ubuntu",
            TargetSuites = "jammy noble",
            TargetArchitectures = "amd64",
            Dependencies =
            {
                new ConditionalValue { Value = "libc6" }
            },
            IncludeFiles =
            {
                new IncludeFileItem { Source = "src/app", Target = "/usr/bin/app" }
            },
            DpkgTriggers =
            {
                new DpkgTriggerItem { Name = "/etc/dconf/db", Type = "interest-noawait" },
                new DpkgTriggerItem { Name = "/usr/share/glib-2.0/schemas", Type = "interest" }
            }
        };

        var doc = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(doc);

        Assert.AreEqual(original.PackageName, roundTripped.PackageName);
        Assert.AreEqual(1, roundTripped.IncludeFiles.Count);
        Assert.AreEqual(2, roundTripped.DpkgTriggers.Count);
        Assert.AreEqual("/etc/dconf/db", roundTripped.DpkgTriggers[0].Name);
        Assert.AreEqual("interest-noawait", roundTripped.DpkgTriggers[0].Type);
        Assert.AreEqual("/usr/share/glib-2.0/schemas", roundTripped.DpkgTriggers[1].Name);
        Assert.AreEqual("interest", roundTripped.DpkgTriggers[1].Type);
    }

    [TestMethod]
    public void Deserialize_DependencyCheckSource_NoSuiteMap()
    {
        var xml = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <PackageName>test</PackageName>
                <PackageVersion>1.0</PackageVersion>
                <PackageDescription>desc</PackageDescription>
              </PropertyGroup>
              <ItemGroup>
                <DependencyCheckSource Url="https://example.com/apt" />
              </ItemGroup>
            </Project>
            """);

        var project = _serializer.Deserialize(xml);

        Assert.AreEqual(1, project.DependencyCheckSources.Count);
        Assert.AreEqual("https://example.com/apt", project.DependencyCheckSources[0].Url);
        Assert.AreEqual("", project.DependencyCheckSources[0].SuiteMap);
    }
}
