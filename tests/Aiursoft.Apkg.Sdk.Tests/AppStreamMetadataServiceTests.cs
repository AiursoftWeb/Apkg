using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiursoft.Apkg.Sdk.Tests;

[TestClass]
public class AppStreamMetadataServiceTests
{
    [TestMethod]
    public async Task InstallAsync_GeneratesMetainfoAndInstallsDesktopAndIcon()
    {
        var root = Path.Combine(Path.GetTempPath(), $"apkg-appstream-{Guid.NewGuid():N}");
        var stage = Path.Combine(root, "stage");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "com.example.demo.desktop"),
                "[Desktop Entry]\nType=Application\nName=Demo\nComment=A useful demo application\nIcon=com.example.demo\nExec=demo\n");
            await File.WriteAllTextAsync(Path.Combine(root, "com.example.demo.svg"), "<svg/>");
            var project = new AosprojProject
            {
                PackageName = "demo",
                PackageDescription = "A useful demo application for testing generated metadata.",
                PackageAuthors = "Example Team",
                AppStreamDeveloperName = "Example",
                LicenseType = "MIT",
                PackageHomepage = "https://example.com/demo",
                AppStreamApplications =
                {
                    new AppStreamApplicationItem
                    {
                        Source = "com.example.demo.desktop",
                        Icon = "com.example.demo.svg"
                    }
                }
            };

            var service = new AppStreamMetadataService(NullLogger<AppStreamMetadataService>.Instance);
            await service.InstallAsync(root, stage, project, project.AppStreamApplications);

            Assert.IsTrue(File.Exists(Path.Combine(stage, "usr/share/applications/com.example.demo.desktop")));
            Assert.IsTrue(File.Exists(Path.Combine(stage, "usr/share/icons/hicolor/scalable/apps/com.example.demo.svg")));
            var metainfo = Path.Combine(stage, "usr/share/metainfo/com.example.demo.metainfo.xml");
            Assert.IsTrue(File.Exists(metainfo));
            var xml = await File.ReadAllTextAsync(metainfo);
            StringAssert.Contains(xml, "<id>com.example.demo</id>");
            StringAssert.Contains(xml, "<launchable type=\"desktop-id\">com.example.demo.desktop</launchable>");
            StringAssert.Contains(xml, "<metadata_license>CC0-1.0</metadata_license>");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ImageMetadataReader_ReadsPngDimensions()
    {
        var path = Path.GetTempFileName();
        try
        {
            var bytes = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9WlR8Z8AAAAASUVORK5CYII=");
            File.WriteAllBytes(path, bytes);
            var metadata = ImageMetadataReader.Read(path);
            Assert.AreEqual("image/png", metadata.MediaType);
            Assert.AreEqual(1, metadata.Width);
            Assert.AreEqual(1, metadata.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
