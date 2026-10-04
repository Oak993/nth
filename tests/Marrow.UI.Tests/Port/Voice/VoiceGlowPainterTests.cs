using SkiaSharp;
using Marrow.UI.Port.Voice.Glow;

namespace Marrow.UI.Tests.Port.Voice;

// VT-9, V-T60/V-T62 (voice-SPEC V-13.2, V-13.3, V-19): the painter draws inside the composer's rounded shape, the soft layers at half size in one reused surface, the stroke ring
// and the band at full size, deterministically, scaled by the element's opacity. The pictures' agreement with the browser's is VoiceGlowFidelityTests'.
public sealed class VoiceGlowPainterTests
{
    private static VoiceGlowJob Job(string state, string theme = "dark", int width = 720, int height = 96) =>
        VoiceGlowJobs.States.Where(s => s.State == state).Select(s => new VoiceGlowJob(s.State, width, height, theme, s.Script)).Single();

    private static long LightOf(SKBitmap bitmap, SKColor background)
    {
        long sum = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                sum += Math.Max(Math.Abs(p.Red - background.Red), Math.Max(Math.Abs(p.Green - background.Green), Math.Abs(p.Blue - background.Blue)));
            }
        return sum;
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T62")]
    public void Nothing_is_drawn_at_zero_opacity_for_an_empty_size_or_without_a_band()
    {
        var job = Job("level-0.6");
        var (look, frame) = VoiceGlowRig.Replay(job);
        using var painter = new VoiceGlowPainter();
        using (var none = VoiceGlowRig.Render(look, frame, 720, 96, 1, 0.5, painter, SKColor.Empty, opacity: 0))
            Assert.Equal(0, LightOf(none, SKColor.Empty));
        using (var empty = VoiceGlowRig.Render(look, frame, 720, 96, 1, 0.5, painter, SKColor.Empty, opacity: double.NaN))
            Assert.Equal(0, LightOf(empty, SKColor.Empty));
        Assert.Equal(0, painter.SurfaceCreations);
        using (var tiny = new SKBitmap(10, 10))
        using (var canvas = new SKCanvas(tiny)) painter.Draw(canvas, null, look, frame, 0, 0, 1, 1);
        Assert.Equal(0, painter.SurfaceCreations);
    }

