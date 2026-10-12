using System.Diagnostics;
using System.Reflection;
using Avalonia.OpenGL;
using Avalonia.Platform;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Orb;
using Marrow.UI.Port.Voice.Orb;
using SkiaSharp;
using Xunit;

namespace Marrow.App.Tests.Port.Voice;

/// <summary>
/// ED-NEW-voicefix-ui-7: the view runs the orb shader into a 2x surface at every scale (the design stops at 128 device pixels). This bounds what that costs at 200% and
/// 300% (the design's dpr cap), both orbs, Listening at full level: on the window's GPU path (ANGLE on Direct3D 11, the context the app's windows use, made here with no
/// window) a frame, drawn, flushed and waited for, stays a small part of a 60 Hz frame; on the software rasterizer, where the orb is one still (V-11.3), that still is
/// drawn well inside a quarter second. Measured on this PC (RTX 4080): GPU 0.08 to 0.11 ms median, under 0.5 ms at the 95th percentile; CPU 33 to 94 ms.
/// </summary>
[Trait("Decision", "ED-NEW-reltrace-1")] // ED-NEW-voicefix-ui-7
[Trait("Spec", "V-T51")]
public sealed class OrbFrameCostTests
{
    private static readonly OrbFrameState Loud = new(Act: 1, Bands: new OrbBands(0.95, 0.9, 0.8, 0.9), Speed: 1, Scale: 1, Glow: 1);

    private static VoiceOrbRuntime Runtime(VoiceOrbKind kind, double dpr) =>
        new(kind, new VoiceOrbRuntimeOptions(OrbScene.ViewPx, 32, dpr, true, SupersampleBelow: OrbScene.SupersampleBelow));

    private static double[] Times(VoiceOrbRuntime runtime, GRContext? gpu, SKSurface surface, int warm, int n)
    {
        using var renderer = new OrbRenderer();
        var t = 0.0;
        var times = new double[n];
        var clock = new Stopwatch();
        for (var i = -warm; i < n; i++)
        {
            clock.Restart();
            t += 1 / 60.0;
            runtime.Frame(t, Loud);
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(28, 28, 28));
            canvas.Save();
            canvas.Scale((float)(runtime.DevicePx / OrbScene.ViewPx));
            renderer.Draw(canvas, gpu, runtime.Values, runtime.Mrw, runtime.DevicePx, runtime.RenderPx, (float)OrbScene.ViewPx);
            canvas.Restore();
            surface.Flush();
            gpu?.Submit(true);
            if (i >= 0) times[i] = clock.Elapsed.TotalMilliseconds;
        }
        Array.Sort(times);
        return times;
    }

    [Theory]
    [InlineData(VoiceOrbKind.Siri, 2.0)]
    [InlineData(VoiceOrbKind.Membrane, 2.0)]
    [InlineData(VoiceOrbKind.Siri, 3.0)]
    [InlineData(VoiceOrbKind.Membrane, 3.0)]
    public void A_supersampled_frame_on_the_gpu_is_a_small_part_of_a_60_hz_frame(VoiceOrbKind kind, double dpr)
    {
        var factory = typeof(Avalonia.Win32.AngleOptions).Assembly.GetType("Avalonia.Win32.OpenGl.Angle.AngleWin32PlatformGraphicsFactory")
            ?.GetMethod("TryCreate", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Avalonia.Win32's ANGLE factory changed: the GPU path cannot be measured");
        var graphics = factory.Invoke(null, [new Avalonia.Win32.AngleOptions()]) as IPlatformGraphics
            ?? throw new InvalidOperationException("no ANGLE Direct3D 11 device on this machine: the GPU path cannot be measured");
        using var context = (IGlContext)graphics.CreateContext();
        using (context.MakeCurrent())
        {
            using var gl = GRGlInterface.CreateGles(name => context.GlInterface.GetProcAddress(name));
            using var gpu = GRContext.CreateGl(gl);
            var runtime = Runtime(kind, dpr);
            Assert.Equal(2, runtime.RenderScale);
            using var surface = SKSurface.Create(gpu, false, new SKImageInfo(runtime.DevicePx, runtime.DevicePx, SKColorType.Rgba8888, SKAlphaType.Premul));
            var times = Times(runtime, gpu, surface, 30, 240);
            var median = times[times.Length / 2];
            var p95 = times[(int)(times.Length * .95)];
            Assert.True(median <= 2 && p95 <= 8, $"{kind} at {dpr}: {median:F3} ms median, {p95:F3} ms at the 95th percentile, against a 16.7 ms frame");
        }
    }

    [Theory]
    [InlineData(VoiceOrbKind.Siri, 3.0)]
    [InlineData(VoiceOrbKind.Membrane, 3.0)]
    public void On_the_software_rasterizer_the_supersampled_still_is_drawn_inside_a_quarter_second(VoiceOrbKind kind, double dpr)
    {
        var runtime = Runtime(kind, dpr);
        Assert.Equal(2, runtime.RenderScale);
        using var surface = SKSurface.Create(new SKImageInfo(runtime.DevicePx, runtime.DevicePx, SKColorType.Rgba8888, SKAlphaType.Premul));
        var fastest = Times(runtime, null, surface, 1, 5)[0];
        Assert.True(fastest <= 250, $"{kind} at {dpr}: the still took {fastest:F1} ms");
    }
}
