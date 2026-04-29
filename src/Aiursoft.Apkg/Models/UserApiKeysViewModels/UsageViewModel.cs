using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.UserApiKeysViewModels;

public class UsageViewModel : UiStackLayoutViewModel
{
    /// <summary>First 8 chars of the raw key followed by "..." — shown in headings and code samples.</summary>
    public required string KeyDisplay { get; set; }

    public required string KeyName { get; set; }

    /// <summary>Full raw key — only set immediately after creation (via TempData). Null on revisit.</summary>
    public string? RawKey { get; set; }

    public required string BaseUrl { get; set; }
}
