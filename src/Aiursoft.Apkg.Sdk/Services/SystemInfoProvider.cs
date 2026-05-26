using System.ComponentModel;
using System.Diagnostics;

namespace Aiursoft.Apkg.Sdk.Services;

public class SystemInfoProvider
{
    /// <summary>
    /// Reads /etc/os-release and returns (distro, suite).
    /// distro = ID field (e.g. "ubuntu", "debian")
    /// suite = VERSION_CODENAME field (e.g. "jammy", "noble", "resolute")
    /// </summary>
    public (string Distro, string Suite) GetOsInfo()
    {
        const string osReleasePath = "/etc/os-release";
        if (!File.Exists(osReleasePath))
            throw new InvalidOperationException("Cannot detect OS: /etc/os-release not found. Is this a Debian/Ubuntu system?");

        string? distro = null;
        string? suite = null;

        foreach (var line in File.ReadLines(osReleasePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
                continue;

            var key = line[..separatorIndex];
            var value = Unquote(line[(separatorIndex + 1)..].Trim());

            switch (key)
            {
                case "ID":
                    distro = value;
                    break;
                case "VERSION_CODENAME":
                    suite = value;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(distro) || string.IsNullOrWhiteSpace(suite))
            throw new InvalidOperationException("Cannot detect OS: missing ID or VERSION_CODENAME in /etc/os-release. Is this a Debian/Ubuntu system?");

        return (distro, suite);
    }

    /// <summary>
    /// Runs dpkg --print-architecture and returns result (e.g. "amd64", "arm64")
    /// </summary>
    public async Task<string> GetArchitectureAsync()
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dpkg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add("--print-architecture");

        try
        {
            process.Start();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            throw new InvalidOperationException("Cannot detect architecture: dpkg not found. Is this a Debian/Ubuntu system?", ex);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Cannot detect architecture: dpkg exited with code {process.ExitCode}. {error}".Trim());

        if (string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException("Cannot detect architecture: dpkg returned an empty result.");

        return output;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
            return value[1..^1];

        return value;
    }
}
