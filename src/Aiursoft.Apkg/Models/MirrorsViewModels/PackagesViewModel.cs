using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class PackagesViewModel : UiStackLayoutViewModel
{
    public required AptMirror Mirror { get; set; }
    public required List<AptPackage> Packages { get; set; }
    public string? SearchName { get; set; }
    public int Page { get; set; } = 1;
    public int TotalCount { get; set; }
    public int PageSize { get; set; } = 100;
}
