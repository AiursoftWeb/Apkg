using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models.LocalPackagesViewModels;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.ApkgPackagesViewModels;

public class ApkgPackagesPackageHistoryViewModel : UiStackLayoutViewModel
{
    public required string PackageName { get; init; }
    public required List<ApkgRevision> Revisions { get; init; }
    public List<PackageStatusInfo> AllPackageStatuses { get; init; } = [];
    public bool IsAdmin { get; init; }
    public int? LatestVersionId { get; init; }
    public string VersionsFilter { get; init; } = "latest";
}
