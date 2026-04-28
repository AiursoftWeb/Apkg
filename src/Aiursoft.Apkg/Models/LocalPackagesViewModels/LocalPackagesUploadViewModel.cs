using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Aiursoft.Apkg.Models.LocalPackagesViewModels;

public class LocalPackagesUploadViewModel : UiStackLayoutViewModel
{
    [Required]
    [Display(Name = "Repository")]
    public int RepositoryId { get; set; }

    [Required]
    [Display(Name = "Component")]
    public string Component { get; set; } = "main";

    [Display(Name = ".deb File")]
    [Required(ErrorMessage = "Please upload a valid .deb file.")]
    [MaxLength(200)]
    [RegularExpression(@"^deb/.*", ErrorMessage = "Please upload a valid .deb file.")]
    public string? DebFilePath { get; set; }

    public List<SelectListItem> AvailableRepositories { get; set; } = [];
}
