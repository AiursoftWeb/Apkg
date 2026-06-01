using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.ApkgPackagesViewModels;

public class ApkgPackagesUploadViewModel : UiStackLayoutViewModel
{
    public ApkgPackagesUploadViewModel()
    {
        PageTitle = "Upload apkg mixed package";
    }

    [Display(Name = ".apkg File")]
    [Required(ErrorMessage = "Please upload a valid .apkg file.")]
    [MaxLength(512)]
    [RegularExpression(@"^apkg-upload/.*", ErrorMessage = "Please upload a valid .apkg file.")]
    public string? ApkgFilePath { get; set; }
}
