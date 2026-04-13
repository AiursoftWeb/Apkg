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
    public static async Task<bool> VerifyInReleaseAsync(byte[] inReleaseData, string keyringPath)
    {
        if (string.IsNullOrWhiteSpace(keyringPath)) return true;

        if (!File.Exists(keyringPath))
        {
            Console.Error.WriteLine($"[Warning] Keyring not found: {keyringPath}");
            return false;
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
    public static async Task<bool> VerifyInReleaseAsync(string inReleaseContent, string keyringPath)
    {
        return await VerifyInReleaseAsync(Encoding.UTF8.GetBytes(inReleaseContent), keyringPath);
    }

    /// <summary>
    /// Verifies a file (InRelease or detached signature pair) using gpgv.
    /// </summary>
    public static async Task<bool> VerifyFileAsync(string signedFilePath, string keyringPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gpgv",
            // --status-fd 1 writes status to stdout
            Arguments = $"--status-fd 1 --keyring \"{keyringPath}\" \"{signedFilePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null) return false;

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var output = await outputTask;
            // Check for GOODSIG. gpgv might return non-zero if othersigs fail, but GOODSIG means at least one is valid.
            if (output.Contains("[GNUPG:] GOODSIG")) return true;

            // If we are here, verification failed.
            var err = await errorTask;
            if (!string.IsNullOrWhiteSpace(err))
            {
                Console.Error.WriteLine($"[GPG Error on {signedFilePath}] code {process.ExitCode}:\n{err}");
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error running gpgv: {ex.Message}");
            return false;
        }
    }
}

