using System.Formats.Tar;
using System.IO.Compression;
using Aiursoft.Apkg.Client.Handlers;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.CommandFramework;
using Aiursoft.CommandFramework.Models;

namespace Aiursoft.Apkg.Client.Tests;

[TestClass]
public class IntegrationTests
{
    private NestedCommandApp Program => new NestedCommandApp()
        .WithGlobalOptions(CommonOptionsProvider.VerboseOption)
        .WithFeature(new NewHandler())
        .WithFeature(new PackHandler())
        .WithFeature(new PushHandler());        .WithFeature(new InstallHandler());

    [TestMethod]
    public async Task InvokeHelp()
    {
        var result = await Program.TestRunAsync(["--help"]);

        Assert.AreEqual(0, result.ProgramReturn);
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [TestMethod]
    public async Task InvokeVersion()
    {
        var result = await Program.TestRunAsync(["--version"]);

        Assert.AreEqual(0, result.ProgramReturn);
    }

    [TestMethod]
    public async Task InvokeUnknown()
    {
        var result = await Program.TestRunAsync(["--wtf"]);

        Assert.AreEqual(1, result.ProgramReturn);
    }

    [TestMethod]
    public async Task InvokeWithoutArg()
    {
        var result = await Program.TestRunAsync([]);

        Assert.AreEqual(1, result.ProgramReturn);
    }

    [TestMethod]
    public async Task InvokeNewHelp()
    {
        var result = await Program.TestRunAsync(["new", "--help"]);

        Assert.AreEqual(0, result.ProgramReturn);
        Assert.IsTrue(result.StdOut.Contains("--name"));
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [TestMethod]
    public async Task InvokePackHelp()
    {
        var result = await Program.TestRunAsync(["pack", "--help"]);

        Assert.AreEqual(0, result.ProgramReturn);
        Assert.IsTrue(result.StdOut.Contains("--path"));
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [TestMethod]
    public async Task InvokeInstallHelp()
    {
        var result = await Program.TestRunAsync(["install", "--help"]);

        Assert.AreEqual(0, result.ProgramReturn);
        Assert.IsTrue(result.StdOut.Contains("--file"));
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [TestMethod]
    public async Task InvokeInstall_FailsWhenFileNotFound()
    {
        var result = await Program.TestRunAsync(["install", "--file", "/nonexistent/path.apkg"]);

        Assert.AreNotEqual(0, result.ProgramReturn);
    }

    [TestMethod]
    public async Task InvokeNew_CreatesProjectStructure()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = await Program.TestRunAsync(["new", "--name", "my-test-pkg", "--output", tempDir]);

            Assert.AreEqual(0, result.ProgramReturn, result.StdErr);

            var projectDir = Path.Combine(tempDir, "my-test-pkg");
            Assert.IsTrue(Directory.Exists(projectDir), "Project directory should be created.");
            Assert.IsTrue(File.Exists(Path.Combine(projectDir, "manifest.xml")), "manifest.xml should exist.");
            Assert.IsTrue(Directory.Exists(Path.Combine(projectDir, "debs")), "debs/ directory should exist.");

            // Verify manifest is valid XML with the correct package name.
            var serializer = new ManifestSerializer();
            var manifest = await serializer.DeserializeFromFileAsync(Path.Combine(projectDir, "manifest.xml"));
            Assert.AreEqual("my-test-pkg", manifest.Package);
            Assert.AreEqual("main", manifest.Component);
            Assert.IsTrue(manifest.Targets.Count > 0, "At least one target should exist in the template.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task InvokeNew_FailsIfDirectoryAlreadyExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(tempDir, "my-pkg"));
        try
        {
            var result = await Program.TestRunAsync(["new", "--name", "my-pkg", "--output", tempDir]);

            Assert.AreNotEqual(0, result.ProgramReturn, "Should fail when directory already exists.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task InvokePack_CreatesApkgFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            // Set up a project directory manually.
            var projectDir = Path.Combine(tempDir, "my-pkg");
            var debsDir = Path.Combine(projectDir, "debs");
            Directory.CreateDirectory(debsDir);

            var serializer = new ManifestSerializer();
            var manifest = new ApkgManifest
            {
                Package = "my-pkg",
                Version = "2.0.0",
                Maintainer = "Test <test@example.com>",
                Description = "Test package",
                License = "MIT",
                Component = "main",
                Targets =
                [
                    new ManifestTarget
                    {
                        Distro = "ubuntu",
                        Suites = "plucky",
                        Architecture = "amd64",
                        DebFile = "debs/my-pkg_2.0.0_amd64.deb"
                    }
                ]
            };
            await serializer.SerializeToFileAsync(manifest, Path.Combine(projectDir, "manifest.xml"));

            // Create a dummy .deb file (content doesn't matter for packing).
            await File.WriteAllBytesAsync(Path.Combine(debsDir, "my-pkg_2.0.0_amd64.deb"), [0x00, 0x01, 0x02]);

            var outputDir = Path.Combine(tempDir, "output");
            var result = await Program.TestRunAsync(["pack", "--path", projectDir, "--output", outputDir]);

            Assert.AreEqual(0, result.ProgramReturn, result.StdErr);

            var apkgPath = Path.Combine(outputDir, "my-pkg.2.0.0.apkg");
            Assert.IsTrue(File.Exists(apkgPath), ".apkg file should be created.");

            // Verify the archive contains manifest.xml and the .deb.
            var entries = new List<string>();
            await using var fs = File.OpenRead(apkgPath);
            await using var gz = new GZipStream(fs, CompressionMode.Decompress);
            await using var tar = new TarReader(gz);
            TarEntry? entry;
            while ((entry = await tar.GetNextEntryAsync()) != null)
                entries.Add(entry.Name);

            Assert.IsTrue(entries.Contains("manifest.xml"), "Archive must contain manifest.xml.");
            Assert.IsTrue(entries.Contains("debs/my-pkg_2.0.0_amd64.deb"), "Archive must contain the .deb file.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task InvokePack_FailsWhenDebMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var projectDir = Path.Combine(tempDir, "my-pkg");
            Directory.CreateDirectory(Path.Combine(projectDir, "debs"));

            var serializer = new ManifestSerializer();
            var manifest = new ApkgManifest
            {
                Package = "my-pkg",
                Version = "1.0.0",
                Component = "main",
                Targets =
                [
                    new ManifestTarget
                    {
                        Distro = "ubuntu",
                        Suites = "plucky",
                        Architecture = "amd64",
                        DebFile = "debs/missing.deb"   // intentionally absent
                    }
                ]
            };
            await serializer.SerializeToFileAsync(manifest, Path.Combine(projectDir, "manifest.xml"));

            var result = await Program.TestRunAsync(["pack", "--path", projectDir, "--output", tempDir]);

            Assert.AreNotEqual(0, result.ProgramReturn, "Should fail when a .deb file is missing.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void ManifestSerializer_RoundTrip()
    {
        var serializer = new ManifestSerializer();
        var original = new ApkgManifest
        {
            Package = "vim",
            Version = "9.1.0",
            Maintainer = "Anduin <anduin@example.com>",
            Description = "Vi IMproved",
            Homepage = "https://vim.org",
            License = "GPL-2.0",
            Component = "universe",
            Targets =
            [
                new ManifestTarget
                {
                    Distro = "ubuntu",
                    Suites = "plucky plucky-updates",
                    Architecture = "amd64",
                    DebFile = "debs/vim_9.1.0_amd64.deb"
                },
                new ManifestTarget
                {
                    Distro = "ubuntu",
                    Suites = "jammy",
                    Architecture = "arm64",
                    DebFile = "debs/vim_9.1.0_arm64.deb"
                }
            ]
        };

        var xml = serializer.Serialize(original);
        var roundTripped = serializer.Deserialize(xml);

        Assert.AreEqual(original.Package, roundTripped.Package);
        Assert.AreEqual(original.Version, roundTripped.Version);
        Assert.AreEqual(original.Component, roundTripped.Component);
        Assert.AreEqual(2, roundTripped.Targets.Count);
        Assert.AreEqual("plucky plucky-updates", roundTripped.Targets[0].Suites);
        CollectionAssert.AreEqual(
            new[] { "plucky", "plucky-updates" },
            roundTripped.Targets[0].SuiteList);
        Assert.AreEqual("arm64", roundTripped.Targets[1].Architecture);
    }
}
