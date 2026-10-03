using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// REVIEW (final review of unit/effort-title-fidelity, round 1, 2026-10-03, finding 3): the card's 13 pt tip band and its
// 11 pt of room for the shadow on the other three sides are part of the popup's native window, which Windows hit-tests as
// a client area: a press there went to the popup window, so a popover that does not light-dismiss (PromptEditor,
// DiffInspector, AttachmentPreviewPanel, ChatBadge) neither closed nor let the press reach the monitors and the window
// under it. The popup window now answers the hit test itself: outside the card's outline it is transparent to the window
// beneath (HTTRANSPARENT). Win32's own hit test cannot be reached headless; what is tested here is the answer the hook
// gives to the message Windows sends it, and where it is put and taken off. The press reaching the window under a real
// popup is owed at the quiet-PC gate.
[Collection("Headless")]
public sealed class PopoverWindowHitTestTests(HeadlessSession headless)
{
    private const uint WmMouseMove = 0x0200;

    // MAKELPARAM(x, y): the two signed 16-bit words, x low.
    private static nint LParam(PixelPoint point) => unchecked((nint)(((long)(ushort)(short)point.Y << 16) | (ushort)(short)point.X));

    // A card in a popup window of its own, as Popup.Open builds one (the headless window has no Win32 procedure).
    private static (Window Owner, Popup Popup, PopupRoot Root, PopoverCard Card) Hung()
    {
        var owner = HeadlessPopupHost.OverlayWindow(new Border { Width = 20, Height = 20 });
        owner.Show();
        Dispatcher.UIThread.RunJobs();
        var card = new PopoverCard
        {
            Child = new Border { Width = 120, Height = 60 },
            TipEdge = PopoverTipEdge.Bottom,
            ShadowRoom = true,
            Background = Brushes.Black,
        };
        var popup = new Popup { PlacementTarget = owner, Placement = PlacementMode.Top };
        var root = HeadlessPopupHost.Open(popup, owner, card);
        root.Measure(new Size(400, 300));
        root.Arrange(new Rect(0, 0, 400, 300));
        Dispatcher.UIThread.RunJobs();
        return (owner, popup, root, card);
    }

