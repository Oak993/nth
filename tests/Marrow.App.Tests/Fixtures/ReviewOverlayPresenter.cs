using Avalonia.Controls.Primitives;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>The real popover presenter, with each popup kept in the window's overlay (Avalonia 12.1.2 headless cannot
/// open a PopupRoot), recording the popups it showed (REVIEW, unit/effort-title-fidelity).</summary>
public sealed class ReviewOverlayPresenter(IPopoverPresenter real) : IPopoverPresenter
{
    public List<Popup> Popups { get; } = [];

    public void SetPresented(Popup popup, bool presented)
    {
        popup.ShouldUseOverlayLayer = true;
        if (!Popups.Contains(popup)) Popups.Add(popup);
        real.SetPresented(popup, presented);
    }
}
