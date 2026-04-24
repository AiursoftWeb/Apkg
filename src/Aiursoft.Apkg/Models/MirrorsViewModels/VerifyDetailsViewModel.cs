using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class VerifyDetailsViewModel : UiStackLayoutViewModel
{
    public required AptMirror Mirror { get; set; }
}
