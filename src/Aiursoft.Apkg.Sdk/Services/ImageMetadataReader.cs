using System.Buffers.Binary;

namespace Aiursoft.Apkg.Sdk.Services;

public sealed record ImageMetadata(string MediaType, int Width, int Height, string NormalizedExtension);

/// <summary>Reads dimensions from the image formats accepted for AppStream screenshots.</summary>
public static class ImageMetadataReader
{
    public static ImageMetadata Read(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[30];
        var read = stream.Read(header);
        if (read >= 24 && header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return new ImageMetadata(
                "image/png",
                BinaryPrimitives.ReadInt32BigEndian(header[16..20]),
                BinaryPrimitives.ReadInt32BigEndian(header[20..24]),
                ".png");
        if (read >= 12 && header[0] == 0xff && header[1] == 0xd8)
            return ReadJpeg(stream);
        if (read >= 30 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return ReadWebP(header);

        throw new InvalidDataException("Only PNG, JPEG, and WebP images are supported.");
    }

    private static ImageMetadata ReadJpeg(Stream stream)
    {
        stream.Position = 2;
        Span<byte> lengthBytes = stackalloc byte[2];
        Span<byte> dimensions = stackalloc byte[5];
        while (stream.Position < stream.Length)
        {
            if (stream.ReadByte() != 0xff)
                continue;
            int marker;
            do marker = stream.ReadByte(); while (marker == 0xff);
            if (marker < 0)
                break;
            if (marker is 0xd8 or 0xd9 || marker is >= 0xd0 and <= 0xd7)
                continue;

            stream.ReadExactly(lengthBytes);
            var length = BinaryPrimitives.ReadUInt16BigEndian(lengthBytes);
            if (length < 2)
                break;
            if (marker is >= 0xc0 and <= 0xc3 or >= 0xc5 and <= 0xc7 or >= 0xc9 and <= 0xcb or >= 0xcd and <= 0xcf)
            {
                stream.ReadExactly(dimensions);
                return new ImageMetadata(
                    "image/jpeg",
                    BinaryPrimitives.ReadUInt16BigEndian(dimensions[3..5]),
                    BinaryPrimitives.ReadUInt16BigEndian(dimensions[1..3]),
                    ".jpg");
            }
            stream.Position += length - 2;
        }

        throw new InvalidDataException("The JPEG dimensions could not be read.");
    }

    private static ImageMetadata ReadWebP(ReadOnlySpan<byte> header)
    {
        var chunk = header[12..16];
        if (chunk.SequenceEqual("VP8X"u8))
        {
            var width = 1 + ReadUInt24LittleEndian(header[24..27]);
            var height = 1 + ReadUInt24LittleEndian(header[27..30]);
            return new ImageMetadata("image/webp", width, height, ".webp");
        }
        if (chunk.SequenceEqual("VP8L"u8) && header[20] == 0x2f)
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(header[21..25]);
            var width = (int)(bits & 0x3fff) + 1;
            var height = (int)((bits >> 14) & 0x3fff) + 1;
            return new ImageMetadata("image/webp", width, height, ".webp");
        }
        if (chunk.SequenceEqual("VP8 "u8) && header[23..26].SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a }))
        {
            var width = BinaryPrimitives.ReadUInt16LittleEndian(header[26..28]) & 0x3fff;
            var height = BinaryPrimitives.ReadUInt16LittleEndian(header[28..30]) & 0x3fff;
            return new ImageMetadata("image/webp", width, height, ".webp");
        }

        throw new InvalidDataException("The WebP dimensions could not be read.");
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes) =>
        bytes[0] | bytes[1] << 8 | bytes[2] << 16;
}
