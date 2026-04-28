using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class MirrorEditViewModel : UiStackLayoutViewModel, IValidatableObject
{
    public int Id { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(100, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Distro")]
    public string Distro { get; set; } = "ubuntu";

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(255, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Base URL")]
    public string BaseUrl { get; set; } = string.Empty;

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(100, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Suite")]
    public string Suite { get; set; } = string.Empty;

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(255, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Components")]
    public string Components { get; set; } = string.Empty;

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(100, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Architecture")]
    public string Architecture { get; set; } = "amd64";

    [Display(Name = "GPG Public Key (Optional)")]
    public string? SignedBy { get; set; } = "/usr/share/keyrings/ubuntu-archive-keyring.gpg";

    [Display(Name = "Allow Insecure Source")]
    public bool AllowInsecure { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!AllowInsecure && string.IsNullOrWhiteSpace(SignedBy))
        {
            yield return new ValidationResult(
                "A GPG keyring path is required when Allow Insecure Source is not checked. " +
                "Either provide a keyring path or enable Allow Insecure Source.",
                [nameof(SignedBy)]);
        }
    }
}
