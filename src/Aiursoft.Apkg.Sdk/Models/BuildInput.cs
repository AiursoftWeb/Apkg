namespace Aiursoft.Apkg.Sdk.Models;

/// <summary>A named, explicitly trusted set of APT pockets used only for build inputs.</summary>
public class BuildSourceItem
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Suites { get; set; } = string.Empty;
    public string Components { get; set; } = string.Empty;
    public string SignedBy { get; set; } = string.Empty;
    public string? Condition { get; set; }
}

/// <summary>One package payload. No dependencies or maintainer scripts are inherited.</summary>
public class BuildInputItem
{
    public string Package { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Sources { get; set; } = string.Empty;
    public string Arch { get; set; } = string.Empty;
    public string? Condition { get; set; }
}

public sealed record FetchSource(string Url, string Suite, string Component, string SignedBy);
public sealed record FetchInput(string Id, string Package, string Arch, IReadOnlyList<FetchSource> Sources);

/// <summary>Public manifest contract consumed by build scripts through JSON serialization.</summary>
// System.Text.Json reads these properties through reflection; their consumers are outside this solution.
// ReSharper disable NotAccessedPositionalProperty.Global
public sealed record FetchedInput(
    string Id, string Package, string Version, string Architecture, string RequestedArchitecture,
    string Repository, string Suite, string Component, string Sha256, string DebPath, string? ExtractedPath);
// ReSharper restore NotAccessedPositionalProperty.Global

public sealed record FetchManifest(string Format, IReadOnlyList<FetchedInput> Inputs);
