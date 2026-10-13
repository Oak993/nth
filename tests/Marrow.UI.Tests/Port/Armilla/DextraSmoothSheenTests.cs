using Marrow.UI.Port.Armilla;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Armilla;

[Trait("Decision", "ED-NEW-dextra-smooth-8")]
public sealed class DextraSmoothSheenTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_warm_shimmer_allocates_nothing(bool dark)
    {
        using var surface = SKSurface.Create(new SKImageInfo(320, 40));
        var painter = new SheenHandler(new ArmillaHub(), null, TimeProvider.System);
        var band = new SheenHandler.Band(24, 240, 30, true, dark);
        for (var i = 0; i < 120; i++) painter.Draw(surface.Canvas, band, i / 60.0, .6);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 120; i < 720; i++) painter.Draw(surface.Canvas, band, i / 60.0, .6);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        painter.OnMessage(SheenHandler.Stop.Instance);
        Assert.True(bytes == 0, $"shimmer={bytes} B over 600 frames ({bytes / 600.0} B/frame)");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cached_shimmer_matches_the_original_gradient_through_extent_theme_and_opacity_changes(bool dark)
    {
        var painter = new SheenHandler(new ArmillaHub(), null, TimeProvider.System);
        using var actual = new SKBitmap(320, 40);
        using var expected = new SKBitmap(320, 40);
        using var a = new SKCanvas(actual); using var e = new SKCanvas(expected);
        foreach (var b in new[] { new SheenHandler.Band(24, 240, 30, true, dark), new SheenHandler.Band(0, 180, 20, true, !dark), new SheenHandler.Band(36, 180, 20, true, dark) })
        foreach (var opacity in new[] { 1.0, .6, .0, 1.0 })
        foreach (var at in new[] { .4, 1.4, 2.2, 3.1 })
        {
            a.Clear(new SKColor(90, 110, 130)); e.Clear(new SKColor(90, 110, 130));
            painter.Draw(a, b, at, opacity); Original(e, b, at, opacity);
            Assert.Equal(expected.Bytes, actual.Bytes);
        }
        painter.OnMessage(SheenHandler.Stop.Instance);
    }

    // Frozen pre-fix shader coordinates and compositing; tests the optimized painter's resulting pixels, not its cache internals.
    private static void Original(SKCanvas sk, SheenHandler.Band b, double at, double opacity)
    {
        var saved = sk.Save();
        try
        {
            var w = (float)b.Width; var h = (float)b.Height;
            sk.ClipRect(new SKRect((float)b.X, 0, (float)b.X + w, h));
            var left = (float)(b.X + SheenHandler.Offset(at) * b.Width);
            var hi = b.Dark ? new SKColor(255, 255, 255, 102) : new SKColor(255, 255, 255, 250);
            var tilt = (float)(Math.Tan(10 * Math.PI / 180) * h / 2);
            using var shader = SKShader.CreateLinearGradient(new SKPoint(left - tilt, h), new SKPoint(left + w + tilt, 0),
                [SKColors.Transparent, SKColors.Transparent, hi, SKColors.Transparent, SKColors.Transparent], [0, .3f, .5f, .7f, 1], SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Overlay, IsAntialias = true };
            if (opacity < 1) paint.Color = new SKColor(255, 255, 255, (byte)Math.Round(opacity * 255));
            sk.DrawRect(new SKRect(left, 0, left + w, h), paint);
        }
        finally { sk.RestoreToCount(saved); }
    }
}
