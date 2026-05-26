using System.ComponentModel;
using System.Diagnostics;
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
        .WithFeature(new PushHandler())
        .WithFeature(new InstallHandler())
        .WithFeature(new UnpackHandler())
        .WithFeature(new AddSourceHandler());

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
    public async Task InvokePushHelp()
    {
        var result = await Program.TestRunAsync(["push", "--help"]);
        Assert.AreEqual(0, result.ProgramReturn);
        Assert.IsTrue(result.StdOut.Contains("--source"));
        Assert.IsTrue(result.StdOut.Contains("--api-key"));
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
        var tempDir = CreateTestDirectory();
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
        var tempDir = CreateTestDirectory();
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
        var tempDir = CreateTestDirectory();
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

            await EnsureDpkgDebAvailableAsync();
            await CreateMinimalDebAsync(
                path: Path.Combine(debsDir, "my-pkg_2.0.0_amd64.deb"),
                packageName: manifest.Package,
                version: manifest.Version,
                arch: "amd64");

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
        var tempDir = CreateTestDirectory();
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
    public async Task InvokePack_CreatesApkgFileWhenDebArchitectureIsAll()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "my-pkg");
            var debsDir = Path.Combine(projectDir, "debs");
            Directory.CreateDirectory(debsDir);

            var serializer = new ManifestSerializer();
            var manifest = new ApkgManifest
            {
                Package = "my-pkg",
                Version = "2.1.0",
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
                        DebFile = "debs/my-pkg_2.1.0_all.deb"
                    }
                ]
            };
            await serializer.SerializeToFileAsync(manifest, Path.Combine(projectDir, "manifest.xml"));

            await EnsureDpkgDebAvailableAsync();
            await CreateMinimalDebAsync(
                path: Path.Combine(debsDir, "my-pkg_2.1.0_all.deb"),
                packageName: manifest.Package,
                version: manifest.Version,
                arch: "all");

            var outputDir = Path.Combine(tempDir, "output");
            var result = await Program.TestRunAsync(["pack", "--path", projectDir, "--output", outputDir]);

            Assert.AreEqual(0, result.ProgramReturn, result.StdErr);
            Assert.IsTrue(File.Exists(Path.Combine(outputDir, "my-pkg.2.1.0.apkg")), ".apkg file should be created for arch:all debs.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task InvokePack_FailsWhenDebArchMismatch()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "my-pkg");
            var debsDir = Path.Combine(projectDir, "debs");
            Directory.CreateDirectory(debsDir);

            var serializer = new ManifestSerializer();
            var manifest = new ApkgManifest
            {
                Package = "my-pkg",
                Version = "3.0.0",
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
                        DebFile = "debs/my-pkg_3.0.0_arm64.deb"
                    }
                ]
            };
            await serializer.SerializeToFileAsync(manifest, Path.Combine(projectDir, "manifest.xml"));

            await EnsureDpkgDebAvailableAsync();
            await CreateMinimalDebAsync(
                path: Path.Combine(debsDir, "my-pkg_3.0.0_arm64.deb"),
                packageName: manifest.Package,
                version: manifest.Version,
                arch: "arm64");

            var outputDir = Path.Combine(tempDir, "output");
            var result = await Program.TestRunAsync(["pack", "--path", projectDir, "--output", outputDir]);
            var output = string.Concat(result.StdOut, result.StdErr);

            Assert.AreNotEqual(0, result.ProgramReturn, "Should fail when deb architecture mismatches the manifest target.");
            Assert.IsTrue(output.Contains("Architecture mismatch"), "Expected architecture mismatch error message.");
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

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "test-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task EnsureDpkgDebAvailableAsync()
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dpkg-deb",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add("--version");

        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            Assert.Inconclusive("Requires dpkg-deb.");
            return;
        }

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            Assert.Inconclusive("Requires dpkg-deb.");
    }

    private static async Task CreateMinimalDebAsync(string path, string packageName, string version, string arch)
    {
        var packageDir = Path.Combine(Path.GetDirectoryName(path)!, Guid.NewGuid().ToString("N"));
        var controlDir = Path.Combine(packageDir, "DEBIAN");
        Directory.CreateDirectory(controlDir);

        try
        {
            var controlFile = Path.Combine(controlDir, "control");
            await File.WriteAllTextAsync(
                controlFile,
                $"Package: {packageName}\nVersion: {version}\nArchitecture: {arch}\nMaintainer: Test <test@example.com>\nDescription: Test package\n");

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "dpkg-deb",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add("--build");
            process.StartInfo.ArgumentList.Add(packageDir);
            process.StartInfo.ArgumentList.Add(path);
            process.Start();

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var standardError = await standardErrorTask;
            if (process.ExitCode != 0)
            {
                Assert.Fail($"Failed to build test .deb file: {standardError}");
            }

            await standardOutputTask;
        }
        finally
        {
            if (Directory.Exists(packageDir))
                Directory.Delete(packageDir, recursive: true);
        }
    }
}
