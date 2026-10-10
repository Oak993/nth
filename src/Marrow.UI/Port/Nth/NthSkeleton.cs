// PORT-ONLY: OD-0136 amendment 2, steps 5, 6, 7 and 11 (ED-NEW-nth-motion). The soft shapes that stand for content until it
// arrives, with the light that passes over them (the v2 sweep the owner liked): the diff's rows (step 5, one diagonal band
// across all bars), list rows (step 7, a glossier finish and one band per row, 70 ms apart), the attachment plate (step 6) and
// the search results' bars (step 11). The light lives only inside the shapes. When the content arrives the shapes fade in 0.2 s.
using Avalonia;
using SkiaSharp;

namespace Marrow.UI.Port.Nth;

public enum NthSkeletonKind { List, Diff, Plate, Bars }

public sealed record NthSkeletonState(NthSkeletonKind Kind, int Rows, double? FinishedAt, double[]? Widths);

/// <summary>A skeleton of one <see cref="Kind"/>. Call <see cref="Finish"/> when the content arrives (it fades out in 0.2 s).</summary>
public sealed class NthSkeleton : NthMotionControl
{
    private double? finishedAt;
    private readonly double[]? widths;

    /// <param name="kind">Which skeleton.</param>
    /// <param name="rows">List: rows (40 px each); Diff: rows that fit are drawn (18 px apart).</param>
    /// <param name="widths">Bars: each bar's share of the width (the page's search results: 0.82, 0.64, 0.74).</param>
    public NthSkeleton(NthSkeletonKind kind, int rows = 5, double[]? widths = null)
    {
        Kind = kind; Rows = rows; this.widths = widths;
        if (kind == NthSkeletonKind.List) Height = rows * 40;
        if (kind == NthSkeletonKind.Bars) Height = (widths?.Length ?? 3) * 17 - 10 + 8; // 7 px bars, 10 px apart, 4 px padding
    }

    public NthSkeletonKind Kind { get; }
    public int Rows { get; }

    /// <summary>The content arrived: the shapes fade out over 0.2 s.</summary>
    public void Finish() { finishedAt ??= Now; Send(); }

    protected override object? State => new NthSkeletonState(Kind, Rows, finishedAt, widths);

    protected override NthPainter CreatePainter() => new NthSkeletonPainter();
}

public sealed class NthSkeletonPainter : NthSkiaPainter
{
    private readonly SKPaint paint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath shapes = new();
    private readonly SKPath row = new();
    private SKShader? band, cssBand;
    private SKShader? rowFinish, unitBand;
    private (double Half, bool Dark) bandFor = (-1, false);
    private (double W, double H, bool Dark) cssFor = (-1, -1, false);
    private bool rowFor, unitFor;

    /// <summary>Diff band half width (140 px band) and list band half width (120 px band).</summary>
    public const double DiffHalf = 70, ListHalf = 60;

    public override bool Moving(NthFrame frame, double now) =>
        frame.State is NthSkeletonState s && (s.FinishedAt is not { } f || now - f < .2) && (!frame.Calm || now - frame.Started < .6);

    public override void Draw(SKCanvas canvas, NthFrame frame, double now)
    {
        if (frame.State is not NthSkeletonState s) return;
        double w = frame.Size.Width, h = frame.Size.Height;
        if (w <= 0 || h <= 0) return;
        var fade = s.FinishedAt is { } f ? 1 - NthEase.Clamp01((now - f) / .2) : 1;
        if (fade <= 0) return;
        var t = now - frame.Started;
        var palette = frame.Palette;
        switch (s.Kind)
        {
            case NthSkeletonKind.Diff: DrawDiff(canvas, w, h, t, palette, frame.Dark, frame.Calm, fade); break;
            case NthSkeletonKind.List: DrawList(canvas, w, s.Rows, t, palette, frame.Dark, frame.Calm, fade); break;
            case NthSkeletonKind.Plate: DrawPlate(canvas, w, h, t, palette, frame.Dark, frame.Calm, fade); break;
            case NthSkeletonKind.Bars: DrawBars(canvas, w, s.Widths ?? [.82, .64, .74], t, palette, frame.Dark, frame.Calm, fade); break;
        }
        paint.Shader = null;
    }

    private void Color(SKColor color, double alpha) { paint.Shader = null; paint.Color = color.WithAlpha((byte)Math.Round(color.Alpha * NthEase.Clamp01(alpha))); }

