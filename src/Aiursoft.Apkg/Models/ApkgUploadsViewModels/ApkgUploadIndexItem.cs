using Aiursoft.Apkg.Entities;

namespace Aiursoft.Apkg.Models.ApkgUploadsViewModels;

public class ApkgUploadIndexItem
{
    public required ApkgUpload Upload { get; init; }
    public int PublishedCount { get; init; }
    public int TotalPackageCount { get; init; }
    public List<string> LiveVersions { get; init; } = [];
    public UploadSyncStatus SyncStatus { get; init; }
}
