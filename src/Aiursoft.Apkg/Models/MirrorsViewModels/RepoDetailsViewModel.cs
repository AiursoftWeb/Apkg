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
    public int PackageCount { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
}
