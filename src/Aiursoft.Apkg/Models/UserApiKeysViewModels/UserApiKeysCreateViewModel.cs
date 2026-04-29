using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.UserApiKeysViewModels;

public class UserApiKeysCreateViewModel : UiStackLayoutViewModel
{
    [Required]
    [MaxLength(100)]
    [Display(Name = "Key Name")]
    public string Name { get; set; } = "";
}
