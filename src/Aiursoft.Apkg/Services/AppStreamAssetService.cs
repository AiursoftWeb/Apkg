using System.Security.Cryptography;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Services.FileStorage;
using SkiaSharp;

namespace Aiursoft.Apkg.Services;

public sealed record PreparedAppStreamAsset(
    ApkgAppStreamScreenshot Manifest,
    string NormalizedTempPath,
    string ObjectSha256,
    int Width,
    int Height);

public sealed class AppStreamAssetService(FeatureFoldersProvider folders)
{
    public const long MaxSourceBytes = 14L * 1024 * 1024;
    public const int MaxDimension = 16384;
    public const long MaxPixels = 50_000_000;

    public async Task<PreparedAppStreamAsset> ValidateAndNormalizeAsync(
        ApkgAppStreamScreenshot manifest,
        string extractedPath)
    {
        var sourceInfo = new FileInfo(extractedPath);
        if (sourceInfo.Length > MaxSourceBytes)
            throw new InvalidDataException($"AppStream screenshot '{manifest.File}' exceeds the 14 MiB limit.");

        await using (var source = File.OpenRead(extractedPath))
        {
            var actualSourceHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(source));
            if (!string.Equals(actualSourceHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"AppStream screenshot '{manifest.File}' SHA-256 mismatch. Expected {manifest.Sha256}, got {actualSourceHash}.");
        }

        using var codec = SKCodec.Create(extractedPath)
            ?? throw new InvalidDataException($"AppStream screenshot '{manifest.File}' could not be decoded.");
        var actualMediaType = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Png => "image/png",
            SKEncodedImageFormat.Jpeg => "image/jpeg",
            SKEncodedImageFormat.Webp => "image/webp",
            _ => throw new InvalidDataException(
                $"AppStream screenshot '{manifest.File}' must be PNG, JPEG, or WebP.")
        };
        if (!string.Equals(manifest.MediaType, actualMediaType, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"AppStream screenshot '{manifest.File}' media type does not match the file. " +
                $"Expected {manifest.MediaType}, decoded {actualMediaType}.");

        using var bitmap = SKBitmap.Decode(extractedPath)
            ?? throw new InvalidDataException($"AppStream screenshot '{manifest.File}' could not be decoded.");
        if (bitmap.Width <= 0 || bitmap.Height <= 0 ||
            bitmap.Width > MaxDimension || bitmap.Height > MaxDimension ||
            (long)bitmap.Width * bitmap.Height > MaxPixels)
            throw new InvalidDataException(
                $"AppStream screenshot '{manifest.File}' has unsafe dimensions {bitmap.Width}x{bitmap.Height}.");
        if (manifest.Width != bitmap.Width || manifest.Height != bitmap.Height)
            throw new InvalidDataException(
                $"AppStream screenshot '{manifest.File}' dimensions do not match the manifest. " +
                $"Expected {manifest.Width}x{manifest.Height}, decoded {bitmap.Width}x{bitmap.Height}.");

        var normalizedTempPath = Path.Combine(
            folders.GetWorkspaceFolder(), $"appstream-{Guid.NewGuid():N}.png");
        using (var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100))
        await using (var output = File.Create(normalizedTempPath))
            encoded.SaveTo(output);

        await using var normalized = File.OpenRead(normalizedTempPath);
        var objectHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(normalized));
        return new PreparedAppStreamAsset(
            manifest, normalizedTempPath, objectHash, bitmap.Width, bitmap.Height);
    }

    public string Commit(PreparedAppStreamAsset asset)
    {
        var directory = Path.Combine(folders.GetAppStreamObjectsFolder(), asset.ObjectSha256[..2]);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"{asset.ObjectSha256}.png");
        if (File.Exists(destination))
        {
            File.Delete(asset.NormalizedTempPath);
        }
        else
        {
            try
            {
                File.Move(asset.NormalizedTempPath, destination);
            }
            catch (IOException) when (File.Exists(destination))
            {
                File.Delete(asset.NormalizedTempPath);
            }
        }
        return destination;
    }
}
