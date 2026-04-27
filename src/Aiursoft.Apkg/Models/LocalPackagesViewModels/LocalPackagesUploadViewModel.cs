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
    public IFormFile? DebFile { get; set; }

    public List<SelectListItem> AvailableRepositories { get; set; } = [];
}
