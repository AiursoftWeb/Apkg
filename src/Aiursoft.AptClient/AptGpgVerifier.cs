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
    /// </summary>
    public static async Task<(bool IsValid, string Log)> VerifyFileAsync(string signedFilePath, string keyringPath)
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
            if (process == null) return (false, "Failed to start gpgv process.");

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var output = await outputTask;
            var err = await errorTask;
            var log = $"Output:\n{output}\nError:\n{err}";

            // Check for GOODSIG. gpgv might return non-zero if othersigs fail, but GOODSIG means at least one is valid.
            if (output.Contains("[GNUPG:] GOODSIG")) return (true, log);

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
    }
}

