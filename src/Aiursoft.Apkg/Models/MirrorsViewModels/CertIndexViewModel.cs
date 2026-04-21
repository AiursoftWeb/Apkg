using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class CertIndexViewModel : UiStackLayoutViewModel
{
    public required List<AptCertificate> Certificates { get; set; }
}
