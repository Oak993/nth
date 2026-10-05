// PORT-ONLY, owner-ruled addition (OD-0097, OD-0096; first-release voice typing, VT-9, voice-SPEC V-13.5): one representative frame of the glow, for the GPU warm-up.
using Marrow.Core.Port.Voice.Glow;
using SkiaSharp;

namespace Marrow.UI.Port.Voice.Glow;

/// <summary>
/// What a warm-up draws for the glow (GR-1's registry, ED-252): the glow's real frames through the real painter, into an offscreen surface made by the window's GPU context, so
/// its three programs (the lobes, the ellipse gradients, the layer's mask and displacement with Skia's noise) are made, and the blur and colour-filter passes with them, and the
/// painter's half-size surface exists, before the first visible draw (V-13.5: "a 50 ms first frame otherwise"). Both themes are drawn, the light one with its epicentre; the
/// processing frame turns the displacement and the sweep on. Leaves nothing on screen.
/// </summary>
public static class VoiceGlowWarm
{
    private const int Width = 720, Height = 96;

    /// <summary>Draws the glow on <paramref name="gpu"/> (null: the CPU renderer) at the window's <paramref name="scaling"/>.</summary>
    public static void Draw(GRContext? gpu, double scaling)
    {
        var dpr = Math.Max(1, scaling);
        var info = new SKImageInfo((int)Math.Ceiling(Width * dpr), (int)Math.Ceiling(Height * dpr), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = (gpu is not null ? SKSurface.Create(gpu, false, info) : null) ?? SKSurface.Create(info);
        using var painter = new VoiceGlowPainter();
        foreach (var dark in new[] { true, false })
        {
            var look = VoiceGlowLook.From(VoiceGlowComposerProps.For(dark, processing: false, reduceMotion: false));
            var driver = new VoiceGlowDriver(look.Config);
            VoiceGlowFrame frame = driver.Frame;
            for (var i = 0; i < 30; i++) frame = driver.Step(1.0 / 60, 0.6, Width, Height);
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.Save();
            surface.Canvas.Scale((float)dpr);
            painter.Draw(surface.Canvas, gpu, look, frame, Width, Height, dpr, 1);
            surface.Canvas.Restore();
        }
        surface.Flush();
    }
}
