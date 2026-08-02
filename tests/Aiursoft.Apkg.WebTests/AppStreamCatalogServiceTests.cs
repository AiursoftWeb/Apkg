using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.FileStorage;
using Aiursoft.Apkg.Sqlite;
using Aiursoft.AptClient;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Aiursoft.Apkg.WebTests;

[TestClass]
public class AppStreamCatalogServiceTests
{
    [TestMethod]
    public async Task GenerateAsync_WritesDep11CatalogAndCachedIcons()
    {
        var root = Path.Combine(Path.GetTempPath(), $"apkg-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:Path"] = root,
                    ["GlobalSettings:PublicAptServerDomain"] = "https://packages.example.com"
                })
                .Build();
            var options = new DbContextOptionsBuilder<SqliteContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "test.db")}")
                .Options;
            await using var db = new SqliteContext(options);
            await db.Database.EnsureCreatedAsync();

            var folders = new FeatureFoldersProvider(new StorageRootPathProvider(configuration));
            var debPath = await BuildTestDebAsync(root);
            var debBytes = await File.ReadAllBytesAsync(debPath);
            var debHash = Convert.ToHexStringLower(SHA256.HashData(debBytes));
            var casDir = Path.Combine(folders.GetObjectsFolder(), debHash[..2]);
            Directory.CreateDirectory(casDir);
            await File.WriteAllBytesAsync(Path.Combine(casDir, $"{debHash}.deb"), debBytes);
            byte[] screenshotBytes;
            using (var screenshot = new SKBitmap(1280, 720))
            {
                screenshot.Erase(SKColors.DarkBlue);
                using var png = screenshot.Encode(SKEncodedImageFormat.Png, 100);
                screenshotBytes = png.ToArray();
            }
            var screenshotHash = Convert.ToHexStringLower(SHA256.HashData(screenshotBytes));
            var screenshotDir = Path.Combine(folders.GetAppStreamObjectsFolder(), screenshotHash[..2]);
            Directory.CreateDirectory(screenshotDir);
            await File.WriteAllBytesAsync(Path.Combine(screenshotDir, $"{screenshotHash}.png"), screenshotBytes);

            var user = new User
            {
                Id = "test-user",
                UserName = "test-user",
                DisplayName = "Test User"
            };
            var repository = new AptRepository
            {
                Name = "anduinos",
                Distro = "anduinos",
                Suite = "resolute",
                Components = "main",
                Architecture = "amd64"
            };
            var package = new ApkgPackage
            {
                Name = "demo-app",
                Distro = "anduinos",
                Component = "main",
                OwnerUserId = user.Id
            };
            var revision = new ApkgRevision
            {
                ApkgPackage = package,
                UploadedByUserId = user.Id,
                FileName = "demo-app.1.0.0.apkg",
                AppStreamApplications =
                {
                    new ApkgAppStreamApplication
                    {
                        ComponentId = "com.example.demo",
                        DesktopId = "com.example.demo.desktop",
                        MetainfoPath = "/usr/share/metainfo/com.example.demo.metainfo.xml",
                        Assets =
                        {
                            new ApkgAppStreamAsset
                            {
                                SourceSha256 = screenshotHash,
                                ObjectSha256 = screenshotHash,
                                MediaType = "image/png",
                                Width = 1280,
                                Height = 720,
                                IsDefault = true,
                                Order = 0,
                                Locale = "C",
                                Caption = "Overview"
                            }
                        }
                    }
                }
            };
            db.Users.Add(user);
            db.AptRepositories.Add(repository);
            db.ApkgRevisions.Add(revision);
            await db.SaveChangesAsync();
            db.ApkgDebPackages.Add(new ApkgDebPackage
            {
                UploadedByUserId = user.Id,
                ApkgRevisionId = revision.Id,
                RepositoryId = repository.Id,
                Package = "demo-app",
                Version = "1.0.0",
                Architecture = "amd64",
                Maintainer = "Test",
                Filename = "pool/main/d/demo-app/demo-app_1.0.0_amd64.deb",
                Size = debBytes.Length.ToString(),
                SHA256 = debHash
            });
            await db.SaveChangesAsync();

            var storage = new StorageService(
                folders,
                new FileLockProvider(),
                DataProtectionProvider.Create(Path.Combine(root, "keys")));
            var settings = new GlobalSettingsService(
                db, configuration, storage, new MemoryCache(new MemoryCacheOptions()));
            var service = new AppStreamCatalogService(
                db,
                folders,
                new DebResolutionService(new AptVersionComparisonService()),
                settings,
                NullLogger<AppStreamCatalogService>.Instance);

            var files = await service.GenerateAsync(repository, 42, ["amd64"], ["main"]);

            Assert.AreEqual(6, files.Count);
            var dep11 = Path.Combine(folders.GetBucketsFolder(), "42", "main", "dep11");
            var yaml = await File.ReadAllTextAsync(Path.Combine(dep11, "Components-amd64.yml"));
            StringAssert.Contains(yaml, "ID: com.example.demo");
            StringAssert.Contains(yaml, "Package: demo-app");
            StringAssert.Contains(yaml, "cached:");
            StringAssert.Contains(yaml, "MediaBaseUrl: https://packages.example.com/artifacts/anduinos/media/resolute");
            StringAssert.Contains(yaml, $"url: {screenshotHash}.png");
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "main/dep11/Components-amd64.yml",
                    "main/dep11/Components-amd64.yml.gz",
                    "main/dep11/icons-48x48.tar",
                    "main/dep11/icons-48x48.tar.gz",
                    "main/dep11/icons-64x64.tar",
                    "main/dep11/icons-64x64.tar.gz"
                },
                files.Select(file => file.RelativePath).ToArray());

            var rawIconPath = Path.Combine(dep11, "icons-64x64.tar");
            await using var rawIconArchive = File.OpenRead(rawIconPath);
            await AssertIconArchiveContainsExpectedEntryAsync(rawIconArchive);

            await using var compressedIconArchive = File.OpenRead(rawIconPath + ".gz");
            await using var iconGzip = new GZipStream(compressedIconArchive, CompressionMode.Decompress);
            await AssertIconArchiveContainsExpectedEntryAsync(iconGzip);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AssertIconArchiveContainsExpectedEntryAsync(Stream stream)
    {
        using var iconTar = new TarReader(stream, leaveOpen: true);
        var iconEntry = await iconTar.GetNextEntryAsync();
        Assert.IsNotNull(iconEntry);
        Assert.AreEqual("demo-app_com.example.demo.png", iconEntry.Name);
    }

    private static async Task<string> BuildTestDebAsync(string root)
    {
        var stage = Path.Combine(root, "deb-stage");
        Directory.CreateDirectory(Path.Combine(stage, "DEBIAN"));
        Directory.CreateDirectory(Path.Combine(stage, "usr/share/metainfo"));
        Directory.CreateDirectory(Path.Combine(stage, "usr/share/applications"));
        Directory.CreateDirectory(Path.Combine(stage, "usr/share/icons/hicolor/128x128/apps"));
        await File.WriteAllTextAsync(Path.Combine(stage, "DEBIAN/control"),
            "Package: demo-app\nVersion: 1.0.0\nArchitecture: amd64\nMaintainer: Test\nDescription: Demo application\n");
        await File.WriteAllTextAsync(Path.Combine(stage, "usr/share/metainfo/com.example.demo.metainfo.xml"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<component type=\"desktop-application\"><id>com.example.demo</id>" +
            "<metadata_license>CC0-1.0</metadata_license><project_license>MIT</project_license>" +
            "<name>Demo</name><summary>A useful demonstration application</summary>" +
            "<description><p>A useful demonstration application for testing software catalogs.</p></description>" +
            "<launchable type=\"desktop-id\">com.example.demo.desktop</launchable></component>");
        await File.WriteAllTextAsync(Path.Combine(stage, "usr/share/applications/com.example.demo.desktop"),
            "[Desktop Entry]\nType=Application\nName=Demo\nComment=A useful demonstration application\nIcon=com.example.demo\nExec=demo\n");
        using (var bitmap = new SKBitmap(128, 128))
        {
            bitmap.Erase(SKColors.Blue);
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            await using var icon = File.Create(Path.Combine(
                stage, "usr/share/icons/hicolor/128x128/apps/com.example.demo.png"));
            png.SaveTo(icon);
        }
        var output = Path.Combine(root, "demo-app_1.0.0_amd64.deb");
        await RunProcessAsync("dpkg-deb", ["--build", "--root-owner-group", stage, output]);
        return output;
    }

    private static async Task RunProcessAsync(string fileName, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            Assert.Fail(await error);
    }

}
