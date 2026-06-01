using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.ApkgPackagesViewModels;

public class ApkgPackagesPreviewViewModel : UiStackLayoutViewModel
{
    public ApkgPackagesPreviewViewModel()
    {
        PageTitle = "Preview APKG Upload";
    }

    public required string VaultPath { get; set; }
    public required string FileName { get; set; }
    public required ApkgPackageManifest Manifest { get; init; }
    public required List<ApkgPreviewTargetInfo> Targets { get; init; }
    public bool HasWarnings => Targets.Any(t => !t.HasMatch);
    public bool AllTargetsUnmatched => Targets.All(t => !t.HasMatch);
}
