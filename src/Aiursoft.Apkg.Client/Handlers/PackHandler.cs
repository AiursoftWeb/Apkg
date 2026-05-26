using System.CommandLine;
using System.Formats.Tar;
using System.IO.Compression;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.CommandFramework.Models;
using Aiursoft.CommandFramework.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aiursoft.Apkg.Client.Handlers;

public class PackHandler : ExecutableCommandHandlerBuilder
{
    protected override string Name => "pack";
    protected override string Description => "Pack an Apkg package project into a .apkg archive.";

    private static readonly Option<string> PathOption =
        new(name: "--path", aliases: ["-p"])
        {
            Description = "The path to the package source directory (must contain manifest.xml).",
            Required = true
        };

    private static readonly Option<string> OutputOption =
        new(name: "--output", aliases: ["-o"])
        {
            Description = "The output directory for the packed .apkg file.",
            DefaultValueFactory = _ => "."
        };

    protected override IEnumerable<Option> GetCommandOptions() =>
    [
        PathOption,
        OutputOption,
    ];

    protected override async Task Execute(ParseResult context)
    {
        var verbose = context.GetValue(CommonOptionsProvider.VerboseOption);
        var sourcePath = context.GetValue(PathOption)!;
        var outputPath = context.GetValue(OutputOption)!;

        var services = ServiceBuilder
            .CreateCommandHostBuilder<Startup>(verbose)
            .Build()
            .Services;

        var serializer = services.GetRequiredService<ManifestSerializer>();
        var validator = services.GetRequiredService<DebPackageValidator>();
        var logger = services.GetRequiredService<ILogger<PackHandler>>();

        var projectDir = Path.GetFullPath(sourcePath);
        var manifestPath = Path.Combine(projectDir, "manifest.xml");

        if (!File.Exists(manifestPath))
        {
            logger.LogError("manifest.xml not found at {Path}", manifestPath);
            throw new FileNotFoundException($"manifest.xml not found in {projectDir}");
        }

        logger.LogInformation("Reading manifest from {Path}", manifestPath);
        var manifest = await serializer.DeserializeFromFileAsync(manifestPath);

        if (string.IsNullOrWhiteSpace(manifest.Package))
            throw new InvalidOperationException("manifest.xml: <Package> is required.");
        if (string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidOperationException("manifest.xml: <Version> is required.");
        if (manifest.Targets.Count == 0)
            throw new InvalidOperationException("manifest.xml: at least one <Target> is required.");

        // Validate all referenced .deb files exist before creating the archive.
        var debFiles = manifest.Targets
            .Select(t => t.DebFile)
            .Distinct()
            .ToList();

        foreach (var relativeDebPath in debFiles)
        {
            var absoluteDebPath = Path.Combine(projectDir, relativeDebPath);
            if (!File.Exists(absoluteDebPath))
            {
                logger.LogError("Missing .deb file referenced in manifest: {Path}", absoluteDebPath);
                throw new FileNotFoundException($"Referenced .deb file not found: {relativeDebPath}");
            }
        }

        logger.LogInformation("Validating .deb files against manifest...");
        foreach (var target in manifest.Targets)
        {
            var absoluteDebPath = Path.Combine(projectDir, target.DebFile);
            await validator.ValidateAsync(absoluteDebPath, target.DebFile, target, manifest);
            logger.LogDebug("  ✓ {DebFile} ({Architecture})", target.DebFile, target.Architecture);
        }

        var outputFileName = $"{manifest.Package}.{manifest.Version}.apkg";
        var outputDir = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(outputDir);
        var outputFilePath = Path.Combine(outputDir, outputFileName);

        logger.LogInformation("Packing {Count} .deb file(s) into {Output}", debFiles.Count, outputFilePath);

        // Write tar.gz directly to the .apkg file.
        await using (var fileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
        await using (var gzip = new GZipStream(fileStream, CompressionLevel.Optimal))
        await using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false))
        {
            await tar.WriteEntryAsync(manifestPath, "manifest.xml");
            logger.LogDebug("  + manifest.xml");

            foreach (var relativeDebPath in debFiles)
            {
                var absoluteDebPath = Path.Combine(projectDir, relativeDebPath);
                var entryName = relativeDebPath.Replace('\\', '/');
                await tar.WriteEntryAsync(absoluteDebPath, entryName);
                logger.LogDebug("  + {Entry}", entryName);
            }
        }

        logger.LogInformation("Done! Created {Output}", outputFilePath);
    }
}
