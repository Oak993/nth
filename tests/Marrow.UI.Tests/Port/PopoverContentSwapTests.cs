using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// REVIEW (final review of unit/effort-title-fidelity, round 1, 2026-10-03, the pre-existing finding that is HIGH for the
// port): Avalonia 12.1.2 gives a Popup's child to its host once, when the popup opens (Popup.Open: popupHost.SetChild(Child));
// Popup.ChildChanged only moves it in the logical tree. A child a site sets while its popup is open was therefore never
// shown: PlainControls.Popover(buildBeforeOpen: false) (the MCP page's add and edit editors showed only their one-pixel
// placeholder), AttachmentPreviewPanel shown again at another thumbnail kept the old preview, MessagePopoverMenu clicked
// again kept the old actions, and a re-show inside the 0.17 s a popover shrinks away kept the old content. The checks here
// are on the visual tree, not on popup.Child: what is attached to the host's tree is what is on screen.
[Collection("Headless")]
public sealed class PopoverContentSwapTests(HeadlessSession headless)
{
    private static Popup Shown(Control anchor, Control child, PlacementMode placement, bool sheet = false)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = placement, ShouldUseOverlayLayer = true, Child = child };
        if (sheet) PopoverHost.SetIsSheet(popup, true);
        Presentation.Presenter.Current.SetPresented(popup, true);
        Dispatcher.UIThread.RunJobs();
        return popup;
    }

    [Theory]
    [Trait("Decision", "ED-238")] // OD-0095
    [InlineData(PlacementMode.Top, false)]
    [InlineData(PlacementMode.Bottom, false)]
    [InlineData(PlacementMode.Right, false)]
    [InlineData(PlacementMode.Center, true)]
    public void A_content_set_while_the_popover_is_open_is_what_is_shown(PlacementMode placement, bool sheet) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var anchor = new Border { Width = 20, Height = 20 };
        var window = HeadlessPopupHost.OverlayWindow(anchor);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var first = new Border { Width = 100, Height = 40 };
        var second = new Border { Width = 140, Height = 50 };
        var popup = Shown(anchor, first, placement, sheet);
        try
        {
            Assert.True(first.IsAttachedToVisualTree());
            popup.Child = second; // the site's new content, while the popup is open
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(second.IsAttachedToVisualTree(), "the content set while the popup is open is not shown");
            Assert.False(first.IsAttachedToVisualTree(), "the old content is still shown");
            Assert.Same(second, PopoverHost.Content(popup));
            if (!sheet) Assert.True(Assert.IsType<PopoverCard>(popup.Child).IsAttachedToVisualTree(), "the card it hangs on is not shown");
            Assert.Equal(sheet ? 0 : 1, window.GetVisualDescendants().OfType<PopoverCard>().Count());
            var third = new Border { Width = 90, Height = 30 };
            popup.Child = third; // and again
            Dispatcher.UIThread.RunJobs();
            Assert.True(third.IsAttachedToVisualTree() && !second.IsAttachedToVisualTree(), "the content set a second time is not what is shown");
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
        }
    });

    // The way a site shows a popover again before the last one has gone: the presenter keeps a closing popover open for the
    // 0.17 s it shrinks into its tip, and a show in that time sets the new content and the new anchor on the same popup.
    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void A_popover_shown_again_while_it_shrinks_away_shows_the_new_content() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var anchor = new Border { Width = 20, Height = 20 };
        var window = HeadlessPopupHost.OverlayWindow(anchor);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var first = new Border { Width = 100, Height = 40 };
        var popup = Shown(anchor, first, PlacementMode.Bottom);
        try
        {
            Presentation.Presenter.Current.SetPresented(popup, false); // shrinking into its tip: still open
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            var second = new Border { Width = 140, Height = 50 };
            popup.Child = second;
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            Assert.True(second.IsAttachedToVisualTree(), "the new content is not shown");
            Assert.False(first.IsAttachedToVisualTree(), "the old content is still shown");
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
        }
    });

    // PromptEditor takes its content away while it measures it in a window of its own and puts it back: a null child, set
    // while the popup is open, keeps what the host shows rather than emptying it.
    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void A_child_taken_away_for_a_while_leaves_what_the_host_shows() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var anchor = new Border { Width = 20, Height = 20 };
        var window = HeadlessPopupHost.OverlayWindow(anchor);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var content = new Border { Width = 100, Height = 40 };
        var popup = Shown(anchor, content, PlacementMode.Bottom);
        try
        {
            var card = Assert.IsType<PopoverCard>(popup.Child);
            popup.Child = null;
            Assert.True(content.IsAttachedToVisualTree(), "the host's content went with the child");
            popup.Child = card;
            Dispatcher.UIThread.RunJobs();
            Assert.True(content.IsAttachedToVisualTree() && card.IsAttachedToVisualTree());
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
        }
    });
}
