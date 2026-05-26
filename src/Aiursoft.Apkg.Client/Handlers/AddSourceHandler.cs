using System.CommandLine;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.CommandFramework.Models;
using Aiursoft.CommandFramework.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aiursoft.Apkg.Client.Handlers;

public class AddSourceHandler : ExecutableCommandHandlerBuilder
{
    protected override string Name => "add-source";
    protected override string Description => "Add an APT source from an Apkg source config URL.";

    private static readonly Option<string> UrlOption =
        new(name: "--url", aliases: ["-u"])
        {
            Description = "Source config URL (e.g. https://server/api/sources/42).",
            Required = true
        };

    protected override IEnumerable<Option> GetCommandOptions() =>
    [
        UrlOption,
        CommonOptionsProvider.DryRunOption,
    ];

    protected override async Task Execute(ParseResult context)
    {
        var verbose = context.GetValue(CommonOptionsProvider.VerboseOption);
        var dryRun = context.GetValue(CommonOptionsProvider.DryRunOption);
        var url = context.GetValue(UrlOption)!;

        if (!IsRunningAsRoot())
            throw new InvalidOperationException($"This command requires root privileges. Try: sudo apkg add-source {url}");

        var services = ServiceBuilder
            .CreateCommandHostBuilder<Startup>(verbose)
            .Build()
            .Services;

        var sourceService = services.GetRequiredService<ApkgSourceService>();
        var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
        var logger = services.GetRequiredService<ILogger<AddSourceHandler>>();

        logger.LogInformation("Fetching source config from {Url}", url);
        var config = await sourceService.GetSourceConfigAsync(url);

        var sourcesPath = Path.Combine("/etc/apt/sources.list.d", config.SourcesFileName);
        var keyringPath = string.IsNullOrWhiteSpace(config.KeyFileName)
            ? null
            : Path.Combine("/usr/share/keyrings", config.KeyFileName);

        if (config.EnableGpgSign)
        {
            if (string.IsNullOrWhiteSpace(config.KeyUrl) || string.IsNullOrWhiteSpace(config.KeyFileName) || string.IsNullOrWhiteSpace(keyringPath))
                throw new InvalidOperationException("The source config requires GPG signing, but keyUrl or keyFileName is missing.");

            logger.LogInformation("Downloading repository signing key from {KeyUrl}", config.KeyUrl);
            if (dryRun)
            {
                logger.LogInformation("Dry run: would write keyring to {KeyringPath}", keyringPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(keyringPath)!);
                using var httpClient = httpClientFactory.CreateClient();
                var armoredKey = await DownloadBytesAsync(httpClient, config.KeyUrl);
                var dearmoredKey = await DearmorAsync(armoredKey);
                await File.WriteAllBytesAsync(keyringPath, dearmoredKey);
            }
        }

        var sourcesContent = BuildSourcesContent(config, url, keyringPath);
        logger.LogInformation("Preparing source file at {SourcesPath}", sourcesPath);
        if (dryRun)
        {
            logger.LogInformation("Dry run: would write source file:{NewLine}{Content}", Environment.NewLine, sourcesContent);
            logger.LogInformation("Dry run: would run apt-get update");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(sourcesPath)!);
        await File.WriteAllTextAsync(sourcesPath, sourcesContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        logger.LogInformation("Running apt-get update...");
        await RunProcessAsync("apt-get", ["update"], "apt-get update failed.");
        logger.LogInformation("Source '{Name}' added successfully.", config.Name);
    }

    private static string BuildSourcesContent(SourceConfig config, string sourceUrl, string? keyringPath)
    {
        var components = NormalizeComponents(config.Components);
        var builder = new StringBuilder()
            .AppendLine("# Added by apkg add-source")
            .AppendLine($"# Source: {sourceUrl}")
            .AppendLine("Types: deb")
            .AppendLine($"URIs: {config.AptBaseUrl}")
            .AppendLine($"Suites: {config.Suite}")
            .AppendLine($"Components: {components}")
            .AppendLine($"Architectures: {config.Architecture}");

        if (config.EnableGpgSign)
        {
            if (string.IsNullOrWhiteSpace(keyringPath))
                throw new InvalidOperationException("A signed source requires a keyring path.");

            builder.AppendLine($"Signed-By: {keyringPath}");
        }
        else
        {
            builder.AppendLine("Trusted: yes");
        }

        return builder.ToString();
    }

    private static string NormalizeComponents(string components)
    {
        var values = components
            .Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return values.Length == 0 ? "main" : string.Join(' ', values);
    }

    private static bool IsRunningAsRoot()
    {
        const string statusPath = "/proc/self/status";
        if (File.Exists(statusPath))
        {
            foreach (var line in File.ReadLines(statusPath))
            {
                if (!line.StartsWith("Uid:", StringComparison.Ordinal))
                    continue;

                var parts = line[4..]
                    .Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out var effectiveUid))
                    return effectiveUid == 0;
            }
        }

        return string.Equals(Environment.UserName, "root", StringComparison.Ordinal);
    }

    private static async Task<byte[]> DownloadBytesAsync(HttpClient httpClient, string url)
    {
        using var response = await httpClient.GetAsync(url);
        var body = await response.Content.ReadAsByteArrayAsync();

        if (!response.IsSuccessStatusCode)
        {
            var text = Encoding.UTF8.GetString(body);
            throw new InvalidOperationException($"Failed to download key from {url}: {response.StatusCode}{Environment.NewLine}{text}");
        }

        return body;
    }

    private static async Task<byte[]> DearmorAsync(byte[] armoredKey)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "gpg",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add("--dearmor");

        try
        {
            process.Start();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            throw new InvalidOperationException("gpg was not found. Please install GnuPG and try again.", ex);
        }

        await process.StandardInput.BaseStream.WriteAsync(armoredKey);
        await process.StandardInput.BaseStream.FlushAsync();
        process.StandardInput.Close();

        await using var output = new MemoryStream();
        var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(output);
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync());

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"gpg --dearmor failed with exit code {process.ExitCode}: {stderrTask.Result}".Trim());

        return output.ToArray();
    }

    private static async Task RunProcessAsync(string fileName, IEnumerable<string> arguments, string errorMessage)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            process.Start();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            throw new InvalidOperationException($"{fileName} was not found. Is this a Debian/Ubuntu system?", ex);
        }

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{errorMessage} Exit code: {process.ExitCode}");
    }
}
