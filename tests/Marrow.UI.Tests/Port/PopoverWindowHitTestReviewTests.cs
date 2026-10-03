using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// REVIEW (final review of unit/effort-title-fidelity, round 2, 2026-10-03): the popup window's hit test (PopoverHost.Track,
// PassPressesBeside) answers WM_NCHITTEST, whose lParam is a point on the SCREEN in physical pixels, against the card's
// outline in the card's own units. PopoverWindowHitTestTests run on a headless popup window at scale 1, where a screen point
// and a client point are the same numbers, so a hook that skipped the window's own conversion (PointToClient: the window's
// scale, and on Windows its place on the screen) passed them; and they call the hook with a card fixed when it was made,
// while the hook a window gets reads the popup's card live, so a hook that kept the card it was put on with passed them too.
// A real popup window sits away from the screen's origin, often at 125-200 % scale, and its card is replaced while it shows
// (content set while open: PopoverHost.Follow wraps it in a new card).
[Collection("Headless")]
public sealed class PopoverWindowHitTestReviewTests(HeadlessSession headless)
{
    // MAKELPARAM(x, y): the two signed 16-bit words, x low.
    private static nint LParam(PixelPoint point) => unchecked((nint)(((long)(ushort)(short)point.Y << 16) | (ushort)(short)point.X));

    private static Window Owner()
    {
        var owner = HeadlessPopupHost.OverlayWindow(new Border { Width = 20, Height = 20 });
        owner.Show();
        Dispatcher.UIThread.RunJobs();
        return owner;
    }

    private static PopoverCard Card(double width, double height) => new()
    {
        Child = new Border { Width = width, Height = height },
        TipEdge = PopoverTipEdge.Bottom,
        ShadowRoom = true,
        Background = Brushes.Black,
    };

    private static void Lay(PopupRoot root)
    {
        root.Measure(new Size(400, 300));
        root.Arrange(new Rect(0, 0, 400, 300));
        Dispatcher.UIThread.RunJobs();
    }

    private static (nint Answer, bool Handled) Hit(Win32Properties.CustomWndProcHookCallback hook, PopupRoot root, PopoverCard card, Point inCard)
    {
        var handled = false;
        var screen = root.PointToScreen(card.TranslatePoint(inCard, root)!.Value);
        var answer = hook(0, PopoverHost.WmNcHitTest, 0, LParam(screen), ref handled);
        return (answer, handled);
    }

    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void REVIEW_A_popup_window_at_twice_the_scale_answers_for_the_point_under_the_pointer() => headless.OnUiThread(() =>
    {
        var owner = Owner();
        var card = Card(120, 60);
        var popup = new Popup { PlacementTarget = owner, Placement = PlacementMode.Top };
        var root = HeadlessPopupHost.Open(popup, owner, card);
        try
        {
            // The headless window's own scale (Avalonia.Headless.IHeadlessWindow.SetRenderScaling, internal): its screen points
            // are then twice its client points, as on a 200 % monitor.
            var scaling = typeof(Avalonia.Headless.AvaloniaHeadlessPlatformOptions).Assembly.GetType("Avalonia.Headless.IHeadlessWindow", throwOnError: true)!;
            scaling.GetMethod("SetRenderScaling")!.Invoke(root.PlatformImpl, [2.0]);
            Lay(root);
            Assert.Equal(2.0, root.RenderScaling);
            var hook = PopoverHost.PassPressesBeside(() => card, root);
            var tip = card.TipPoint;
            Assert.Equal(((nint)0, false), Hit(hook, root, card, new Point(card.Bounds.Width - 12, 10))); // on the card's body, near its right edge
            Assert.Equal(((nint)0, false), Hit(hook, root, card, new Point(tip.X, tip.Y - 4))); // on the tip
            Assert.Equal(((nint)PopoverHost.HtTransparent, true), Hit(hook, root, card, new Point(-5, 20))); // in the shadow's room, left of the card
            Assert.Equal(((nint)PopoverHost.HtTransparent, true), Hit(hook, root, card, new Point(tip.X + PopoverCard.TipHalfWidth + 10, tip.Y - 3))); // beside the tip
        }
        finally { root.Dispose(); owner.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-238")] // OD-0095
    public void REVIEW_The_hook_a_popup_window_gets_tests_the_card_it_shows_after_its_content_is_replaced() => headless.OnUiThread(() =>
    {
        var hooks = new RecordingWindowProcedureHooks();
        using var seam = PopoverHost.WindowProcedure.Override(hooks);
        var owner = Owner();
        var first = Card(120, 60);
        var popup = new Popup { PlacementTarget = owner, Placement = PlacementMode.Top, Child = first };
        var root = HeadlessPopupHost.Open(popup, owner, first);
        try
        {
            Lay(root);
            PopoverHost.Track(popup, root); // what PopoverHost.Follow does when the popup opens
            var (_, hook) = Assert.Single(hooks.Added);
            Assert.Equal(((nint)PopoverHost.HtTransparent, true), Hit(hook, root, first, new Point(-5, 20)));
            // The site sets new content while the popover shows: it hangs on a new card, which the popup window then shows
            // (PopoverHost.Follow: Wrap, then the host's Content).
            var second = Card(200, 100);
            popup.Child = second;
            root.SetChild(second);
            root.Presenter?.UpdateChild(); // the root's presenter takes its new content (Popup.Open's SetChild, then a layout pass)
            root.InvalidateMeasure();
            Lay(root);
            Assert.True(second.Bounds.Width > 0 && second.IsAttachedToVisualTree(), $"the new card is not laid out on the window ({second.Bounds})");
            Assert.Equal(((nint)PopoverHost.HtTransparent, true), Hit(hook, root, second, new Point(-5, 20))); // beside the card on screen
            Assert.Equal(((nint)PopoverHost.HtTransparent, true), Hit(hook, root, second, new Point(second.TipPoint.X + PopoverCard.TipHalfWidth + 10, second.TipPoint.Y - 3)));
            Assert.Equal(((nint)0, false), Hit(hook, root, second, new Point(second.Bounds.Width - 12, 10))); // on its body
        }
        finally { PopoverHost.Untrack(popup); root.Dispose(); owner.Close(); }
    });
}
