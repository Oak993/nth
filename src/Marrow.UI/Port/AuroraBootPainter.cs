using Avalonia;
using SkiaSharp;

namespace Marrow.UI.Port;

// Locked Stage.renderB/renderRM, loading-aurora.html:618-670: the stage, its scrim, the tile's grow into the window frame, its
// clip and glow. Real desktop and main window supply the backdrop/content. OD-0136: what the tile shows is the nth mark B
// (NthMark, the stage's .bs.metal tile). OD-0142: it moves by NthMotion.Boot (the tile pops toward the viewer while its gradient
// turns to rest and its shadow lifts, the n draws, the bead springs in; no shine); the pop's scale replaces the Aurora's .93-to-1
// entrance scale in the tile's side, so from the hand-over on (at least 1.8 s, where it is 1) the grow is the same rectangle.
// Until the grow the mark is drawn as vectors straight onto the stage at its current size (crisp at every scale, nothing
// rasterised per frame, no managed allocation per frame); the grow and its glow draw one cached image of the settled tile.
public sealed class AuroraBootPainter : IDisposable
{
    private readonly NthMark mark = new();
    private readonly SKPaint paint = new() { IsAntialias = true };
    private readonly SKPaint fade = new();
    private readonly SKRoundRect clip = new();
    private SKSurface? icon, glow;
    private SKImage? still;
    private int iconPixels, glowWidth, glowHeight;
    private GRContext? graphics;
    private static SKRect Rect(Avalonia.Rect r) => new((float)r.X, (float)r.Y, (float)r.Right, (float)r.Bottom);
    private void Color(byte r, byte g, byte b, double alpha) => paint.ColorF = new SKColorF(r / 255f, g / 255f, b / 255f, (float)alpha);
    public void Draw(SKCanvas canvas, Size area, Avalonia.Rect window, AuroraBootFrame f, double seconds, bool dark, bool reduceMotion, double opacity = 1, GRContext? gpu = null)
    {
        var saved = canvas.Save();
        try
        {
            Color(dark ? (byte)2 : (byte)8, dark ? (byte)6 : (byte)14, dark ? (byte)16 : (byte)28, (dark ? .5 : .46) * f.ScrimOpacity * opacity);
            paint.Style = SKPaintStyle.Fill; canvas.DrawRect(SKRect.Create((float)area.Width, (float)area.Height), paint);
            if (f.StageClosed) return; // the pop starts with the stage (the Aurora's launch waited .12 s)
            if (graphics != gpu)
            {
                still?.Dispose(); icon?.Dispose(); glow?.Dispose(); still = null; icon = glow = null; graphics = gpu;
            }
            var pose = NthMotion.Boot(seconds, reduceMotion);
            var growing = !reduceMotion && f.Progress > 0;
            var side = reduceMotion ? AuroraBootSequence.MarkSize : AuroraBootSequence.MarkSize * pose.TileScale +
                (Math.Max(window.Width, window.Height) / .88 - AuroraBootSequence.MarkSize * pose.TileScale) * f.Progress;
            var settled = growing ? Still(canvas, window) : null;
            // the still is only ever enlarged: while it would be shrunk (side below its .6-of-the-width size) the tile stays vectors,
            // so the grow's first frames keep the hold's crisp edges and the switch happens where the image is drawn 1:1
            var image = settled is not null && side >= Math.Round(window.Width * .6) ? settled : null;
            var dest = new SKRect((float)(window.Center.X - side / 2), (float)(window.Center.Y - side / 2), (float)(window.Center.X + side / 2), (float)(window.Center.Y + side / 2));
            if (f.GlowOpacity > .002 && settled is not null)
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
                Color(255, 255, 255, 1); glow.Canvas.DrawImage(settled, dest, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
                using var blurred = glow.Snapshot(); // the hand-over's glow, unchanged (its only per-frame object, after the mark)
                canvas.Save(); clip.SetRect(Rect(f.Tile), (float)f.CornerRadius, (float)f.CornerRadius); canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
                Color(255, 255, 255, f.GlowOpacity * opacity); canvas.DrawImage(blurred, SKRect.Create((float)area.Width, (float)area.Height), new SKSamplingOptions(SKCubicResampler.Mitchell), paint); canvas.Restore();
            }
            var alpha = f.TileOpacity * opacity * pose.TileOpacity;
            if (alpha > .002)
            {
                // .bs's box-shadow lies on the desktop under the tile, outside the tile's clip; it lifts with the pop (OD-0142) and
                // lifts away in the grow's first quarter.
                var tile = SKRect.Inflate(dest, (float)(-.06 * side), (float)(-.06 * side));
                var away = reduceMotion ? 1 : 1 - AuroraLoaderMotion.Smooth(f.Progress * 4);
                mark.DrawStageShadow(canvas, tile, alpha * away, pose.Lift);
                canvas.Save();
                var inflate = reduceMotion ? 0 : (1 - AuroraLoaderMotion.Smooth(f.Progress * 4)) * .06 * AuroraBootSequence.MarkSize;
                clip.SetRect(Rect(f.Tile.Inflate(inflate)), (float)(f.CornerRadius + inflate), (float)(f.CornerRadius + inflate));
                canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
                if (image is not null)
                {
                    Color(255, 255, 255, alpha); canvas.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear), paint);
                }
                else
                {
                    // The tile as vectors at its current size: .88 of the side, centred (the stage svg's 8 8 240 240 viewBox).
                    var faded = alpha < .998;
                    if (faded) { fade.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * alpha)); canvas.SaveLayer(dest, fade); }
                    var unit = (float)(.88 * side / NthMark.TileSide);
                    var box = new SKRect(tile.Left - NthMark.TileInset * unit, tile.Top - NthMark.TileInset * unit,
                        tile.Left + (NthMark.Box - NthMark.TileInset) * unit, tile.Top + (NthMark.Box - NthMark.TileInset) * unit);
                    mark.Draw(canvas, box, NthTileStyle.Stage, NthGlyph.Regular, pose);
                    if (faded) canvas.Restore();
                }
                canvas.Restore();
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

    // The settled tile for the grow, drawn once on the compositor GPU at .6 of the window's width (the grow scales it up toward
    // the window) and kept: the grow only ever shows the finished mark (the hand-over is at least 1.8 s, the mark settles at 1.36).
    private SKImage Still(SKCanvas canvas, Avalonia.Rect window)
    {
        var matrix = canvas.TotalMatrix;
        var deviceScale = Math.Sqrt(matrix.ScaleX * matrix.ScaleX + matrix.SkewY * matrix.SkewY);
        var size = Math.Round(window.Width * .6);
        var pixels = Math.Max(1, (int)Math.Round(size * deviceScale));
        if (still is not null && iconPixels == pixels) return still;
        still?.Dispose(); still = null; icon?.Dispose();
        icon = Surface(pixels, pixels) ?? throw new InvalidOperationException("Aurora boot icon surface was not created.");
        iconPixels = pixels;
        icon.Canvas.Clear(SKColors.Transparent); icon.Canvas.ResetMatrix(); icon.Canvas.Scale((float)(pixels / size));
        var unit = .88 * size / NthMark.TileSide;
        var box = (float)(.06 * size - NthMark.TileInset * unit);
        mark.Draw(icon.Canvas, new SKRect(box, box, (float)(box + NthMark.Box * unit), (float)(box + NthMark.Box * unit)), NthTileStyle.Stage, NthGlyph.Regular, NthPose.Still);
        return still = icon.Snapshot();
    }

    private SKSurface? Surface(int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        return graphics is { } gpu ? SKSurface.Create(gpu, false, info) : SKSurface.Create(info);
    }
    public void Dispose() { mark.Dispose(); paint.Dispose(); fade.Dispose(); clip.Dispose(); still?.Dispose(); icon?.Dispose(); glow?.Dispose(); }
}
