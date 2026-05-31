namespace Aiursoft.Apkg.Sdk.Models;

/// <summary>
/// Result of <see cref="Services.DebBuilder.BuildAsync"/>,
/// carrying all the metadata the caller needs so it doesn't have to
/// reverse-engineer suite/arch/version from the .deb filename.
/// </summary>
public sealed class DebBuildResult
{
    public required string DebPath { get; init; }
    public required string Version { get; init; }
    public required string Suite { get; init; }
    public required string Arch { get; init; }
}
