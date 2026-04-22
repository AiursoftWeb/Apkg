using Aiursoft.UiStack.Layout;
using System.ComponentModel.DataAnnotations;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class CertCreateViewModel : UiStackLayoutViewModel
{
    [Required]
    [MaxLength(100)]
    [RegularExpression(@"^[a-z0-9]+$", ErrorMessage = "Only lowercase letters and numbers are allowed.")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Friendly Name")]
    public string FriendlyName { get; set; } = string.Empty;
}
