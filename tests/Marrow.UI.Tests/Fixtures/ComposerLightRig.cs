// DEVIATION OD-0089 (owner-ruled addition; ED-251): renders a composer light the way the playground's beam-fix/pill.js frames
// its references: the composer (820 x 52, corner 22) on a flat card (#1c1c1c dark, #f4f4f4 light) 40 px inside a
// #171717 / #e8e8e8 field, the light drawn over it, device scale 1 unless asked.
using Marrow.Core.Models.ComposerLights;
using Marrow.UI.Port.ComposerLights;
using SkiaSharp;

namespace Marrow.UI.Tests.Fixtures;

internal static class ComposerLightRig
{
    public const int Margin = 40;

    public static SKBitmap Render(ComposerLightAnimator light, bool dark, float scale = 1, ComposerLightRenderer? renderer = null, double w = 820, double h = 52)
    {
        var info = new SKImageInfo((int)Math.Ceiling((w + 2 * Margin) * scale), (int)Math.Ceiling((h + 2 * Margin) * scale), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Scale(scale);
        canvas.Clear(dark ? new SKColor(0x17, 0x17, 0x17) : new SKColor(0xe8, 0xe8, 0xe8));
        using (var card = new SKPaint { IsAntialias = true, Color = dark ? new SKColor(0x1c, 0x1c, 0x1c) : new SKColor(0xf4, 0xf4, 0xf4) })
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(Margin, Margin, (float)(Margin + w), (float)(Margin + h)), 22), card);
        canvas.Translate(Margin, Margin);
        var own = renderer is null;
        var r = renderer ?? new ComposerLightRenderer();
        try { r.Draw(canvas, null, light, scale); }
        finally { if (own) r.Dispose(); }
        var bitmap = new SKBitmap(info);
        surface.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0);
        return bitmap;
    }

    /// <summary>Writes the frame as PNG evidence when MARROW_COMPOSER_LIGHT_EVIDENCE names a folder.</summary>
    public static void Evidence(SKBitmap bitmap, string name)
    {
        var folder = Environment.GetEnvironmentVariable("MARROW_COMPOSER_LIGHT_EVIDENCE");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), data.ToArray());
    }

    public static ComposerLightAnimator Light(ComposerLightSettings settings, bool dark, ComposerLightPhase phase = ComposerLightPhase.Rest, bool reduce = false) =>
        new(settings, phase, dark, 820, 52, reduce);

    /// <summary>The largest channel difference between two frames over a region (frame pixels).</summary>
    public static int MaxDelta(SKBitmap a, SKBitmap b, int x0, int y0, int x1, int y1)
    {
        var max = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                SKColor p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                max = Math.Max(max, Math.Max(Math.Abs(p.Red - q.Red), Math.Max(Math.Abs(p.Green - q.Green), Math.Abs(p.Blue - q.Blue))));
            }
        return max;
    }
}

/// <summary>One reference frame of the playground (the same job list drives scratch ref.js under node).</summary>
internal sealed record ComposerLightJob(string Name, string Th, string Light, System.Text.Json.JsonElement? Beam, System.Text.Json.JsonElement? Aura,
    double T, double? TH, double? AuraP, double? AuraBp, string? Mode)
{
    public ComposerLightSettings Settings()
    {
        var json = "{\"style\":\"" + Light + "\",\"beam\":" + (Beam?.GetRawText() ?? "{}") + ",\"aura\":" + (Aura?.GetRawText() ?? "{}") + "}";
        return ComposerLightSettings.FromJson(System.Text.Encoding.UTF8.GetBytes(json));
    }

    public ComposerLightPhase Phase => Mode switch { "typing" => ComposerLightPhase.Typing, "running" => ComposerLightPhase.Running, _ => ComposerLightPhase.Rest };

    public static IReadOnlyList<ComposerLightJob> Load(string path)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(path));
        var list = new List<ComposerLightJob>();
        foreach (var j in doc.RootElement.EnumerateArray())
        {
            System.Text.Json.JsonElement? Get(string k) => j.TryGetProperty(k, out var v) ? v.Clone() : null;
            double? Num(string k) => j.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetDouble() : null;
            list.Add(new ComposerLightJob(j.GetProperty("name").GetString()!, j.GetProperty("th").GetString()!, j.GetProperty("light").GetString()!,
                Get("beam"), Get("aura"), Num("T") ?? 0, Num("TH"), Num("auraP"), Num("auraBp"), j.TryGetProperty("mode", out var m) ? m.GetString() : null));
        }
        return list;
    }
}

internal static class ComposerLightFixtures
{
    public static string Path(params string[] parts)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = System.IO.Path.Combine([folder.FullName, "tests", "fixtures", "composer-lights", .. parts]);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(string.Join('/', parts));
    }

    /// <summary>The reference jobs with their tolerances: (job, largest channel difference, share of pixels off by more than 6 / 255, %).</summary>
    public static IReadOnlyList<(ComposerLightJob Job, int MaxDelta, double MaxOffPercent)> Jobs()
    {
        var path = Path("jobs.json");
        var jobs = ComposerLightJob.Load(path);
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(path));
        var list = new List<(ComposerLightJob, int, double)>();
        var i = 0;
        foreach (var j in doc.RootElement.EnumerateArray())
            list.Add((jobs[i++], j.GetProperty("maxDelta").GetInt32(), j.GetProperty("maxOffPercent").GetDouble()));
        return list;
    }

    /// <summary>(largest channel difference, share of pixels off by more than 6 / 255 in %).</summary>
    public static (int Max, double OffPercent) Compare(SKBitmap a, SKBitmap b)
    {
        var max = 0; var off = 0;
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
            {
                SKColor p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                var d = Math.Max(Math.Abs(p.Red - q.Red), Math.Max(Math.Abs(p.Green - q.Green), Math.Abs(p.Blue - q.Blue)));
                max = Math.Max(max, d);
                if (d > 6) off++;
            }
        return (max, off * 100.0 / (a.Width * a.Height));
    }
}
