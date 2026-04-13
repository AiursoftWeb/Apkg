using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class PackageDetailsViewModel : UiStackLayoutViewModel
{
    public required AptPackage Package { get; set; }
}
