using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.ApkgUploadsViewModels;

public class ApkgUploadsPackageHistoryViewModel : UiStackLayoutViewModel
{
    public required string PackageName { get; init; }
    public required List<ApkgUpload> Uploads { get; init; }
    public HashSet<string> LiveVersions { get; init; } = new();
    public bool IsAdmin { get; init; }
}
