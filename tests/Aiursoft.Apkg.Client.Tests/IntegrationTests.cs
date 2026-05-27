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
        .WithFeature(new BuildHandler())
        .WithFeature(new LintHandler())
        .WithFeature(new AddHandler())
        .WithFeature(new PublishHandler())
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
    public async Task InvokePublishHelp()
    {
        var result = await Program.TestRunAsync(["publish", "--help"]);

        Assert.AreEqual(0, result.ProgramReturn);
        Assert.IsTrue(result.StdOut.Contains("--path"));
        Assert.IsTrue(result.StdOut.Contains("--no-build"));
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
    public async Task InvokeUnpackHelp()
    {
        var result = await Program.TestRunAsync(["unpack", "--help"]);

        Assert.AreEqual(0, result.ProgramReturn);
        Assert.IsTrue(result.StdOut.Contains("--file"));
        Assert.IsTrue(result.StdOut.Contains("--output"));
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [TestMethod]
    public async Task InvokeUnpack_FailsWhenFileNotFound()
    {
        var result = await Program.TestRunAsync(["unpack", "/nonexistent/path.apkg"]);

        Assert.AreNotEqual(0, result.ProgramReturn);
    }

    [TestMethod]
    public async Task InvokeUnpack_ExtractsMatchingDeb()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var debsDir = Path.Combine(tempDir, "debs");
            Directory.CreateDirectory(debsDir);

            await EnsureDpkgDebAvailableAsync();
            var debPath = Path.Combine(debsDir, "my-pkg_1.5.0_amd64.deb");
            await CreateMinimalDebAsync(debPath, "my-pkg", "1.5.0", "amd64");

            var outputPackDir = Path.Combine(tempDir, "packed");
            var apkgPath = await CreateApkgDirectlyAsync(outputPackDir, "my-pkg", "1.5.0", "main",
            [
                ("ubuntu", "noble", "amd64", debPath)
            ]);
            Assert.IsTrue(File.Exists(apkgPath));

            // Now unpack it with arch override to avoid system-detection dependency
            var unpackOutputDir = Path.Combine(tempDir, "unpacked");
            var unpackResult = await Program.TestRunAsync(
            [
                "unpack",
                "--file", apkgPath,
                "--output", unpackOutputDir,
                "--arch", "amd64",
                "--distro", "ubuntu",
                "--suite", "noble"
            ]);

            Assert.AreEqual(0, unpackResult.ProgramReturn, unpackResult.StdErr);
            Assert.IsTrue(File.Exists(Path.Combine(unpackOutputDir, "my-pkg_1.5.0_amd64.deb")),
                "Unpack should extract the matching .deb file.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task InvokeUnpack_FailsWhenNoTargetMatches()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var debsDir = Path.Combine(tempDir, "debs");
            Directory.CreateDirectory(debsDir);

            await EnsureDpkgDebAvailableAsync();
            var debPath = Path.Combine(debsDir, "my-pkg_1.0.0_amd64.deb");
            await CreateMinimalDebAsync(debPath, "my-pkg", "1.0.0", "amd64");

            var outputPackDir = Path.Combine(tempDir, "packed");
            var apkgPath = await CreateApkgDirectlyAsync(outputPackDir, "my-pkg", "1.0.0", "main",
            [
                ("ubuntu", "noble", "amd64", debPath)
            ]);

            // Request arch that doesn't exist in the archive
            var unpackResult = await Program.TestRunAsync(
            [
                "unpack",
                "--file", apkgPath,
                "--output", Path.Combine(tempDir, "unpacked"),
                "--arch", "riscv64",
                "--distro", "ubuntu",
                "--suite", "noble"
            ]);

            Assert.AreNotEqual(0, unpackResult.ProgramReturn, "Should fail when no targets match.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task InvokeAddSourceHelp()
    {
        var result = await Program.TestRunAsync(["add-source", "--help"]);

        Assert.AreEqual(0, result.ProgramReturn);
        Assert.IsTrue(result.StdOut.Contains("--url") || result.StdOut.Contains("url"),
            "Should mention url argument.");
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [TestMethod]
    public async Task InvokeAddSource_FailsWhenUrlNotReachable()
    {
        var result = await Program.TestRunAsync(
            ["add-source", "--url", "http://localhost:19999/api/sources/1"]);

        Assert.AreNotEqual(0, result.ProgramReturn,
            "Should fail when the source config URL is not reachable.");
    }

    [TestMethod]
    public async Task InvokeNew_CreatesProjectStructure()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var result = await Program.TestRunAsync(["new", "--name", "my-test-pkg", "--output", tempDir]);

            Assert.AreEqual(0, result.ProgramReturn, result.StdErr);

            var projectFile = Path.Combine(tempDir, "my-test-pkg.aosproj");
            Assert.IsTrue(File.Exists(projectFile), "my-test-pkg.aosproj should be created.");

            // Verify it is valid XML with the correct package name.
            var serializer = new AosprojSerializer();
            var project = await serializer.DeserializeFromFileAsync(projectFile);
            Assert.AreEqual("my-test-pkg", project.PackageName);
            Assert.AreEqual("main", project.Component);
            Assert.IsFalse(string.IsNullOrWhiteSpace(project.TargetSuites), "TargetSuites should be set.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task InvokeNew_FailsIfProjectFileAlreadyExists()
    {
        var tempDir = CreateTestDirectory();
        await File.WriteAllTextAsync(Path.Combine(tempDir, "my-pkg.aosproj"), "<Project />");
        try
        {
            var result = await Program.TestRunAsync(["new", "--name", "my-pkg", "--output", tempDir]);

            Assert.AreNotEqual(0, result.ProgramReturn, "Should fail when .aosproj file already exists.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task<string> CreateApkgDirectlyAsync(
        string outputDir, string packageName, string version, string component,
        List<(string distro, string suite, string arch, string debPath)> targets)
    {
        Directory.CreateDirectory(outputDir);

        var manifest = new ApkgPackageManifest
        {
            Name = packageName,
            Version = version,
            Maintainer = "Test <test@example.com>",
            Description = "Test package",
            License = "MIT",
            Entries = targets.Select(t => new ApkgPackageEntry
            {
                Distro = t.distro,
                Suite = t.suite,
                Architecture = t.arch,
                Component = component,
                DebFile = t.debPath
            }).ToList()
        };

        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(ApkgPackageManifest));
        using var sw = new StringWriter();
        serializer.Serialize(sw, manifest);
        var manifestBytes = System.Text.Encoding.UTF8.GetBytes(sw.ToString());

        var apkgFileName = $"{packageName}.{version}.apkg";
        var apkgPath = Path.Combine(outputDir, apkgFileName);

        await using (var fileStream = new FileStream(apkgPath, FileMode.Create, FileAccess.Write))
        await using (var gzip = new GZipStream(fileStream, CompressionLevel.Optimal))
        await using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false))
        {
            var manifestEntry = new PaxTarEntry(TarEntryType.RegularFile, "manifest.xml")
            {
                DataStream = new MemoryStream(manifestBytes)
            };
            await tar.WriteEntryAsync(manifestEntry);

            foreach (var target in targets)
            {
                await tar.WriteEntryAsync(target.debPath, target.debPath);
            }
        }

        return apkgPath;
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
