using Avalonia;
using SkiaSharp;

namespace Marrow.UI.Port;

// Locked Stage.renderB/renderRM, loading-aurora.html:618-670: the stage, its scrim, the tile's grow into the window frame, its
// clip and glow. Real desktop and main window supply the backdrop/content. OD-0136: what the tile shows is the nth mark B
// (NthMark, the stage's .bs.metal tile) moved by NthMotion.Boot (gather, draw, drop, sweep, breath), in place of the Aurora mark;
// the gather's scale and the breath replace the Aurora's .93-to-1 entrance scale in the tile's side, so from the hand-over on
// (at least 1.8 s, both are 1 there) the grow is the same rectangle as before.
public sealed class AuroraBootPainter : IDisposable
{
    private readonly NthMark mark = new();
    private readonly SKPaint paint = new() { IsAntialias = true };
    private readonly SKRoundRect clip = new();
    private SKSurface? icon, glow;
    private int iconPixels, glowWidth, glowHeight;
    private GRContext? graphics;
    private bool drawn;
    private NthPose shown;
    private static SKRect Rect(Avalonia.Rect r) => new((float)r.X, (float)r.Y, (float)r.Right, (float)r.Bottom);
    private void Color(byte r, byte g, byte b, double alpha) => paint.ColorF = new SKColorF(r / 255f, g / 255f, b / 255f, (float)alpha);
    public void Draw(SKCanvas canvas, Size area, Avalonia.Rect window, AuroraBootFrame f, double seconds, bool dark, bool reduceMotion, double opacity = 1, GRContext? gpu = null)
    {
        var saved = canvas.Save();
        try
        {
            Color(dark ? (byte)2 : (byte)8, dark ? (byte)6 : (byte)14, dark ? (byte)16 : (byte)28, (dark ? .5 : .46) * f.ScrimOpacity * opacity);
            paint.Style = SKPaintStyle.Fill; canvas.DrawRect(SKRect.Create((float)area.Width, (float)area.Height), paint);
            if (f.StageClosed) return; // OD-0136: the nth gather starts with the stage (the Aurora's launch waited .12 s)
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
                icon?.Dispose(); icon = Surface(pixels, pixels); iconPixels = pixels; drawn = false;
                if (icon is null) throw new InvalidOperationException("Aurora boot icon surface was not created.");
            }
            // The tile is drawn on the compositor GPU at the stage's tile size (.88 of the icon, as before) and redrawn only when
            // what it shows changes (the stroke, the bead, the sweep): the reduced-motion still and the settled tile are drawn once.
            var pose = NthMotion.Boot(seconds, reduceMotion);
            if (!drawn || !SameContent(pose, shown))
            {
                icon.Canvas.Clear(SKColors.Transparent); icon.Canvas.ResetMatrix(); icon.Canvas.Scale((float)(pixels / size));
                var unit = .88 * size / NthMark.TileSide;
                var box = (float)(.06 * size - NthMark.TileInset * unit);
                mark.Draw(icon.Canvas, new SKRect(box, box, (float)(box + NthMark.Box * unit), (float)(box + NthMark.Box * unit)), NthTileStyle.Stage, NthGlyph.Regular, pose);
                drawn = true; shown = pose;
            }
            using var image = icon.Snapshot();
            var sc0 = pose.GatherScale * pose.Breath; // OD-0136: .bi's gather and .bs's breath (the Aurora's own entrance scale before)
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
                // .bs's box-shadow lies on the desktop under the tile, outside the tile's clip; it lifts away in the grow's first quarter.
                var tile = SKRect.Inflate(dest, (float)(-.06 * side), (float)(-.06 * side));
                var lift = reduceMotion ? 1 : 1 - AuroraLoaderMotion.Smooth(f.Progress * 4);
                mark.DrawStageShadow(canvas, tile, f.TileOpacity * opacity * pose.GatherOpacity * lift, pose.GatherBlur);
                canvas.Save();
                var inflate = reduceMotion ? 0 : (1 - AuroraLoaderMotion.Smooth(f.Progress * 4)) * .06 * AuroraBootSequence.MarkSize;
                clip.SetRect(Rect(f.Tile.Inflate(inflate)), (float)(f.CornerRadius + inflate), (float)(f.CornerRadius + inflate));
                canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
                // .bi's gather: the whole tile fades in and sharpens from blur(14px), in the tile's own units.
                var sigma = pose.GatherBlur * tile.Width / NthMark.TileSide;
                using var gather = sigma > .05 ? SKImageFilter.CreateBlur((float)sigma, (float)sigma) : null;
                paint.ImageFilter = gather;
                Color(255, 255, 255, f.TileOpacity * opacity * pose.GatherOpacity); canvas.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear), paint); canvas.Restore();
                paint.ImageFilter = null;
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
    private static bool SameContent(in NthPose a, in NthPose b) =>
        a.Draw == b.Draw && a.BeadOpacity == b.BeadOpacity && a.BeadLift == b.BeadLift && a.BeadScale == b.BeadScale &&
        (a.Sweep == b.Sweep && a.SweepKind == b.SweepKind || !(a.Sweep > 0 && a.Sweep < 1) && !(b.Sweep > 0 && b.Sweep < 1));

    private SKSurface? Surface(int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        return graphics is { } gpu ? SKSurface.Create(gpu, false, info) : SKSurface.Create(info);
    }
    public void Dispose() { mark.Dispose(); paint.Dispose(); clip.Dispose(); icon?.Dispose(); glow?.Dispose(); }
}
