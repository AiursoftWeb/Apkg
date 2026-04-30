using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.MirrorsViewModels;

public class CertIndexViewModel : UiStackLayoutViewModel
{
    public CertIndexViewModel()
    {
        PageTitle = "Certificates";
    }

    public required List<AptCertificate> Certificates { get; set; }
}
