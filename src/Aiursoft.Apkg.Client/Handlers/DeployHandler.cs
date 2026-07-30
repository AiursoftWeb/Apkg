using System.CommandLine;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.CommandFramework.Models;
using Aiursoft.CommandFramework.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aiursoft.Apkg.Client.Handlers;

public sealed class DeployHandler : ExecutableCommandHandlerBuilder
{
    protected override string Name => "deploy";
    protected override string Description =>
        "Build and push a project, optionally skipping the entire build when every target already exists.";

    private static readonly Option<string> PathOption =
        new(name: "--path", aliases: ["-p"])
        {
            Description = "Directory containing the .aosproj file.",
            DefaultValueFactory = _ => "."
        };

    private static readonly Option<string> OutputOption =
        new(name: "--output", aliases: ["-o"])
        {
            Description = "Output directory for the .apkg archive.",
            DefaultValueFactory = _ => string.Empty
        };

    private static readonly Option<string> SourceOption =
        new(name: "--source", aliases: ["-s"])
        {
            Description = "Destination Apkg server URL.",
            Required = true
        };

    private static readonly Option<string> ApiKeyOption =
        new(name: "--api-key", aliases: ["-k"])
        {
            Description = "API key for the destination server.",
            Required = true
        };

    private static readonly Option<string> DistroOption = new("--distro")
    {
        Description = "Override the target distribution.",
        DefaultValueFactory = _ => string.Empty
    };

    private static readonly Option<string> SuiteOption = new("--suite")
    {
        Description = "Build one target suite.",
        DefaultValueFactory = _ => string.Empty
    };

    private static readonly Option<string> ArchOption = new("--arch")
    {
        Description = "Build one target architecture.",
        DefaultValueFactory = _ => string.Empty
    };

    private static readonly Option<bool> AllOption = new("--all")
    {
        Description = "Build the complete TargetSuites × TargetArchitectures matrix.",
        DefaultValueFactory = _ => false
    };

    private static readonly Option<bool> SkipExistingOption = new("--skip-existing")
    {
        Description =
            "Ask the server before building and skip the entire publish when every target exists. " +
            "Also enables race-safe duplicate skipping during upload.",
        DefaultValueFactory = _ => false
    };

    private static readonly Option<bool> SkipDuplicateOption = new("--skip-duplicate")
    {
        Description = "Skip packages that become duplicates during upload.",
        DefaultValueFactory = _ => false
    };

    private static readonly Option<bool> AllowDowngradeOption = new("--allow-downgrade")
    {
        Description = "Allow uploading an older version than the currently published package.",
        DefaultValueFactory = _ => false
    };

    private static readonly Option<long> ChunkSizeOption = new("--chunk-size")
    {
        Description = "Maximum upload chunk size in bytes. Set to 0 to disable chunked upload.",
        DefaultValueFactory = _ => 90L * 1024 * 1024
    };

    protected override IEnumerable<Option> GetCommandOptions() =>
    [
        PathOption,
        OutputOption,
        SourceOption,
        ApiKeyOption,
        DistroOption,
        SuiteOption,
        ArchOption,
        AllOption,
        SkipExistingOption,
        SkipDuplicateOption,
        AllowDowngradeOption,
        ChunkSizeOption
    ];

