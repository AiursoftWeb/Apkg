using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Sdk.Models;

namespace Aiursoft.Apkg.Models.ApkgPackagesViewModels;

public class ApkgPreviewTargetInfo
{
    public required ApkgPackageEntry Entry { get; init; }
    public required List<AptRepository> MatchingRepositories { get; init; }
    public bool HasMatch => MatchingRepositories.Count > 0;
}
