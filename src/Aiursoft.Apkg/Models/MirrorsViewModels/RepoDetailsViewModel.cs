using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class RepoDetailsViewModel : UiStackLayoutViewModel
{
    public required AptRepository Repo { get; set; }
}
