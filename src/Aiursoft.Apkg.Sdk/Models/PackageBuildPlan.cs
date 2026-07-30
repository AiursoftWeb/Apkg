namespace Aiursoft.Apkg.Sdk.Models;

/// <summary>
/// The exact package coordinates that a publish operation would produce.
/// Resolving a plan may read upstream APT metadata, but never downloads a .deb
/// or runs a package build.
/// </summary>
public sealed class PackageBuildPlan
{
    public required string Name { get; init; }
    public required string Distro { get; init; }
    public required string Component { get; init; }
    public required IReadOnlyList<PackageBuildTarget> Targets { get; init; }
}

public sealed class PackageBuildTarget
{
    public required string Suite { get; init; }
    public required string Architecture { get; init; }
    public required string Version { get; init; }
}
