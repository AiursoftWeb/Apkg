using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class BucketsIndexViewModel : UiStackLayoutViewModel
{
    public required List<AptBucket> Buckets { get; set; }
}