    private SKShader Band(double half, NthPalette palette, bool dark)
    {
        if (band is null || bandFor != (half, dark))
        {
            band?.Dispose();
            var c = palette.SkeletonLight;
            band = SKShader.CreateLinearGradient(new SKPoint((float)-half, 0), new SKPoint((float)half, 0), [c.WithAlpha(0), c, c.WithAlpha(0)], [0, .5f, 1], SKShaderTileMode.Clamp);
            bandFor = (half, dark);
        }
        return band;
    }

    // CSS linear-gradient(100deg, transparent 30%, light 50%, transparent 70%) over a w x h box (its own gradient line).
    private SKShader CssBand(double w, double h, NthPalette palette, bool dark)
    {
        if (cssBand is null || cssFor != (w, h, dark))
        {
            cssBand?.Dispose();
            var angle = 100 * Math.PI / 180;
            double dx = Math.Sin(angle), dy = -Math.Cos(angle);
            var half = (Math.Abs(w * Math.Sin(angle)) + Math.Abs(h * Math.Cos(angle))) / 2;
            var c = palette.SkeletonLight;
            cssBand = SKShader.CreateLinearGradient(new SKPoint((float)(w / 2 - dx * half), (float)(h / 2 - dy * half)), new SKPoint((float)(w / 2 + dx * half), (float)(h / 2 + dy * half)),
                [c.WithAlpha(0), c.WithAlpha(0), c, c.WithAlpha(0), c.WithAlpha(0)], [0, .3f, .5f, .7f, 1], SKShaderTileMode.Clamp);
            cssFor = (w, h, dark);
        }
        return cssBand;
    }

    private void Diff(double w, double h)
    {
        shapes.Rewind();
        for (var i = 0; i < NthMotion.DiffRows.Length; i++)
        {
            var y = 4 + i * 18f;
            if (y + 8 > h) break;
            var (indent, share, sign) = NthMotion.DiffRows[i];
            shapes.AddRoundRect(new SKRect(0, y, 16, y + 8), 4, 4);
            var x = 40 + indent * 12f;
            shapes.AddRoundRect(new SKRect(x, y, x + (float)Math.Max(12, (w - x) * share), y + 8), 4, 4);
        }
    }

