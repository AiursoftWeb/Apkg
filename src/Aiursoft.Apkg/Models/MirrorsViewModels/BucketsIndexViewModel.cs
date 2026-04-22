using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class BucketsIndexViewModel : UiStackLayoutViewModel
{
    public required List<AptBucket> Buckets { get; set; }
    public required Dictionary<int, int> PackageCounts { get; set; }
    public required Dictionary<int, long> StorageUsage { get; set; }
    public required Dictionary<int, string> InUseBy { get; set; }
}
