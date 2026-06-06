using System.Diagnostics;
using System.IO.Compression;
using Aiursoft.Apkg.Services.Contents;

namespace Aiursoft.Apkg.WebTests.Services;

[TestClass]
public class ContentsGeneratorServiceTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "contents-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── ParseDpkgDebContents ────────────────────────────────────────────────

    [TestMethod]
    public void ParseDpkgDebContents_RegularFiles_ReturnsPaths()
    {
        var input = """
            drwxr-xr-x root/root         0 2024-01-01 00:00 ./
            drwxr-xr-x root/root         0 2024-01-01 00:00 ./usr/
            drwxr-xr-x root/root         0 2024-01-01 00:00 ./usr/bin/
            -rwxr-xr-x root/root     12345 2024-01-01 00:00 ./usr/bin/myapp
            -rw-r--r-- root/root       512 2024-01-01 00:00 ./etc/myapp.conf
            """;

        var result = ContentsGeneratorService.ParseDpkgDebContents(input);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("usr/bin/myapp", result[0]);
        Assert.AreEqual("etc/myapp.conf", result[1]);
    }

    [TestMethod]
    public void ParseDpkgDebContents_Symlinks_Extracted()
    {
        var input = """
            drwxr-xr-x root/root         0 2024-01-01 00:00 ./
            -rwxr-xr-x root/root     12345 2024-01-01 00:00 ./usr/bin/myapp
            lrwxrwxrwx root/root         0 2024-01-01 00:00 ./usr/bin/myapp-link -> myapp
            """;

        var result = ContentsGeneratorService.ParseDpkgDebContents(input);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("usr/bin/myapp", result[0]);
        Assert.AreEqual("usr/bin/myapp-link", result[1]);
    }

    [TestMethod]
    public void ParseDpkgDebContents_SkipsDirectories()
    {
        var input = """
            drwxr-xr-x root/root         0 2024-01-01 00:00 ./
            drwxr-xr-x root/root         0 2024-01-01 00:00 ./usr/
            drwxr-xr-x root/root         0 2024-01-01 00:00 ./usr/bin/
            drwxr-xr-x root/root         0 2024-01-01 00:00 ./usr/share/
            -rwxr-xr-x root/root     12345 2024-01-01 00:00 ./usr/bin/tool
            """;

        var result = ContentsGeneratorService.ParseDpkgDebContents(input);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("usr/bin/tool", result[0]);
    }

    [TestMethod]
    public void ParseDpkgDebContents_HardLinks_Extracted()
    {
        var input = """
            drwxr-xr-x root/root         0 2024-01-01 00:00 ./
            -rwxr-xr-x root/root     12345 2024-01-01 00:00 ./usr/bin/tool
            hrwxr-xr-x root/root         0 2024-01-01 00:00 ./usr/bin/tool-hardlink
            """;

        var result = ContentsGeneratorService.ParseDpkgDebContents(input);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("usr/bin/tool", result[0]);
        Assert.AreEqual("usr/bin/tool-hardlink", result[1]);
    }

    [TestMethod]
    public void ParseDpkgDebContents_EmptyInput_ReturnsEmpty()
    {
        var result = ContentsGeneratorService.ParseDpkgDebContents("");
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ParseDpkgDebContents_WhitespaceOnly_ReturnsEmpty()
    {
        var result = ContentsGeneratorService.ParseDpkgDebContents("   \n  \n  ");
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ParseDpkgDebContents_StripsDotSlashPrefix()
    {
        var input = "-rw-r--r-- root/root      1024 2024-01-01 00:00 ./usr/share/doc/readme.txt";

        var result = ContentsGeneratorService.ParseDpkgDebContents(input);

        Assert.AreEqual(1, result.Count);
        Assert.IsFalse(result[0].StartsWith("./"));
        Assert.AreEqual("usr/share/doc/readme.txt", result[0]);
    }

    [TestMethod]
    public void ParseDpkgDebContents_SpecialCharacters_InPath()
    {
        var input = "-rw-r--r-- root/root       256 2024-01-01 00:00 ./usr/lib/python3/dist-packages/my_pkg-1.0.dist-info/METADATA";

        var result = ContentsGeneratorService.ParseDpkgDebContents(input);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("usr/lib/python3/dist-packages/my_pkg-1.0.dist-info/METADATA", result[0]);
    }

    // ── BuildContentsLine ────────────────────────────────────────────────────

    [TestMethod]
    public void BuildContentsLine_TypicalCase()
    {
        var line = ContentsGeneratorService.BuildContentsLine("usr/bin/myapp", "utils", "myapp");

        Assert.IsTrue(line.StartsWith("usr/bin/myapp"));
        Assert.IsTrue(line.EndsWith("utils/myapp"));
        var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(2, parts.Length);
        Assert.AreEqual("usr/bin/myapp", parts[0]);
        Assert.AreEqual("utils/myapp", parts[1]);
    }

    [TestMethod]
    public void BuildContentsLine_LongPath_ProducesAtLeastOneSpaceGap()
    {
        var longPath = "usr/share/doc/" + new string('x', 80) + "/readme.txt";
        var line = ContentsGeneratorService.BuildContentsLine(longPath, "doc", "mypkg");

        Assert.IsTrue(line.StartsWith(longPath));
        Assert.IsTrue(line.EndsWith("doc/mypkg"));
        var idx = line.IndexOf(longPath, StringComparison.Ordinal);
        var afterPath = line[(idx + longPath.Length)..];
        Assert.IsTrue(afterPath.Length >= 1, "Must have at least a space after the path");
        Assert.IsTrue(char.IsWhiteSpace(afterPath[0]), "First char after path must be whitespace");
    }

    // ── End-to-end: GenerateContentsFiles ────────────────────────────────────

    [TestMethod]
    public async Task GenerateContentsFiles_SinglePackage_WritesRawAndGzipped()
    {
        var debPath = await BuildTestDebAsync(_tempDir, "hello", "1.0", "amd64",
            [("usr/bin/hello", "#!/bin/sh\necho hi\n")]);

        var outputDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(outputDir);

        var result = await ContentsGeneratorService.GenerateContentsFilesAsync(
            _tempDir,
            "amd64",
            outputDir,
            [new ContentsPackage(debPath, "hello", "utils")]);

        var rawPath = Path.Combine(outputDir, "Contents-amd64");
        Assert.IsTrue(File.Exists(rawPath), "Contents-amd64 file should exist.");
        var rawContent = await File.ReadAllTextAsync(rawPath);
        Assert.IsTrue(rawContent.Contains("usr/bin/hello"), $"Should contain usr/bin/hello. Actual: {rawContent}");
        Assert.IsTrue(rawContent.Contains("utils/hello"), $"Should contain section/package. Actual: {rawContent}");

        var gzPath = rawPath + ".gz";
        Assert.IsTrue(File.Exists(gzPath), "Contents-amd64.gz should exist.");

        await using var gzFs = File.OpenRead(gzPath);
        using var gzStream = new GZipStream(gzFs, CompressionMode.Decompress);
        using var reader = new StreamReader(gzStream);
        Assert.AreEqual(rawContent, await reader.ReadToEndAsync());

        Assert.IsFalse(string.IsNullOrEmpty(result.RawSha256));
        Assert.IsFalse(string.IsNullOrEmpty(result.GzSha256));
        Assert.IsTrue(result.RawSize > 0);
        Assert.IsTrue(result.GzSize > 0);
    }

    [TestMethod]
    public async Task GenerateContentsFiles_MultiplePackages_MergedAndSorted()
    {
        var debA = await BuildTestDebAsync(_tempDir, "pkg-a", "1.0", "all",
            [("usr/share/pkg-a/README", "readme")]);
        var debB = await BuildTestDebAsync(_tempDir, "pkg-b", "2.0", "all",
            [("usr/share/pkg-b/LICENSE", "license")]);

        var outputDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(outputDir);

        await ContentsGeneratorService.GenerateContentsFilesAsync(
            _tempDir,
            "all",
            outputDir,
            [
                new ContentsPackage(debA, "pkg-a", "admin"),
                new ContentsPackage(debB, "pkg-b", "misc")
            ]);

        var rawPath = Path.Combine(outputDir, "Contents-all");
        Assert.IsTrue(File.Exists(rawPath));
        var content = await File.ReadAllTextAsync(rawPath);

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.IsTrue(lines.Length >= 2, "Should have at least 2 lines.");

        Assert.IsTrue(content.Contains("usr/share/pkg-a/README"));
        Assert.IsTrue(content.Contains("admin/pkg-a"));
        Assert.IsTrue(content.Contains("usr/share/pkg-b/LICENSE"));
        Assert.IsTrue(content.Contains("misc/pkg-b"));
    }

    [TestMethod]
    public async Task GenerateContentsFiles_EmptyPackages_ProducesEmptyFile()
    {
        var outputDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(outputDir);

        await ContentsGeneratorService.GenerateContentsFilesAsync(
            _tempDir, "amd64", outputDir, []);

        var rawPath = Path.Combine(outputDir, "Contents-amd64");
        Assert.IsTrue(File.Exists(rawPath));
        var content = await File.ReadAllTextAsync(rawPath);
        Assert.AreEqual("", content);

        var gzPath = rawPath + ".gz";
        Assert.IsTrue(File.Exists(gzPath));
    }

    [TestMethod]
    public async Task GenerateContentsFiles_SkipsVirtualPackagesWithNoDeb()
    {
        // Packages with no local file should be skipped gracefully
        var outputDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(outputDir);

        var nonExistentPath = Path.Combine(_tempDir, "nonexistent.deb");

        await ContentsGeneratorService.GenerateContentsFilesAsync(
            _tempDir,
            "amd64",
            outputDir,
            [new ContentsPackage(nonExistentPath, "ghost-pkg", "utils")]);

        // Should not throw, should produce empty Contents
        var rawPath = Path.Combine(outputDir, "Contents-amd64");
        Assert.IsTrue(File.Exists(rawPath));
    }

    [TestMethod]
    public async Task GenerateContentsFiles_DirectoriesNotIncluded()
    {
        var debPath = await BuildTestDebAsync(_tempDir, "dirpkg", "1.0", "amd64",
            [
                ("usr/bin/tool", "binary"),
                ("usr/share/doc/dirpkg/README", "docs")
            ]);

        var outputDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(outputDir);

        await ContentsGeneratorService.GenerateContentsFilesAsync(
            _tempDir, "amd64", outputDir,
            [new ContentsPackage(debPath, "dirpkg", "utils")]);

        var content = await File.ReadAllTextAsync(Path.Combine(outputDir, "Contents-amd64"));
        // Should contain files but NOT directory entries
        Assert.IsTrue(content.Contains("usr/bin/tool"));
        Assert.IsTrue(content.Contains("usr/share/doc/dirpkg/README"));
        // No directory-only lines
        Assert.IsFalse(content.Contains(" ./"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<string> BuildTestDebAsync(
        string tempDir, string package, string version, string arch,
        (string path, string content)[] files)
    {
        var debDir = Path.Combine(tempDir, $"build-{package}-{Guid.NewGuid():N}");
        var debianDir = Path.Combine(debDir, "DEBIAN");
        Directory.CreateDirectory(debianDir);

        foreach (var (path, content) in files)
        {
            var fullPath = Path.Combine(debDir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, content);
        }

        await File.WriteAllTextAsync(Path.Combine(debianDir, "control"),
            $"Package: {package}\nVersion: {version}\nArchitecture: {arch}\n" +
            $"Maintainer: Test <test@test>\nDescription: Test package\n");

        var debPath = Path.Combine(tempDir, $"{package}_{version}_{arch}.deb");
        await RunAsync("dpkg-deb", ["--build", "--root-owner-group", debDir, debPath]);
        return debPath;
    }

    private static async Task RunAsync(string fileName, string[] args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) process.StartInfo.ArgumentList.Add(a);
        process.Start();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                $"Command '{fileName} {string.Join(' ', args)}' failed ({process.ExitCode}): {err}");
        }
    }
}
