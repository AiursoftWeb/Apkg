using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class CreateViewModel : UiStackLayoutViewModel
{
    [Required]
    [MaxLength(255)]
    [Display(Name = "Base URL")]
    public string BaseUrl { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Suite { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Components { get; set; } = string.Empty;

    [Display(Name = "Signed By")]
    public string? SignedBy { get; set; }
}
