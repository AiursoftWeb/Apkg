using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class EditViewModel : UiStackLayoutViewModel
{
    [Required]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    [Display(Name = "Base URL")]
    public string BaseUrl { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Suite { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Component { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Architecture { get; set; } = "amd64";

    [Display(Name = "Signed By")]
    public string? SignedBy { get; set; }
}
