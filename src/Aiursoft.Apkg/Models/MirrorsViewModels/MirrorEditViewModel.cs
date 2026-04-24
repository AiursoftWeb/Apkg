using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class MirrorEditViewModel : UiStackLayoutViewModel
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Distro { get; set; } = "ubuntu";

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

    [Required]
    [MaxLength(100)]
    public string Architecture { get; set; } = "amd64";

    [Display(Name = "GPG Public Key URL (Optional)")]
    public string? SignedBy { get; set; }

    [Display(Name = "Allow Insecure Source")]
    public bool AllowInsecure { get; set; }
}
