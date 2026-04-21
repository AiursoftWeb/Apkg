using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class RepoIndexViewModel : UiStackLayoutViewModel
{
    public required List<AptRepository> Repositories { get; set; }
    public required Dictionary<int, int> PackageCounts { get; set; }
}
