using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-3.5.6 (keyboard focus stays clear): a control that takes keyboard focus under the block (or out of the viewer) is scrolled to 8 points below the block's bottom, computed with the compaction the new offset
// will produce, over 0.3 s ease (at once under reduce motion); a pointer focus does not scroll. The default of the framework (an immediate, minimal scroll that leaves the control under the block's edge) must not run.
[Collection("Headless")]
public sealed class StickyStageFocusTests(HeadlessSession headless)
{
    private const double Frame = 1000.0 / 60;

    // the controls a Tab can reach below the block, with where each lies in the content (from the top of the page)
    private static List<(Control Control, double Top)> Reachable(StickyStageProbes p)
    {
        var offset = p.Rig.Offset;
        return p.Rig.Get(() => p.Rig.Page.Sticky.Children.Where(c => c != p.Rig.Page.Stage).SelectMany(c => c.GetVisualDescendants().OfType<Control>())
            .Where(c => c.Focusable && c.IsTabStop && c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c.Bounds.Height > 0)
            .Select(c => (c, c.TranslatePoint(default, p.Rig.Scroll!)!.Value.Y + offset)).OrderBy(t => t.Item2).ToList());
    }

    private static Control Far(StickyStageProbes p, double minTop = 900)
    {
        var far = Reachable(p).Where(t => t.Top >= minTop && t.Top - 288 <= p.MaxOffset).ToList();
        Assert.NotEmpty(far);
        return far[0].Control;
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T67, CZ-3.5.6: Tab to a row under the block's reach scrolls the page, animated over 0.3 s ease, until the control's top is 8 below the block's bottom (the block 280 high by then)
    public void Tab_to_a_row_hidden_below_the_viewer_scrolls_it_to_8_below_the_block_animated() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: false);
        var target = Far(p);
        var top = p.ViewportTop(target);
        Assert.True(top > 700, "the control starts below the 700 high viewer");
        var expectedOffset = top - 288;
        p.Rig.Do(() => target.Focus(NavigationMethod.Tab));
        Assert.True(p.Rig.Offset < 1, "no jump: the framework's own bring-into-view must not run");
        var ease = CzMotion.Ease(0.3);
        var last = 0.0;
        for (var frame = 1; frame <= 24; frame++)
        {
            p.Rig.Advance(Frame);
            var offset = p.Rig.Offset;
            var seconds = Math.Min(0.3, frame / 60.0);
            Assert.True(Math.Abs(offset - expectedOffset * ease.Progress(seconds)) <= 0.01 * expectedOffset + 1, $"frame {frame}: offset {offset} of {expectedOffset}");
            Assert.True(offset >= last);
            Assert.True(offset - last <= expectedOffset * 0.2, "a frame never moves it a fifth of the way: it glides");
            last = offset;
        }
        Assert.Equal(expectedOffset, p.Rig.Offset, 1);
        // the compaction followed the scroll and the control is 8 below the block, the block being 372 - 92
        Assert.Equal(92, p.Rig.Get(() => p.Rig.Page.Stage!.Compaction), 3);
        Assert.Equal(288, p.ViewportTop(target), 0);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T67, CZ-3.5.6: a pointer focus does not scroll
    public void A_pointer_focus_does_not_scroll() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: false);
        var target = Far(p);
        p.Rig.Do(() => target.Focus(NavigationMethod.Pointer));
        p.Rig.Advance(500);
        Assert.Equal(0, p.Rig.Offset);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T67, CZ-3.5.6: a control already clear of the block and inside the viewer is not scrolled to
    public void A_control_that_is_already_clear_is_left_where_it_is() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: false);
        var visible = Reachable(p).First(t => t.Top > 380 && t.Top + 20 < 700).Control;
        p.Rig.Do(() => visible.Focus(NavigationMethod.Tab));
        p.Rig.Advance(500);
        Assert.Equal(0, p.Rig.Offset);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T67, CZ-3.5.6: under reduce motion the scroll is at once and the compaction with it
    public void Under_reduce_motion_the_focus_scroll_is_at_once() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: true);
        var target = Far(p);
        var expectedOffset = p.ViewportTop(target) - 288;
        p.Rig.Do(() => target.Focus(NavigationMethod.Tab));
        Assert.Equal(expectedOffset, p.Rig.Offset, 1);
        Assert.Equal(92, p.Rig.Get(() => p.Rig.Page.Stage!.Compaction), 3);
        Assert.Equal(288, p.ViewportTop(target), 0);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T67, CZ-3.5.6: Shift+Tab back to a row hidden under the block scrolls up to put it 8 below the block
    public void A_row_scrolled_under_the_block_is_brought_back_clear_of_it() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: true);
        var target = Reachable(p).First(t => t.Top > 450).Control;
        var top = p.ViewportTop(target);
        p.Rig.ScrollTo(top + 300); // far past it: the control is above the viewer
        Assert.True(p.ViewportTop(target) < 0);
        p.Rig.Do(() => target.Focus(NavigationMethod.Tab));
        Assert.Equal(288, p.ViewportTop(target), 0);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T67, CZ-3.5.6: the user's own scroll during the focus scroll takes over (the animation lets go)
    public void A_wheel_scroll_during_the_focus_scroll_takes_it_over() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: false);
        var target = Far(p);
        p.Rig.Do(() => target.Focus(NavigationMethod.Tab));
        p.Rig.Advance(Frame * 6);
        var mid = p.Rig.Offset;
        Assert.True(mid > 20);
        p.Rig.ScrollTo(10); // the user wheels back up
        p.Rig.Advance(500);
        Assert.Equal(10, p.Rig.Offset, 1);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-3.5.6: a control inside the block itself, in view, is not scrolled to
    public void A_control_in_the_block_is_not_scrolled_to() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: true);
        p.Rig.ScrollTo(300);
        var inside = p.Rig.Get(() => p.Rig.Page.Stage!.GetVisualDescendants().OfType<Control>().First(c => c.Focusable && c.IsTabStop && c.IsEffectivelyVisible && c.IsEffectivelyEnabled));
        p.Rig.Do(() => inside.Focus(NavigationMethod.Tab));
        Assert.Equal(300, p.Rig.Offset, 1);
    });
}
