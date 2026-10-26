using System.Security.Cryptography;
using Aiursoft.AptClient.Abstractions;

namespace Aiursoft.AptClient;

/// <summary>Strong checksums accepted from APT metadata; never falls back to SHA-1 or MD5.</summary>
public readonly record struct AptChecksum(string Algorithm, string Value)
{
    public static AptChecksum ForPackage(DebianPackage package)
    {
        // Validate every advertised strong digest, even when preferring SHA-512.
        if (!string.IsNullOrEmpty(package.SHA256)) Validate("SHA256", package.SHA256);
        if (!string.IsNullOrEmpty(package.SHA512)) Validate("SHA512", package.SHA512);
        return !string.IsNullOrEmpty(package.SHA512)
            ? new AptChecksum("SHA512", package.SHA512)
            : Validate("SHA256", package.SHA256);
    }

    public static AptChecksum Validate(string algorithm, string value)
    {
        var length = algorithm switch
        {
            "SHA256" => 64,
            "SHA512" => 128,
            _ => throw new InvalidDataException($"Unsupported APT checksum: {algorithm}.")
        };
        if (value.Length != length || !value.All(Uri.IsHexDigit))
            throw new InvalidDataException($"Missing or invalid {algorithm} checksum.");
        return new AptChecksum(algorithm, value);
    }

    // ParseInRelease's existing dictionary contract retains raw hex digests.
    public static AptChecksum FromDigest(string value) =>
        Validate(value.Length == 128 ? "SHA512" : "SHA256", value);

    public HashAlgorithm CreateHasher() => Algorithm switch
    {
        "SHA512" => SHA512.Create(),
        "SHA256" => SHA256.Create(),
        _ => throw new InvalidDataException($"Unsupported APT checksum: {Algorithm}.")
    };
}
