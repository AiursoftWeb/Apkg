using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public required List<AptMirror> Mirrors { get; set; }
    public required Dictionary<int, int> PackageCounts { get; set; }
}