    protected override async Task Execute(ParseResult context)
    {
        var verbose = context.GetValue(CommonOptionsProvider.VerboseOption);
        var services = ServiceBuilder
            .CreateCommandHostBuilder<Startup>(verbose)
            .Build()
            .Services;
        var logger = services.GetRequiredService<ILogger<DeployHandler>>();
        var projectDir = Path.GetFullPath(context.GetValue(PathOption)!);
        var source = context.GetValue(SourceOption)!;
        var apiKey = context.GetValue(ApiKeyOption)!;
        var buildAll = context.GetValue(AllOption);
        var distro = context.GetValue(DistroOption)!;
        var suite = context.GetValue(SuiteOption)!;
        var architecture = context.GetValue(ArchOption)!;

        var serializer = services.GetRequiredService<AosprojSerializer>();
        var project = await serializer.DeserializeFromFileAsync(
            AosprojSerializer.FindProjectFile(projectDir));
        var linter = services.GetRequiredService<AosprojLinter>();
        var issues = linter.Lint(project, projectDir);
        foreach (var issue in issues)
        {
            if (issue.Level == AosprojLinter.Severity.Error)
                logger.LogError("[Lint/{Level}] {Message}", issue.Level, issue.Message);
            else
                logger.LogWarning("[Lint/{Level}] {Message}", issue.Level, issue.Message);
        }
        if (issues.Any(issue => issue.Level == AosprojLinter.Severity.Error))
            throw new InvalidOperationException("Lint found errors. Fix them before deploying.");

        if (context.GetValue(SkipExistingOption))
        {
            var resolver = services.GetRequiredService<PackageBuildPlanResolver>();
            var plan = await resolver.ResolveAsync(
                projectDir, project, buildAll, distro, suite, architecture);
            LogPlan(logger, plan);

            PackagePreflightResponse? preflight;
            try
            {
                preflight = await services.GetRequiredService<ApkgPushService>()
                    .PreflightAsync(plan, source, apiKey);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "Preflight could not reach the server. Falling back to the legacy full build and push.");
                preflight = null;
            }

            if (preflight?.AllPresent == true
                && IsCompleteMatch(plan, preflight))
            {
                logger.LogInformation(
                    "All {Count} target(s) already exist on {Source}. Build and upload skipped.",
                    plan.Targets.Count,
                    source);
                return;
            }

            if (preflight?.AllPresent == true)
            {
                logger.LogWarning(
                    "The preflight response did not exactly match the requested target matrix. " +
                    "Falling back to the full build.");
            }

            if (preflight == null)
            {
                logger.LogWarning(
                    "The destination does not support preflight or is temporarily unavailable. " +
                    "Falling back to the legacy full build and push.");
            }
            else
            {
                foreach (var target in preflight.Targets.Where(target =>
                             target.Status != PackagePreflightStatus.Present))
                {
                    logger.LogInformation(
                        "Target requires deployment: {Suite}/{Arch} {Version} ({Status})",
                        target.Suite,
                        target.Architecture,
                        target.Version,
                        target.Status);
                }

                if (preflight.Targets.Any(target =>
                        target.Status == PackagePreflightStatus.Forbidden))
                    throw new UnauthorizedAccessException(
                        "Preflight found a package or repository that this API key cannot upload to.");
            }
        }

        var apkgPath = await PublishHandler.PublishAsync(
            services,
            projectDir,
            context.GetValue(OutputOption)!,
            distro,
            suite,
            architecture,
            buildAll,
            noBuild: false);
        await PushHandler.PushAsync(
            services,
            apkgPath,
            source,
            apiKey,
            context.GetValue(SkipDuplicateOption)
            || context.GetValue(SkipExistingOption),
            context.GetValue(AllowDowngradeOption),
            context.GetValue(ChunkSizeOption));
    }

    private static void LogPlan(ILogger logger, PackageBuildPlan plan)
    {
        logger.LogInformation(
            "Resolved {Count} target(s) for {Package}:",
            plan.Targets.Count,
            plan.Name);
        foreach (var target in plan.Targets)
        {
            logger.LogInformation(
                "  {Suite}/{Arch}: {Version}",
                target.Suite,
                target.Architecture,
                target.Version);
        }
    }

    private static bool IsCompleteMatch(
        PackageBuildPlan plan,
        PackagePreflightResponse response)
    {
        if (response.Targets.Count != plan.Targets.Count)
            return false;

        return plan.Targets.All(expected =>
            response.Targets.Count(actual =>
                actual.Status == PackagePreflightStatus.Present
                && string.Equals(actual.Suite, expected.Suite, StringComparison.Ordinal)
                && string.Equals(
                    actual.Architecture,
                    expected.Architecture,
                    StringComparison.Ordinal)
                && string.Equals(actual.Version, expected.Version, StringComparison.Ordinal)) == 1);
    }
}
