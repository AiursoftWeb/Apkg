using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class RepoDetailsViewModel : UiStackLayoutViewModel
{
    public RepoDetailsViewModel()
    {
        PageTitle = "Repository Details";
    }

    public required AptRepository Repo { get; set; }
}
