using Avalonia.Controls.Primitives;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>
/// The headless stand-in for the popover seam (R-VIEW-9, R-TEST-7): records every present and dismiss instead of
/// opening an Avalonia Popup, which a theme-less headless window cannot host. Install it with
/// <c>Presentation.Presenter.Override(recorder)</c>.
/// </summary>
public sealed class PopoverRecorder : IPopoverPresenter
{
    /// <summary>Every call, in order: the popup and whether it was being presented (true) or dismissed (false).</summary>
    public List<(Popup Popup, bool Presented)> Calls { get; } = [];

    public void SetPresented(Popup popup, bool presented)
    {
        // What the real presenter does to a popup before it opens it (PopoverHost.Prepare: the panel class, the anchor's
        // appearance, and OD-0095's tipped card round the content), so every site's tests run its content on the card.
        if (presented) PopoverHost.Prepare(popup);
        Calls.Add((popup, presented));
    }
}
