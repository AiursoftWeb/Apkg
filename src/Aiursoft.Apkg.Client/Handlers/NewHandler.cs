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
    protected override string Description => "Create a new .aosproj package project in the current directory.";

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

        var aosprojSerializer = services.GetRequiredService<AosprojSerializer>();
        var logger = services.GetRequiredService<ILogger<NewHandler>>();

        // Strip .aosproj extension if user typed it — we add it ourselves
        var baseName = name.EndsWith(".aosproj", StringComparison.OrdinalIgnoreCase)
            ? name[..^".aosproj".Length]
            : name;

        var outputDir = Path.GetFullPath(output);
        var projectFilePath = Path.Combine(outputDir, $"{baseName}.aosproj");

        if (File.Exists(projectFilePath))
        {
            logger.LogError("File already exists: {Path}", projectFilePath);
            throw new InvalidOperationException($"File already exists: {projectFilePath}");
        }

        logger.LogInformation("Creating {File}", projectFilePath);

        var project = new AosprojProject
        {
            PackageName = baseName,
            PackageVersion = "1.0.0",
            PackageDescription = $"Description of {baseName}",
            PackageAuthors = "Your Name <you@example.com>",
            Maintainer = "Your Name <you@example.com>",
            PackageHomepage = $"https://github.com/example/{baseName}",
            LicenseType = "MIT",
            Component = "main",
            TargetDistro = "ubuntu",
            TargetSuites = "jammy noble resolute",
            TargetArchitectures = "amd64 arm64",
        };

        await aosprojSerializer.SerializeToFileAsync(project, projectFilePath);

        logger.LogInformation("Created {File}", projectFilePath);
        logger.LogInformation("Next steps:");
        logger.LogInformation("  1. Edit {File} to fill in metadata, TargetSuites, TargetArchitectures.", projectFilePath);
        logger.LogInformation("  2. Add source files:  apkg add ./myfile --target /usr/lib/myfile");
        logger.LogInformation("  3. Build debs:        apkg build --all");
        logger.LogInformation("  4. Pack for upload:   apkg publish");
    }
}
