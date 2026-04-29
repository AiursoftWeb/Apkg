using Aiursoft.Apkg.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Apkg.Models.UserApiKeysViewModels;

public class UserApiKeysIndexViewModel : UiStackLayoutViewModel
{
    public required List<UserApiKey> Keys { get; set; }
}
