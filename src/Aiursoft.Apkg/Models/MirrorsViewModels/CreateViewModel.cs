using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class CreateViewModel : UiStackLayoutViewModel
{
    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(255, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Base URL")]
    public string BaseUrl { get; set; } = string.Empty;

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(100, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Suite")]
    public string Suite { get; set; } = string.Empty;

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(100, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Components")]
    public string Component { get; set; } = string.Empty;

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(20, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Architecture")]
    public string Architecture { get; set; } = "amd64";

    [Display(Name = "Signed By")]
    public string? SignedBy { get; set; }
}
