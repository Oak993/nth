using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// What the track's canvas hands the liquid for its bubbles (EffortSlider.swift:1010-1016 "Whether the fill under the knob is light";
// DEVIATION OD-0075 and OD-0073, lead ruling R1, ED-286 (6): the test follows the fill the port draws). The canvas of a
// plain track draws its liquid through LiquidTrack.Draw; the test renders that canvas and the same liquid drawn directly with
// dark-rimmed bubbles and with light-rimmed ones (the same effect's bubbles, its gradient and its flow), and reads the pixels where the
// two rims differ: the canvas must have drawn the rim the fill's paleness asks for, so a canvas that stopped using
// TrackEffect.DarkBubbles (always light, or always dark) is read as the wrong rim.
[Collection("Headless")]
public sealed class EffortSliderBubbleRimTests(HeadlessSession headless)
{
    private const int Width = 240, Height = 34;

    private static TrackEffect Mounted(TrackLook look)
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var effect = new TrackEffect(look) { TrackWidth = Width };
        effect.Measure(new Size(Width, Height)); effect.Arrange(new Rect(0, 0, Width, Height));
        return effect;
    }

    private static SKBitmap Decode(RenderTargetBitmap bitmap)
    {
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    // the canvas as the track draws it, its clock at <seconds>
    private static SKBitmap Canvas(TrackEffect effect, double seconds)
    {
        typeof(TrackEffect).GetField("time", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(effect, seconds);
        using var bitmap = new RenderTargetBitmap(new PixelSize(Width, Height));
        using (var context = bitmap.CreateDrawingContext()) effect.Child!.Render(context);
        return Decode(bitmap);
    }

    // the same liquid and bubbles drawn directly, the rims dark or light as asked
    private static SKBitmap Direct(TrackEffect effect, double seconds, bool dark)
    {
        // SPEC 7/C-05: v22/simmer lanes with the neutral rim at alpha*ink; independent retained state, exact pixels.
        return MixTrackFixtures.Draw(effect.Look,effect.StrikeGeometryFor(new Size(Width,Height)),effect.StrikeSeed,effect.Bubbles(Width,Height).Seed,seconds,effect.StrikeOffset,inkAmount:dark?1:0);
    }

    private static bool Same(SKColor a, SKColor b) => a == b;

    // how many moments were read where the two rims differ: (differing pixels at a moment, read against the canvas)
    private static int Read(TrackLook look, bool expectDark)
    {
        var effect = Mounted(look);
        var moments = 0;
        for (var seconds = 0.9; seconds < 14; seconds += 0.7)
        {
            using var canvas = Canvas(effect, seconds);
            using var dark = Direct(effect, seconds, true);
            using var light = Direct(effect, seconds, false);
            var differing = new List<(int X, int Y)>();
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                    if (!Same(dark.GetPixel(x, y), light.GetPixel(x, y))) differing.Add((x, y));
            if (differing.Count == 0) continue; // no bubble risen at this moment
            moments++;
            var (want, other) = expectDark ? (dark, light) : (light, dark);
            foreach (var (x, y) in differing)
            {
                Assert.True(Same(want.GetPixel(x, y), canvas.GetPixel(x, y)),
                    $"{look.Brand} at {seconds:F1} s, pixel ({x}, {y}): the canvas drew {canvas.GetPixel(x, y)} where the {(expectDark ? "dark" : "light")}-rimmed bubble is {want.GetPixel(x, y)} and the {(expectDark ? "light" : "dark")}-rimmed one {other.GetPixel(x, y)}");
            }
        }
        return moments;
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-131")] // EffortSlider.swift:1010-1016 isLightFill and stopColor, as amended by OD-0075 and OD-0073 (lead ruling R1): the bubbles' rim follows the light-fill test (a white liquid's dark rim)
    public void A_silver_plain_track_draws_its_bubbles_with_the_dark_rim_on_its_white_liquid() => headless.OnUiThread(() =>
    {
        Assert.True(Read(new TrackLook(TrackLook.Kind.Plain, EffortBrand.Silver), expectDark: true) >= 3, "bubbles were risen at fewer than three of the moments read");
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-131")] // EffortSlider.swift:1010-1016 isLightFill and stopColor, as amended by OD-0075 and OD-0073 (lead ruling R1): the bubbles' rim follows the light-fill test (a coloured liquid's light rim)
    public void A_claude_plain_track_draws_its_bubbles_with_the_light_rim_on_its_coloured_liquid() => headless.OnUiThread(() =>
    {
        Assert.True(Read(new TrackLook(TrackLook.Kind.Plain, EffortBrand.Claude), expectDark: false) >= 3, "bubbles were risen at fewer than three of the moments read");
    });
}
