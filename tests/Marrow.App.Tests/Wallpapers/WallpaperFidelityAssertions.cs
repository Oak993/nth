using SkiaSharp;
using System.Text.Json;
using Xunit.Abstractions;

namespace Marrow.App.Tests.Wallpapers;

public static class WallpaperFidelityAssertions
{
    private static readonly object metricGate = new();
    public static void Compare(string collection, string wallpaper, bool premium, ITestOutputHelper output)
    {
        var cases = WallpaperReferences.Cases.Where(c => string.Equals(c.Collection, collection, StringComparison.Ordinal)
            && string.Equals(c.Wallpaper, wallpaper, StringComparison.Ordinal) && c.Premium == premium).ToArray();
        Assert.NotEmpty(cases);
        var failures = new List<string>();
        foreach (var reference in cases)
        {
            try
            {
                using var expected = SKBitmap.Decode(Path.Combine(WallpaperReferences.Folder, reference.File))
                    ?? throw new InvalidDataException("Reference PNG did not decode");
                using var actual = WallpaperRendererBridge.Render(reference);
                var result = WallpaperPerceptualComparison.Measure(expected, actual);
                var line = FormattableString.Invariant($"{reference.Label}: mean OKLab ΔE {result.Mean:F6} <= {WallpaperPerceptualComparison.MeanLimit:F3}; max {result.Max:F6} <= {WallpaperPerceptualComparison.PixelLimit:F3} at ({result.X},{result.Y}); reference {reference.Inputs.GetProperty("sha256").GetString()}");
                output.WriteLine(line);
                if (Environment.GetEnvironmentVariable("WALLPAPER_METRICS_PATH") is { Length: > 0 } metrics)
                {
                    var record = JsonSerializer.Serialize(new { reference.Label, reference.Collection, reference.Variant,
                        result.Mean, result.Max, result.X, result.Y, Reference = reference.File,
                        Passed = WallpaperPerceptualComparison.Within(result.Mean, result.Max) });
                    lock (metricGate) File.AppendAllText(metrics, record + Environment.NewLine);
                }
                if (!WallpaperPerceptualComparison.Within(result.Mean, result.Max)) failures.Add(line);
            }
            catch (Exception e) { failures.Add(reference.Label + ": " + e.Message); }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
