using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.ApkgUploadsViewModels;

public enum UploadSyncStatus
{
    /// <summary>Upload was created (file in vault) but Publish was never clicked.</summary>
    Draft,
    /// <summary>Upload is unlisted and hidden from APT clients.</summary>
    Unlisted,
    /// <summary>Published; packages are waiting for the RepositorySyncJob.</summary>
    Syncing,
    /// <summary>Published; packages are in a SecondaryBucket awaiting signing.</summary>
    Signing,
    /// <summary>At least one package is live in a PrimaryBucket.</summary>
    Live,
    /// <summary>All packages were superseded by a newer version.</summary>
    Superseded
}

public class ApkgUploadsIndexViewModel : UiStackLayoutViewModel
{
    public ApkgUploadsIndexViewModel()
    {
        PageTitle = "My Packages (APKG)";
    }

    public required List<ApkgUploadIndexItem> Uploads { get; init; }
    public bool IsAdmin { get; init; }
    public Dictionary<int, UploadSyncStatus> UploadStatuses { get; init; } = new();
}
