using Avalonia.Controls.Primitives;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Window.Native;

// Passes every request to the presenter the app uses and remembers the popups it was asked to present, for a view that keeps its
// popups out of the visual tree. It changes nothing about how a popup opens.
internal sealed class NativePopupRecorder(IPopoverPresenter inner) : IPopoverPresenter
{
    private readonly List<Popup> popups = [];

    internal IReadOnlyList<Popup> Popups => popups;

    public void SetPresented(Popup popup, bool presented)
    {
        if (!popups.Contains(popup)) popups.Add(popup);
        inner.SetPresented(popup, presented);
    }
}
