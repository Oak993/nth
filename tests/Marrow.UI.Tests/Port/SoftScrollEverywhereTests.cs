using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Port;

// ED-NEW-softscroll-1 (OD-0071: "every scroll area ... uses one scrolling behaviour"): soft scrolling is the default of every
// ScrollViewer, not an opt-in. A scroll view nobody set anything on (a page's, a popover's, a menu's) glides a notch over
// several frames; one that opted out steps at once; a control that takes the wheel itself keeps it; nesting still chains.
[Collection("Headless")]
public sealed class SoftScrollEverywhereTests(HeadlessSession headless)
{
    // A notch on `viewer` (over `at`): nothing moves in the report's own frame; then, frame by frame at 60 Hz, the two-stage
    // curve of ED-NEW-softscroll-2 (from rest: 4.5 points in the first frame), over at least six frames, never an eighth of
    // the notch in one, and at rest exactly one notch (100 points at the default 3 lines) on.
    private static void AssertGlides(SoftScrollRig rig, ScrollViewer viewer, Point at)
    {
        var before = viewer.Offset.Y;
        var start = rig.Seconds;
        rig.Wheel(new Vector(0, -1), at);
        Assert.Equal(before, viewer.Offset.Y); // nothing in the report's own frame
        var moved = new List<double>();
        for (var k = 0; k < 60; k++)
        {
            rig.Frame(60);
            rig.Window.UpdateLayout();
            moved.Add(viewer.Offset.Y - before);
            var t = rig.Seconds - start;
            var curve = Math.Min(100, 100.1 * (1 - (1 + t / ScrollPhysics.GlideStage) * Math.Exp(-t / ScrollPhysics.GlideStage)));
            Assert.True(Math.Abs(moved[^1] - curve) < 1e-6, FormattableString.Invariant($"frame {k + 1}: {moved[^1]:F4}, the curve {curve:F4}"));
        }
        Assert.InRange(moved[0], 4.4, 4.5); // the first frame starts the motion from rest
        var steps = moved.Zip(moved.Prepend(0), (now, was) => now - was).ToArray();
        Assert.True(steps.Count(step => step >= 0.5) >= 6, "the notch moved in fewer than six frames: " + string.Join(" ", moved.Take(12).Select(m => m.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))));
        Assert.All(steps, step => Assert.InRange(step, -1e-6, 12.5)); // never back, never an eighth of the notch in one frame
        Assert.Equal(100, moved[^1], 6);
        Assert.False(SoftScroll.For(viewer).IsMoving);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-softscroll-1")]
    public void A_plain_scroll_view_glides_with_nothing_set_on_it() => headless.OnUiThread(() =>
    {
        using var rig = new SoftScrollRig();
        Assert.False(rig.Scroll.IsSet(SoftScroll.IsEnabledProperty));
        AssertGlides(rig, rig.Scroll, new Point(250, 150));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-softscroll-1")] // Popovers.swift :94-115 PopoverMenu and :43-92 PopoverScroll: plain ScrollViews
    public void A_popover_menu_and_a_popover_scroll_glide_and_the_page_around_them_stays() => headless.OnUiThread(() =>
    {
        var menu = new PopoverMenu(() => new Border { Height = 1000, Width = 200 }, maxHeight: 150) { VerticalAlignment = VerticalAlignment.Top };
        using (var rig = new SoftScrollRig(content: new StackPanel { Children = { menu, new Border { Height = 2000 } } }))
        {
            var scroll = menu.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.False(scroll.IsSet(SoftScroll.IsEnabledProperty));
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 100, $"the menu does not overflow: {scroll.Extent} in {scroll.Viewport}");
            AssertGlides(rig, scroll, new Point(50, 60));
            Assert.Equal(0, rig.Scroll.Offset.Y);
        }

        var popover = new PopoverScroll(() => new Border { Height = 1000, Width = 200 }, maxHeight: 150, width: 200) { VerticalAlignment = VerticalAlignment.Top };
        using (var rig = new SoftScrollRig(content: new StackPanel { Children = { popover, new Border { Height = 2000 } } }))
        {
            var scroll = popover.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.Equal(150, scroll.Viewport.Height, 3);
            AssertGlides(rig, scroll, new Point(50, 60));
            Assert.Equal(0, rig.Scroll.Offset.Y);
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-softscroll-1")]
    public void A_scroll_view_that_opted_out_steps_at_once_and_at_its_end_hands_the_wheel_to_a_soft_page() => headless.OnUiThread(() =>
    {
        var inner = new ScrollViewer { Height = 100, VerticalAlignment = VerticalAlignment.Top, Content = new Border { Height = 300, Width = 100 } };
        SoftScroll.SetIsEnabled(inner, false);
        using var rig = new SoftScrollRig(content: new StackPanel { Children = { inner, new Border { Height = 2000 } } });
        var over = new Point(50, 20);
        rig.Wheel(new Vector(0, -1), over);
        Assert.Equal(50, inner.Offset.Y); // Avalonia's step, in the report's own frame
        Assert.False(SoftScroll.IsScrolling(inner));
        rig.Wheel(new Vector(0, -3), over);
        Assert.Equal(200, inner.Offset.Y); // at its end
        Assert.Equal(0, rig.Scroll.Offset.Y);
        // The next notch chains to the page, which glides it.
        AssertGlides(rig, rig.Scroll, over);
        Assert.Equal(200, inner.Offset.Y);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-softscroll-1")]
    public void A_soft_inner_scroll_view_glides_and_at_its_end_the_page_glides_on() => headless.OnUiThread(() =>
    {
        var inner = new ScrollViewer { Height = 100, VerticalAlignment = VerticalAlignment.Top, Content = new Border { Height = 200, Width = 100 } };
        using var rig = new SoftScrollRig(content: new StackPanel { Children = { inner, new Border { Height = 2000 } } });
        var over = new Point(50, 20);
        AssertGlides(rig, inner, over); // 100 of its 100: at its end now
        Assert.Equal(0, rig.Scroll.Offset.Y);
        AssertGlides(rig, rig.Scroll, over); // chained
        Assert.Equal(100, inner.Offset.Y);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-softscroll-1")]
    public void A_control_that_owns_its_wheel_keeps_it_inside_a_soft_page() => headless.OnUiThread(() =>
    {
        var taker = new WheelTaker { Height = 100, Width = 200, Background = Avalonia.Media.Brushes.Transparent, VerticalAlignment = VerticalAlignment.Top };
        SoftScroll.SetOwnsWheel(taker, true);
        using var rig = new SoftScrollRig(content: new StackPanel { Children = { taker, new Border { Height = 2000 } } });
        rig.Wheel(new Vector(0, -1), new Point(50, 20));
        rig.Run(0.5);
        Assert.Equal(1, taker.Reports);
        Assert.Equal(0, rig.Scroll.Offset.Y);
        // Without the mark the page takes the wheel first (tunnelling) and glides.
        SoftScroll.SetOwnsWheel(taker, false);
        rig.Wheel(new Vector(0, -1), new Point(50, 20));
        rig.Run(0.5);
        Assert.Equal(1, taker.Reports);
        Assert.Equal(100, rig.Scroll.Offset.Y, 6);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-softscroll-1")]
    public void Taking_a_scroll_bars_thumb_stops_the_glide_where_it_is() => headless.OnUiThread(() =>
    {
        var bar = new ScrollBar
        {
            Orientation = Orientation.Vertical, Width = 12, Height = 100, HorizontalAlignment = HorizontalAlignment.Left,
            Template = new FuncControlTemplate<ScrollBar>((_, scope) => new Thumb { Name = "PART_Thumb", Height = 20 }.RegisterInNameScope(scope)),
        };
        using var rig = new SoftScrollRig(content: new StackPanel { Children = { bar, new Border { Height = 2000 } } });
        rig.Wheel(new Vector(0, -4), new Point(250, 150));
        rig.Run(0.03);
        var at = rig.Scroll.Offset.Y;
        Assert.InRange(at, 1, 150);
        Assert.True(SoftScroll.For(rig.Scroll).IsMoving);
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        thumb.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragStartedEvent, Vector = default });
        Assert.False(SoftScroll.For(rig.Scroll).IsMoving);
        rig.Run(0.5);
        Assert.Equal(at, rig.Scroll.Offset.Y);
    });
}
