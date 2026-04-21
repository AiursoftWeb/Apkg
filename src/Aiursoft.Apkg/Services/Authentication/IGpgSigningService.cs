namespace Aiursoft.Apkg.Services.Authentication;

public interface IGpgSigningService
{
    /// <summary>
    /// Generates a new RSA key pair for APT signing.
    /// </summary>
    /// <param name="friendlyName">The name for the key (UID).</param>
    /// <returns>A tuple containing (PublicKey, PrivateKey, Fingerprint).</returns>
    Task<(string publicKey, string privateKey, string fingerprint)> GenerateKeyPairAsync(string friendlyName);

    /// <summary>
    /// Signs a string using the provided private key in GPG clearsign format.
    /// </summary>
    /// <param name="content">The text to sign (e.g. Release file content).</param>
    /// <param name="privateKey">The ASCII-armored private key.</param>
    /// <returns>The signed content (InRelease format).</returns>
    Task<string> SignClearsignAsync(string content, string privateKey);
}
