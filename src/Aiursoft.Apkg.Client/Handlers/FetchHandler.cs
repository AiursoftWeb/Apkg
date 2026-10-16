using System.CommandLine;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.CommandFramework.Models;
using Aiursoft.CommandFramework.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aiursoft.Apkg.Client.Handlers;

public class FetchHandler : ExecutableCommandHandlerBuilder
{
    protected override string Name => "fetch";
    protected override string Description => "Download verified APT build inputs without installing packages. All sources and target settings are explicit.";

    private static readonly Option<string[]> PackageOption = new("--package")
        { Description = "Package to fetch. Repeat for multiple packages.", AllowMultipleArgumentsPerToken = false };
    private static readonly Option<string> RepositoryOption = new("--repository")
        { Description = "APT repository base URL. Use --sources for multiple repositories." };
    private static readonly Option<string[]> SuiteOption = new("--suite")
        { Description = "Exact suite to query; repeat for pockets. Project mode accepts one build target suite.", AllowMultipleArgumentsPerToken = false };
    private static readonly Option<string[]> ComponentOption = new("--component")
        { Description = "APT component. Repeat for multiple components; no default.", AllowMultipleArgumentsPerToken = false };
    private static readonly Option<string> ArchOption = new("--arch")
        { Description = "Explicit Debian architecture (e.g. amd64 or arm64).", Required = true };
    private static readonly Option<string> KeyOption = new("--signed-by")
        { Description = "Trusted local archive keyring file." };
    private static readonly Option<string> SourcesOption = new("--sources")
        { Description = "Explicit deb822 .sources file; Signed-By paths are relative to that file." };
    private static readonly Option<string> PathOption = new("--path", ["-p"])
        { Description = "Read BuildInput declarations from this project file or directory." };
    private static readonly Option<string> DistroOption = new("--distro")
        { Description = "Explicit build target distro, required in project mode." };
    private static readonly Option<string> OutputOption = new("--output", ["-o"])
        { Description = "Dedicated output directory. Must be empty or owned by apkg fetch.", Required = true };
    private static readonly Option<bool> ExtractOption = new("--extract")
        { Description = "Also extract each package payload to extracted/<input-id>; never run maintainer scripts." };

    protected override IEnumerable<Option> GetCommandOptions() =>
        [PackageOption, RepositoryOption, SuiteOption, ComponentOption, ArchOption, KeyOption,
            SourcesOption, PathOption, DistroOption, OutputOption, ExtractOption];

    protected override async Task Execute(ParseResult context)
    {
        var arch = context.GetValue(ArchOption)!;
        var suites = context.GetValue(SuiteOption) ?? [];
        var components = context.GetValue(ComponentOption) ?? [];
        var packages = context.GetValue(PackageOption) ?? [];
        var path = context.GetValue(PathOption);
        var repository = context.GetValue(RepositoryOption);
        var signedBy = context.GetValue(KeyOption);
        var sourcesPath = context.GetValue(SourcesOption);
        var distro = context.GetValue(DistroOption);
        using var host = ServiceBuilder.CreateCommandHostBuilder<Startup>(
            context.GetValue(CommonOptionsProvider.VerboseOption)).Build();
        IReadOnlyList<FetchInput> inputs;
        if (path != null)
        {
            if (packages.Length > 0 || repository != null || signedBy != null || sourcesPath != null || components.Length > 0)
                throw new ArgumentException("--path cannot be combined with direct package/source options.");
            if (suites.Length != 1) throw new ArgumentException("Project fetch requires exactly one --suite.");
            BuildInputResolver.Require(distro, "--distro in project mode");
            path = Path.GetFullPath(path);
            var file = Directory.Exists(path) ? AosprojSerializer.FindProjectFile(path) : path;
            var project = await host.Services.GetRequiredService<AosprojSerializer>().DeserializeFromFileAsync(file);
            inputs = BuildInputResolver.Resolve(project, Path.GetDirectoryName(file)!, distro!, suites[0], arch);
        }
        else
        {
            if (distro != null) throw new ArgumentException("--distro applies only with --path; choose sources explicitly for direct fetch.");
            if (packages.Length == 0) throw new ArgumentException("Specify at least one --package (or use --path for a project).");
            IReadOnlyList<FetchSource> sources;
            if (sourcesPath != null)
            {
                if (repository != null || signedBy != null || suites.Length > 0 || components.Length > 0)
                    throw new ArgumentException("--sources cannot be combined with --repository, --suite, --component or --signed-by.");
                sources = BuildInputResolver.ReadSourcesFile(sourcesPath, arch);
            }
            else
            {
                BuildInputResolver.Require(repository, "--repository or --sources");
                BuildInputResolver.Require(signedBy, "--signed-by");
                if (suites.Length == 0) throw new ArgumentException("At least one explicit --suite is required.");
                if (components.Length == 0) throw new ArgumentException("At least one explicit --component is required.");
                sources = (from suite in suites from component in components
                    select new FetchSource(repository!, suite, component, Path.GetFullPath(signedBy!))).ToArray();
            }
            inputs = packages.Select(p => new FetchInput(p, p, arch, sources)).ToArray();
        }
        if (inputs.Count == 0) throw new InvalidOperationException("No active BuildInput entries for this target.");
        var result = await host.Services.GetRequiredService<PackageFetchService>().FetchAsync(
            inputs, context.GetValue(OutputOption)!, context.GetValue(ExtractOption));
        host.Services.GetRequiredService<ILogger<FetchHandler>>().LogInformation(
            "Fetched {Count} inputs. Manifest: {Manifest}", result.Inputs.Count,
            Path.GetFullPath(Path.Combine(context.GetValue(OutputOption)!, "manifest.json")));
    }
}
