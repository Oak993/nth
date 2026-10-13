using Marrow.App.Tray;
using SkiaSharp;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-6 (DESIGN.md rev 5, 2.5 "The tray icon": one colour, pre-rendered still frames at 16, 20, 24 and 32 physical px;
// ED-NEW-dextra-as6-10): the icon file holds a PNG frame per size, each state draws its own pose in the one colour, and nothing
// is drawn on the box's edge, so the mark is never clipped.
[Trait("Decision", "ED-NEW-dextra-as6-10")]
[Trait("Feature", "AS-6")]
public sealed class TrayGlyphTests
{
    private static readonly SKColor Ink = new(0xEC, 0xEC, 0xEE);

    private static SKBitmap Frame(TrayState state, int size) => SKBitmap.Decode(TrayGlyph.Png(state, size, Ink));

    private static byte[] Pixels(SKBitmap bitmap) => bitmap.Bytes;

    [Fact]
    public void The_icon_file_holds_one_png_frame_for_each_size_from_100_to_200_percent()
    {
        var ico = TrayGlyph.Ico(TrayState.Ready, Ink);
        Assert.Equal([0, 0, 1, 0, 4, 0], ico[..6]);
        var sizes = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            var entry = 6 + 16 * i;
            sizes.Add(ico[entry]);
            Assert.Equal(ico[entry], ico[entry + 1]);
            var length = BitConverter.ToInt32(ico, entry + 8);
            var offset = BitConverter.ToInt32(ico, entry + 12);
            Assert.Equal([0x89, 0x50, 0x4E, 0x47], ico[offset..(offset + 4)]); // a PNG frame
            using var decoded = SKBitmap.Decode(ico[offset..(offset + length)]);
            Assert.Equal((ico[entry], ico[entry]), (decoded.Width, decoded.Height));
        }
        Assert.Equal([16, 20, 24, 32], sizes);
        Assert.Equal(TrayGlyph.Sizes, sizes);
    }

    [Theory]
    [InlineData(TrayState.Ready, 16)]
    [InlineData(TrayState.Working, 20)]
    [InlineData(TrayState.Paused, 24)]
    [InlineData(TrayState.NeedsYou, 32)]
    public void Nothing_is_drawn_on_the_edge_of_the_box_and_something_is_drawn_inside(TrayState state, int size)
    {
        using var bitmap = Frame(state, size);
        var inside = 0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var alpha = bitmap.GetPixel(x, y).Alpha;
                if (x == 0 || y == 0 || x == size - 1 || y == size - 1) Assert.Equal(0, alpha);
                else if (alpha > 0) inside++;
            }
        }
        Assert.True(inside > size, $"{state} at {size} px drew almost nothing");
    }

    [Fact]
    public void Each_state_draws_its_own_pose_so_no_two_look_alike_at_any_size()
    {
        foreach (var size in TrayGlyph.Sizes)
        {
            var frames = new[] { TrayState.Ready, TrayState.Working, TrayState.Paused, TrayState.NeedsYou }
                .Select(s => Convert.ToHexString(Pixels(Frame(s, size)))).ToList();
            Assert.Equal(frames.Count, frames.Distinct().Count());
        }
    }

    [Fact]
    public void The_marks_are_one_colour_and_only_Needs_you_turns_warm()
    {
        foreach (var state in new[] { TrayState.Ready, TrayState.Working, TrayState.Paused })
        {
            using var bitmap = Frame(state, 32);
            for (var y = 0; y < 32; y++)
            {
                for (var x = 0; x < 32; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.Alpha < 200) continue;
                    // Pixels that are nearly covered carry the ink (premultiplied storage rounds by a unit or two).
                    Assert.InRange(pixel.Red, Ink.Red - 3, Ink.Red + 3);
                    Assert.InRange(pixel.Green, Ink.Green - 3, Ink.Green + 3);
                    Assert.InRange(pixel.Blue, Ink.Blue - 3, Ink.Blue + 3);
                }
            }
        }
        using var warm = Frame(TrayState.NeedsYou, 32);
        var warmPixels = Enumerable.Range(0, 32 * 32).Select(i => warm.GetPixel(i % 32, i / 32)).Where(p => p.Alpha >= 200).ToList();
        Assert.NotEmpty(warmPixels);
        Assert.All(warmPixels, p => Assert.True(p.Red > p.Blue + 40, "Needs you is amber"));
    }

    [Fact]
    public void Working_below_28_px_is_one_still_ring_with_a_small_opening_at_the_left_and_Paused_is_dimmed()
    {
        using var working = Frame(TrayState.Working, 24);
        // The opening faces left (9 o'clock): the ring is there at the right and at the top, and empty at the left.
        var centre = 12;
        var ring = (int)Math.Round(12 * 0.72);
        Assert.Equal(0, working.GetPixel(centre - ring, centre).Alpha);
        Assert.Equal(0, working.GetPixel(centre - ring + 1, centre).Alpha);
        Assert.True(working.GetPixel(centre + ring - 1, centre).Alpha > 200);
        Assert.True(working.GetPixel(centre, centre - ring).Alpha > 200);
        // Paused is the same figure dimmed to about half strength; Ready reaches full strength.
        using var paused = Frame(TrayState.Paused, 24);
        using var ready = Frame(TrayState.Ready, 24);
        Assert.InRange(MaxAlpha(paused), 100, 140);
        Assert.Equal(255, MaxAlpha(ready));
        Assert.NotEqual(Convert.ToHexString(Pixels(ready)), Convert.ToHexString(Pixels(paused)));
    }

    private static int MaxAlpha(SKBitmap bitmap) => Enumerable.Range(0, bitmap.Width * bitmap.Height).Max(i => bitmap.GetPixel(i % bitmap.Width, i / bitmap.Width).Alpha);
}
