using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class RepoCheckReportViewModel : UiStackLayoutViewModel
{
    public required DependencyCheckReport Report { get; set; }
}
