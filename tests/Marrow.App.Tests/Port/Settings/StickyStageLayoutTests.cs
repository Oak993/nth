using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-3.5.7 (frame cost) and CZ-16: scrolling changes only the block's transform, the clip rectangles, the compaction-driven values and the stage's own height. No section is measured during a scroll,
// not by a wheel step, not by an eased jump, not by a focus scroll. A section is counted by its own measure pass (MeasureProbe) and by whether its layout was ever invalidated.
[Collection("Headless")]
public sealed class StickyStageLayoutTests(HeadlessSession headless)
{
    private const double Frame = 1000.0 / 60;

    private static (int Dots, int Speech) Counts(StickyStageProbes p) => (p.WorkingDots.Measures, p.Speech.Measures);

    // every part of the page but the stage still has the layout it had: valid, so nothing queued a measure for it
    private static void AssertNoMeasureQueued(StickyStageProbes p) => p.Rig.Main.OnMain(() =>
    {
        var page = p.Rig.Page;
        Assert.True(page.IsMeasureValid, "the page");
        Assert.True(page.Sticky.IsMeasureValid, "the sticky container");
        Assert.True(page.Rest.IsMeasureValid, "the rest");
        foreach (var child in page.Sticky.Children.Where(c => c != page.Stage)) Assert.True(child.IsMeasureValid, $"a section ({child.GetType().Name})");
        Assert.True(p.WorkingDots.IsMeasureValid && p.Speech.IsMeasureValid);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T68, CZ-3.5.7: wheel steps (pin, clip, compaction) cause no section measure pass: not one section is invalidated and the probes are not measured again
    public void A_wheel_scroll_measures_no_section() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: false);
        var before = Counts(p);
        foreach (var y in new[] { 5.0, 20, 50, 80, 92, 140, 300, 520, 300, 92, 60, 30, 0 })
        {
            p.Rig.Main.OnMain(() => p.Rig.Scroll!.Offset = new Vector(0, y));
            AssertNoMeasureQueued(p); // before any layout pass: nothing was invalidated by the step
            p.Rig.Settle();
            p.Rig.Advance(Frame);
            Assert.Equal(before, Counts(p));
        }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T68, CZ-3.5.7: an eased jump (the stage folding over 40 frames, its height changing every one) measures no section
    public void An_eased_jump_measures_no_section() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: false);
        var before = Counts(p);
        p.Rig.ScrollTo(700);
        for (var frame = 0; frame < 40; frame++)
        {
            p.Rig.Advance(Frame, settle: false);
            AssertNoMeasureQueued(p);
            p.Rig.Settle();
            Assert.Equal(before, Counts(p));
        }
        Assert.Equal(92, p.Rig.Get(() => p.Rig.Page.Stage!.Compaction), 3);
        Assert.Equal(before, Counts(p));
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T68, CZ-3.5.7: the stage's height does change layout, inside the stage alone: the slot it asks of the page stays 372 and the sections keep their places
    public void The_stage_re_lays_out_inside_its_own_slot_only() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: true);
        var stage = p.Rig.Page.Stage!;
        var desired = p.Rig.Get(() => stage.DesiredSize);
        var surface = p.Rig.Get(() => stage.Surface.Bounds.Height);
        p.Rig.ScrollTo(92);
        Assert.NotEqual(surface, p.Rig.Get(() => stage.Surface.Bounds.Height));
        Assert.Equal(desired, p.Rig.Get(() => stage.DesiredSize));
        Assert.Equal(372, p.Rig.Get(() => stage.Bounds.Height));
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T68, CZ-3.5.7: a focus scroll measures no section either
    public void A_focus_scroll_measures_no_section() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: false);
        var target = p.Rig.Get(() => p.Rig.Page.Sticky.Children.Where(c => c != p.Rig.Page.Stage).SelectMany(c => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(c).OfType<Control>())
            .First(c => c.Focusable && c.IsTabStop && c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c.TranslatePoint(default, p.Rig.Scroll!)!.Value.Y > 900));
        var before = Counts(p);
        p.Rig.Do(() => target.Focus(NavigationMethod.Tab));
        for (var frame = 0; frame < 30; frame++)
        {
            p.Rig.Advance(Frame, settle: false);
            AssertNoMeasureQueued(p);
            p.Rig.Settle();
            Assert.Equal(before, Counts(p));
        }
        Assert.True(p.Rig.Offset > 100);
    });
}
