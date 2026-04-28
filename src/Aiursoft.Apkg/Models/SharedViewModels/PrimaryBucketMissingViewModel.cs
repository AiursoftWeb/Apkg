using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.SharedViewModels;

public class PrimaryBucketMissingViewModel : UiStackLayoutViewModel
{
    public PrimaryBucketMissingViewModel()
    {
        PageTitle = "Primary Bucket Missing";
    }

    public required string TargetName { get; set; }
    public required string[] RequiredJobs { get; set; }
}
