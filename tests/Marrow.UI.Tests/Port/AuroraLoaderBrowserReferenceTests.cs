using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using SkiaSharp;

namespace Marrow.UI.Tests.Port;

// OD-0113: independent Chrome executes the locked Canvas code and actual CSS.
// Lead ruling: premultiplied RGBA, each channel peak <=24/255 and mean <=.5/255; straight RGBA diagnostic only.
// No masks, rescaling or omitted pixels. All 72 source reference frames, both themes and four DPI scales.
public sealed class AuroraLoaderBrowserReferenceTests
{
    [Fact]
    [Trait("Decision", "ED-097")]
    public void Chrome_references_are_bound_to_the_locked_source_and_every_png_hash()
    {
        var root = Root();
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "tests/fixtures/aurora-loaders/manifest.json")));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "porting/first-release/design-src/loading-aurora/loading-aurora.html")))),
            manifest.RootElement.GetProperty("source_sha256").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "tests/fixtures/aurora-loaders/render.py")))),
            manifest.RootElement.GetProperty("tool_sha256").GetString());
        var frames = manifest.RootElement.GetProperty("frames").EnumerateArray().ToArray();
        Assert.Equal(72, frames.Length);
        foreach (var frame in frames)
        {
            var bytes = File.ReadAllBytes(Path.Combine(root, "tests/fixtures/aurora-loaders/png", frame.GetProperty("file").GetString()!));
            Assert.Equal(frame.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
    }

    [Theory]
    [InlineData(AuroraLoaderKind.CurtainFold, true)]
    [InlineData(AuroraLoaderKind.CurtainFold, false)]
    [InlineData(AuroraLoaderKind.FlowArc, true)]
    [InlineData(AuroraLoaderKind.FlowArc, false)]
    [InlineData(AuroraLoaderKind.AuroraSheen, true)]
    [InlineData(AuroraLoaderKind.AuroraSheen, false)]
    [Trait("Decision", "ED-097")]
    public void Both_themes_and_all_four_scales_draw_the_locked_browser_frames(AuroraLoaderKind kind, bool dark)
    {
        using var painter = new AuroraLoaderPainter();
        var failures = new List<string>();
        foreach (var scale in new[] { 1, 1.25, 1.5, 2 })
            foreach (var seconds in new[] { .6, .84, 1.2 })
            {
                var width = kind == AuroraLoaderKind.AuroraSheen ? 180 : kind == AuroraLoaderKind.CurtainFold ? 48 : 40;
                var height = kind == AuroraLoaderKind.AuroraSheen ? 88 : width;
                var name = string.Create(CultureInfo.InvariantCulture, $"{(int)kind}-{(dark ? "dark" : "light")}-{scale}-{seconds}.png");
                using var reference = SKBitmap.Decode(Path.Combine(Root(), "tests/fixtures/aurora-loaders/png", name));
                using var surface = SKSurface.Create(new SKImageInfo((int)(width * scale), (int)(height * scale)));
                surface.Canvas.Clear(SKColors.Transparent);
                surface.Canvas.Scale((float)scale);
                painter.Draw(surface.Canvas, kind, width, height, seconds, dark, false);
                using var image = surface.Snapshot();
                using var actual = SKBitmap.FromImage(image);
                if (Environment.GetEnvironmentVariable("MARROW_AURORA_ACTUAL") is { } output)
                {
                    Directory.CreateDirectory(output);
                    using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                    File.WriteAllBytes(Path.Combine(output, name), encoded.ToArray());
                }
                Assert.Equal(reference.Width, actual.Width); Assert.Equal(reference.Height, actual.Height);
                var metrics = new AuroraPixelMetrics();
                for (var y = 0; y < actual.Height; y++)
                    for (var x = 0; x < actual.Width; x++)
                    {
                        var p = reference.GetPixel(x, y); var q = actual.GetPixel(x, y);
                        metrics.Add(p, q);
                    }
                if (Environment.GetEnvironmentVariable("MARROW_AURORA_ACTUAL") is { } diagnostics)
                    File.WriteAllText(Path.Combine(diagnostics, name + ".json"), JsonSerializer.Serialize(new { metrics.Peak, metrics.Mean, metrics.StraightPeak, metrics.StraightMean, metrics.Accepted }));
                if (!metrics.Accepted) failures.Add($"{name}: premul RGBA peak [{string.Join(",", metrics.Peak.Select(x => x.ToString("F3", CultureInfo.InvariantCulture)))}]/255, mean [{string.Join(",", metrics.Mean.Select(x => x.ToString("F6", CultureInfo.InvariantCulture)))}]/255; straight peak [{string.Join(",", metrics.StraightPeak)}]");
            }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static string Root()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
            if (File.Exists(Path.Combine(at.FullName, "porting/first-release/design-src/loading-aurora/loading-aurora.html"))) return at.FullName;
        throw new DirectoryNotFoundException("The locked loader source was not found.");
    }
}
