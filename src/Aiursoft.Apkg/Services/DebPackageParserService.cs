using System.Diagnostics;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Apkg.Services;

public class DebPackageParserService : ITransientDependency
{
    public async Task<Dictionary<string, string>> ParseControlAsync(string debFilePath)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dpkg-deb",
            ArgumentList = { "--field", debFilePath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"dpkg-deb failed (exit {process.ExitCode}): {err}");
        }

        return ParseRfc822(output);
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
            }
            else
            {
                var colonIdx = line.IndexOf(':');
                if (colonIdx > 0)
                {
                    if (currentKey != null)
                        result[currentKey] = currentValue.ToString().TrimEnd();
                    currentKey = line[..colonIdx].Trim();
                    currentValue.Clear();
                    currentValue.Append(line[(colonIdx + 1)..].Trim());
                }
            }
        }
        if (currentKey != null)
            result[currentKey] = currentValue.ToString().TrimEnd();

        return result;
    }
}
