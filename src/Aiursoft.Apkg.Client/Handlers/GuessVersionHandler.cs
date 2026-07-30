using System.CommandLine;
using System.Text.Json;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.CommandFramework.Models;
using Aiursoft.CommandFramework.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.Apkg.Client.Handlers;

public sealed class GuessVersionHandler : ExecutableCommandHandlerBuilder
{
    protected override string Name => "guess-version";
    protected override string Description =>
        "Resolve the exact package versions for every target without building packages.";

    private static readonly Option<string> PathOption =
        new(name: "--path", aliases: ["-p"])
        {
            Description = "Directory containing the .aosproj file.",
            DefaultValueFactory = _ => "."
        };

    private static readonly Option<string> DistroOption = new("--distro")
    {
        Description = "Override the target distribution.",
        DefaultValueFactory = _ => string.Empty
    };

    private static readonly Option<string> SuiteOption = new("--suite")
    {
        Description = "Resolve one target suite.",
        DefaultValueFactory = _ => string.Empty
    };

    private static readonly Option<string> ArchOption = new("--arch")
    {
        Description = "Resolve one target architecture.",
        DefaultValueFactory = _ => string.Empty
    };

    private static readonly Option<bool> AllOption = new("--all")
    {
        Description = "Resolve the complete TargetSuites × TargetArchitectures matrix.",
        DefaultValueFactory = _ => false
    };

    private static readonly Option<bool> JsonOption = new("--json")
    {
        Description = "Write the resolved plan as JSON.",
        DefaultValueFactory = _ => false
    };

    protected override IEnumerable<Option> GetCommandOptions() =>
    [
        PathOption,
        DistroOption,
        SuiteOption,
        ArchOption,
        AllOption,
        JsonOption
    ];

    protected override async Task Execute(ParseResult context)
    {
        var verbose = context.GetValue(CommonOptionsProvider.VerboseOption);
        var services = ServiceBuilder
            .CreateCommandHostBuilder<Startup>(verbose)
            .Build()
            .Services;
        var projectDir = Path.GetFullPath(context.GetValue(PathOption)!);
        var serializer = services.GetRequiredService<AosprojSerializer>();
        var project = await serializer.DeserializeFromFileAsync(
            AosprojSerializer.FindProjectFile(projectDir));
        var resolver = services.GetRequiredService<PackageBuildPlanResolver>();
        var plan = await resolver.ResolveAsync(
            projectDir,
            project,
            context.GetValue(AllOption),
            context.GetValue(DistroOption)!,
            context.GetValue(SuiteOption)!,
            context.GetValue(ArchOption)!);

        if (context.GetValue(JsonOption))
        {
            Console.WriteLine(JsonSerializer.Serialize(plan, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
            return;
        }

        foreach (var target in plan.Targets)
        {
            Console.WriteLine(
                $"{plan.Name}\t{target.Version}\t{plan.Distro}\t" +
                $"{target.Suite}\t{target.Architecture}\t{plan.Component}");
        }
    }
}
