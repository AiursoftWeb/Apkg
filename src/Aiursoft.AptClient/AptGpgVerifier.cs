using System.Diagnostics;
using System.Text;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.AptClient;

public class AptGpgVerifier
{
    /// <summary>
    /// Verifies the signature of a given content using the specified keyring.
    /// InRelease files contain the signature inline (clear-signed).
    /// </summary>
    public static async Task<(bool IsValid, string Log)> VerifyInReleaseAsync(byte[] inReleaseData, string keyringPath)
    {
        if (string.IsNullOrWhiteSpace(keyringPath)) return (true, "Keyring not specified, verification skipped.");

        if (!File.Exists(keyringPath))
        {
            var err = $"[Warning] Keyring not found: {keyringPath}";
            Console.Error.WriteLine(err);
            return (false, err);
        }

        // Write content to a temp file
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tempFile, inReleaseData);
            return await VerifyFileAsync(tempFile, keyringPath);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // String overload for compatibility if needed (but we prefer byte[])
    [ExcludeFromCodeCoverage]
    public static async Task<(bool IsValid, string Log)> VerifyInReleaseAsync(string inReleaseContent, string keyringPath)
    {
        return await VerifyInReleaseAsync(Encoding.UTF8.GetBytes(inReleaseContent), keyringPath);
    }

    /// <summary>
    /// Verifies a file (InRelease or detached signature pair) using gpgv.
    /// Automatically converts ASCII-armored keyrings to binary format
    /// (gpgv only accepts binary OpenPGP keyring format, RFC 4880 §4).
    /// </summary>
    public static async Task<(bool IsValid, string Log)> VerifyFileAsync(string signedFilePath, string keyringPath)
    {
        // gpgv requires binary OpenPGP keyring format. Auto-dearmor
        // ASCII-armored keyrings (-----BEGIN PGP PUBLIC KEY BLOCK-----).
        string actualKeyring = keyringPath;
        string? tempKeyring = null;

        try
        {
            var firstLine = await ReadFirstLineAsync(keyringPath);
            if (firstLine != null && firstLine.StartsWith("-----BEGIN PGP", StringComparison.Ordinal))
            {
                tempKeyring = Path.GetTempFileName();
                File.Delete(tempKeyring); // gpg --dearmor refuses to overwrite
                var dearmorPsi = new ProcessStartInfo
                {
                    FileName = "gpg",
                    Arguments = $"--batch --no-tty --dearmor --output \"{tempKeyring}\" \"{keyringPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var dearmorProcess = Process.Start(dearmorPsi);
                if (dearmorProcess == null)
                    return (false, "Failed to start gpg dearmor process.");
                await dearmorProcess.WaitForExitAsync();
                if (dearmorProcess.ExitCode != 0)
                {
                    var dearmorErr = await dearmorProcess.StandardError.ReadToEndAsync();
                    return (false, $"Failed to dearmor keyring '{keyringPath}': {dearmorErr}");
                }
                actualKeyring = tempKeyring;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "gpgv",
                // --status-fd 1 writes status to stdout
                Arguments = $"--status-fd 1 --keyring \"{actualKeyring}\" \"{signedFilePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return (false, "Failed to start gpgv process.");

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var output = await outputTask;
            var err = await errorTask;
            var log = $"Output:\n{output}\nError:\n{err}";

            // A good signature packet alone does not mean verification succeeded.
            if (process.ExitCode == 0 && output.Contains("[GNUPG:] VALIDSIG")) return (true, log);

            // If we are here, verification failed.
            if (!string.IsNullOrWhiteSpace(err))
            {
                Console.Error.WriteLine($"[GPG Error on {signedFilePath}] code {process.ExitCode}:\n{err}");
            }
            return (false, log);
        }
        catch (Exception ex)
        {
            var msg = $"Error running gpgv: {ex.Message}";
            Console.Error.WriteLine(msg);
            return (false, msg);
        }
        finally
        {
            if (tempKeyring != null && File.Exists(tempKeyring))
                File.Delete(tempKeyring);
        }
    }

    private static async Task<string?> ReadFirstLineAsync(string path)
    {
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8);
            return await reader.ReadLineAsync();
        }
        catch
        {
            return null;
        }
    }
}
