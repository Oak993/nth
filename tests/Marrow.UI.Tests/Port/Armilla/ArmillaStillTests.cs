using Marrow.UI.Port.Armilla;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Armilla;

// Dextra AS-9 (DESIGN.md rev 5, 2.5 size ladder "Notifications: the needs-you toast", "Never clipped"; the canvas's 48 px notification;
// ED-NEW-dextra-as9-7): the picture a Windows toast shows for Armilla. It is the live mark's own engine and Skia drawing snapped to the
// state's still pose: a square PNG of the size asked, transparent round the mark with its outermost pixels clear, warm for Needs you, and
// its ink follows the theme (light on dark, dark on light).
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-7")]
public sealed class ArmillaStillTests
{
    private static SKBitmap Decode(bool dark, int pixels = 96) => SKBitmap.Decode(ArmillaStill.Png(ArmillaStill.NeedsYouLook(dark), pixels));

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    public void The_needs_you_still_is_a_square_png_of_the_size_asked_with_the_mark_drawn_and_the_outermost_pixels_clear(int pixels)
    {
        foreach (var dark in new[] { true, false })
        {
            using var bitmap = Decode(dark, pixels);
            Assert.Equal(pixels, bitmap.Width);
            Assert.Equal(pixels, bitmap.Height);
            var drawn = 0;
            for (var y = 0; y < pixels; y++)
            for (var x = 0; x < pixels; x++)
            {
                var alpha = bitmap.GetPixel(x, y).Alpha;
                if (alpha > 200) drawn++;
                if (x == 0 || y == 0 || x == pixels - 1 || y == pixels - 1) Assert.Equal(0, alpha);
            }
            Assert.True(drawn > pixels, $"the mark was not drawn ({drawn} solid pixels, dark {dark})");
        }
    }

    [Fact]
    public void Needs_you_is_warm_and_the_rest_of_the_figure_takes_the_ink_of_the_theme()
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
                if (pixel.Alpha < 120) continue;                  // the long arc is drawn at a fraction of full strength
                if (pixel.Red - pixel.Blue >= 60) { warm++; continue; }
                ink += (pixel.Red + pixel.Green + pixel.Blue) / 3.0;
                inkPixels++;
            }
            Assert.True(warm > 10, $"no amber attention arc (dark {dark})");
            Assert.True(inkPixels > 10, $"no ink (dark {dark})");
            var average = ink / inkPixels;
            if (dark) Assert.True(average > 150, $"the ink on dark is {average:F0}, not light");
            else Assert.True(average < 100, $"the ink on light is {average:F0}, not dark");
        }
    }

    [Fact]
    public void The_same_look_gives_the_same_bytes_so_an_installed_picture_is_written_once()
    {
        Assert.Equal(ArmillaStill.Png(ArmillaStill.NeedsYouLook(true), 96), ArmillaStill.Png(ArmillaStill.NeedsYouLook(true), 96));
        Assert.NotEqual(ArmillaStill.Png(ArmillaStill.NeedsYouLook(true), 96), ArmillaStill.Png(ArmillaStill.NeedsYouLook(false), 96));
    }

    [Fact]
    public void A_picture_too_small_to_hold_the_mark_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ArmillaStill.Png(ArmillaStill.NeedsYouLook(true), 4));
        Assert.Throws<ArgumentNullException>(() => ArmillaStill.Png(null!, 96));
    }

    [Fact]
    public void The_look_is_the_notification_size_of_the_ladder_and_does_not_move()
    {
        var look = ArmillaStill.NeedsYouLook(true);
        Assert.Equal(48, look.Size);
        Assert.Equal(ArmillaState.NeedsYou, look.State);
        Assert.Empty(look.Nodes);
        Assert.True(look.Reduce);
    }
}
