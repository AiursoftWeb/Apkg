using System.CommandLine;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.CommandFramework.Models;
using Aiursoft.CommandFramework.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aiursoft.Apkg.Client.Handlers;

public class NewHandler : ExecutableCommandHandlerBuilder
{
    protected override string Name => "new";
    protected override string Description => "Create a new Apkg package manifest in the current directory.";

    private static readonly Option<string> NameOption =
        new(name: "--name", aliases: ["-n"])
        {
            Description = "The name of the new package.",
            Required = true
        };

    private static readonly Option<string> OutputOption =
        new(name: "--output", aliases: ["-o"])
        {
            Description = "The output directory where the package project will be created.",
            DefaultValueFactory = _ => "."
        };

    protected override IEnumerable<Option> GetCommandOptions() =>
    [
        NameOption,
        OutputOption,
    ];

    protected override async Task Execute(ParseResult context)
    {
        var verbose = context.GetValue(CommonOptionsProvider.VerboseOption);
        var name = context.GetValue(NameOption)!;
        var output = context.GetValue(OutputOption)!;

        var services = ServiceBuilder
            .CreateCommandHostBuilder<Startup>(verbose)
            .Build()
            .Services;

        var serializer = services.GetRequiredService<ManifestSerializer>();
        var logger = services.GetRequiredService<ILogger<NewHandler>>();

        var projectDir = Path.GetFullPath(Path.Combine(output, name));
        var debsDir = Path.Combine(projectDir, "debs");
        var manifestPath = Path.Combine(projectDir, "manifest.xml");

        if (Directory.Exists(projectDir))
        {
            logger.LogError("Directory already exists: {Path}", projectDir);
            throw new InvalidOperationException($"Directory already exists: {projectDir}");
        }

        logger.LogInformation("Creating package project at {Path}", projectDir);
        Directory.CreateDirectory(debsDir);

        var manifest = new ApkgManifest
        {
            Package = name,
            Version = "1.0.0",
            Maintainer = "Your Name <you@example.com>",
            Description = $"Description of {name}",
            Homepage = $"https://github.com/example/{name}",
            License = "MIT",
            Component = "main",
            Targets =
            [
                new ManifestTarget
                {
                    Distro = "ubuntu",
                    Suites = "jammy noble resolute",
                    Architecture = "amd64",
                    DebFile = $"debs/{name}_1.0.0_amd64.deb"
                },
                new ManifestTarget
                {
                    Distro = "ubuntu",
                    Suites = "jammy noble resolute",
                    Architecture = "arm64",
                    DebFile = $"debs/{name}_1.0.0_arm64.deb"
                }
            ]
        };

        await serializer.SerializeToFileAsync(manifest, manifestPath);
        await File.WriteAllTextAsync(Path.Combine(debsDir, ".gitkeep"), string.Empty);

        logger.LogInformation("Created manifest.xml and debs/ directory.");
        logger.LogInformation("Next steps:");
        logger.LogInformation("  1. Edit {ManifestPath} to fill in your package metadata.", manifestPath);
        logger.LogInformation("  2. Add your .deb files to {DebsDir}", debsDir);
        logger.LogInformation("  3. Run: apkg pack --path {ProjectDir}", projectDir);
    }
}
