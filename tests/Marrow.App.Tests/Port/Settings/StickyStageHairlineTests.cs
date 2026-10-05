using Avalonia;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-3.5.5 (hairline): while the page is scrolled past 4 points a 1 point line in the divider colour shows along the block's bottom edge across the 520 column, fading in and out over 0.25 s (ease).
[Collection("Headless")]
public sealed class StickyStageHairlineTests(HeadlessSession headless)
{
    private const double Frame = 1000.0 / 60;

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T66, CZ-3.5.5: nothing at the top; no hairline up to 4; past 4 it fades in over 0.25 s ease; back at 0 it fades out the same way (each sampled at 60 Hz)
    public void The_hairline_fades_in_past_4_points_and_out_at_the_top() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: false);
        var hairline = p.Rig.Page.Stage!.Hairline;
        var ease = CzMotion.Ease(0.25);
        Assert.Equal(0, p.Rig.Get(() => hairline.Opacity));
        p.Rig.ScrollTo(4);
        p.Rig.Advance(400);
        Assert.Equal(0, p.Rig.Get(() => hairline.Opacity)); // 4 is not past 4
        p.Rig.ScrollTo(5);
        var last = 0.0;
        for (var frame = 1; frame <= 20; frame++)
        {
            p.Rig.Advance(Frame);
            var seconds = Math.Min(0.25, frame / 60.0);
            var opacity = p.Rig.Get(() => hairline.Opacity);
            Assert.Equal(ease.Progress(seconds), opacity, 2);
            Assert.True(opacity >= last);
            last = opacity;
        }
        Assert.Equal(1, last);
        p.Rig.ScrollTo(200);
        p.Rig.Advance(100);
        Assert.Equal(1, p.Rig.Get(() => hairline.Opacity)); // it stays while scrolled
        p.Rig.ScrollTo(0);
        last = 1;
        for (var frame = 1; frame <= 20; frame++)
        {
            p.Rig.Advance(Frame);
            var seconds = Math.Min(0.25, frame / 60.0);
            var opacity = p.Rig.Get(() => hairline.Opacity);
            Assert.Equal(1 - ease.Progress(seconds), opacity, 2);
            Assert.True(opacity <= last);
            last = opacity;
        }
        Assert.Equal(0, last);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T66, CZ-3.5.5: under reduce motion the hairline is at its state at once
    public void Under_reduce_motion_the_hairline_is_at_once() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: true);
        var hairline = p.Rig.Page.Stage!.Hairline;
        p.Rig.ScrollTo(5);
        Assert.Equal(1, p.Rig.Get(() => hairline.Opacity));
        p.Rig.ScrollTo(0);
        Assert.Equal(0, p.Rig.Get(() => hairline.Opacity));
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T66, CZ-3.5.5: the line is 1 point, 520 wide, in the row divider's colour, on the block's bottom edge (372 - col below the viewer's top while pinned), and takes no hit
    public void The_hairline_lies_on_the_blocks_bottom_edge_across_the_column_in_the_divider_colour() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: true);
        var stage = p.Rig.Page.Stage!;
        var hairline = stage.Hairline;
        foreach (var y in new[] { 0.0, 10, 46, 92, 300 })
        {
            p.Rig.ScrollTo(y);
            var col = Math.Min(92, y);
            Assert.Equal(372 - col, p.ViewportTop(hairline) + 1, 0);
            Assert.Equal(1, p.Rig.Get(() => hairline.Bounds.Height));
            Assert.Equal(520, p.Rig.Get(() => hairline.Bounds.Width));
            Assert.Equal(0, p.Rig.Get(() => hairline.TranslatePoint(default, p.Rig.Scroll!)!.Value.X));
        }
        Assert.False(p.Rig.Get(() => hairline.IsHitTestVisible));
        // the colour of ChromeRowDivider: Overlay(0.12), resolved in the window's appearance
        var expected = Chrome.Overlay(0.12).Resolve(p.Rig.Get(() => hairline.ActualThemeVariant));
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(p.Rig.Get(() => hairline.Background));
        Assert.Equal(expected, brush.Color);
    });
}
