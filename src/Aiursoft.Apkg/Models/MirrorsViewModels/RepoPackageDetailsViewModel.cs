using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class RepoPackageDetailsViewModel : UiStackLayoutViewModel
{
    public required AptPackage Package { get; set; }
    public AptRepository? Repo { get; set; }

    /// <summary>
    /// Maps each known package name (from Depends/Recommends/etc.) to its AptPackage.Id
    /// within the same bucket, so the view can render clickable links.
    /// </summary>
    public Dictionary<string, int> DepLookup { get; set; } = [];
}
