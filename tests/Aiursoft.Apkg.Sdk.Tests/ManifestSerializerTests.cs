using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;

namespace Aiursoft.Apkg.Sdk.Tests;

[TestClass]
public class ManifestSerializerTests
{
    private readonly ManifestSerializer _serializer = new();

    // ── Commit de829ad: v2 (ApkgPackageManifest) ──────────────────────────────

    [TestMethod]
    public void DeserializePackageManifest_BasicRoundTrip()
    {
        var original = new ApkgPackageManifest
        {
            FormatVersion = 2,
            Name = "my-pkg",
            Version = "3.2.1",
            Maintainer = "Team <team@example.com>",
            Description = "A superb package",
            Homepage = "https://example.com",
            License = "Apache-2.0",
            Entries =
            {
                new ApkgPackageEntry
                {
                    DebFile = "my-pkg_3.2.1_jammy_amd64.deb",
                    Distro = "ubuntu",
                    Suite = "jammy",
                    Component = "universe",
                    Architecture = "amd64"
                }
            }
        };

        // Serialize v2
        var xml = SerializeV2(original);
        var roundTripped = _serializer.DeserializePackageManifest(xml);

        Assert.AreEqual(original.Name, roundTripped.Name);
        Assert.AreEqual(original.Version, roundTripped.Version);
        Assert.AreEqual(original.Maintainer, roundTripped.Maintainer);
        Assert.AreEqual(original.Description, roundTripped.Description);
        Assert.AreEqual(original.Homepage, roundTripped.Homepage);
        Assert.AreEqual(original.License, roundTripped.License);
        Assert.AreEqual(original.FormatVersion, roundTripped.FormatVersion);
        Assert.AreEqual(1, roundTripped.Entries.Count);
        Assert.AreEqual(original.Entries[0].DebFile, roundTripped.Entries[0].DebFile);
        Assert.AreEqual(original.Entries[0].Distro, roundTripped.Entries[0].Distro);
        Assert.AreEqual(original.Entries[0].Suite, roundTripped.Entries[0].Suite);
        Assert.AreEqual(original.Entries[0].Component, roundTripped.Entries[0].Component);
        Assert.AreEqual(original.Entries[0].Architecture, roundTripped.Entries[0].Architecture);
    }

    [TestMethod]
    public void DeserializePackageManifest_ArchitectureField()
    {
        var original = new ApkgPackageManifest
        {
            Name = "test",
            Entries =
            {
                new ApkgPackageEntry
                {
                    Architecture = "arm64",
                    DebFile = "test_1.0_noble_arm64.deb",
                    Suite = "noble"
                }
            }
        };

        var xml = SerializeV2(original);
        var result = _serializer.DeserializePackageManifest(xml);

        Assert.AreEqual("arm64", result.Entries[0].Architecture);
    }

    [TestMethod]
    public void DeserializePackageManifest_MultipleEntries()
    {
        var original = new ApkgPackageManifest
        {
            Name = "multi-arch",
            Version = "1.0.0",
            Entries =
            {
                new ApkgPackageEntry
                {
                    DebFile = "multi-arch_1.0.0_jammy_amd64.deb",
                    Distro = "ubuntu",
                    Suite = "jammy",
                    Component = "main",
                    Architecture = "amd64"
                },
                new ApkgPackageEntry
                {
                    DebFile = "multi-arch_1.0.0_jammy_arm64.deb",
                    Distro = "ubuntu",
                    Suite = "jammy",
                    Component = "main",
                    Architecture = "arm64"
                },
                new ApkgPackageEntry
                {
                    DebFile = "multi-arch_1.0.0_noble_amd64.deb",
                    Distro = "ubuntu",
                    Suite = "noble",
                    Component = "main",
                    Architecture = "amd64"
                }
            }
        };

        var xml = SerializeV2(original);
        var result = _serializer.DeserializePackageManifest(xml);

        Assert.AreEqual(3, result.Entries.Count);
        Assert.AreEqual("arm64", result.Entries[1].Architecture);
        Assert.AreEqual("noble", result.Entries[2].Suite);
    }

