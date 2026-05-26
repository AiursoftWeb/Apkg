using System.ComponentModel;
using System.Diagnostics;
using Aiursoft.Apkg.Sdk.Models;

namespace Aiursoft.Apkg.Sdk.Services;

public class DebPackageValidator
{
    public async Task ValidateAsync(string absoluteDebPath, string relativeDebPath, ManifestTarget target, ApkgManifest manifest)
    {
        Dictionary<string, string> control;
        try
        {
            control = await ReadControlAsync(absoluteDebPath);
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException("dpkg-deb is not installed. Install it with: sudo apt-get install dpkg");
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException($"'{relativeDebPath}' is not a valid .deb file: {ex.Message}");
        }

        var package = GetRequiredField(control, "Package", relativeDebPath);
        var version = GetRequiredField(control, "Version", relativeDebPath);
        var architecture = GetRequiredField(control, "Architecture", relativeDebPath);

        if (!package.Equals(manifest.Package, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Validation failed for {relativeDebPath}: Package mismatch. manifest declares '{manifest.Package}' but .deb control says '{package}'.");
        }

        if (!version.Equals(manifest.Version, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Validation failed for {relativeDebPath}: Version mismatch. manifest declares '{manifest.Version}' but .deb control says '{version}'.");
        }

        if (!architecture.Equals("all", StringComparison.OrdinalIgnoreCase)
            && !architecture.Equals(target.Architecture, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Validation failed for {relativeDebPath}: Architecture mismatch. manifest declares '{target.Architecture}' but .deb control says '{architecture}'.");
        }
    }

    private static async Task<Dictionary<string, string>> ReadControlAsync(string absoluteDebPath)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dpkg-deb",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add("--field");
        process.StartInfo.ArgumentList.Add(absoluteDebPath);

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(error) ? "Unknown error." : error.Trim();
            throw new InvalidDataException(details);
        }

        return ParseRfc822(output);
    }

    private static string GetRequiredField(Dictionary<string, string> control, string fieldName, string relativeDebPath)
    {
        if (!control.TryGetValue(fieldName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Validation failed for {relativeDebPath}: .deb control is missing required field '{fieldName}'.");
        }

        return value.Trim();
    }

    private static Dictionary<string, string> ParseRfc822(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;
        var currentValue = new System.Text.StringBuilder();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrEmpty(line))
            {
                if (currentKey != null)
                {
                    result[currentKey] = currentValue.ToString().TrimEnd();
                    currentKey = null;
                    currentValue.Clear();
                }

                continue;
            }

            if (line[0] == ' ' || line[0] == '\t')
            {
                if (currentKey != null)
                {
                    currentValue.Append('\n');
                    currentValue.Append(line.TrimEnd());
                }

                continue;
            }

            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0)
            {
                continue;
            }

            if (currentKey != null)
                result[currentKey] = currentValue.ToString().TrimEnd();

            currentKey = line[..colonIndex].Trim();
            currentValue.Clear();
            currentValue.Append(line[(colonIndex + 1)..].Trim());
        }

        if (currentKey != null)
            result[currentKey] = currentValue.ToString().TrimEnd();

        return result;
    }
}
