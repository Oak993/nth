using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port.Settings;
using SkiaSharp;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-16, CZ-T44 (as ED-252's allocation tests): the preview's frames and the style glyphs' frames make no managed object once warm. The UI thread's share of a page frame is measured on a frame source that
// allocates nothing itself; the glyph's operation is taken back from Avalonia as the Jewel knob's is; the drawing is measured on a bitmap canvas and held to the picture CZ-1 first drew.
[Collection("Headless")]
public sealed class CustomizationAllocationTests(HeadlessSession headless)
{
    private const float Width = 160;

    private static byte[] Pixels(Action<SKCanvas> draw)
    {
        using var bitmap = new SKBitmap(new SKImageInfo((int)Width, 56, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        draw(canvas);
        return bitmap.Bytes;
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T44, CZ-16: a warm page frame (the preview light and the three style glyphs wait for their frame and turn) allocates nothing on the UI thread. The Running ring is not in it: its spin sets RotateTransform.Angle, which Avalonia answers with a change record (80 B a frame; CZ-3 file, outside T44)
    [Trait("Spec", "CZ-T44")]
    public void A_warm_page_frame_allocates_nothing_on_the_ui_thread() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(quiet: true);
        rig.Advance(3000, settle: false); // warm: every cache is made
        long bytes = 0;
        var measure = () =>
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            rig.Quiet!.Advance(TimeSpan.FromMilliseconds(1000.0 / 60));
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
        };
        for (var frame = 0; frame < 120; frame++)
        {
            rig.Main.AdvanceMilliseconds(1000.0 / 60);
            rig.Main.OnMain(measure);
        }
        Assert.True(rig.Quiet!.Pending > 0, "something is still animating");
        Assert.True(bytes == 0, $"a page frame allocates {bytes / 120.0:F1} B on the UI thread");
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T44: a style glyph's frame takes the drawing operation back from Avalonia instead of making one
    [Trait("Spec", "CZ-T44")]
    public void A_style_glyphs_frame_makes_no_new_drawing_operation() => headless.OnUiThread(() =>
    {
        var glyph = new CzStyleGlyph("beam") { Width = Width };
        glyph.Measure(new Avalonia.Size(Width, 56));
        glyph.Arrange(new Avalonia.Rect(0, 0, Width, 56));
        var first = glyph.NextOperation();
        first.Dispose(); // Avalonia has finished with it
        Assert.Same(first, glyph.NextOperation());
        var second = glyph.NextOperation(); // the first is still in flight
        Assert.NotSame(first, second);
        second.Dispose();
        first.Dispose();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 120; i++) glyph.NextOperation().Dispose();
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before == 0, "a glyph's operation is made again every frame");
    });

    [Theory]
    [InlineData("beam")]
    [InlineData("aura")]
    [InlineData("off")]
    [Trait("Decision", "ED-251")] // CZ-T44: drawing a glyph (the render thread's share) allocates nothing once warm
    [Trait("Spec", "CZ-T44")]
    public void Drawing_a_style_glyph_allocates_nothing(string style)
    {
        var painter = new CzStyleGlyphPainter();
        using var bitmap = new SKBitmap(new SKImageInfo((int)Width, 56, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        for (var i = 0; i < 20; i++) painter.Draw(canvas, Width, style, i * 7, dark: true, 1); // warm
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 120; i++) painter.Draw(canvas, Width, style, i * 3.1, dark: i % 2 == 0, 0.9f);
        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 120.0;
        Assert.True(perFrame == 0, $"drawing a {style} glyph allocates {perFrame:F0} B a frame");
    }

    [Theory]
    [InlineData("beam", true)]
    [InlineData("beam", false)]
    [InlineData("aura", true)]
    [InlineData("aura", false)]
    [InlineData("off", true)]
    [Trait("Decision", "ED-251")] // CZ-T44: the allocation-free drawing gives the picture CZ-1 first drew, at every turn of the sweep
    public void The_glyph_still_draws_what_it_drew(string style, bool dark)
    {
        var painter = new CzStyleGlyphPainter();
        long differing = 0, total = 0;
        double sum = 0;
        foreach (var (angle, opacity) in new[] { (0.0, 1f), (33.3, 1f), (90.0, 1f), (147.5, 1f), (180.0, 1f), (270.0, 1f), (359.0, 1f), (33.3, 0.5f), (200.0, 0.3f) }) // the card is drawn under a parent's opacity too
        {
            var expected = Pixels(c => GlyphReference.Draw(c, Width, style, angle, dark, opacity));
            var actual = Pixels(c => painter.Draw(c, Width, style, angle, dark, opacity));
            for (var i = 0; i < expected.Length; i++)
            {
                var delta = Math.Abs(expected[i] - actual[i]);
                sum += delta;
                if (delta > 8) differing++;
                total++;
            }
        }
        Assert.True(sum / total < 0.5, $"{style}: the mean difference is {sum / total:F3} levels");
        Assert.True(differing / (double)total < 0.005, $"{style}: {differing / (double)total:P2} of the values differ by more than 8 levels");
    }
}
