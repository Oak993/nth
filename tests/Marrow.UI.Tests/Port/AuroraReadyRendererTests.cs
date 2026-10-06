using System.Security.Cryptography;
using System.Text.Json;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using SkiaSharp;

namespace Marrow.UI.Tests.Port;

// T10's independent regression of the READY renderer. Its branch retains its own parity tests.
// Imported Chrome oracle bytes are unchanged. T10 applies the lead's per-channel premultiplied bound.
public sealed class AuroraReadyRendererTests
{
    [Fact]
    [Trait("Decision", "ED-097")]
    public void The_READY_Aurora_drawing_meets_the_per_channel_bound_on_all_210_Chrome_frames()
    {
        var root = Root();
        var fixtures = Path.Combine(root, "tests/fixtures/aurora/chrome");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtures, "manifest.json")));
        Assert.Equal(manifest.RootElement.GetProperty("inputsSha256").GetString(), Hash(Path.Combine(fixtures, "inputs.json")));
        foreach (var source in manifest.RootElement.GetProperty("sources").EnumerateArray())
            Assert.Equal(source.GetProperty("sha256").GetString(), Hash(Path.Combine(root, source.GetProperty("path").GetString()!)));
        var frames = manifest.RootElement.GetProperty("frames");
        Assert.Equal(210, frames.GetArrayLength());
        using var renderer = new AuroraMark();
        var failures = new List<string>();
        var reports = new List<object>();
        foreach (var frame in frames.EnumerateArray())
        {
            var name = frame.GetProperty("file").GetString()!;
            var path = Path.Combine(fixtures, name);
            Assert.Equal(frame.GetProperty("sha256").GetString(), Hash(path));
            using var referenceStream = new SKMemoryStream(File.ReadAllBytes(path));
            using var referenceCodec = SKCodec.Create(referenceStream);
            using var reference = SKBitmap.Decode(referenceCodec, referenceCodec.Info.WithAlphaType(SKAlphaType.Unpremul));
            var size = frame.GetProperty("size").GetInt32();
            using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            renderer.Draw(canvas, size, frame.GetProperty("theme").GetString()!,
                new AuroraTime(frame.GetProperty("launch").GetDouble(), frame.GetProperty("idle").GetDouble(), frame.GetProperty("still").GetBoolean()));
            using var actualImage = SKImage.FromBitmap(bitmap);
            using var encoded = actualImage.Encode(SKEncodedImageFormat.Png, 100);
            using var actualStream = new SKMemoryStream(encoded.ToArray());
            using var actualCodec = SKCodec.Create(actualStream);
            using var actual = SKBitmap.Decode(actualCodec, actualCodec.Info.WithAlphaType(SKAlphaType.Unpremul));
            Assert.Equal(reference.Width, actual.Width); Assert.Equal(reference.Height, actual.Height);
            var metrics = new AuroraPixelMetrics();
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++) metrics.Add(reference.GetPixel(x, y), actual.GetPixel(x, y));
            reports.Add(new { name, metrics.Peak, metrics.Mean, metrics.StraightPeak, metrics.StraightMean, metrics.Accepted });
            if (!metrics.Accepted) failures.Add($"{name}: peak [{string.Join(",", metrics.Peak)}], mean [{string.Join(",", metrics.Mean)}]");
        }
        if (Environment.GetEnvironmentVariable("MARROW_AURORA_ACTUAL") is { } output)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "ready-renderer-210.json"), JsonSerializer.Serialize(reports));
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Root()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
            if (File.Exists(Path.Combine(at.FullName, "porting/first-release/design-src/app-icon/src/iconkit.js"))) return at.FullName;
        throw new DirectoryNotFoundException("The locked Aurora sources were not found.");
    }
}
