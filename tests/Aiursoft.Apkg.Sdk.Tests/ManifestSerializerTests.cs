using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;

namespace Aiursoft.Apkg.Sdk.Tests;

[TestClass]
public class ManifestSerializerTests
{
    private readonly ManifestSerializer _serializer = new();

    [TestMethod]
    public void DeserializePackageManifest_AppStreamV3RoundTrip()
    {
        var manifest = new ApkgPackageManifest
        {
            FormatVersion = 3,
            Name = "demo",
            AppStreamApplications =
            {
                new ApkgAppStreamApplication
                {
                    Id = "com.example.demo",
                    DesktopId = "com.example.demo.desktop",
                    MetainfoPath = "/usr/share/metainfo/com.example.demo.metainfo.xml",
                    Screenshots =
                    {
                        new ApkgAppStreamScreenshot
                        {
                            File = "appstream/com.example.demo/screenshots/hash.png",
                            Sha256 = new string('a', 64),
                            MediaType = "image/png",
                            Width = 1280,
                            Height = 720,
                            Default = true,
                            Order = 0,
                            Locale = "C",
                            Caption = "Overview"
                        }
                    }
                }
            }
        };

        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(ApkgPackageManifest));
        using var writer = new StringWriter();
        serializer.Serialize(writer, manifest);
        var result = _serializer.DeserializePackageManifest(writer.ToString());

        Assert.AreEqual(3, result.FormatVersion);
        Assert.AreEqual(1, result.AppStreamApplications.Count);
        Assert.AreEqual("com.example.demo", result.AppStreamApplications[0].Id);
        Assert.AreEqual(1280, result.AppStreamApplications[0].Screenshots[0].Width);
        Assert.IsTrue(result.AppStreamApplications[0].Screenshots[0].Default);
    }

    [TestMethod]
    public void DeserializePackageManifest_BasicRoundTrip()
    {
        var original = new ApkgPackageManifest
        {
            FormatVersion = 2,
            Name = "my-pkg",
            Distro = "ubuntu",
            Component = "universe",
            Maintainer = "Team <team@example.com>",
            Description = "A superb package",
            Homepage = "https://example.com",
            License = "Apache-2.0",
            Entries =
            {
                new ApkgPackageEntry
                {
                    DebFile = "my-pkg_3.2.1_jammy_amd64.deb",
                    Suite = "jammy",
                    Architecture = "amd64"
                }
            }
        };

        var xml = SerializeV2(original);
        var roundTripped = _serializer.DeserializePackageManifest(xml);

        Assert.AreEqual(original.Name, roundTripped.Name);
        Assert.AreEqual(original.Distro, roundTripped.Distro);
        Assert.AreEqual(original.Component, roundTripped.Component);
        Assert.AreEqual(original.Maintainer, roundTripped.Maintainer);
        Assert.AreEqual(original.Description, roundTripped.Description);
        Assert.AreEqual(original.Homepage, roundTripped.Homepage);
        Assert.AreEqual(original.License, roundTripped.License);
        Assert.AreEqual(original.FormatVersion, roundTripped.FormatVersion);
        Assert.AreEqual(1, roundTripped.Entries.Count);
        Assert.AreEqual(original.Entries[0].DebFile, roundTripped.Entries[0].DebFile);
        Assert.AreEqual(original.Entries[0].Suite, roundTripped.Entries[0].Suite);
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
            Distro = "ubuntu",
            Component = "main",
            Entries =
            {
                new ApkgPackageEntry
                {
                    DebFile = "multi-arch_1.0.0_jammy_amd64.deb",
                    Suite = "jammy",
                    Architecture = "amd64"
                },
                new ApkgPackageEntry
                {
                    DebFile = "multi-arch_1.0.0_jammy_arm64.deb",
                    Suite = "jammy",
                    Architecture = "arm64"
                },
                new ApkgPackageEntry
                {
                    DebFile = "multi-arch_1.0.0_noble_amd64.deb",
                    Suite = "noble",
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
            Description = "File-based round-trip test",
            Entries =
            {
                new ApkgPackageEntry
                {
                    DebFile = "file-test_4.0.0_plucky_amd64.deb",
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
            Assert.AreEqual(1, result.Entries.Count);
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
        };

        var xml = SerializeV2(manifest);
        var result = _serializer.DeserializePackageManifest(xml);

        Assert.AreEqual(2, result.FormatVersion);
    }

    [TestMethod]
    public void DeserializePackageManifest_RootDefaults()
    {
        var manifest = new ApkgPackageManifest
        {
            Name = "test",
        };

        var xml = SerializeV2(manifest);
        var result = _serializer.DeserializePackageManifest(xml);

        Assert.AreEqual("MIT", result.License);
        Assert.AreEqual("ubuntu", result.Distro);
        Assert.AreEqual("main", result.Component);
    }

    [TestMethod]
    public void DeserializePackageManifest_EntryDefaults()
    {
        var manifest = new ApkgPackageManifest
        {
            Name = "test",
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
        Assert.AreEqual("amd64", entry.Architecture);
    }

    [TestMethod]
    public void V2_UsesNameNotPackage()
    {
        var v2 = new ApkgPackageManifest
        {
            Name = "name-field",
        };

        var xml = SerializeV2(v2);
        Assert.IsTrue(xml.Contains("<Name>name-field</Name>"));
    }

    [TestMethod]
    public void V2_UsesSuiteNotSuites()
    {
        var v2 = new ApkgPackageManifest
        {
            Name = "test",
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
            Entries =
            {
                new ApkgPackageEntry { DebFile = "test_1.0_all.deb", Suite = "jammy" }
            }
        };

        var xml = SerializeV2(v2);
        Assert.IsTrue(xml.Contains("<Entries>"));
        Assert.IsTrue(xml.Contains("<Entry>"));
    }

    [TestMethod]
    public void V2_DistroAndComponentAtRootLevel()
    {
        var v2 = new ApkgPackageManifest
        {
            Name = "test",
            Distro = "anduinos",
            Component = "webapps",
            Entries =
            {
                new ApkgPackageEntry
                {
                    DebFile = "test_1.0_jammy_amd64.deb",
                    Suite = "jammy",
                    Architecture = "amd64"
                }
            }
        };

        var xml = SerializeV2(v2);
        var result = _serializer.DeserializePackageManifest(xml);

        Assert.AreEqual("anduinos", result.Distro);
        Assert.AreEqual("webapps", result.Component);
        // Distro and Component are at root, not per-entry
        Assert.IsTrue(xml.Contains("<Distro>anduinos</Distro>"));
        Assert.IsTrue(xml.Contains("<Component>webapps</Component>"));
    }

    // ── Helper ────────────────────────────────────────────────────────

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
