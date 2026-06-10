using System.Diagnostics;
using System.Text;

namespace Aiursoft.AptClient.Tests;

[TestClass]
public class AptGpgVerifierTests
{
    /// <summary>
    /// ASCII-armored keyring (-----BEGIN PGP PUBLIC KEY BLOCK-----) must be
    /// auto-dearmored before passing to gpgv, which only accepts binary format.
    /// </summary>
    [TestMethod]
    public async Task VerifyInRelease_AsciiArmoredKeyring_ReturnsValid()
    {
        var (armoredKey, _, signedContent) = GenerateTestKeyAndContent();

        var ascPath = Path.GetTempFileName() + ".asc";

        try
        {
            await File.WriteAllTextAsync(ascPath, armoredKey);

            // Verify with ASCII-armored keyring (should auto-dearmor)
            var (isValid, log) = await AptGpgVerifier.VerifyInReleaseAsync(
                Encoding.UTF8.GetBytes(signedContent), ascPath);

            Assert.IsTrue(isValid, $"ASCII keyring verification failed.\nLog:\n{log}");
            Assert.IsTrue(log.Contains("[GNUPG:] GOODSIG"), $"Log should contain GOODSIG.\nLog:\n{log}");
        }
        finally
        {
            DeleteIfExists(ascPath);
        }
    }

    /// <summary>
    /// Binary keyring (.gpg) must work without any conversion.
    /// </summary>
    [TestMethod]
    public async Task VerifyInRelease_BinaryKeyring_ReturnsValid()
    {
        var (armoredKey, _, signedContent) = GenerateTestKeyAndContent();

        var gpgPath = Path.GetTempFileName() + ".gpg";

        try
        {
            await File.WriteAllBytesAsync(gpgPath, ConvertArmoredToBinary(armoredKey));

            var (isValid, log) = await AptGpgVerifier.VerifyInReleaseAsync(
                Encoding.UTF8.GetBytes(signedContent), gpgPath);

            Assert.IsTrue(isValid, $"Binary keyring verification failed.\nLog:\n{log}");
            Assert.IsTrue(log.Contains("[GNUPG:] GOODSIG"), $"Log should contain GOODSIG.\nLog:\n{log}");
        }
        finally
        {
            DeleteIfExists(gpgPath);
        }
    }

    /// <summary>
    /// Missing keyring file must produce a warning, not crash.
    /// </summary>
    [TestMethod]
    public async Task VerifyInRelease_MissingKeyring_ReturnsInvalid()
    {
        var (_, _, signedContent) = GenerateTestKeyAndContent();

        var nonexistentPath = "/tmp/nonexistent-keyring-42d8361a.gpg";

        var (isValid, log) = await AptGpgVerifier.VerifyInReleaseAsync(
            Encoding.UTF8.GetBytes(signedContent), nonexistentPath);

        Assert.IsFalse(isValid);
        Assert.IsTrue(log.Contains("Keyring not found"), $"Log should mention missing keyring.\nLog:\n{log}");
    }

    /// <summary>
    /// Generates a GPG key pair and returns:
    /// 1. ASCII-armored public key (-----BEGIN PGP PUBLIC KEY BLOCK-----)
    /// 2. Binary public key
    /// 3. A clear-signed InRelease-style message signed with that key
    /// </summary>
    private static (string ArmoredKey, string BinaryKey, string SignedContent) GenerateTestKeyAndContent()
    {
        var tempHome = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempHome);

        try
        {
            // Generate a key pair
            RunGpg(tempHome,
                "--batch --pinentry-mode loopback --passphrase '' " +
                "--quick-gen-key \"Apkg Test Key\" default default");

            // Export public key as ASCII-armored
            var armoredKey = RunGpg(tempHome, "--export --armor \"Apkg Test Key\"");

            // Export public key as binary
            var binaryKeyHex = RunGpg(tempHome, "--export \"Apkg Test Key\"");

            // Create unsigned content (simulates InRelease control data)
            var unsignedContent =
                "Origin: Test\n" +
                "Label: Test\n" +
                "Suite: test\n" +
                "Codename: test\n" +
                "Architectures: amd64\n" +
                "Components: main\n" +
                "Description: Test repository\n" +
                "Date: Thu, 01 Jan 2025 00:00:00 UTC\n" +
                "SHA256:\n" +
                " 0a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b 1234 main/binary-amd64/Packages.gz\n";

            var unsignedFile = Path.Combine(tempHome, "unsigned.txt");
            File.WriteAllText(unsignedFile, unsignedContent);

            // Clear-sign it (InRelease format)
            RunGpg(tempHome,
                $"--batch --yes --pinentry-mode loopback --passphrase '' " +
                $"--clearsign --output \"{unsignedFile}.asc\" \"{unsignedFile}\"");
            var signedContent = File.ReadAllText(unsignedFile + ".asc");

            return (armoredKey, binaryKeyHex, signedContent);
        }
        finally
        {
            try { Directory.Delete(tempHome, true); }
            catch (Exception) { /* cleanup best-effort */ }
        }
    }

    private static string RunGpg(string home, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "gpg",
            Arguments = $"--homedir \"{home}\" --no-random-seed-file {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi);
        if (p == null) throw new InvalidOperationException("Failed to start gpg process.");

        var outputReader = p.StandardOutput;
        var errorReader = p.StandardError;

        p.WaitForExit();

        if (p.ExitCode != 0)
        {
            var err = errorReader.ReadToEnd();
            throw new InvalidOperationException(
                $"GPG Failed (Exit {p.ExitCode}): {err}\nArgs: {args}");
        }

        return outputReader.ReadToEnd();
    }

    private static byte[] ConvertArmoredToBinary(string armoredKey)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var tempIn = Path.Combine(tempDir, "key.asc");
        var tempOut = Path.Combine(tempDir, "key.gpg");

        try
        {
            File.WriteAllText(tempIn, armoredKey);

            var psi = new ProcessStartInfo
            {
                FileName = "gpg",
                Arguments = $"--batch --yes --dearmor --output \"{tempOut}\" \"{tempIn}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null) throw new InvalidOperationException("Failed to start gpg dearmor.");
            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                var err = p.StandardError.ReadToEnd();
                throw new InvalidOperationException(
                    $"gpg --dearmor failed (exit {p.ExitCode}): {err}");
            }

            return File.ReadAllBytes(tempOut);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); }
            catch { /* best effort */ }
        }
    }

    private static void DeleteIfExists(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }
}
