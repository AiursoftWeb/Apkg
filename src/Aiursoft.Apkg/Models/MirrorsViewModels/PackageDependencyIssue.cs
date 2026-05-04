namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class PackageDependencyIssue
{
    public required string Package { get; set; }
    public required string Version { get; set; }
    public required string Architecture { get; set; }
    public bool IsVirtual { get; set; }
    // ReSharper disable once CollectionNeverUpdated.Global - Populated during JSON deserialization
    public required List<MissingDependency> MissingDeps { get; set; }
}

public class MissingDependency
{
    public required string Required { get; set; }
    public required string Available { get; set; }
}
