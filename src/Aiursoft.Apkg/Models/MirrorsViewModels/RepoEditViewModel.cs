using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class RepoEditViewModel : UiStackLayoutViewModel
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Suite { get; set; } = string.Empty;

    [Display(Name = "Mirror Source")]
    public int? MirrorId { get; set; }

    [Required]
    [Display(Name = "Signing Certificate")]
    public int? CertificateId { get; set; }
    
    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>? AvailableMirrors { get; set; }
    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>? AvailableCertificates { get; set; }
}
