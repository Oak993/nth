using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :1232-1335; ED-213.
[Collection("Headless")]
public sealed class EffortSliderFusionTests(HeadlessSession headless)
{
    private static Type Operation => Assert.IsAssignableFrom<Type>(typeof(TrackEffect).GetNestedType("FusionOperation", BindingFlags.NonPublic));

    private static void Layer(SKCanvas canvas, SKPath path, SKPaint paint, double sigma, bool additive, SKPath? clip = null) =>
        Operation.GetMethod("PaintLayer", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [canvas, path, paint, sigma, additive, clip]);

    private static SKSurface Surface() => SKSurface.Create(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul));
    private static SKBitmap Pixels(SKSurface surface)
    {
        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // App/Views/Composer/EffortSlider.swift:1278,1320 (ED-213 saturating premultiplied add)
    public void Plus_layers_add_half_alpha_red_and_clamp_the_sum()
    {
        using var surface = Surface();
        surface.Canvas.Clear(SKColors.Transparent);
        using var path = new SKPath(); path.AddRect(new SKRect(8, 8, 56, 56));
        using var paint = new SKPaint { Color = new SKColor(255, 0, 0, 128), IsAntialias = true };
        Layer(surface.Canvas, path, paint, 0, true);
        using (var first = Pixels(surface)) Assert.Equal((byte)128, first.GetPixel(32, 32).Alpha);
        Layer(surface.Canvas, path, paint, 0, true);
        using (var second = Pixels(surface)) Assert.Equal(new SKColor(255, 0, 0, 255), second.GetPixel(32, 32));
        Layer(surface.Canvas, path, paint, 0, true);
        using var third = Pixels(surface);
        Assert.Equal(new SKColor(255, 0, 0, 255), third.GetPixel(32, 32));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // App/Views/Composer/EffortSlider.swift:1279 (Gaussian sigma 2.5, ED-213)
    public void Plasma_blur_spreads_beyond_the_path_edge()
    {
        using var surface = Surface(); surface.Canvas.Clear(SKColors.Transparent);
        using var path = new SKPath(); path.AddRect(new SKRect(32, 8, 36, 56));
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        Layer(surface.Canvas, path, paint, 2.5, true);
        using var pixels = Pixels(surface);
        Assert.True(pixels.GetPixel(29, 32).Alpha > 0); // Three units beyond the original x=32 edge.
        Assert.True(pixels.GetPixel(34, 32).Alpha > pixels.GetPixel(29, 32).Alpha);
        Assert.Equal((byte)0, pixels.GetPixel(12, 32).Alpha);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // App/Views/Composer/EffortSlider.swift:1280-1282 (side path clip, ED-213)
    public void The_side_clip_bounds_the_blurred_additive_result()
    {
        using var surface = Surface(); surface.Canvas.Clear(SKColors.Transparent);
        using var path = new SKPath(); path.AddRect(new SKRect(30, 8, 34, 56));
        using var clip = new SKPath(); clip.AddRect(new SKRect(0, 0, 32, 64));
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        Layer(surface.Canvas, path, paint, 2.5, true, clip);
        using var pixels = Pixels(surface);
        Assert.True(pixels.GetPixel(29, 32).Alpha > 0);
        Assert.Equal((byte)0, pixels.GetPixel(33, 32).Alpha);
        Assert.Equal((byte)0, pixels.GetPixel(40, 32).Alpha);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // App/Views/Composer/EffortSlider.swift:1232-1335
    public void Fusion_frames_are_deterministic_and_the_seam_changes_with_time()
    {
        SKBitmap Frame(double time)
        {
            using var surface = SKSurface.Create(new SKImageInfo(240, 34));
            surface.Canvas.Clear(SKColors.Transparent);
            Operation.GetMethod("DrawFrame", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
                [surface.Canvas, new Size(240, 34), time, new SwiftColor(0.7, 0.1, 0.1), new SwiftColor[] { new(0.1, 0.1, 0.7), new(0.1, 0.7, 0.1) }]);
            return Pixels(surface);
        }
        using var first = Frame(0);
        using var again = Frame(0);
        using var moved = Frame(2);
        Assert.Equal(first.Bytes, again.Bytes);
        Assert.False(first.Bytes.SequenceEqual(moved.Bytes));
        // :1235: seam at t=0 is 240*(.5+.05*sin(1)); at t=2 use both source sine terms.
        foreach (var (bitmap, time) in new[] { (first, 0.0), (moved, 2.0) })
        {
            var seam = 240 * (0.5 + 0.13 * Math.Sin(time * 0.7) + 0.05 * Math.Sin(time * 1.9 + 1));
            var core = bitmap.GetPixel((int)Math.Round(seam), 17);
            Assert.InRange(core.Red, (byte)240, (byte)255);
            Assert.InRange(core.Green, (byte)240, (byte)255);
            Assert.InRange(core.Blue, (byte)240, (byte)255);
        }
        Assert.True(first.GetPixel(20, 17).Red > first.GetPixel(20, 17).Blue);
        Assert.True(first.GetPixel(230, 17).Green > first.GetPixel(230, 17).Red);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-138")] // App/Views/Composer/EffortSlider.swift:1054-1056,1232-1335 (Skia lease ED-213)
    public void A_pair_draws_fusion_even_in_the_plain_track_look() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var look = new TrackLook(TrackLook.Kind.Plain, EffortBrand.Purple,
            new EffortPairBrand(new HydraPair(ProviderKind.Codex)));
        var effect = new TrackEffect(look);
        effect.Measure(new Size(240, 34)); effect.Arrange(new Rect(0, 0, 240, 34));
        using var bitmap = new RenderTargetBitmap(new PixelSize(240, 34));
        using (var context = bitmap.CreateDrawingContext()) effect.Child!.Render(context);
        using var pixels = SKBitmap.Decode(bitmapToBytes(bitmap));
        Assert.True(pixels.GetPixel(120, 17).Alpha > 0); // Plain has no ordinary particles/streaks/sheen.
    });

    private static byte[] bitmapToBytes(RenderTargetBitmap bitmap)
    {
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); return stream.ToArray();
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-116")] // App/Views/Composer/EffortSlider.swift:650
    [Trait("Req", "UI-COMPOSER-037")] // App/Views/Composer/EffortSlider.swift:650
    public void The_pair_caption_enters_with_the_shared_soft_appear_transition() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var frameBinding = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var clockBinding = SwiftClock.Provider.Override(clock);
        using var harness = new AppModelHarness(withRuntime: false);
        var model = new AppModel();
        var pair = new EffortPairLook(new HydraPair(ProviderKind.Codex) { WorkerEffort = "high" }, model.Providers, null);
        var card = new EffortSliderCard("Lead", ProviderKind.Codex, new ValueList<string>(["low", "high"]), "low", false,
            new Binding<string?>(() => "low", _ => { }), new Binding<bool>(() => false, _ => { }), () => { }, pair);
        var window = new Avalonia.Controls.Window { Content = card, Width = 330, Height = 220 };
        window.Show(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var caption = Assert.Single(card.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == pair.Caption);
        Assert.Contains(caption.GetVisualAncestors().OfType<Control>(), ancestor => ancestor.Opacity == 0);
        clock.Advance(TimeSpan.FromSeconds(Animation.SoftAppear.Duration));
        frames.Advance(TimeSpan.FromSeconds(Animation.SoftAppear.Duration)); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.All(caption.GetVisualAncestors().OfType<Control>(), ancestor => Assert.Equal(1, ancestor.Opacity));
        window.Close();
    });
}
