using Aiursoft.Apkg.Entities;

namespace Aiursoft.Apkg.Models.ApkgPackagesViewModels;

public class ApkgPackageIndexItem
{
    public required ApkgPackage Package { get; init; }
    public ApkgRevision? DisplayRevision { get; init; }
    public int PublishedCount { get; init; }
    public int TotalPackageCount { get; init; }
    public List<string> LiveVersions { get; init; } = [];
    public UploadSyncStatus SyncStatus { get; init; }
    public int? NextVersionRevisionId { get; init; }
    public string? NextVersionSummary { get; init; }
    public bool IsUnpublished { get; init; }
}