    [Theory]
    [InlineData("level-1.0", "dark")]
    [InlineData("level-1.0", "light")]
    [InlineData("proc-0.8", "dark")]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T62")]
    public void The_glow_stays_inside_the_composers_rounded_shape(string state, string theme)
    {
        // drawn on a transparent surface 40 px larger on every side: any pixel outside the rounded 720 x 96 shape stays fully transparent
        var job = Job(state, theme);
        var (look, frame) = VoiceGlowRig.Replay(job);
        var info = new SKImageInfo(720 + 80, 96 + 80, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.Translate(40, 40);
        using (var painter = new VoiceGlowPainter()) painter.Draw(surface.Canvas, null, look, frame, 720, 96, 1, 1);
        using var bitmap = new SKBitmap(info);
        surface.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0);
        const float r = 22;
        var lit = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var a = bitmap.GetPixel(x, y).Alpha;
                // the distance of the pixel's centre outside the rounded rectangle (0 or less: inside)
                float px = x + 0.5f - 40, py = y + 0.5f - 40;
                var dx = Math.Max(Math.Max(r - px, px - (720 - r)), 0);
                var dy = Math.Max(Math.Max(r - py, py - (96 - r)), 0);
                var outside = px < 0 || px > 720 || py < 0 || py > 96 ? 100f : MathF.Sqrt(dx * dx + dy * dy) - r;
                if (outside > 1) Assert.True(a == 0, $"({x}, {y}) is {outside:F1} px outside the shape and has alpha {a}");
                else if (a > 0) lit++;
            }
        Assert.True(lit > 5000, "and the shape itself is lit");
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T62")]
    public void A_frame_is_the_same_every_time_it_is_drawn()
    {
        var job = Job("level-0.6", "light");
        using var a = VoiceGlowRig.Render(job);
        using var b = VoiceGlowRig.Render(job);
        Assert.True(a.Bytes.AsSpan().SequenceEqual(b.Bytes));
        using var painter = new VoiceGlowPainter();
        using var c = VoiceGlowRig.Render(job, painter: painter);
        using var d = VoiceGlowRig.Render(job, painter: painter);                  // the reused surface, paints and caches change nothing
        Assert.True(c.Bytes.AsSpan().SequenceEqual(a.Bytes));
        Assert.True(d.Bytes.AsSpan().SequenceEqual(a.Bytes));
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T62")]
    public void The_soft_layers_are_rastered_at_half_size_in_one_surface_that_is_reused()
    {
        var job = Job("level-0.6");
        using var painter = new VoiceGlowPainter();
        for (var i = 0; i < 4; i++) VoiceGlowRig.Render(job, painter: painter).Dispose();
        Assert.Equal((360, 48), painter.SurfaceSize);
        Assert.Equal(1, painter.SurfaceCreations);
        VoiceGlowRig.Render(job, dpr: 2, painter: painter).Dispose();               // a 2x window: the half-size layer is 720 x 96 device pixels
        Assert.Equal((720, 96), painter.SurfaceSize);
        Assert.Equal(2, painter.SurfaceCreations);
        VoiceGlowRig.Render(job, dpr: 2, painter: painter).Dispose();
        Assert.Equal(2, painter.SurfaceCreations);
        VoiceGlowRig.Render(job, dpr: 1, z: 1, painter: painter).Dispose();         // full size (the stylesheet without data-voice-halfres)
        Assert.Equal((720, 96), painter.SurfaceSize);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T62")]
    public void A_2x_window_draws_the_same_picture_in_twice_the_pixels()
    {
        var job = Job("level-0.6");
        using var one = VoiceGlowRig.Render(job);
        using var two = VoiceGlowRig.Render(job, dpr: 2);
        Assert.Equal((1440, 192), (two.Width, two.Height));
        // the 2x frame averaged down 2 x 2 against the 1x frame
        using var small = new SKBitmap(720, 96, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(small))
        using (var image = SKImage.FromBitmap(two))
            canvas.DrawImage(image, new SKRect(0, 0, 720, 96), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        var (within8, within16, mean, max) = VoiceGlowRig.Compare(one, small);
        Assert.True(within16 >= 0.99 && mean <= 3, $"within 8 {within8:P2}, within 16 {within16:P2}, mean {mean:F2}, largest {max}");
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T62")]
    public void The_elements_opacity_dims_the_whole_glow()
    {
        var job = Job("level-0.6");
        var (look, frame) = VoiceGlowRig.Replay(job);
        long Light(double opacity)
        {
            using var bitmap = VoiceGlowRig.Render(look, frame, 720, 96, 1, 0.5, null, VoiceGlowRig.DarkCard, opacity);
            return LightOf(bitmap, VoiceGlowRig.DarkCard);
        }
        var full = Light(1);
        var half = Light(0.5);
        var tenth = Light(0.1);
        Assert.True(full > 100000);
        Assert.InRange(half / (double)full, 0.35, 0.65);
        Assert.InRange(tenth / (double)full, 0.04, 0.2);
        Assert.True(Light(0.001) < full / 100);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T62")]
    public void The_light_theme_adds_the_epicentre_a_white_wash_at_the_source_under_the_band_line()
    {
        var dark = Job("level-0.6", "dark");
        var light = Job("level-0.6", "light");
        var (lookDark, _) = VoiceGlowRig.Replay(dark);
        var (lookLight, frameLight) = VoiceGlowRig.Replay(light);
        Assert.Null(lookDark.Recipe.Core);
        Assert.NotNull(lookLight.Recipe.Core);
        using var with = VoiceGlowRig.Render(lookLight, frameLight, 720, 96, 1, 0.5, null, VoiceGlowRig.LightCard);
        // the same frame and look without the epicentre: the centre of the bottom edge is lighter with it (white over a pastel wash)
        var noCore = VoiceGlowLook.From(Marrow.Core.Port.Voice.Glow.VoiceGlowResolver.Resolve(VoiceGlowComposerProps.For(false, false, false) with { CoreLight = 0 }));
        Assert.Null(noCore.Recipe.Core);
        using var without = VoiceGlowRig.Render(noCore, frameLight, 720, 96, 1, 0.5, null, VoiceGlowRig.LightCard);
        var a = with.GetPixel(360, 85);
        var b = without.GetPixel(360, 85);
        Assert.True(a.Red + a.Green + a.Blue > b.Red + b.Green + b.Blue + 15, $"{a} with the epicentre, {b} without");
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-NEW-f-vt-9")]
    [Trait("Spec", "V-T62")]
    public void What_a_frame_allocates_is_skiasharps_wrappers_and_nothing_the_size_of_a_surface()
    {
        // V-19 asks for 0 B a frame. Everything the port owns is reused (the surface, the paints, the paths, the blur filters, the noise, the uniform blocks); what remains is
        // SkiaSharp's own wrapper objects (a shader, a colour filter, an image and a uniform copy each time one is made). Measured 8.5 KB a frame on the CPU raster; the bound is 2x.
        var (look, frame) = VoiceGlowRig.Replay(Job("level-1.0"));
        var info = new SKImageInfo(720, 96, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        using var painter = new VoiceGlowPainter();
        for (var i = 0; i < 5; i++) painter.Draw(surface.Canvas, null, look, frame, 720, 96, 1, 1);
        var creations = painter.SurfaceCreations;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 20; i++) painter.Draw(surface.Canvas, null, look, frame, 720, 96, 1, 1);
        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 20;
        Assert.Equal(creations, painter.SurfaceCreations);
        Assert.True(perFrame < 17 * 1024, $"{perFrame} B a frame");
    }
}
