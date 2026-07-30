using Aiursoft.Apkg.Sdk.Models;

namespace Aiursoft.Apkg.Sdk.Services;

/// <summary>
/// Resolves the complete target matrix and exact versions for a project without
/// building or downloading package payloads.
/// </summary>
public sealed class PackageBuildPlanResolver(DebBuilder debBuilder)
{
    public async Task<PackageBuildPlan> ResolveAsync(
        string projectDir,
        AosprojProject project,
        bool buildAll = false,
        string distroArg = "",
        string suiteArg = "",
        string archArg = "")
    {
        var targets = ResolveBuildTargets(project, buildAll, distroArg, suiteArg, archArg);
        var resolvedTargets = new List<PackageBuildTarget>(targets.Count);

        foreach (var (distro, suite, arch) in targets)
        {
            var version = await debBuilder.ResolvePackageVersionAsync(
                projectDir, project, distro, suite, arch);
            resolvedTargets.Add(new PackageBuildTarget
            {
                Suite = suite,
                Architecture = arch,
                Version = version
            });
        }

        return new PackageBuildPlan
        {
            Name = project.PackageName,
            Distro = targets.Select(target => target.distro).Distinct(StringComparer.OrdinalIgnoreCase).Single(),
            Component = project.Component,
            Targets = resolvedTargets
        };
    }

    public static List<(string distro, string suite, string arch)> ResolveBuildTargets(
        AosprojProject project,
        bool buildAll,
        string distroArg,
        string suiteArg,
        string archArg)
    {
        if (buildAll || (string.IsNullOrWhiteSpace(suiteArg) && string.IsNullOrWhiteSpace(archArg)))
        {
            if (string.IsNullOrWhiteSpace(project.TargetDistro))
                throw new InvalidOperationException("Project has no <TargetDistro> declared.");
            if (project.SuiteList.Length == 0)
                throw new InvalidOperationException("Project has no <TargetSuites> declared.");
            if (project.ArchList.Length == 0)
                throw new InvalidOperationException("Project has no <TargetArchitectures> declared.");

            return (
                from suite in project.SuiteList
                from arch in project.ArchList
                select (project.TargetDistro, suite, arch)
            ).ToList();
        }

        if (string.IsNullOrWhiteSpace(suiteArg))
            throw new InvalidOperationException("Specify --suite (e.g. --suite jammy).");
        if (string.IsNullOrWhiteSpace(archArg))
            throw new InvalidOperationException("Specify --arch (e.g. --arch amd64).");

        var distro = string.IsNullOrWhiteSpace(distroArg)
            ? (string.IsNullOrWhiteSpace(project.TargetDistro) ? "ubuntu" : project.TargetDistro)
            : distroArg;

        return [(distro, suiteArg, archArg)];
    }
}
