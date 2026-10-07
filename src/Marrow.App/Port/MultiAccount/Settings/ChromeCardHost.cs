// PORT-ONLY: T11 step 2. The app's card (ChromeCard: rows stacked on a continuous rounded fill) around a control that is built once.
using Avalonia.Controls;
using Marrow.UI.Chrome;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class ChromeCardHost : Decorator
{
    internal ChromeCardHost(Control content) => Child = new ChromeCard(() => content);
}
