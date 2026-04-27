using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.LocalPackagesViewModels;

public class LocalPackagesIndexViewModel : UiStackLayoutViewModel
{
    public List<LocalPackage> Packages { get; set; } = [];
    public bool IsAdmin { get; set; }
}
