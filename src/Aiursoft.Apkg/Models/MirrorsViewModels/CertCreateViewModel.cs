using Aiursoft.UiStack.Layout;
using System.ComponentModel.DataAnnotations;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class CertCreateViewModel : UiStackLayoutViewModel
{
    public CertCreateViewModel()
    {
        PageTitle = "Create Certificate";
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(100, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [RegularExpression(@"^[a-z0-9]+$", ErrorMessage = "Only lowercase letters and numbers are allowed.")]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Friendly Name")]
    public string FriendlyName { get; set; } = string.Empty;
}
