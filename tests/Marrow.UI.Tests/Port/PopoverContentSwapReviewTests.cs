using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// REVIEW (final review of unit/effort-title-fidelity, round 2, 2026-10-03): ED-238's final-round-1 amendment (3) says content
// set while a presented popover is open is given to its host, and that "it covers every popup PopoverHost.Prepare readies
// (all presenter sites, sheets included)". PopoverHost.Follow learns the host only from the child the host already shows
// (the changed child's old value, or the child at Opened). A popup presented with no child, whose site builds its content
// in its own Opened handler, has neither: Avalonia's Popup.Open gives the host a null child, the site's Opened sets
// popup.Child while the popup is open, the old value is null, so the host stays unknown and the content (wrapped on its card)
// is never shown; Follow's own Opened then finds no host either (the card is in no tree). The port's sites that build their
// content that way: AboutUpdateViews.cs:389 (the release notes' section popovers) and HydraSettingsPage.cs:318 (the
// cookbook popover). Their first showing is an empty, now clear, popup window (later showings open with the card of the
// first and swap in the new content, which the host is then known from). Avalonia's own Popup.Host is the popup's host
// from Popup.Open on.
[Collection("Headless")]
public sealed class PopoverContentSwapReviewTests(HeadlessSession headless)
{
    [Theory]
    [Trait("Decision", "ED-238")] // OD-0095
    [InlineData(PlacementMode.Top)]
    [InlineData(PlacementMode.Bottom)]
    public void REVIEW_Content_a_site_builds_when_its_popover_opens_is_shown_the_first_time(PlacementMode placement) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var anchor = new Border { Width = 20, Height = 20 };
        var window = HeadlessPopupHost.OverlayWindow(anchor);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Border? built = null;
        // As AboutUpdateViews.cs:389 and HydraSettingsPage.cs:318 build theirs: no child until the popup has opened.
        var popup = new Popup { PlacementTarget = anchor, Placement = placement, IsLightDismissEnabled = true, ShouldUseOverlayLayer = true };
        popup.Opened += (_, _) => popup.Child = built = new Border { Width = 120, Height = 60 };
        try
        {
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(popup.IsOpen);
            Assert.NotNull(built);
            Assert.True(built!.IsAttachedToVisualTree(), "the content built as the popover opened is not shown");
            Assert.True(Assert.IsType<PopoverCard>(popup.Child).IsAttachedToVisualTree(), "the card it hangs on is not shown");
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
        }
    });
}
