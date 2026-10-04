// PORT-ONLY CZ-1: Skia equivalent of the design's conic gradients, CSS masks and blur copies.
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
namespace Marrow.UI.Port.Settings;

internal sealed class CzStyleGlyphDraw(Rect bounds, string style, double angle, bool dark) : ICustomDrawOperation
{
    public Rect Bounds { get; } = bounds;
    public bool HitTest(Point point) => false;
    public bool Equals(ICustomDrawOperation? other) => false;
    public void Dispose() { }
    public void Render(ImmediateDrawingContext context)
    {
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>(); if (feature is null) return;
        using var lease = feature.Lease(); var canvas = lease.SkCanvas;
        using var paint = new SKPaint { IsAntialias = true };
        var width = (float)Bounds.Width; var opacity = (float)lease.CurrentOpacity;
        var primary = dark ? new SKColor(236, 236, 236) : new SKColor(28, 28, 30);
        canvas.Save(); canvas.ClipRoundRect(new SKRoundRect(new SKRect(0, 0, width, 56), 8));
        paint.Color = dark ? new SKColor(255, 255, 255, (byte)(.07 * 255 * opacity)) : new SKColor(0, 0, 0, (byte)(.05 * 255 * opacity));
        canvas.DrawRect(0, 0, width, 56, paint);
        var capsule = new SKRect(width / 2 - 52, 16, width / 2 + 52, 40);
        if (style != "off")
        {
            SKColor[] colors; float[] stops;
            if (style == "beam")
            {
                colors = [SKColors.Transparent, SKColors.Transparent, SKColor.Parse("#4F8BFF"), SKColor.Parse("#B46BFF"), SKColor.Parse("#FF6F7D"), SKColor.Parse("#FFB45C"), SKColors.Transparent, SKColors.Transparent];
                stops = [0, .48f, .60f, .72f, .84f, .92f, .98f, 1];
            }
            else
            {
                colors = [SKColor.Parse("#FF5500"), SKColor.Parse("#FF5500"), SKColors.Transparent, SKColors.Transparent, SKColor.Parse("#0055FF"), SKColor.Parse("#0055FF")];
                stops = [0, .16f, .32f, .68f, .84f, 1];
            }
            using var shader = SKShader.CreateSweepGradient(new SKPoint(width / 2, 28), colors, stops, SKShaderTileMode.Clamp, 0, 360,
                SKMatrix.CreateRotationDegrees((float)angle - 90, width / 2, 28));
            paint.Shader = shader; paint.Color = SKColors.White.WithAlpha((byte)(.55 * 255 * opacity));
            using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, style == "beam" ? 6 : 8); paint.MaskFilter = blur;
            var glow = capsule; glow.Inflate(style == "beam" ? 3 : 6, style == "beam" ? 3 : 6);
            canvas.DrawRoundRect(glow, style == "beam" ? 15 : 18, style == "beam" ? 15 : 18, paint);
            paint.MaskFilter = null;
            if (style == "beam")
            {
                paint.Color = SKColors.White.WithAlpha((byte)(255 * opacity));
                using var mask = new SKPath(); var outer = capsule; outer.Inflate(1, 1); mask.AddRoundRect(outer, 13, 13);
                var inner = outer; inner.Inflate(-1.5f, -1.5f); mask.AddRoundRect(inner, 11.5f, 11.5f); mask.FillType = SKPathFillType.EvenOdd;
                canvas.DrawPath(mask, paint);
            }
            paint.Shader = null;
        }
        paint.Color = primary.WithAlpha((byte)(.05 * 255 * opacity)); canvas.DrawRoundRect(capsule, 12, 12, paint);
        paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1; paint.Color = primary.WithAlpha((byte)(.16 * 255 * opacity)); canvas.DrawRoundRect(capsule, 12, 12, paint);
        canvas.Restore();
    }
}
