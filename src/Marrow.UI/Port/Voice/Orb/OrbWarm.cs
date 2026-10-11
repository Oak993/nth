// PORT-ONLY, owner-ruled addition (OD-0091, OD-0096; first-release voice typing, VT-8, voice-SPEC V-11.3): one representative frame of an orb, for the GPU warm-up.
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Orb;
using SkiaSharp;

namespace Marrow.UI.Port.Voice.Orb;

/// <summary>
/// What a warm-up draws for an orb (GR-1's registry, ED-252): the orb's real frame through the real renderer, into a small offscreen surface made by the window's GPU
/// context, so the program is made (and, on ANGLE, its binary kept by the cache) and the supersampling surface exists before the first visible draw. Leaves nothing on screen.
/// </summary>
public static class OrbWarm
{
    /// <summary>Draws one frame of <paramref name="kind"/> at the window's <paramref name="scaling"/> on <paramref name="gpu"/> (null: the CPU renderer).</summary>
    public static void Draw(GRContext? gpu, double scaling, VoiceOrbKind kind)
    {
        var dpr = Math.Max(1, scaling);
        var runtime = new VoiceOrbRuntime(kind, new VoiceOrbRuntimeOptions(OrbScene.ViewPx, 32, dpr, Dark: true, SupersampleBelow: OrbScene.SupersampleBelow));
        runtime.Frame(0, new OrbFrameState(Act: 1, Bands: new OrbBands(0.5, 0.5, 0.5, 0.5), Speed: 1, Scale: 1.16, Glow: 0.45, Lift: 1));
        using var renderer = new OrbRenderer();
        var info = new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = (gpu is not null ? SKSurface.Create(gpu, false, info) : null) ?? SKSurface.Create(info);
        renderer.Draw(surface.Canvas, gpu, runtime.Values, runtime.Mrw, runtime.DevicePx, runtime.RenderPx, 16);
        surface.Flush();
    }
}
