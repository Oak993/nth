using Marrow.Core.Port.Voice.Orb;
using Marrow.UI.Port.Voice.Orb;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Voice;

/// <summary>VT-8: renders an orb the way the view does (the runtime's block, the renderer's 2x surface) onto the composer card's colour, at a device ratio.</summary>
internal static class OrbRig
{
    public static readonly SKColor DarkCard = new(0x1c, 0x1c, 0x1c);
    public static readonly SKColor LightCard = new(0xf4, 0xf4, 0xf4);

    public static VoiceOrbRuntime Runtime(OrbJob job, double dpr = 1) =>
        new(job.Kind, new VoiceOrbRuntimeOptions(OrbScene.ViewPx, job.Ball, dpr, job.Dark, Phase: job.Phase, SupersampleBelow: OrbScene.SupersampleBelow));

    /// <summary>The job's frame, 72 view px square, at device ratio <paramref name="dpr"/> (the bitmap is round(72 * dpr) px).</summary>
    public static SKBitmap Render(OrbJob job, double dpr = 1, OrbRenderer? renderer = null, SKColor? background = null)
    {
        var runtime = Runtime(job, dpr);
        runtime.Frame(job.T, job.FrameState);
        return Render(runtime, dpr, renderer, background ?? (job.Dark ? DarkCard : LightCard));
    }

    public static SKBitmap Render(VoiceOrbRuntime runtime, double dpr, OrbRenderer? renderer, SKColor background)
    {
        var px = runtime.DevicePx;
        var info = new SKImageInfo(px, px, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(background);
        surface.Canvas.Scale((float)(px / OrbScene.ViewPx));
        var own = renderer is null;
        var r = renderer ?? new OrbRenderer();
        try { r.Draw(surface.Canvas, null, runtime.Values, runtime.Mrw, runtime.DevicePx, runtime.RenderPx, (float)OrbScene.ViewPx); }
        finally { if (own) r.Dispose(); }
        var bitmap = new SKBitmap(info);
        surface.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0);
        return bitmap;
    }

    /// <summary>The share (0 to 1) of pixels whose every channel differs by at most <paramref name="tolerance"/>; also the largest difference.</summary>
    public static (double Within, int Max) Compare(SKBitmap a, SKBitmap b, int tolerance)
    {
        var within = 0; var max = 0;
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
            {
                SKColor p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                var d = Math.Max(Math.Abs(p.Red - q.Red), Math.Max(Math.Abs(p.Green - q.Green), Math.Abs(p.Blue - q.Blue)));
                max = Math.Max(max, d);
                if (d <= tolerance) within++;
            }
        return (within / (double)(a.Width * a.Height), max);
    }

    /// <summary>Folder of the reference PNGs: MARROW_REF_ORBS_DIR, else tests/fixtures/voice/ref-orbs found from the build folder up.</summary>
    public static string ReferenceFolder()
    {
        var env = Environment.GetEnvironmentVariable("MARROW_REF_ORBS_DIR");
        if (!string.IsNullOrEmpty(env)) return env;
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "tests", "fixtures", "voice", "ref-orbs");
            if (Directory.Exists(candidate)) return candidate;
            if (Directory.Exists(Path.Combine(folder.FullName, "tests", "fixtures", "voice"))) return candidate;
        }
        return Path.Combine(AppContext.BaseDirectory, "ref-orbs");
    }
}
