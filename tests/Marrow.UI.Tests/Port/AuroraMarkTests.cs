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
    public void Every_Chrome_reference_frame_meets_the_leads_perceptual_bound()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures(), "manifest.json")));
        var frames = manifest.RootElement.GetProperty("frames");
        Assert.Equal(210, frames.GetArrayLength());
        using var renderer = new AuroraMark();
        var worst = 0;
        var failures = new List<string>();
        var reports = new List<object>();
        var maxBySize = new SortedDictionary<int, int>();
        var strictMaxBySize = new SortedDictionary<int, int>();
        var meanBySize = new SortedDictionary<int, double>();
        var withinFourBySize = new SortedDictionary<int, double>();
        var strictFailures = 0;
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
            using var actualPng = PngPixels(actual);
            var difference = Measure(reference, actualPng);
            var max = difference.PremulMax;
            worst = Math.Max(worst, max);
            maxBySize[size] = Math.Max(maxBySize.GetValueOrDefault(size), max);
            meanBySize[size] = Math.Max(meanBySize.GetValueOrDefault(size), difference.PremulMean);
            withinFourBySize[size] = Math.Min(withinFourBySize.GetValueOrDefault(size, 1), difference.PremulWithinFour);
            strictMaxBySize[size] = Math.Max(strictMaxBySize.GetValueOrDefault(size), difference.Max);
            if (difference.Max > 2) strictFailures++;
            var detail = string.Create(CultureInfo.InvariantCulture,
                $"{file}: strict max {difference.Max}/255 at ({difference.X},{difference.Y}) {difference.Channel}, expected {difference.Expected}, actual {difference.Actual}; {difference.PixelsAboveTolerance} pixels exceed 2; mean {difference.Mean:F6}/255, {difference.WithinFour:P6} within 4; premultiplied peak {difference.PremulMax}, mean {difference.PremulMean:F6}, {difference.PremulWithinFour:P6} within 4");
            Console.WriteLine(detail);
            reports.Add(new { file, size, theme = frame.GetProperty("theme").GetString(),
                mode = frame.GetProperty("mode").GetString(), ms = frame.GetProperty("ms").GetInt32(),
                strictMaxDifference = difference.Max, x = difference.X, y = difference.Y, channel = difference.Channel,
                expected = difference.Expected.ToString(), actual = difference.Actual.ToString(),
                pixelsAboveTolerance = difference.PixelsAboveTolerance, meanChannelDifference = difference.Mean,
                fractionWithinFour = difference.WithinFour, premultipliedPeak = difference.PremulMax,
                premultipliedMean = difference.PremulMean, premultipliedFractionWithinFour = difference.PremulWithinFour });
            if (max > 24 || difference.PremulMean > .5 || difference.PremulWithinFour < .995) failures.Add(detail);
        }
        foreach (var (size, max) in maxBySize)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Aurora {size}px: peak {max}/255, worst mean {meanBySize[size]:F6}/255, minimum {withinFourBySize[size]:P6} within 4"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Aurora measured maximum: {worst}/255 over {frames.GetArrayLength()} frames"));
        var reportFile = Environment.GetEnvironmentVariable("AURORA_PARITY_REPORT");
        if (!string.IsNullOrEmpty(reportFile))
            File.WriteAllText(reportFile, JsonSerializer.Serialize(new {
                oracle = "Chrome 154.0.8037.93 Canvas 2D", peakLimit = 24, meanLimit = .5, fractionWithinFourLimit = .995, strictDiagnosticLimit = 2,
                comparison = "Every premultiplied RGBA channel for acceptance; strict unpremultiplied PNG RGBA diagnostic; no masks",
                nativeDestinationFormat = "BGRA8888 premultiplied (Chromium CPU Canvas 2D N32)", maxDifference = worst,
                maxBySize, meanBySize, withinFourBySize, strictMaxBySize, framesWithinPerceptualBound = frames.GetArrayLength() - failures.Count,
                framesWithinStrictDiagnostic = frames.GetArrayLength() - strictFailures, frames = reports,
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
        // Match the browser's CPU canvas storage, then compare logical RGBA channels.
        // Skia 119's RGBA blitters use different rounding from its native N32/BGRA blitters.
        var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        renderer.Draw(canvas, size, theme, time);
        return bitmap;
    }

    private static int Compare(SKBitmap expected, SKBitmap actual) => Measure(expected, actual).Max;

    private static SKBitmap PngPixels(SKBitmap bitmap)
    {
        // The strict diagnostic compares the actual encoded PNG's straight RGBA,
        // including its unpremultiplication quantization at transparent edges.
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new SKMemoryStream(png);
        using var codec = SKCodec.Create(stream);
        return SKBitmap.Decode(codec, codec.Info.WithAlphaType(SKAlphaType.Unpremul));
    }

    private static (int Max, int X, int Y, string Channel, SKColor Expected, SKColor Actual, int PixelsAboveTolerance,
        double Mean, double WithinFour, int PremulMax, double PremulMean, double PremulWithinFour)
        Measure(SKBitmap expected, SKBitmap actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        var max = 0; var worstX = 0; var worstY = 0; var worstChannel = "";
        var worstExpected = SKColors.Transparent; var worstActual = SKColors.Transparent;
        var count = 0; var withinFour = 0; var premulWithinFour = 0; var premulMax = 0;
        long total = 0; long premulTotal = 0;
        for (var y = 0; y < expected.Height; y++)
            for (var x = 0; x < expected.Width; x++)
            {
                var a = expected.GetPixel(x, y);
                var b = actual.GetPixel(x, y);
                var red = Math.Abs(a.Red - b.Red); var green = Math.Abs(a.Green - b.Green);
                var blue = Math.Abs(a.Blue - b.Blue); var alpha = Math.Abs(a.Alpha - b.Alpha);
                var pixelMax = Math.Max(Math.Max(red, green), Math.Max(blue, alpha));
                if (pixelMax > 2) count++;
                if (pixelMax <= 4) withinFour++;
                total += red + green + blue + alpha;
                var pr = Math.Abs(Premultiply(a.Red, a.Alpha) - Premultiply(b.Red, b.Alpha));
                var pg = Math.Abs(Premultiply(a.Green, a.Alpha) - Premultiply(b.Green, b.Alpha));
                var pb = Math.Abs(Premultiply(a.Blue, a.Alpha) - Premultiply(b.Blue, b.Alpha));
                var pm = Math.Max(Math.Max(pr, pg), Math.Max(pb, alpha));
                premulMax = Math.Max(premulMax, pm); premulTotal += pr + pg + pb + alpha;
                if (pm <= 4) premulWithinFour++;
                if (pixelMax > max)
                {
                    max = pixelMax; worstX = x; worstY = y;
                    worstChannel = red == max ? "R" : green == max ? "G" : blue == max ? "B" : "A";
                    worstExpected = a; worstActual = b;
                }
            }
        var pixels = (double)expected.Width * expected.Height;
        return (max, worstX, worstY, worstChannel, worstExpected, worstActual, count,
            total / (4 * pixels), withinFour / pixels, premulMax, premulTotal / (4 * pixels), premulWithinFour / pixels);
    }
    // Convert both encoded PNGs to 8-bit premultiplied RGBA, rounding to nearest.
    // Every channel of every pixel contributes, including alpha and zero-alpha pixels.
    private static int Premultiply(byte channel, byte alpha) => (channel * alpha + 127) / 255;
}
