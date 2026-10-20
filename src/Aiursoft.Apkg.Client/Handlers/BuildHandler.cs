using System.CommandLine;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.CommandFramework.Models;
using Aiursoft.CommandFramework.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aiursoft.Apkg.Client.Handlers;

public class BuildHandler : ExecutableCommandHandlerBuilder
{
    protected override string Name => "build";
    protected override string Description => "Build .deb packages from a .aosproj project.";

    private static readonly Option<string> PathOption =
        new(name: "--path", aliases: ["-p"])
        {
            Description = "Directory containing the .aosproj file (defaults to current directory).",
            DefaultValueFactory = _ => "."
        };

    private static readonly Option<string> OutputOption =
        new(name: "--output", aliases: ["-o"])
        {
            Description = "Output directory for built .deb files (defaults to bin/ inside the project directory).",
            DefaultValueFactory = _ => string.Empty
        };

    private static readonly Option<string> DistroOption =
        new(name: "--distro")
        {
            Description = "Target Linux distribution (e.g. ubuntu). Required unless --all is specified.",
            DefaultValueFactory = _ => string.Empty
        };

    private static readonly Option<string> SuiteOption =
        new(name: "--suite")
        {
            Description = "Target suite/codename (e.g. jammy). Defaults to all suites declared in the project.",
            DefaultValueFactory = _ => string.Empty
        };

    private static readonly Option<string> ArchOption =
        new(name: "--arch")
        {
            Description = "Target CPU architecture (e.g. amd64, arm64). Defaults to all architectures declared in the project.",
            DefaultValueFactory = _ => string.Empty
        };

    private static readonly Option<bool> AllOption =
        new(name: "--all")
        {
            Description = "Build for all combinations of TargetSuites × TargetArchitectures declared in the project.",
            DefaultValueFactory = _ => false
        };

    private static readonly Option<bool> SkipExistingOption = new("--skip-existing")
    {
        Description = "Skip building when every exact target version is published on --source."
    };

    private static readonly Option<string> SourceOption = new("--source")
    {
        Description = "Apkg server URL to query anonymously with --skip-existing.",
        DefaultValueFactory = _ => string.Empty
    };

    protected override IEnumerable<Option> GetCommandOptions() =>
    [
        PathOption,
        OutputOption,
        DistroOption,
        SuiteOption,
        ArchOption,
        AllOption,
        SkipExistingOption,
        SourceOption,
    ];

    protected override async Task Execute(ParseResult context)
    {
        var verbose = context.GetValue(CommonOptionsProvider.VerboseOption);
        var pathArg = context.GetValue(PathOption)!;
        var outputArg = context.GetValue(OutputOption)!;
        var distroArg = context.GetValue(DistroOption)!;
        var suiteArg = context.GetValue(SuiteOption)!;
        var archArg = context.GetValue(ArchOption)!;
        var buildAll = context.GetValue(AllOption);
        var skipExisting = context.GetValue(SkipExistingOption);
        var source = context.GetValue(SourceOption)!;
        if (skipExisting && string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("--skip-existing requires --source.");

        var services = ServiceBuilder
            .CreateCommandHostBuilder<Startup>(verbose)
            .Build()
            .Services;

        var aosprojSerializer = services.GetRequiredService<AosprojSerializer>();
        var debBuilder = services.GetRequiredService<DebBuilder>();
        var logger = services.GetRequiredService<ILogger<BuildHandler>>();

        var projectDir = Path.GetFullPath(pathArg);
        var projectFile = AosprojSerializer.FindProjectFile(projectDir);
        var project = await aosprojSerializer.DeserializeFromFileAsync(projectFile);

        // Run lint before build — errors abort, warnings are printed
        var conditionEvaluator = services.GetRequiredService<ConditionEvaluator>();
        var linter = new AosprojLinter(conditionEvaluator);
        var issues = linter.Lint(project, projectDir);
        foreach (var issue in issues)
        {
            if (issue.Level == AosprojLinter.Severity.Error)
            {
                logger.LogError("[Lint/{Level}] {Message}", issue.Level, issue.Message);
            }
            else
            {
                logger.LogWarning("[Lint/{Level}] {Message}", issue.Level, issue.Message);
            }
        }
        if (issues.Any(i => i.Level == AosprojLinter.Severity.Error))
            throw new InvalidOperationException("Lint found errors. Fix them before building.");

        var outputDir = string.IsNullOrWhiteSpace(outputArg)
            ? Path.Combine(projectDir, "bin")
            : Path.GetFullPath(outputArg);

        var targets = PackageBuildPlanResolver.ResolveBuildTargets(
            project, buildAll, distroArg, suiteArg, archArg);
        if (skipExisting)
        {
            var plan = await services.GetRequiredService<PackageBuildPlanResolver>()
                .ResolveAsync(projectDir, project, buildAll, distroArg, suiteArg, archArg);
            try
            {
                if (await services.GetRequiredService<AptPackageIndexClient>()
                        .IsPublishedAsync(plan, source))
                {
                    logger.LogInformation(
                        "All {Count} target version(s) are published on {Source}. Build skipped.",
                        plan.Targets.Count, source);
                    return;
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException or InvalidDataException)
            {
                logger.LogWarning(exception,
                    "Could not verify published versions. Falling back to the full build.");
            }
        }

        logger.LogInformation("Building {Count} target(s) for {Package} {Version}...",
            targets.Count, project.PackageName, project.PackageVersion);

        foreach (var (distro, suite, arch) in targets)
        {
            logger.LogInformation("  [{Distro}/{Suite}/{Arch}]", distro, suite, arch);
            await debBuilder.BuildAsync(projectDir, project, distro, suite, arch, outputDir).ConfigureAwait(false);
        }

        logger.LogInformation("Build complete. Debs written to {OutputDir}", outputDir);
    }
}