    [TestMethod]
    public async Task DeserializePackageManifestFromFile_ReadsFile()
    {
        var original = new ApkgPackageManifest
        {
            Name = "file-test",
            Version = "4.0.0",
            Description = "File-based round-trip test",
            Entries =
            {
                new ApkgPackageEntry
                {
                    DebFile = "file-test_4.0.0_plucky_amd64.deb",
                    Distro = "ubuntu",
                    Suite = "plucky",
                    Architecture = "amd64"
                }
            }
        };

        var xml = SerializeV2(original);
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, xml);
            var result = await _serializer.DeserializePackageManifestFromFileAsync(path);

            Assert.AreEqual(original.Name, result.Name);
            Assert.AreEqual(original.Version, result.Version);
            Assert.AreEqual(1, result.Entries.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task V1_DeserializeFromFile_Works()
    {
        var original = new ApkgManifest
        {
            Package = "fromfile",
            Version = "1.0.0",
            Component = "main",
            Targets =
            {
                new ManifestTarget
                {
                    Distro = "ubuntu",
                    Suites = "jammy",
                    Architecture = "amd64",
                    DebFile = "debs/pkg.deb"
                }
            }
        };

        var xml = _serializer.Serialize(original);
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, xml);
            var result = await _serializer.DeserializeFromFileAsync(path);

            Assert.AreEqual(original.Package, result.Package);
            Assert.AreEqual(original.Version, result.Version);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void DeserializePackageManifest_FormatVersionIsV2()
    {
        var manifest = new ApkgPackageManifest
        {
            Name = "test",
            Version = "1.0.0"
        };

        var xml = SerializeV2(manifest);
        var result = _serializer.DeserializePackageManifest(xml);

        Assert.AreEqual(2, result.FormatVersion);
    }

    [TestMethod]
    public void DeserializePackageManifest_Defaults()
    {
        var manifest = new ApkgPackageManifest
        {
            Name = "test",
            Version = "1.0.0"
        };

        var xml = SerializeV2(manifest);
        var result = _serializer.DeserializePackageManifest(xml);

        Assert.AreEqual("MIT", result.License);
    }

    [TestMethod]
    public void DeserializePackageManifest_EntryDefaults()
    {
        var manifest = new ApkgPackageManifest
        {
            Name = "test",
            Version = "1.0.0",
            Entries =
            {
                new ApkgPackageEntry
                {
                    DebFile = "test_1.0_all.deb"
                }
            }
        };

        var xml = SerializeV2(manifest);
        var result = _serializer.DeserializePackageManifest(xml);

        var entry = result.Entries[0];
        Assert.AreEqual("ubuntu", entry.Distro);
        Assert.AreEqual("main", entry.Component);
        Assert.AreEqual("amd64", entry.Architecture);
    }

    // ── Field mapping vs v1: Package → Name, Suites → Suite ───────────────────

    [TestMethod]
    public void V2_UsesNameNotPackage()
    {
        var v2 = new ApkgPackageManifest
        {
            Name = "name-field",
            Version = "1.0"
        };

        var xml = SerializeV2(v2);
        // v2 uses <Name>, NOT <Package>
        Assert.IsTrue(xml.Contains("<Name>name-field</Name>"));
    }

    [TestMethod]
    public void V2_UsesSuiteNotSuites()
    {
        var v2 = new ApkgPackageManifest
        {
            Name = "test",
            Version = "1.0",
            Entries =
            {
                new ApkgPackageEntry
                {
                    DebFile = "test_1.0_all.deb",
                    Suite = "noble"
                }
            }
        };

        var xml = SerializeV2(v2);
        var result = _serializer.DeserializePackageManifest(xml);
        Assert.AreEqual("noble", result.Entries[0].Suite);
    }

    [TestMethod]
    public void V2_UsesEntriesNotTargets()
    {
        var v2 = new ApkgPackageManifest
        {
            Name = "test",
            Version = "1.0",
            Entries =
            {
                new ApkgPackageEntry { DebFile = "test_1.0_all.deb", Suite = "jammy" }
            }
        };

        var xml = SerializeV2(v2);
        // v2 uses <Entries> and <Entry>, not <Targets> and <Target>
        Assert.IsTrue(xml.Contains("<Entries>"));
        Assert.IsTrue(xml.Contains("<Entry>"));
    }

    /// <summary>
    /// Each entry has its own Component in v2 (was global in v1).
    /// </summary>
    [TestMethod]
    public void V2_ComponentPerEntry()
    {
        var v2 = new ApkgPackageManifest
        {
            Name = "test",
            Version = "1.0",
            Entries =
            {
                new ApkgPackageEntry
                {
                    DebFile = "test_1.0_jammy_amd64.deb",
                    Suite = "jammy",
                    Component = "universe"
                },
                new ApkgPackageEntry
                {
                    DebFile = "test_1.0_noble_amd64.deb",
                    Suite = "noble",
                    Component = "restricted"
                }
            }
        };

        var xml = SerializeV2(v2);
        var result = _serializer.DeserializePackageManifest(xml);

        Assert.AreEqual("universe", result.Entries[0].Component);
        Assert.AreEqual("restricted", result.Entries[1].Component);
    }

    // ── v1 (ApkgManifest) legacy compat ───────────────────────────────────────

    [TestMethod]
    public void V1_LegacyRoundTrip()
    {
        var original = new ApkgManifest
        {
            Package = "legacy-pkg",
            Version = "1.0.0",
            Maintainer = "Old <old@example.com>",
            Description = "Legacy package",
            Homepage = "https://legacy.example.com",
            License = "GPL-3.0",
            Component = "main",
            Targets =
            {
                new ManifestTarget
                {
                    Distro = "ubuntu",
                    Suites = "jammy jammy-updates",
                    Architecture = "amd64",
                    DebFile = "debs/legacy_1.0.0_amd64.deb"
                }
            }
        };

        var xml = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize(xml);

        Assert.AreEqual(original.Package, roundTripped.Package);
        Assert.AreEqual(original.Version, roundTripped.Version);
        Assert.AreEqual(original.Component, roundTripped.Component);
        Assert.AreEqual(1, roundTripped.Targets.Count);
        Assert.AreEqual("ubuntu", roundTripped.Targets[0].Distro);
        Assert.AreEqual("jammy jammy-updates", roundTripped.Targets[0].Suites);
        Assert.AreEqual("amd64", roundTripped.Targets[0].Architecture);
    }

    [TestMethod]
    public void V1_SuiteListProperty()
    {
        var manifest = new ApkgManifest
        {
            Package = "test",
            Targets =
            {
                new ManifestTarget { Suites = "jammy jammy-updates jammy-security" }
            }
        };

        var target = manifest.Targets[0];
        var suites = target.SuiteList;
        Assert.AreEqual(3, suites.Length);
        Assert.AreEqual("jammy", suites[0]);
        Assert.AreEqual("jammy-updates", suites[1]);
        Assert.AreEqual("jammy-security", suites[2]);
    }

    // ── Helper: serialize v2 via XmlSerializer ────────────────────────────────

    private static string SerializeV2(ApkgPackageManifest manifest)
    {
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(ApkgPackageManifest));
        using var ms = new MemoryStream();
        var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var writer = System.Xml.XmlWriter.Create(ms, new System.Xml.XmlWriterSettings
        {
            Indent = true,
            Encoding = utf8NoBom,
            OmitXmlDeclaration = false
        });
        serializer.Serialize(writer, manifest);
        return utf8NoBom.GetString(ms.ToArray());
    }
}
