using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class BucketPackagesViewModel : UiStackLayoutViewModel
{
    public BucketPackagesViewModel()
    {
        PageTitle = "Bucket Packages";
    }

    public required AptBucket Bucket { get; set; }
    public required List<AptPackage> Packages { get; set; }
    public string? SortOrder { get; set; }
    public int Page { get; set; }
    public int TotalCount { get; set; }
    public int PageSize { get; set; }
}
