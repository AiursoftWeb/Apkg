using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class PackagesViewModel : UiStackLayoutViewModel
{
    public required MirrorRepository Mirror { get; set; }
    public required List<AptPackage> Packages { get; set; }
}
