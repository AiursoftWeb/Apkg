using System.CommandLine;
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

public class UnpackHandler : ExecutableCommandHandlerBuilder
{
    protected override string Name => "unpack";
    protected override string Description => "Extract the matching .deb file(s) from a .apkg package.";

    private static readonly Option<string> FileOption =
        new(name: "--file", aliases: ["-f"])
        {
            Description = "Path to the .apkg file.",
            Required = true
        };

    private static readonly Option<string> OutputOption =
        new(name: "--output", aliases: ["-o"])
        {
            Description = "Output directory for extracted .deb files. Defaults to current directory.",
            DefaultValueFactory = _ => "."
        };

    private static readonly Option<string?> ArchOption =
        new(name: "--arch")
        {
            Description = "Override architecture (e.g. amd64, arm64). Defaults to current system."
        };

    private static readonly Option<string?> DistroOption =
        new(name: "--distro")
        {
            Description = "Override distro ID (e.g. ubuntu). Defaults to current system."
        };

    private static readonly Option<string?> SuiteOption =
        new(name: "--suite")
        {
            Description = "Override suite/codename (e.g. noble, jammy). Defaults to current system."
        };

    protected override IEnumerable<Option> GetCommandOptions() =>
    [
        FileOption,
        OutputOption,
        ArchOption,
        DistroOption,
        SuiteOption,
        CommonOptionsProvider.DryRunOption,
    ];

    protected override async Task Execute(ParseResult context)
    {
        var verbose = context.GetValue(CommonOptionsProvider.VerboseOption);
        var dryRun = context.GetValue(CommonOptionsProvider.DryRunOption);
        var filePath = Path.GetFullPath(context.GetValue(FileOption)!);
        var outputDir = Path.GetFullPath(context.GetValue(OutputOption)!);
        var archOverride = context.GetValue(ArchOption);
        var distroOverride = context.GetValue(DistroOption);
        var suiteOverride = context.GetValue(SuiteOption);

        var services = ServiceBuilder
            .CreateCommandHostBuilder<Startup>(verbose)
            .Build()
            .Services;

        var serializer = services.GetRequiredService<ManifestSerializer>();
        var systemInfoProvider = services.GetRequiredService<SystemInfoProvider>();
        var logger = services.GetRequiredService<ILogger<UnpackHandler>>();

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Package file not found: {filePath}", filePath);

        var (detectedDistro, detectedSuite) = systemInfoProvider.GetOsInfo();
        var currentDistro = distroOverride ?? detectedDistro;
        var currentSuite = suiteOverride ?? detectedSuite;
        var currentArch = archOverride ?? await systemInfoProvider.GetArchitectureAsync();

        logger.LogInformation("Selecting targets for {Distro} {Suite} ({Architecture})", currentDistro, currentSuite, currentArch);

        var manifest = await ReadManifestAsync(filePath, serializer);
        var matchingDebFiles = manifest.Targets
            .Where(target =>
                string.Equals(target.Distro, currentDistro, StringComparison.OrdinalIgnoreCase) &&
                target.SuiteList.Any(targetSuite => string.Equals(targetSuite, currentSuite, StringComparison.OrdinalIgnoreCase)) &&
                (string.Equals(target.Architecture, currentArch, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(target.Architecture, "all", StringComparison.OrdinalIgnoreCase)))
            .Select(target => NormalizeEntryName(target.DebFile))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matchingDebFiles.Count == 0)
        {
            var availableTargets = string.Join(Environment.NewLine, manifest.Targets.Select(FormatTarget));
            throw new InvalidOperationException(
                $"No matching target found in {filePath} for {currentDistro} {currentSuite} ({currentArch}).{Environment.NewLine}Available targets:{Environment.NewLine}{availableTargets}");
        }

        if (dryRun)
        {
            logger.LogInformation("Dry run: would extract {Count} .deb file(s) to {OutputDir}", matchingDebFiles.Count, outputDir);
        }
        else
        {
            Directory.CreateDirectory(outputDir);
        }

        var extractedCount = await ExtractMatchingEntriesAsync(filePath, matchingDebFiles, outputDir, dryRun, logger);
        if (extractedCount == 0)
            throw new InvalidOperationException("No matching .deb files were found in the archive.");

        if (dryRun)
        {
            logger.LogInformation("Dry run complete.");
        }
        else
        {
            logger.LogInformation("Extracted {Count} .deb file(s) to {OutputDir}", extractedCount, outputDir);
        }
    }

    private static async Task<ApkgManifest> ReadManifestAsync(string apkgPath, ManifestSerializer serializer)
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
            var manifestXml = await reader.ReadToEndAsync();
            return serializer.Deserialize(manifestXml);
        }

        throw new InvalidOperationException($"manifest.xml not found in {apkgPath}");
    }

    private static async Task<int> ExtractMatchingEntriesAsync(
        string apkgPath,
        IReadOnlyCollection<string> matchingDebFiles,
        string outputDir,
        bool dryRun,
        ILogger logger)
    {
        var extractedCount = 0;
        var fileSet = new HashSet<string>(matchingDebFiles, StringComparer.OrdinalIgnoreCase);

        await using var fileStream = new FileStream(apkgPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        await using var tarReader = new TarReader(gzipStream);

        TarEntry? entry;
        while ((entry = await tarReader.GetNextEntryAsync()) != null)
        {
            var entryName = NormalizeEntryName(entry.Name);
            if (!fileSet.Contains(entryName))
                continue;

            var destination = Path.Combine(outputDir, Path.GetFileName(entryName));
            if (dryRun)
            {
                logger.LogInformation("Would extract {Entry} to {Destination}", entryName, destination);
                extractedCount++;
                continue;
            }

            if (entry.DataStream == null)
                throw new InvalidOperationException($"Archive entry {entryName} in {apkgPath} is empty.");

            await using var outputStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await entry.DataStream.CopyToAsync(outputStream);
            logger.LogInformation("Extracted {Entry} to {Destination}", entryName, destination);
            extractedCount++;
        }

        return extractedCount;
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