    private void DrawDiff(SKCanvas canvas, double w, double h, double t, NthPalette palette, bool dark, bool calm, double fade)
    {
        Diff(w, h);
        for (var i = 0; i < NthMotion.DiffRows.Length; i++)
        {
            var y = 4 + i * 18f;
            if (y + 8 > h) break;
            var (indent, share, sign) = NthMotion.DiffRows[i];
            var alpha = fade * NthMotion.RowEnter(t, i, calm);
            row.Rewind();
            row.AddRoundRect(new SKRect(0, y, 16, y + 8), 4, 4);
            var x = 40 + indent * 12f;
            row.AddRoundRect(new SKRect(x, y, x + (float)Math.Max(12, (w - x) * share), y + 8), 4, 4);
            Color(palette.Skeleton, alpha);
            canvas.DrawPath(row, paint);
            if (sign != 0)
            {
                Color(palette.Text3, alpha);
                canvas.DrawRect(new SKRect(24, y + 3, 32, y + 5), paint);
                if (sign > 0) canvas.DrawRect(new SKRect(27, y, 29, y + 8), paint);
            }
        }
        if (NthMotion.SkeletonLight(t, calm) is not { } a) return;
        // one diagonal band across all bars: 140 px wide, skewed -0.4, clipped to the bars
        var x0 = -DiffHalf + (w + 2 * DiffHalf + h * .4) * a;
        canvas.Save();
        canvas.ClipPath(shapes, SKClipOperation.Intersect, true);
        canvas.Translate((float)x0, 0);
        var skew = new SKMatrix(1, -.4f, 0, 0, 1, 0, 0, 0, 1);
        canvas.Concat(in skew);
        paint.Shader = Band(DiffHalf, palette, dark);
        paint.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * fade));
        canvas.DrawRect(new SKRect((float)(-DiffHalf - 80), 0, (float)(DiffHalf + 80), (float)h), paint);
        canvas.Restore();
    }

    private SKShader RowFinish(NthPalette palette, bool dark)
    {
        if (rowFinish is null || rowFor != dark)
        {
            rowFinish?.Dispose();
            // each row a little lighter at the top: --sk-hi to --sk over the row's 22 px tile (in the row's own coordinates)
            rowFinish = SKShader.CreateLinearGradient(new SKPoint(0, 9), new SKPoint(0, 31), [palette.SkeletonHigh, palette.Skeleton], null, SKShaderTileMode.Clamp);
            rowFor = dark;
        }
        return rowFinish;
    }

    private void DrawList(SKCanvas canvas, double w, int rows, double t, NthPalette palette, bool dark, bool calm, double fade)
    {
        for (var i = 0; i < rows; i++)
        {
            row.Rewind();
            row.AddRoundRect(new SKRect(0, 9, 22, 31), 7, 7);
            var tw = (float)Math.Max(20, (w - 40) * NthMotion.ListShares[i % NthMotion.ListShares.Length]);
            row.AddRoundRect(new SKRect(34, 11, 34 + tw, 19), 4, 4);
            row.AddRoundRect(new SKRect(34, 24, 34 + tw * .6f, 30), 3, 3);
            var alpha = fade * NthMotion.RowEnter(t, i, calm);
            canvas.Save();
            canvas.Translate(0, i * 40f);
            paint.Shader = RowFinish(palette, dark);
            paint.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * alpha));
            canvas.DrawPath(row, paint);
            if (NthMotion.SkeletonLight(t - i * .07, calm) is { } a)
            {
                canvas.ClipPath(row, SKClipOperation.Intersect, true);
                canvas.Translate((float)(-ListHalf + (w + 2 * ListHalf) * a), 0);
                paint.Shader = Band(ListHalf, palette, dark);
                canvas.DrawRect(new SKRect((float)-ListHalf, 0, (float)ListHalf, 40), paint);
            }
            canvas.Restore();
        }
    }

    private void DrawPlate(SKCanvas canvas, double w, double h, double t, NthPalette palette, bool dark, bool calm, double fade)
    {
        shapes.Rewind();
        shapes.AddRoundRect(new SKRect(0, 0, (float)w, (float)h), 10, 10);
        Color(palette.Track, fade);
        canvas.DrawPath(shapes, paint);
        CssLight(canvas, shapes, w, h, t, palette, dark, calm, fade);
    }

    // The CSS band (.skb::after, .thumb .band::after): the gradient box moves translateX(-100% to 100%) in 70 % of 2.2 s.
    private void CssLight(SKCanvas canvas, SKPath clip, double w, double h, double t, NthPalette palette, bool dark, bool calm, double fade)
    {
        if (NthMotion.SkeletonLight(t, calm) is not { } a) return;
        var x = -w + 2 * w * a;
        canvas.Save();
        canvas.ClipPath(clip, SKClipOperation.Intersect, true);
        canvas.Translate((float)x, 0);
        paint.Shader = CssBand(w, h, palette, dark);
        paint.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * fade));
        canvas.DrawRect(new SKRect(0, 0, (float)w, (float)h), paint);
        canvas.Restore();
    }

    // The search bars' band: the CSS band of a 7 px bar, whose 100 degree tilt moves its light 1.2 px over the bar's height, drawn
    // level (one shader for every bar width, scaled to the bar).
    private SKShader UnitBand(NthPalette palette, bool dark)
    {
        if (unitBand is null || unitFor != dark)
        {
            unitBand?.Dispose();
            var c = palette.SkeletonLight;
            unitBand = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1, 0), [c.WithAlpha(0), c.WithAlpha(0), c, c.WithAlpha(0), c.WithAlpha(0)], [0, .3f, .5f, .7f, 1], SKShaderTileMode.Clamp);
            unitFor = dark;
        }
        return unitBand;
    }

    private void DrawBars(SKCanvas canvas, double w, double[] widths, double t, NthPalette palette, bool dark, bool calm, double fade)
    {
        for (var i = 0; i < widths.Length; i++)
        {
            var y = 4 + i * 17f; // padding 4, 7 px bars, 10 px apart
            var bw = (float)(w * widths[i]);
            row.Rewind();
            row.AddRoundRect(new SKRect(0, y, bw, y + 7), 3.5f, 3.5f);
            Color(palette.Skeleton, fade);
            canvas.DrawPath(row, paint);
            if (NthMotion.SkeletonLight(t - i * .07, calm) is not { } a) continue;
            canvas.Save();
            canvas.ClipPath(row, SKClipOperation.Intersect, true);
            canvas.Translate((float)(-bw + 2 * bw * a), y);
            canvas.Scale(bw, 1);
            paint.Shader = UnitBand(palette, dark);
            paint.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * fade));
            canvas.DrawRect(new SKRect(0, 0, 1, 7), paint);
            canvas.Restore();
        }
    }

    public override void Dispose()
    {
        paint.Dispose(); shapes.Dispose(); row.Dispose(); band?.Dispose(); cssBand?.Dispose();
        rowFinish?.Dispose(); unitBand?.Dispose();
        base.Dispose();
    }
}
