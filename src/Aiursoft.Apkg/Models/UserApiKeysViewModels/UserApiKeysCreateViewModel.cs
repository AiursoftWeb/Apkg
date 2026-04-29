using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.UserApiKeysViewModels;

public class UserApiKeysCreateViewModel : UiStackLayoutViewModel
{
    [Required]
    [MaxLength(100)]
    [Display(Name = "Key Name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// How many days until the key expires. 0 = never expires.
    /// </summary>
    [Display(Name = "Expiration")]
    public int ExpirationDays { get; set; } = 365;
}
