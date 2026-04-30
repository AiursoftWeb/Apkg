using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class BucketsIndexViewModel : UiStackLayoutViewModel
{
    public BucketsIndexViewModel()
    {
        PageTitle = "Buckets";
    }

    public required List<AptBucket> Buckets { get; set; }
    public required Dictionary<int, int> PackageCounts { get; set; }
    public required Dictionary<int, long> StorageUsage { get; set; }
    /// <summary>bucketId → "Mirror: suite" or "Repo: name" for LIVE (PrimaryBucketId) buckets.</summary>
    public required Dictionary<int, string> InUseBy { get; set; }
    /// <summary>bucketId → "Repo: name" for PENDING (SecondaryBucketId) buckets.</summary>
    public required Dictionary<int, string> PendingUsage { get; set; }
}
