using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-3.5.1 (pin), CZ-3.5.3 (release), CZ-3.5.4 (what scrolls under it): the Customization page in a scroll viewer, as the Settings window holds it. The sections of the later units are stood in for
// by two probes (StickyStageProbes): the last section of the sticky container (Working dots, 300 high) and the rest after it (Speech, 900 high). Reduce motion is on where a test reads positions, so the compaction follows at once.
[Collection("Headless")]
public sealed class StickyStagePinTests(HeadlessSession headless)
{
    private static List<Control> Sections(StickyStageProbes p) => p.Rig.Get(() => p.Rig.Page.Sticky.Children.Where(c => c != p.Rig.Page.Stage && c.Bounds.Height > 0).ToList());

    private static double[] Offsets(StickyStageProbes p)
    {
        var hc = p.ContainerHeight;
        return [0, 1, 4, 5, 50, 92, 93, 200, 600, hc - 281, hc - 280, hc - 279, hc - 180, p.MaxOffset];
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T60, CZ-3.5.1: the block's visual top, in content coordinates, is clamp(y, 0, H_c - h_pin), h_pin = 372 - col; it is the highest in the page
    public void The_pinned_block_is_at_the_top_of_the_viewer_until_the_container_runs_out() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: true);
        var stage = p.Rig.Page.Stage!;
        var hc = p.ContainerHeight;
        Assert.True(hc > 372 + 2 + 300);
        foreach (var y in Offsets(p))
        {
            p.Rig.ScrollTo(y);
            var offset = p.Rig.Offset;
            var hPin = 372 - Math.Min(92, offset);
            var visualTop = p.ViewportTop(stage) + offset;
            Assert.Equal(Math.Clamp(offset, 0, hc - hPin), visualTop, 1);
        }
        Assert.True(stage.ZIndex > p.Rig.Page.Sticky.Children.Where(c => c != stage).Max(c => c.ZIndex));
        // the block is never re-laid out to move: its slot is where it was at rest
        Assert.Equal(372, p.Rig.Get(() => stage.Bounds.Height));
        Assert.Equal(0, p.Rig.Get(() => stage.Bounds.Y));
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T63, CZ-3.5.2: the layout keeps the block's rest height, so a section's place in the content never depends on the offset (nothing moves faster than the scroll)
    public void Sections_are_where_they_were_at_every_offset() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: true);
        var sections = Sections(p);
        Assert.True(sections.Count >= 5);
        var rest = sections.Select(s => p.ViewportTop(s) + p.Rig.Offset).ToArray();
        var restSpeech = p.ViewportTop(p.Speech) + p.Rig.Offset;
        foreach (var y in Offsets(p))
        {
            p.Rig.ScrollTo(y);
            var offset = p.Rig.Offset;
            for (var i = 0; i < sections.Count; i++) Assert.Equal(rest[i], p.ViewportTop(sections[i]) + offset, 2);
            Assert.Equal(restSpeech, p.ViewportTop(p.Speech) + offset, 2);
        }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T64, CZ-3.5.3: the block lets go exactly when the last section's bottom meets the block's bottom; the rest after it scrolls freely
    public void The_block_lets_go_when_the_last_sections_bottom_meets_its_bottom_and_the_rest_scrolls_freely() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: true);
        var stage = p.Rig.Page.Stage!;
        var hc = p.ContainerHeight;
        var dotsBottom = p.ViewportTop(p.WorkingDots) + p.WorkingDots.Bounds.Height + p.Rig.Offset;
        Assert.Equal(hc, dotsBottom, 2); // the last section ends the container
        foreach (var before in new[] { 120.0, 10, 1 })
        {
            p.Rig.ScrollTo(hc - 280 - before);
            Assert.Equal(0, p.ViewportTop(stage), 1);
        }
        p.Rig.ScrollTo(hc - 280);
        Assert.Equal(0, p.ViewportTop(stage), 1);
        // at the release point the block's bottom is the last section's bottom
        Assert.Equal(dotsBottom, p.ViewportTop(stage) + p.Rig.Offset + 280, 1);
        foreach (var after in new[] { 1.0, 40, 180 })
        {
            p.Rig.ScrollTo(hc - 280 + after);
            Assert.Equal(-after, p.ViewportTop(stage), 1); // carried away with the page, no faster and no slower
        }
        // the Speech probe is never clipped and is hit where it lies
        p.Rig.ScrollTo(hc - 280 + 100);
        var speechTop = p.ViewportTop(p.Speech);
        Assert.Null(p.Rig.Get(() => p.Speech.Clip));
        var hit = p.HitAt(new Point(260, speechTop + 10));
        Assert.True(ReferenceEquals(p.Speech, hit), $"hit {hit?.GetType().Name} at 260,{speechTop + 10}; speech top {speechTop}, stage top {p.ViewportTop(stage)}");
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T65, CZ-3.5.4: what scrolls under the block is clipped to the region below the block's bottom, and is not hit; what is below the block is, and is not clipped
    public void Content_under_the_block_is_clipped_and_not_hit_and_content_below_it_is_neither() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: true);
        var hc = p.ContainerHeight;
        p.Rig.ScrollTo(hc - 380); // pinned, compacted: the block is 280 high; Working dots lies from 80 to 380 in the viewer
        var top = p.ViewportTop(p.WorkingDots);
        Assert.Equal(80, top, 1);
        // the part of the probe under the block is not hit; the part below it is
        Assert.NotSame(p.WorkingDots, p.HitAt(new Point(260, 200)));
        Assert.Same(p.WorkingDots, p.HitAt(new Point(260, 330)));
        // the clip is a rectangle from the block's bottom edge downward, in the probe's own coordinates (280 - 80 = 200 down)
        var clip = p.Rig.Get(() => p.WorkingDots.Clip)!;
        Assert.Equal(200, p.Rig.Get(() => clip.Bounds.Top), 1);
        Assert.True(p.Rig.Get(() => clip.Bounds.Bottom) >= 300);
        // it follows the offset: 40 further on the probe has gone 40 up the viewer and the block has not, so the edge is 40 further down the probe
        p.Rig.ScrollTo(hc - 340);
        Assert.Equal(240, p.Rig.Get(() => p.WorkingDots.Clip!.Bounds.Top), 1);
        // a section wholly under the block is clipped away wholly
        p.Rig.ScrollTo(hc - 280);
        Assert.True(p.Rig.Get(() => p.WorkingDots.Clip!.Bounds.Top) >= p.WorkingDots.Bounds.Height);
        // one wholly below it is not clipped at all: nothing it draws outside its bounds (a focus ring, a shadow) is cut
        p.Rig.ScrollTo(0);
        Assert.True(p.Rig.Get(() => p.WorkingDots.Clip is null || p.WorkingDots.Clip.Bounds.Top <= -1));
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T65, CZ-3.5.4: the pixels under the block are the block's own: the page with its sections hidden draws the same there, and differs below
    public void Nothing_of_the_sections_is_drawn_under_the_block() => headless.OnUiThread(() =>
    {
        using var p = new StickyStageProbes(reduceMotion: true);
        p.Rig.ScrollTo(200); // the block is 280 high; Style and Look run under it and out below it
        var window = p.Rig.Window;
        var under = new Rect(0, 90, 520, 180); // the card's body: under the foot, past the heading
        var below = new Rect(0, 290, 520, 300);
        var shownUnder = PreviewPixels.Region(window, under);
        var shownBelow = PreviewPixels.Region(window, below);
        var sections = Sections(p);
        p.Rig.Do(() => { foreach (var s in sections) s.Opacity = 0; });
        var hiddenUnder = PreviewPixels.Region(window, under);
        var hiddenBelow = PreviewPixels.Region(window, below);
        Assert.Equal(0, PreviewPixels.MeanDifference(shownUnder, hiddenUnder));
        Assert.True(PreviewPixels.MeanDifference(shownBelow, hiddenBelow) > 0.5, "the sections below the block are drawn");
    });
}
