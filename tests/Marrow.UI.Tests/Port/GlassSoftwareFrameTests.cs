// ED-NEW-composer-stack-2: software layer reuse must preserve every byte of the original shader and its two blends.
using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using SkiaSharp;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassSoftwareFrameTests(HeadlessSession headless)
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static TheoryData<double, bool, int> Cases()
    {
        var cases = new TheoryData<double, bool, int>();
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
            foreach (var dark in new[] { false, true })
                foreach (var mode in new[] { 0, 1, 2 }) cases.Add(scale, dark, mode);
        foreach (var scale in new[] { 1d, 2 })
            foreach (var dark in new[] { false, true })
                foreach (var mode in new[] { 3, 4, 5, 6 }) cases.Add(scale, dark, mode);
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
            foreach (var dark in new[] { false, true })
                foreach (var mode in new[] { 7, 8, 9 }) cases.Add(scale, dark, mode);
        cases.Add(2, true, 10); // Its complete F32 surface pair would exceed the temporary cap: fall back without a cache hit.
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Req", "PR-WIN-180")]
    public void Software_layers_preserve_every_pixel_on_reuse_and_backdrop_or_clip_changes(double scale, bool dark, int mode) => headless.OnUiThread(() =>
    {
        // Legacy pill, composer, mixed containers, homogeneous counts 2 and 8, legacy interior and flat canvas.
        var reference = Render(scale, dark, mode, general: true);
        var specialized = Render(scale, dark, mode, general: false);
        for (var frame = 0; frame < reference.Length; frame++)
        {
            var first = Enumerable.Range(0, reference[frame].Length).FirstOrDefault(i => reference[frame][i] != specialized[frame][i], -1);
            Assert.True(first < 0, $"Frame {frame}, byte {first}: original {(first < 0 ? 0 : reference[frame][first])}, software {(first < 0 ? 0 : specialized[frame][first])}");
            Assert.Equal(reference[frame], specialized[frame]);
        }
    });

    private static byte[][] Render(double scale, bool dark, int mode, bool general)
    {
        var type = typeof(GlassMaterial);
        var regionType = type.GetNestedType("Region", BindingFlags.NonPublic)!;
        var regions = Array.CreateInstance(regionType, mode is 2 or 3 or 4 ? 2 : mode is 5 or 6 ? 8 : 1);
        for (var i = 0; i < regions.Length; i++)
        {
            var canvas = mode is 1 or 4 or 6 or 8 or 9 or 10 || mode == 2 && i == 1;
            var own = mode == 0;
            var rectangle = canvas || mode == 7;
            var shape = rectangle ? GlassShape.RoundedRectangle(26, RoundedCornerStyle.Continuous) : GlassShape.Capsule(RoundedCornerStyle.Continuous);
            var region = Activator.CreateInstance(regionType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [new Size(mode is 1 or 9 or 10 ? 900 : 240, mode is 1 or 9 or 10 ? 135.5 : rectangle ? 96 : 40), shape, new CornerRadius(26), Color.FromArgb(76, 38, 142, 210), true,
                 dark ? 1d : 0d, true, false, new Point(135, 19), Matrix.CreateTranslation(mode is 9 or 10 ? 180.375 : 24.375, (mode is 9 or 10 ? 160.125 : 40.125) + i * 14), dark ? 1d : 0.35d,
                 own, own, canvas ? GlassLooks.For(mode == 8 ? GlassSurface.Inner : GlassSurface.Composer) : GlassLook.Release, 0.6d, 0.2d, own], null)!;
            regions.SetValue(region, i);
        }
        var state = Activator.CreateInstance(type.GetNestedType("State", BindingFlags.NonPublic)!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [regions, 8d], null)!;
        var backdrop = Activator.CreateInstance(type.GetNestedType("Backdrop", BindingFlags.NonPublic)!, nonPublic: true)!;
        var width = mode == 10 ? 1350 : mode == 9 ? 1280 : mode == 1 ? 980 : 320;
        var height = mode == 10 ? 450 : mode == 9 ? 400 : mode == 1 ? 300 : 220;
        using var surface = SKSurface.Create(new SKImageInfo((int)(width * scale), (int)(height * scale), SKColorType.Rgba8888, SKAlphaType.Premul));
        var target = surface.Canvas;
        var frames = new List<byte[]>();
        var hits = GlassMaterial.SoftwareFrameHitsForTests;
        try
        {
            for (var frame = 0; frame < (mode is 1 or 9 ? 8 : 4); frame++)
            {
                target.ResetMatrix();
                target.Clear(new SKColor(frame < 2 ? (byte)48 : (byte)90, 70, 85, dark ? (byte)170 : (byte)255));
                target.Save();
                target.Scale((float)scale);
                using (var paint = new SKPaint())
                    for (var x = 0; x < width; x += 8)
                    {
                        paint.Color = x % 24 == 0 ? new SKColor(255, 170, 30, 190) : new SKColor(15, 210, 240, 140);
                        target.DrawRect(x, 0, 4, height, paint);
                    }
                if (frame == 3)
                {
                    var matrix = target.TotalMatrix;
                    target.ResetMatrix();
                    target.ClipRect(new SKRect(12, 24, target.DeviceClipBounds.Right - 15, target.DeviceClipBounds.Bottom - 10));
                    target.SetMatrix(matrix);
                }
                if (mode == 9) target.Translate(0.375f, -0.625f);
                if (frame == 4)
                {
                    using var path = new SKPath();
                    path.AddRoundRect(new SKRect(20, 30, width - 20, height - 30), 24, 24);
                    target.ClipPath(path, antialias: true);
                }
                if (frame == 5) target.Translate(0.375f, -0.625f);
                var frameState = state;
                if (frame == 7)
                {
                    var changed = (Array)regions.Clone();
                    var region = regionType.GetMethod("<Clone>$")!.Invoke(regions.GetValue(0), null)!;
                    regionType.GetProperty("Tint")!.SetValue(region, Colors.Red);
                    changed.SetValue(region, 0);
                    frameState = Activator.CreateInstance(type.GetNestedType("State", BindingFlags.NonPublic)!,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [changed, 8d], null)!;
                }
                var effect = general ? GlassMaterial.WarmupProgram(regions.Length) : null;
                Assert.NotNull(type.GetMethod("Draw", Private)!.Invoke(null, [new GlassFakeLease(surface, frame == 6 ? 0.4 : 0.7), frameState, backdrop, null, effect]));
                using var image = surface.Snapshot();
                using var bitmap = SKBitmap.FromImage(image);
                frames.Add(bitmap.Bytes);
                if (!general && mode is 1 or 9)
                {
                    Assert.Equal(hits + (frame == 0 ? 0 : frame == 1 || frame == 2 ? 1 : frame == 3 ? 2 : 3), GlassMaterial.SoftwareFrameHitsForTests);
                    Assert.InRange((long)type.GetField("softwareFrameBytes", Private)!.GetValue(null)!, 1, 64L * 1024 * 1024);
                    var entries = (System.Collections.ICollection)type.GetField("softwareFrames", Private)!.GetValue(null)!;
                    Assert.InRange(entries.Count, 1, 4);
                    var frameEntry = ((System.Collections.IEnumerable)entries).Cast<object>().Last();
                    Assert.Equal(SKColorType.RgbaF32, ((SKImage)frameEntry.GetType().GetProperty("Mask")!.GetValue(frameEntry)!).ColorType);
                    Assert.Equal(SKColorType.RgbaF32, ((SKImage)frameEntry.GetType().GetProperty("Colour")!.GetValue(frameEntry)!).ColorType);
                    if (mode == 9)
                    {
                        var last = ((System.Collections.IEnumerable)entries).Cast<object>().Last();
                        var output = (SKRectI)last.GetType().GetProperty("Output")!.GetValue(last)!;
                        Assert.True(output.Left > 0 && output.Top > 0);
                    }
                }
                if (!general && mode == 10) Assert.Equal(hits, GlassMaterial.SoftwareFrameHitsForTests);
                target.Restore();
            }
            return frames.ToArray();
        }
        finally
        {
            backdrop.GetType().GetMethod("DisposeOwned")!.Invoke(backdrop, null);
            var entries = (System.Collections.IEnumerable)type.GetField("softwareFrames", Private)!.GetValue(null)!;
            Assert.DoesNotContain(entries.Cast<object>(), entry => ReferenceEquals(entry.GetType().GetProperty("Owner")!.GetValue(entry), backdrop));
        }
    }
}