    private static PixelPoint Screen(PopupRoot root, PopoverCard card, Point inCard) => root.PointToScreen(card.TranslatePoint(inCard, root)!.Value);

    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void A_pointer_message_carries_a_signed_screen_point() => headless.OnUiThread(() =>
    {
        foreach (var (x, y) in new[] { (100, 200), (-5, 7), (3, -9), (-1920, -1080), (2559, 1439), (0, 0) })
        {
            var point = new PixelPoint(x, y);
            Assert.Equal(point, PopoverHost.ScreenPoint(LParam(point)));
            Assert.Equal(point, PopoverHost.ScreenPoint(unchecked((nint)(int)LParam(point)))); // sign-extended from 32 bits, as a negative y comes
        }
    });

    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void A_press_beside_the_tip_or_in_the_shadows_room_reaches_the_window_under_the_popup() => headless.OnUiThread(() =>
    {
        var (owner, popup, root, card) = Hung();
        try
        {
            Assert.True(card.Bounds.Width > 0, "the card has no size");
            var hook = PopoverHost.PassPressesBeside(() => card, root);
            (nint Answer, bool Handled) Hit(Point inCard, uint message = PopoverHost.WmNcHitTest)
            {
                var handled = false;
                var answer = hook(0, message, 0, LParam(Screen(root, card, inCard)), ref handled);
                return (answer, handled);
            }
            var tip = card.TipPoint;
            Assert.Equal(((nint)0, false), Hit(new Point(card.Bounds.Width / 2, 20))); // on the card's body: Windows' own answer
            Assert.Equal(((nint)0, false), Hit(new Point(tip.X, tip.Y - 4))); // on the tip
            Assert.Equal(((nint)PopoverHost.HtTransparent, true), Hit(new Point(tip.X + PopoverCard.TipHalfWidth + 10, tip.Y - 3))); // beside the tip
            Assert.Equal(((nint)PopoverHost.HtTransparent, true), Hit(new Point(-5, 20))); // in the room kept for the shadow, left of the card
            Assert.Equal(((nint)PopoverHost.HtTransparent, true), Hit(new Point(card.Bounds.Width / 2, -4))); // above it
            Assert.Equal(((nint)PopoverHost.HtTransparent, true), Hit(new Point(tip.X + 40, tip.Y + 0.5))); // under the card's edge, beside the tip
            Assert.Equal(((nint)0, false), Hit(new Point(tip.X + PopoverCard.TipHalfWidth + 10, tip.Y - 3), WmMouseMove)); // only the hit test is answered
        }
        finally { root.Dispose(); owner.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void A_card_not_yet_hung_leaves_the_hit_test_to_windows() => headless.OnUiThread(() =>
    {
        var (owner, popup, root, card) = Hung();
        try
        {
            var hook = PopoverHost.PassPressesBeside(() => null, root);
            var handled = false;
            Assert.Equal(0, hook(0, PopoverHost.WmNcHitTest, 0, LParam(Screen(root, card, new Point(-5, 20))), ref handled));
            Assert.False(handled);
        }
        finally { root.Dispose(); owner.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void A_popup_window_gets_the_hook_while_it_is_open_and_loses_it_when_it_closes() => headless.OnUiThread(() =>
    {
        var hooks = new RecordingWindowProcedureHooks();
        using var seam = PopoverHost.WindowProcedure.Override(hooks);
        var (owner, popup, root, card) = Hung();
        try
        {
            PopoverHost.Track(popup, root);
            var (window, hook) = Assert.Single(hooks.Added);
            Assert.Same(root, window);
            Assert.Same(root, PopoverHost.Hosted(popup));
            Assert.Empty(hooks.Removed);
            PopoverHost.Track(popup, root); // opened again without closing: the first hook goes
            Assert.Equal(2, hooks.Added.Count);
            Assert.Equal(hook, Assert.Single(hooks.Removed).Hook);
            PopoverHost.Untrack(popup);
            Assert.Equal(2, hooks.Removed.Count);
            Assert.Equal(hooks.Added[1].Hook, hooks.Removed[1].Hook);
            Assert.Null(PopoverHost.Hosted(popup));
            PopoverHost.Untrack(popup); // closed twice: nothing more comes off
            Assert.Equal(2, hooks.Removed.Count);
        }
        finally { root.Dispose(); owner.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void The_overlays_host_is_no_window_of_its_own_and_gets_no_hook() => headless.OnUiThread(() =>
    {
        var hooks = new RecordingWindowProcedureHooks();
        using var seam = PopoverHost.WindowProcedure.Override(hooks);
        var popup = new Popup();
        var host = new Border();
        PopoverHost.Track(popup, host);
        Assert.Empty(hooks.Added);
        Assert.Same(host, PopoverHost.Hosted(popup));
        PopoverHost.Untrack(popup);
        Assert.Null(PopoverHost.Hosted(popup));
    });

    // The presenter's popup has its host tracked from the moment it opens to the moment it closes (a headless popup opens
    // in the overlay's host, the one that is no window of its own).
    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void A_presented_popover_is_tracked_from_its_opening_to_its_closing() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var anchor = new Border { Width = 20, Height = 20 };
        var window = HeadlessPopupHost.OverlayWindow(anchor);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Top, ShouldUseOverlayLayer = true, Child = new Border { Width = 120, Height = 60 } };
        try
        {
            Assert.Null(PopoverHost.Hosted(popup));
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<OverlayPopupHost>(PopoverHost.Hosted(popup));
            popup.IsOpen = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(PopoverHost.Hosted(popup));
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
        }
    });
}
