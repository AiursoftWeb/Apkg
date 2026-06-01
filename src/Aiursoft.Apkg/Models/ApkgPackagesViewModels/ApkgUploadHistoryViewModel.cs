using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.ApkgPackagesViewModels;

public class ApkgUploadHistoryItem
{
    public required ApkgRevision Revision { get; init; }
    public required string PackageName { get; init; }
    public required string Distro { get; init; }
    public required string Component { get; init; }
    public int PackageCount { get; init; }
}

public class ApkgUploadHistoryViewModel : UiStackLayoutViewModel
{
    public ApkgUploadHistoryViewModel()
    {
        PageTitle = "My Upload History";
    }

    public required List<ApkgUploadHistoryItem> Uploads { get; init; }
}
