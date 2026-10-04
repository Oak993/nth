using Marrow.Core.Port.Voice.Glow;
using Marrow.UI.Port.Voice.Glow;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Voice;

/// <summary>VT-9: renders the glow the way ref-glow.js frames its references: the driver stepped through the state's script at 1/60 s, the painter on the composer card's colour.</summary>
internal static class VoiceGlowRig
{
    public static readonly SKColor DarkCard = new(0x1c, 0x1c, 0x1c);
    public static readonly SKColor LightCard = new(0xf4, 0xf4, 0xf4);

    /// <summary>The look and the driver's frame after a job's script (V-13.3's configuration; the processing flag switches the driver's config between segments).</summary>
    public static (VoiceGlowLook Look, VoiceGlowFrame Frame) Replay(VoiceGlowJob job)
    {
        var look = VoiceGlowLook.From(VoiceGlowComposerProps.For(job.Dark, processing: false, reduceMotion: false));
        var driver = new VoiceGlowDriver(look.Config);
        VoiceGlowFrame frame = driver.Frame;
        foreach (var segment in job.Script)
        {
            driver.Configure(VoiceGlowResolver.Resolve(VoiceGlowComposerProps.For(job.Dark, segment.Processing, reduceMotion: false)).Config);
            for (var i = 0; i < segment.Frames; i++) frame = driver.Step(1.0 / 60, segment.Level, job.Width, job.Height);
        }
        return (look, frame);
    }

    /// <summary>The job's frame, <c>Width</c> x <c>Height</c> canvas units, at device ratio <paramref name="dpr"/> (the bitmap is rounded up to the device pixels).</summary>
    public static SKBitmap Render(VoiceGlowJob job, double dpr = 1, double z = VoiceGlowPainter.HalfSize, VoiceGlowPainter? painter = null, SKColor? background = null)
    {
        var (look, frame) = Replay(job);
        return Render(look, frame, job.Width, job.Height, dpr, z, painter, background ?? (job.Dark ? DarkCard : LightCard));
    }

    public static SKBitmap Render(VoiceGlowLook look, VoiceGlowFrame frame, double width, double height, double dpr, double z, VoiceGlowPainter? painter, SKColor background, double opacity = 1)
    {
        var info = new SKImageInfo((int)Math.Ceiling(width * dpr), (int)Math.Ceiling(height * dpr), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(background);
        surface.Canvas.Scale((float)dpr);
        var own = painter is null;
        var p = painter ?? new VoiceGlowPainter();
        try { p.Draw(surface.Canvas, null, look, frame, width, height, dpr, opacity, z); }
        finally { if (own) p.Dispose(); }
        var bitmap = new SKBitmap(info);
        surface.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0);
        return bitmap;
    }

    /// <summary>The share of pixels whose every channel differs by at most 8 and 16, the mean over pixels of the largest channel difference, and the largest difference.</summary>
    public static (double Within8, double Within16, double Mean, int Max) Compare(SKBitmap a, SKBitmap b)
    {
        int w8 = 0, w16 = 0, max = 0;
        long sum = 0;
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
            {
                SKColor p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                var d = Math.Max(Math.Abs(p.Red - q.Red), Math.Max(Math.Abs(p.Green - q.Green), Math.Abs(p.Blue - q.Blue)));
                max = Math.Max(max, d);
                sum += d;
                if (d <= 8) w8++;
                if (d <= 16) w16++;
            }
        var n = (double)(a.Width * a.Height);
        return (w8 / n, w16 / n, sum / n, max);
    }

    /// <summary>Folder of the reference PNGs: MARROW_REF_GLOW_DIR, else tests/fixtures/voice/ref-glow found from the build folder up.</summary>
    public static string ReferenceFolder()
    {
        var env = Environment.GetEnvironmentVariable("MARROW_REF_GLOW_DIR");
        if (!string.IsNullOrEmpty(env)) return env;
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "tests", "fixtures", "voice", "ref-glow");
            if (Directory.Exists(candidate)) return candidate;
            if (Directory.Exists(Path.Combine(folder.FullName, "tests", "fixtures", "voice"))) return candidate;
        }
        return Path.Combine(AppContext.BaseDirectory, "ref-glow");
    }
}
