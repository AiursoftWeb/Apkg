using System.CommandLine;
using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.CommandFramework.Models;
using Aiursoft.CommandFramework.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aiursoft.Apkg.Client.Handlers;

public class InstallHandler : ExecutableCommandHandlerBuilder
{
    protected override string Name => "install";
    protected override string Description => "Install an .apkg package on the current system using dpkg.";

    private static readonly Option<string> FileOption =
        new(name: "--file", aliases: ["-f"])
        {
            Description = "Path to the .apkg file to install.",
            Required = true
        };

    protected override IEnumerable<Option> GetCommandOptions() =>
    [
        FileOption,
    ];

    protected override async Task Execute(ParseResult context)
    {
        var verbose = context.GetValue(CommonOptionsProvider.VerboseOption);
        var filePath = context.GetValue(FileOption)!;

        var services = ServiceBuilder
            .CreateCommandHostBuilder<Startup>(verbose)
            .Build()
            .Services;

        var serializer = services.GetRequiredService<ManifestSerializer>();
        var systemInfoProvider = services.GetRequiredService<SystemInfoProvider>();
        var logger = services.GetRequiredService<ILogger<InstallHandler>>();

        var apkgPath = Path.GetFullPath(filePath);
        if (!File.Exists(apkgPath))
        {
            logger.LogError(".apkg file not found at {Path}", apkgPath);
            throw new FileNotFoundException($"Package file not found: {apkgPath}", apkgPath);
        }

        var (distro, suite) = systemInfoProvider.GetOsInfo();
        var architecture = await systemInfoProvider.GetArchitectureAsync();
        logger.LogInformation("Detected system: {Distro} {Suite} ({Architecture})", distro, suite, architecture);

        var manifestContent = await ReadManifestAsync(apkgPath);
        var manifest = serializer.Deserialize(manifestContent);

        var target = manifest.Targets.FirstOrDefault(target =>
            string.Equals(target.Distro, distro, StringComparison.OrdinalIgnoreCase) &&
            target.SuiteList.Any(targetSuite => string.Equals(targetSuite, suite, StringComparison.OrdinalIgnoreCase)) &&
            (string.Equals(target.Architecture, architecture, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(target.Architecture, "all", StringComparison.OrdinalIgnoreCase)));

        if (target == null)
        {
            var availableTargets = string.Join(Environment.NewLine, manifest.Targets.Select(FormatTarget));
            var message = $"No matching target found in {apkgPath} for {distro} {suite} ({architecture}).{Environment.NewLine}Available targets:{Environment.NewLine}{availableTargets}";
            logger.LogError(message);
            throw new InvalidOperationException(message);
        }

        logger.LogInformation("Found matching target: {DebFile} for {Distro} {Suites} ({Architecture})", target.DebFile, distro, target.Suites, architecture);

        var tempDebPath = CreateTempDebPath();
        try
        {
            await ExtractEntryToFileAsync(apkgPath, target.DebFile, tempDebPath);
            logger.LogInformation("Running dpkg -i {DebFile}", tempDebPath);
            await InstallDebAsync(tempDebPath);
            logger.LogInformation("Package installed successfully.");
        }
        finally
        {
            if (File.Exists(tempDebPath))
                File.Delete(tempDebPath);
        }
    }

    private static async Task<string> ReadManifestAsync(string apkgPath)
    {
        await using var fileStream = new FileStream(apkgPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        await using var tarReader = new TarReader(gzipStream);

        TarEntry? entry;
        while ((entry = await tarReader.GetNextEntryAsync()) != null)
        {
            if (!string.Equals(NormalizeEntryName(entry.Name), "manifest.xml", StringComparison.OrdinalIgnoreCase))
                continue;

            if (entry.DataStream == null)
                throw new InvalidOperationException($"manifest.xml in {apkgPath} is empty.");

            using var reader = new StreamReader(entry.DataStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            return await reader.ReadToEndAsync();
        }

        throw new InvalidOperationException($"manifest.xml not found in {apkgPath}");
    }

    private static async Task ExtractEntryToFileAsync(string apkgPath, string entryName, string outputFilePath)
    {
        var normalizedEntryName = NormalizeEntryName(entryName);

        await using var fileStream = new FileStream(apkgPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        await using var tarReader = new TarReader(gzipStream);

        TarEntry? entry;
        while ((entry = await tarReader.GetNextEntryAsync()) != null)
        {
            if (!string.Equals(NormalizeEntryName(entry.Name), normalizedEntryName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (entry.DataStream == null)
                throw new InvalidOperationException($"Archive entry {entryName} in {apkgPath} is empty.");

            await using var outputStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await entry.DataStream.CopyToAsync(outputStream);
            return;
        }

        throw new InvalidOperationException($"Archive entry not found in {apkgPath}: {entryName}");
    }

    private static async Task InstallDebAsync(string debPath)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dpkg",
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false
        };
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(debPath);

        try
        {
            process.Start();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            throw new InvalidOperationException("dpkg -i failed because dpkg was not found. Is this a Debian/Ubuntu system?", ex);
        }

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"dpkg -i failed with exit code {process.ExitCode}");
    }

    private static string CreateTempDebPath()
    {
        var tempFilePath = Path.GetTempFileName();
        File.Delete(tempFilePath);
        return $"{tempFilePath}.deb";
    }

    private static string FormatTarget(ManifestTarget target)
    {
        return $"- {target.Distro} {target.Suites} ({target.Architecture}) => {target.DebFile}";
    }

    private static string NormalizeEntryName(string entryName)
    {
        var normalized = entryName.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        return normalized.TrimStart('/');
    }
}
