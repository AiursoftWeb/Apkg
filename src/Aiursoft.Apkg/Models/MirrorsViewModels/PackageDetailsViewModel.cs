using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class PackageDetailsViewModel : UiStackLayoutViewModel
{
    public PackageDetailsViewModel()
    {
        PageTitle = "Package Details";
    }

    public required AptPackage Package { get; set; }

    /// <summary>Maps each known package name to its AptPackage.Id within the same bucket.</summary>
    public Dictionary<string, int> DepLookup { get; set; } = [];
}
