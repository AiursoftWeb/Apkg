using Aiursoft.UiStack.Layout;
using System.ComponentModel.DataAnnotations;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class CertCreateViewModel : UiStackLayoutViewModel
{
    [Required]
    [Display(Name = "Friendly Name")]
    public string FriendlyName { get; set; } = string.Empty;
}
