using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Models.LocalPackagesViewModels;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.ApkgPackagesViewModels;

public class ApkgPackagesDetailsViewModel : UiStackLayoutViewModel
{
    public ApkgPackagesDetailsViewModel()
    {
        PageTitle = "APKG Upload Details";
    }

    public required ApkgRevision Revision { get; init; }
    public required List<PackageStatusInfo> Packages { get; init; }
    public List<PackageStatusInfo> AllPackageStatuses { get; init; } = [];
    public required List<ApkgRevision> VersionHistory { get; init; }
    public int? LatestVersionId { get; init; }
    public required string ActiveTab { get; init; } = "overview";
    public string VersionsFilter { get; init; } = "latest";
    public bool IsAdmin { get; init; }
    public bool IsOwner { get; init; }
}
