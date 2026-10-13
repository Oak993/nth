using Marrow.UI.Port.Armilla;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Armilla;

// Dextra AS-9 (DESIGN.md rev 5, 2.5 size ladder "Notifications", "Reduce motion", "Never clipped"; 13.1 "Duty opt-in"; ED-NEW-dextra-as9-7, -9): the picture of a
// toast that is news and asks nothing, a "Notify me" duty's finding. It is the resting mark, not the needs-you mark: the same engine and drawing snapped to the
// state's still pose, a square PNG with the outermost pixels clear, with none of the warm attention arc, its ink following the theme.
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-9")]
public sealed class ArmillaNewsStillTests
{
    private static SKBitmap Decode(bool dark, int pixels = 96) => SKBitmap.Decode(ArmillaStill.Png(ArmillaStill.NewsLook(dark), pixels));

    [Fact]
    public void The_look_is_the_resting_mark_at_the_notification_size_and_does_not_move()
    {
        var look = ArmillaStill.NewsLook(true);
        Assert.Equal(48, look.Size);
        Assert.Equal(ArmillaState.Idle, look.State);
        Assert.Empty(look.Nodes);
        Assert.True(look.Reduce);
        Assert.True(look.Dark);
        Assert.False(ArmillaStill.NewsLook(false).Dark);
    }

    [Fact]
    public void The_picture_is_drawn_square_with_the_outermost_pixels_clear_in_both_themes()
    {
        foreach (var dark in new[] { true, false })
        {
            using var bitmap = Decode(dark);
            Assert.Equal((96, 96), (bitmap.Width, bitmap.Height));
            var drawn = 0;
            for (var y = 0; y < 96; y++)
            for (var x = 0; x < 96; x++)
            {
                var alpha = bitmap.GetPixel(x, y).Alpha;
                if (alpha > 200) drawn++;
                if (x == 0 || y == 0 || x == 95 || y == 95) Assert.Equal(0, alpha);
            }
            Assert.True(drawn > 96, $"the mark was not drawn ({drawn} solid pixels, dark {dark})");
        }
    }

    [Fact]
    public void It_has_no_warm_attention_arc_because_it_asks_nothing_and_its_ink_follows_the_theme()
    {
        foreach (var dark in new[] { true, false })
        {
            using var bitmap = Decode(dark);
            var warm = 0;
            double ink = 0, inkPixels = 0;
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Alpha < 120) continue;
                if (pixel.Red - pixel.Blue >= 60) { warm++; continue; }
                ink += (pixel.Red + pixel.Green + pixel.Blue) / 3.0;
                inkPixels++;
            }
            Assert.Equal(0, warm);
            Assert.True(inkPixels > 10, $"no ink (dark {dark})");
            var average = ink / inkPixels;
            if (dark) Assert.True(average > 150, $"the ink on dark is {average:F0}, not light");
            else Assert.True(average < 100, $"the ink on light is {average:F0}, not dark");
        }
    }

    [Fact]
    public void It_is_a_different_picture_from_the_needs_you_mark_and_the_same_look_gives_the_same_bytes()
    {
        Assert.NotEqual(ArmillaStill.Png(ArmillaStill.NewsLook(true), 96), ArmillaStill.Png(ArmillaStill.NeedsYouLook(true), 96));
        Assert.Equal(ArmillaStill.Png(ArmillaStill.NewsLook(true), 96), ArmillaStill.Png(ArmillaStill.NewsLook(true), 96));
        Assert.NotEqual(ArmillaStill.Png(ArmillaStill.NewsLook(true), 96), ArmillaStill.Png(ArmillaStill.NewsLook(false), 96));
    }
}
