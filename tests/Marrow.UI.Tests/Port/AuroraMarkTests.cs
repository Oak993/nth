using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.UI.Tests.Port;

// OD-0112; independent Canvas 2D oracle, fixed BEFORE implementing the C# drawing.
public sealed class AuroraMarkTests
{
    [Fact]
    [Trait("Decision", "ED-097")]
    public void References_bind_the_locked_sources_and_cover_every_size_theme_and_motion()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures(), "manifest.json")));
        var root = Path.GetFullPath(Path.Combine(Fixtures(), "../../../.."));
        Assert.Equal("browser Canvas 2D", manifest.RootElement.GetProperty("backend").GetString());
        Assert.Equal(1, manifest.RootElement.GetProperty("dpr").GetInt32());
        Assert.Equal(1, manifest.RootElement.GetProperty("canvasPixelsPerOutputPixel").GetInt32());
        Assert.Equal("fresh", manifest.RootElement.GetProperty("documentPerFrame").GetString());
        Assert.Equal("Google Chrome", manifest.RootElement.GetProperty("browser").GetProperty("name").GetString());
        Assert.Equal(manifest.RootElement.GetProperty("inputsSha256").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Fixtures(), "inputs.json")))));
        foreach (var source in manifest.RootElement.GetProperty("sources").EnumerateArray())
        {
            var bytes = File.ReadAllBytes(Path.Combine(root, source.GetProperty("path").GetString()!));
            Assert.Equal(source.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        Assert.Equal(1200, manifest.RootElement.GetProperty("launchDurationMs").GetInt32());
        Assert.Equal(7200, manifest.RootElement.GetProperty("idlePeriodMs").GetInt32());
        Assert.Equal(2, manifest.RootElement.GetProperty("tolerancePerChannel").GetInt32());
        var frames = manifest.RootElement.GetProperty("frames").EnumerateArray().ToArray();
        foreach (var size in new[] { 16, 24, 32, 48, 64, 256, 512 })
            foreach (var theme in new[] { "dark", "light" })
            {
                var group = frames.Where(f => f.GetProperty("size").GetInt32() == size &&
                    string.Equals(f.GetProperty("theme").GetString(), theme, StringComparison.Ordinal)).ToArray();
                Assert.Single(group, f => string.Equals(f.GetProperty("mode").GetString(), "still", StringComparison.Ordinal));
                foreach (var mode in new[] { "launch", "idle" })
                    Assert.True(group.Where(f => string.Equals(f.GetProperty("mode").GetString(), mode, StringComparison.Ordinal))
                        .Select(f => f.GetProperty("ms").GetInt32()).Distinct().Count() >= 6);
            }
    }

    [Fact]
    [Trait("Decision", "ED-097")]
    public void Every_reference_pixel_matches_within_two_levels_in_all_four_channels()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures(), "manifest.json")));
        var frames = manifest.RootElement.GetProperty("frames");
        Assert.Equal(210, frames.GetArrayLength());
        using var renderer = new AuroraMark();
        var worst = 0;
        var failures = new List<string>();
        var reports = new List<object>();
        var maxBySize = new SortedDictionary<int, int>();
        foreach (var frame in frames.EnumerateArray())
        {
            var file = frame.GetProperty("file").GetString()!;
            var bytes = File.ReadAllBytes(Path.Combine(Fixtures(), file));
            Assert.Equal(frame.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
            using var stream = new SKMemoryStream(bytes);
            using var codec = SKCodec.Create(stream);
            using var reference = SKBitmap.Decode(codec, codec.Info.WithAlphaType(SKAlphaType.Unpremul));
            var size = frame.GetProperty("size").GetInt32();
            using var actual = Render(renderer, size, frame.GetProperty("theme").GetString()!,
                new AuroraTime(frame.GetProperty("launch").GetDouble(), frame.GetProperty("idle").GetDouble(), frame.GetProperty("still").GetBoolean()));
            var difference = Measure(reference, actual);
            var max = difference.Max;
            worst = Math.Max(worst, max);
            maxBySize[size] = Math.Max(maxBySize.GetValueOrDefault(size), max);
            var detail = string.Create(CultureInfo.InvariantCulture,
                $"{file}: max {max}/255 at ({difference.X},{difference.Y}) {difference.Channel}, expected {difference.Expected}, actual {difference.Actual}; {difference.PixelsAboveTolerance} pixels exceed 2");
            Console.WriteLine(detail);
            reports.Add(new { file, size, theme = frame.GetProperty("theme").GetString(),
                mode = frame.GetProperty("mode").GetString(), ms = frame.GetProperty("ms").GetInt32(),
                maxDifference = max, x = difference.X, y = difference.Y, channel = difference.Channel,
                expected = difference.Expected.ToString(), actual = difference.Actual.ToString(),
                pixelsAboveTolerance = difference.PixelsAboveTolerance });
            if (max > 2) failures.Add(detail);
        }
        foreach (var (size, max) in maxBySize)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Aurora {size}px measured maximum: {max}/255"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Aurora measured maximum: {worst}/255 over {frames.GetArrayLength()} frames"));
        var reportFile = Environment.GetEnvironmentVariable("AURORA_PARITY_REPORT");
        if (!string.IsNullOrEmpty(reportFile))
            File.WriteAllText(reportFile, JsonSerializer.Serialize(new {
                oracle = "Chrome 154.0.8037.93 Canvas 2D", tolerancePerChannel = 2,
                comparison = "Every unpremultiplied RGBA channel; no masks", maxDifference = worst,
                maxBySize, framesWithinTolerance = frames.GetArrayLength() - failures.Count, frames = reports,
            }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    [InlineData(256)]
    [InlineData(512)]
    [Trait("Decision", "ED-103")]
    public void Launch_ends_on_the_still_and_idle_wraps_exactly_in_both_themes(int size)
    {
        Assert.Equal(1200, AuroraMark.LaunchDurationMs);
        Assert.Equal(7200, AuroraMark.IdlePeriodMs);
        using var renderer = new AuroraMark();
        foreach (var theme in new[] { "dark", "light" })
        {
            using var still = Render(renderer, size, theme, AuroraTime.Still);
            using var launch = Render(renderer, size, theme, AuroraTime.Launch(1.2));
            using var loop = Render(renderer, size, theme, AuroraTime.Idle(7.2));
            using var negativeLoop = Render(renderer, size, theme, AuroraTime.Idle(-7.2));
            Assert.Equal(0, Compare(still, launch));
            Assert.Equal(0, Compare(still, loop));
            Assert.Equal(0, Compare(still, negativeLoop));
        }
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    [InlineData(256)]
    [InlineData(512)]
    [Trait("Decision", "ED-103")]
    public void Changing_animation_frames_allocate_zero_managed_bytes_after_warmup(int size)
    {
        using var renderer = new AuroraMark();
        using var bitmap = new SKBitmap(size, size);
        using var canvas = new SKCanvas(bitmap);
        for (var i = 0; i < 30; i++)
        {
            canvas.Clear();
            renderer.Draw(canvas, size, "dark", AuroraTime.Launch(i / 25.0));
            renderer.Draw(canvas, size, "light", AuroraTime.Idle(i / 13.0));
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 60; i++)
        {
            canvas.Clear();
            renderer.Draw(canvas, size, "dark", AuroraTime.Launch(i / 50.0));
            renderer.Draw(canvas, size, "light", AuroraTime.Idle(i / 17.0));
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Aurora {size}px: {allocated} B for 120 draws"));
        Assert.Equal(0, allocated);
    }

    [Fact]
    [Trait("Decision", "ED-103")]
    public void Drawing_is_deterministic_and_restores_the_callers_canvas_state()
    {
        using var a = new AuroraMark();
        using var b = new AuroraMark();
        using var first = Render(a, 64, "dark", AuroraTime.Idle(2.375));
        using var unrelated = Render(a, 512, "light", AuroraTime.Launch(0.51));
        using var again = Render(a, 64, "dark", AuroraTime.Idle(2.375));
        using var independent = Render(b, 64, "dark", AuroraTime.Idle(2.375));
        Assert.Equal(0, Compare(first, again));
        Assert.Equal(0, Compare(first, independent));
        using var bitmap = new SKBitmap(128, 128);
        using var canvas = new SKCanvas(bitmap);
        canvas.Translate(3, 5);
        canvas.Scale(1.5f);
        canvas.ClipRect(new SKRect(0, 0, 70, 70));
        var matrix = canvas.TotalMatrix;
        var clip = canvas.DeviceClipBounds;
        var saved = canvas.SaveCount;
        a.Draw(canvas, 64, "dark", AuroraTime.Still);
        Assert.Equal(matrix, canvas.TotalMatrix);
        Assert.Equal(clip, canvas.DeviceClipBounds);
        Assert.Equal(saved, canvas.SaveCount);
    }

    [Theory]
    [InlineData(96, 1.0)]
    [InlineData(256, 1.25)]
    [InlineData(512, 1.5)]
    [InlineData(512, 2.0)]
    [Trait("Decision", "ED-103")]
    public void Warmup_lists_and_draws_each_real_shader_and_blend_path(int size, double scale)
    {
        Assert.NotEmpty(AuroraMark.WarmupPrograms);
        using var bitmap = new SKBitmap((int)(size * scale), (int)(size * scale));
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale((float)scale);
        using var renderer = new AuroraMark();
        foreach (var program in AuroraMark.WarmupPrograms) renderer.Warmup(canvas, program, size);
        Assert.True(bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).Alpha > 0);
    }

    [Fact]
    [Trait("Decision", "ED-103")]
    public void Continuously_changing_canvas_scale_allocates_zero_managed_bytes()
    {
        using var bitmap = new SKBitmap(256, 256);
        using var canvas = new SKCanvas(bitmap);
        using var renderer = new AuroraMark();
        for (var i = 0; i < 10; i++)
        {
            canvas.ResetMatrix(); canvas.Scale(1 + i * .001f);
            renderer.Draw(canvas, 96, "dark", AuroraTime.Idle(i / 60.0));
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 10; i < 60; i++)
        {
            canvas.Clear(); canvas.ResetMatrix(); canvas.Scale(1 + i * .001f);
            renderer.Draw(canvas, 96, "dark", AuroraTime.Idle(i / 60.0));
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    [Trait("Decision", "ED-097")]
    public void The_full_device_canvas_retains_blur_when_the_mark_is_offset()
    {
        using var renderer = new AuroraMark();
        using var origin = new SKBitmap(256, 128);
        using var offset = new SKBitmap(256, 128);
        using (var canvas = new SKCanvas(origin))
        {
            canvas.Clear(new SKColor(17, 24, 39));
            renderer.Draw(canvas, 96, "dark", AuroraTime.Still);
        }
        using (var canvas = new SKCanvas(offset))
        {
            canvas.Clear(new SKColor(17, 24, 39));
            canvas.Translate(120, 0);
            renderer.Draw(canvas, 96, "dark", AuroraTime.Still);
        }
        using var expected = new SKBitmap();
        using var actual = new SKBitmap();
        Assert.True(origin.ExtractSubset(expected, new SKRectI(0, 0, 96, 96)));
        Assert.True(offset.ExtractSubset(actual, new SKRectI(120, 0, 216, 96)));
        Assert.True(Compare(expected, actual) <= 2);
    }

    private static string Fixtures([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../fixtures/aurora/chrome"));

    private static SKBitmap Render(AuroraMark renderer, int size, string theme, AuroraTime time)
    {
        var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        renderer.Draw(canvas, size, theme, time);
        return bitmap;
    }

    private static int Compare(SKBitmap expected, SKBitmap actual) => Measure(expected, actual).Max;

    private static (int Max, int X, int Y, string Channel, SKColor Expected, SKColor Actual, int PixelsAboveTolerance)
        Measure(SKBitmap expected, SKBitmap actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        var max = 0; var worstX = 0; var worstY = 0; var worstChannel = "";
        var worstExpected = SKColors.Transparent; var worstActual = SKColors.Transparent;
        var count = 0;
        for (var y = 0; y < expected.Height; y++)
            for (var x = 0; x < expected.Width; x++)
            {
                var a = expected.GetPixel(x, y);
                var b = actual.GetPixel(x, y);
                var red = Math.Abs(a.Red - b.Red); var green = Math.Abs(a.Green - b.Green);
                var blue = Math.Abs(a.Blue - b.Blue); var alpha = Math.Abs(a.Alpha - b.Alpha);
                var pixelMax = Math.Max(Math.Max(red, green), Math.Max(blue, alpha));
                if (pixelMax > 2) count++;
                if (pixelMax > max)
                {
                    max = pixelMax; worstX = x; worstY = y;
                    worstChannel = red == max ? "R" : green == max ? "G" : blue == max ? "B" : "A";
                    worstExpected = a; worstActual = b;
                }
            }
        return (max, worstX, worstY, worstChannel, worstExpected, worstActual, count);
    }
}
