using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.ApkgPackagesViewModels;

public class ApkgRevisionHistoryItem
{
    public required ApkgRevision Revision { get; init; }
    public int PackageId { get; init; }
    public required string PackageName { get; init; }
    public required string Distro { get; init; }
    public required string Component { get; init; }
    public int PackageCount { get; init; }
}

public class ApkgRevisionHistoryViewModel : UiStackLayoutViewModel
{
    public ApkgRevisionHistoryViewModel()
    {
        PageTitle = "My Upload History";
    }

    public required List<ApkgRevisionHistoryItem> Revisions { get; init; }
}
