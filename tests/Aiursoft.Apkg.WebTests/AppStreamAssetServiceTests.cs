using System.Security.Cryptography;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.FileStorage;
using SkiaSharp;

namespace Aiursoft.Apkg.WebTests;

[TestClass]
public class AppStreamAssetServiceTests
{
    [TestMethod]
    public async Task ValidateNormalizeAndCommit_StripsToContentAddressedPng()
    {
        var root = Path.Combine(Path.GetTempPath(), $"apkg-assets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.png");
            using var bitmap = new SKBitmap(1, 1);
            bitmap.SetPixel(0, 0, SKColors.Blue);
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            var bytes = png.ToArray();
            await File.WriteAllBytesAsync(source, bytes);
            var sourceHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Path"] = root })
                .Build();
            var folders = new FeatureFoldersProvider(new StorageRootPathProvider(configuration));
            var service = new AppStreamAssetService(folders);
            var manifest = new ApkgAppStreamScreenshot
            {
                File = "appstream/com.example.demo/screenshots/source.png",
                Sha256 = sourceHash,
                MediaType = "image/png",
                Width = 1,
                Height = 1,
                Locale = "C"
            };

            var prepared = await service.ValidateAndNormalizeAsync(manifest, source);
            var committed = service.Commit(prepared);

            Assert.IsTrue(File.Exists(committed));
            Assert.AreEqual($"{prepared.ObjectSha256}.png", Path.GetFileName(committed));
            Assert.IsFalse(File.Exists(prepared.NormalizedTempPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ValidateAndNormalize_RejectsUnsupportedImageFormat()
    {
        var root = Path.Combine(Path.GetTempPath(), $"apkg-assets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.gif");
            var bytes = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
            await File.WriteAllBytesAsync(source, bytes);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Path"] = root })
                .Build();
            var folders = new FeatureFoldersProvider(new StorageRootPathProvider(configuration));
            var service = new AppStreamAssetService(folders);
            var manifest = new ApkgAppStreamScreenshot
            {
                File = "appstream/com.example.demo/screenshots/source.gif",
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                MediaType = "image/gif",
                Width = 1,
                Height = 1,
                Locale = "C"
            };

            var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => service.ValidateAndNormalizeAsync(manifest, source));

            StringAssert.Contains(error.Message, "must be PNG, JPEG, or WebP");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ValidateAndNormalize_RejectsMismatchedMediaType()
    {
        var root = Path.Combine(Path.GetTempPath(), $"apkg-assets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.png");
            byte[] bytes;
            using (var bitmap = new SKBitmap(1, 1))
            {
                bitmap.SetPixel(0, 0, SKColors.Blue);
                using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                bytes = png.ToArray();
            }
            await File.WriteAllBytesAsync(source, bytes);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Path"] = root })
                .Build();
            var folders = new FeatureFoldersProvider(new StorageRootPathProvider(configuration));
            var service = new AppStreamAssetService(folders);
            var manifest = new ApkgAppStreamScreenshot
            {
                File = "appstream/com.example.demo/screenshots/source.png",
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                MediaType = "image/jpeg",
                Width = 1,
                Height = 1,
                Locale = "C"
            };

            var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => service.ValidateAndNormalizeAsync(manifest, source));

            StringAssert.Contains(error.Message, "media type does not match");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
