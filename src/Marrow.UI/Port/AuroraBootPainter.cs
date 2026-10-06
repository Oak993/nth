using Avalonia;
using SkiaSharp;

namespace Marrow.UI.Port;

// Locked Stage.renderB/renderRM, loading-aurora.html:618-670. Real desktop and main window supply the backdrop/content.
public sealed class AuroraBootPainter : IDisposable
{
    private readonly AuroraMark mark = new();
    private readonly SKPaint paint = new() { IsAntialias = true };
    private readonly SKRoundRect clip = new();
    private SKSurface? icon, glow;
    private int iconPixels, glowWidth, glowHeight;
    private GRContext? graphics;
    private bool stillDrawn, stillDark;
    private static SKRect Rect(Avalonia.Rect r) => new((float)r.X, (float)r.Y, (float)r.Right, (float)r.Bottom);
    private void Color(byte r, byte g, byte b, double alpha) => paint.ColorF = new SKColorF(r / 255f, g / 255f, b / 255f, (float)alpha);
    public void Draw(SKCanvas canvas, Size area, Avalonia.Rect window, AuroraBootFrame f, double seconds, bool dark, bool reduceMotion, double opacity = 1, GRContext? gpu = null)
    {
        var saved = canvas.Save();
        try
        {
            Color(dark ? (byte)2 : (byte)8, dark ? (byte)6 : (byte)14, dark ? (byte)16 : (byte)28, (dark ? .5 : .46) * f.ScrimOpacity * opacity);
            paint.Style = SKPaintStyle.Fill; canvas.DrawRect(SKRect.Create((float)area.Width, (float)area.Height), paint);
            if (f.Launch <= 0 || f.StageClosed) return;
            var matrix = canvas.TotalMatrix;
            var deviceScale = Math.Sqrt(matrix.ScaleX * matrix.ScaleX + matrix.SkewY * matrix.SkewY);
            var size = reduceMotion ? AuroraBootSequence.MarkSize : Math.Round(window.Width * .6);
            var pixels = Math.Max(1, (int)Math.Round(size * deviceScale));
            if (graphics != gpu)
            {
                icon?.Dispose(); glow?.Dispose(); icon = glow = null; graphics = gpu;
            }
            if (icon is null || iconPixels != pixels)
            {
                icon?.Dispose(); icon = Surface(pixels, pixels); iconPixels = pixels; stillDrawn = false;
                if (icon is null) throw new InvalidOperationException("Aurora boot icon surface was not created.");
            }
            // renderRM caches STILL by theme/size. B draws the same upstream renderer on the compositor GPU,
            // keeping its large canvas and glow on that GPU rather than uploading a CPU canvas each frame.
            if (!reduceMotion || !stillDrawn || stillDark != dark)
            {
                icon.Canvas.Clear(SKColors.Transparent); icon.Canvas.ResetMatrix(); icon.Canvas.Scale((float)(pixels / size));
                mark.Draw(icon.Canvas, size, dark ? "dark" : "light", reduceMotion ? AuroraTime.Still : new AuroraTime(f.Launch, f.Idle), new SKSizeI(pixels, pixels));
                stillDrawn = true; stillDark = dark;
            }
            using var image = icon.Snapshot();
            var sc0 = .93 + .07 * (1 - Math.Pow(1 - Math.Clamp(seconds / .9, 0, 1), 3));
            var side = reduceMotion ? size : size * (AuroraBootSequence.MarkSize / size * sc0 +
                (Math.Max(window.Width, window.Height) / (.88 * size) - AuroraBootSequence.MarkSize / size * sc0) * f.Progress);
            var dest = new SKRect((float)(window.Center.X - side / 2), (float)(window.Center.Y - side / 2), (float)(window.Center.X + side / 2), (float)(window.Center.Y + side / 2));
            if (f.GlowOpacity > .002)
            {
                // The page reduces the whole stage to 1/8 before drawing the glow back inside the growing rectangle.
                var gw = Math.Max(8, (int)Math.Round(area.Width / 8)); var gh = Math.Max(8, (int)Math.Round(area.Height / 8));
                if (glow is null || glowWidth != gw || glowHeight != gh)
                {
                    glow?.Dispose(); glow = Surface(gw, gh); glowWidth = gw; glowHeight = gh;
                    if (glow is null) throw new InvalidOperationException("Aurora boot glow surface was not created.");
                }
                glow.Canvas.Clear(SKColors.Transparent); glow.Canvas.ResetMatrix();
                glow.Canvas.Scale((float)(gw / area.Width), (float)(gh / area.Height));
                Color(255, 255, 255, 1); glow.Canvas.DrawImage(image, dest, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
                using var blurred = glow.Snapshot();
                canvas.Save(); clip.SetRect(Rect(f.Tile), (float)f.CornerRadius, (float)f.CornerRadius); canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
                Color(255, 255, 255, f.GlowOpacity * opacity); canvas.DrawImage(blurred, SKRect.Create((float)area.Width, (float)area.Height), new SKSamplingOptions(SKCubicResampler.Mitchell), paint); canvas.Restore();
            }
            if (f.TileOpacity > .002)
            {
                canvas.Save();
                var inflate = reduceMotion ? 0 : (1 - AuroraLoaderMotion.Smooth(f.Progress * 4)) * .06 * AuroraBootSequence.MarkSize;
                clip.SetRect(Rect(f.Tile.Inflate(inflate)), (float)(f.CornerRadius + inflate), (float)(f.CornerRadius + inflate));
                canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
                Color(255, 255, 255, f.TileOpacity * opacity); canvas.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear), paint); canvas.Restore();
            }
            if (!reduceMotion && f.Progress > .02)
            {
                canvas.Save(); clip.SetRect(Rect(f.Tile), (float)f.CornerRadius, (float)f.CornerRadius); canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
                Color(255, 255, 255, .07 * (1 - f.ContentOpacity) * .9 * opacity); canvas.DrawRect(Rect(f.TitleBand), paint);
                if (f.TitleBand.Height > .5) { Color(255, 255, 255, .16 * (1 - f.ContentOpacity) * .9 * opacity); canvas.DrawRect(new SKRect((float)f.TitleBand.X, (float)f.TitleBand.Bottom - 1, (float)f.TitleBand.Right, (float)f.TitleBand.Bottom), paint); }
                canvas.Restore();
                Color(255, 255, 255, .35 * AuroraLoaderMotion.Smooth((f.Progress - .05) / .35) * (1 - f.ContentOpacity) * opacity);
                paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1;
                canvas.DrawRoundRect(Rect(f.Tile.Deflate(.5)), (float)Math.Max(0, f.CornerRadius - .5), (float)Math.Max(0, f.CornerRadius - .5), paint); paint.Style = SKPaintStyle.Fill;
            }
        }
        finally { canvas.RestoreToCount(saved); }
    }
    private SKSurface? Surface(int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        return graphics is { } gpu ? SKSurface.Create(gpu, false, info) : SKSurface.Create(info);
    }
    public void Dispose() { mark.Dispose(); paint.Dispose(); clip.Dispose(); icon?.Dispose(); glow?.Dispose(); }
}
