using System.Diagnostics;
using System.Text;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Apkg.Services.Authentication;

public class GpgSigningService(ILogger<GpgSigningService> logger) : IGpgSigningService, ITransientDependency
{
    public async Task<(string publicKey, string privateKey, string fingerprint)> GenerateKeyPairAsync(string friendlyName)
    {
        var gpgHome = Path.Combine(Path.GetTempPath(), "apkg-gpg-" + Guid.NewGuid());
        Directory.CreateDirectory(gpgHome);
        try
        {
            logger.LogInformation("Generating GPG key pair for {FriendlyName} in {GpgHome}...", friendlyName, gpgHome);

            // 1. Generate key
            var genKeyScript = $"""
                               Key-Type: RSA
                               Key-Length: 4096
                               Subkey-Type: RSA
                               Subkey-Length: 4096
                               Name-Real: {friendlyName}
                               Expire-Date: 0
                               %no-protection
                               %commit
                               """;

            await RunGpgAsync(gpgHome, "--batch --generate-key", genKeyScript);

            // 2. Get fingerprint
            var listOutput = await RunGpgAsync(gpgHome, "--with-colons --list-keys");
            var fingerprint = listOutput.Split('\n')
                .FirstOrDefault(l => l.StartsWith("fpr:"))?
                .Split(':')[9] ?? throw new Exception("Failed to parse fingerprint from GPG output.");

            // 3. Export Public Key
            var publicKey = await RunGpgAsync(gpgHome, $"--armor --export {fingerprint}");

            // 4. Export Private Key
            var privateKey = await RunGpgAsync(gpgHome, $"--armor --export-secret-keys {fingerprint}");

            return (publicKey, privateKey, fingerprint);
        }
        finally
        {
            if (Directory.Exists(gpgHome)) Directory.Delete(gpgHome, true);
        }
    }

    public async Task<string> SignClearsignAsync(string content, string privateKey)
    {
        var gpgHome = Path.Combine(Path.GetTempPath(), "apkg-gpg-sign-" + Guid.NewGuid());
        Directory.CreateDirectory(gpgHome);
        try
        {
            // 1. Import private key
            await RunGpgAsync(gpgHome, "--import", privateKey);

            // 2. Get key ID/fingerprint for signing
            var listOutput = await RunGpgAsync(gpgHome, "--with-colons --list-secret-keys");
            var keyId = listOutput.Split('\n')
                .FirstOrDefault(l => l.StartsWith("sec:"))?
                .Split(':')[4] ?? throw new Exception("Failed to find imported private key.");

            // 3. Clearsign content
            // We use --digest-algo SHA256 as it is standard for modern APT
            return await RunGpgAsync(gpgHome, $"--clearsign --digest-algo SHA256 --default-key {keyId}", content);
        }
        finally
        {
            if (Directory.Exists(gpgHome)) Directory.Delete(gpgHome, true);
        }
    }

    private async Task<string> RunGpgAsync(string gpgHome, string arguments, string? input = null)
    {
        using var process = new Process();
        process.StartInfo.FileName = "gpg";
        process.StartInfo.Arguments = $"--homedir {gpgHome} " + arguments;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (input != null)
        {
            await using var sw = process.StandardInput;
            await sw.WriteAsync(input);
            await sw.FlushAsync();
            sw.Close();
        }

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            logger.LogError("GPG failed with exit code {ExitCode}. Error: {Error}", process.ExitCode, errorBuilder.ToString());
            throw new Exception($"GPG command failed: {errorBuilder}");
        }

        return outputBuilder.ToString();
    }
}
