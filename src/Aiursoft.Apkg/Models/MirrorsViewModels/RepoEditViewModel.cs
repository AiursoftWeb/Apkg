using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class RepoEditViewModel : UiStackLayoutViewModel
{
    public RepoEditViewModel()
    {
        PageTitle = "Edit Repository";
    }

    public int Id { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(100, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Distro")]
    public string Distro { get; set; } = "ubuntu";

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(100, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Repository Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(100, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Suite")]
    public string Suite { get; set; } = string.Empty;

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(255, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Components")]
    public string Components { get; set; } = "main";

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(100, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Architecture")]
    public string Architecture { get; set; } = "amd64";

    [Display(Name = "Mirror Source")]
    public int? MirrorId { get; set; }

    [Display(Name = "Automatic GPG Sign")]
    public bool EnableGpgSign { get; set; } = true;

    [Display(Name = "Signing Certificate")]
    public int? CertificateId { get; set; }

    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>? AvailableMirrors { get; set; }
    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>? AvailableCertificates { get; set; }

    [Display(Name = "Allow Anyone to Upload")]
    public bool AllowAnyoneToUpload { get; set; }
}
