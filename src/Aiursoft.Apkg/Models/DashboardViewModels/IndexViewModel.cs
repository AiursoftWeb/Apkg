using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.DashboardViewModels;

public class PackageSearchResult
{
    public required AptPackage Package { get; init; }
    public required string RepoName { get; init; }
    public required int RepoId { get; init; }
}

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Package Search";
    }

    public string? Query { get; set; }
    public List<PackageSearchResult> Results { get; set; } = [];
    public int TotalResults { get; set; }
    public int TotalPackages { get; set; }
    public int TotalRepos { get; set; }
    public int Page { get; set; } = 1;
    public const int PageSize = 20;
}
