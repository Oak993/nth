// PORT-ONLY, T33 / OD-0123/0125: CSS background-clip:text paints through a black glyph mask.
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Marrow.App.Port.Voice;

/// <summary>
/// Rasterizes the label's shaped runs as one coverage mask on the current target, then colors it with the sweep (the design's <c>background-clip: text</c>).
/// The coverage is the approved design's (<see cref="VoicePillGlyphMasks"/>), its glyphs placed as Chrome places them: the label box's device centre snapped to a
/// whole pixel, the baseline from the face's rounded ascent and descent under the page's line height, glyph origins on Chrome's quarter-pixel grid from
/// 16.16 advances. Where that is unavailable (not Windows, a rotated or skewed transform), the runs are drawn by Avalonia in black and colored the same way.
/// No bitmap or picture-shader resampling is used: the mask lands on whole device pixels.
/// </summary>
internal sealed class VoicePillTextMask : ICustomDrawOperation
{
    // voice-composer.html `.pl{line-height:1.1}`: the line box the design centres (translate -50%), which with Blink's floored half-leading places the baseline.
    private const double DesignLineHeight = 1.1;

    private readonly (IImmutableGlyphRunReference Glyph, Matrix Transform, IImmutableBrush Brush)[] runs;
    private readonly (GlyphRun Run, Matrix Transform, double Alpha)[] shaped;
    private readonly ImmutableLinearGradientBrush gradient;
    private readonly Rect box;

    public VoicePillTextMask(DrawingGroup drawing, Rect box, LinearGradientBrush gradient)
    {
        var captured = new List<(IImmutableGlyphRunReference, Matrix, IImmutableBrush)>();
        var glyphs = new List<(GlyphRun, Matrix, double)>();
        Capture(drawing, Matrix.Identity, captured, glyphs);
        runs = captured.ToArray();
        shaped = glyphs.ToArray();
        this.box = box;
        // the design's baseline may sit a pixel from the layout's, and the three-column coverage reaches a pixel past the outline
        Bounds = box.Union(drawing.GetBounds()).Inflate(2);
        this.gradient = new ImmutableLinearGradientBrush(gradient);
    }

    public Rect Bounds { get; }
    public bool HitTest(Point point) => false;
    public bool Equals(ICustomDrawOperation? other) => false;

    public void Dispose()
    {
        foreach (var run in runs) run.Glyph.Dispose();
    }

    private static void Capture(Drawing drawing, Matrix transform,
        List<(IImmutableGlyphRunReference, Matrix, IImmutableBrush)> captured, List<(GlyphRun, Matrix, double)> glyphs)
    {
        if (drawing is DrawingGroup group)
        {
            var next = (group.Transform?.Value ?? Matrix.Identity) * transform;
            foreach (var child in group.Children) Capture(child, next, captured, glyphs);
        }
        else if (drawing is GlyphRunDrawing { GlyphRun: { } glyph, Foreground: { } brush }
            && glyph.TryCreateImmutableGlyphRunReference() is { } reference)
        {
            captured.Add((reference, transform, brush.ToImmutable()));
            var alpha = brush is ISolidColorBrush solid ? solid.Opacity * solid.Color.A / 255.0 : brush.Opacity;
            glyphs.Add((glyph, transform, alpha));
        }
    }

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
        int saveCount;
        bool designed;
        using (var lease = feature.Lease())
        {
            var canvas = lease.SkCanvas;
            saveCount = canvas.SaveCount;
            canvas.SaveLayer(new SKRect((float)Bounds.Left, (float)Bounds.Top, (float)Bounds.Right, (float)Bounds.Bottom), null);
            designed = DrawDesignMask(canvas, lease.CurrentOpacity);
        }
        try
        {
            if (!designed)
            {
                // End the lease before Avalonia draws; its normal font path consumes the caller's text options.
                foreach (var run in runs)
                {
                    using var transform = context.PushPreTransform(run.Transform);
                    context.DrawGlyphRun(run.Brush, run.Glyph);
                }
            }
            using var lease = feature.Lease();
            var start = gradient.StartPoint.Point;
            var end = gradient.EndPoint.Point;
            using var shader = SKShader.CreateLinearGradient(new SKPoint((float)start.X, (float)start.Y), new SKPoint((float)end.X, (float)end.Y),
                gradient.GradientStops.Select(s => new SKColor(s.Color.R, s.Color.G, s.Color.B, s.Color.A)).ToArray(),
                gradient.GradientStops.Select(s => (float)s.Offset).ToArray(), SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.SrcIn, IsAntialias = false };
            // The coverage has already consumed the current opacity, once.
            lease.SkCanvas.DrawPaint(paint);
        }
        finally
        {
            using var lease = feature.Lease();
            lease.SkCanvas.RestoreToCount(saveCount);
        }
    }

    // The label's coverage as the design draws it, onto the layer at whole device pixels; false (nothing drawn) where it cannot be made.
    private bool DrawDesignMask(SKCanvas canvas, double opacity)
    {
        var m = canvas.TotalMatrix;
        if (shaped.Length == 0 || m.SkewX != 0 || m.SkewY != 0 || m.Persp0 != 0 || m.Persp1 != 0 || m.ScaleX <= 0 || Math.Abs(m.ScaleX - m.ScaleY) > 1e-5) return false;
        double scale = m.ScaleX;
        var placed = new List<(int X, int Y, VoicePillGlyphMask Mask, double Alpha)>();
        foreach (var (run, transform, alpha) in shaped)
        {
            if (alpha <= 0) continue;
            var typeface = run.GlyphTypeface;
            var em = run.FontRenderingEmSize * scale;
            var metrics = typeface.Metrics;
            var origin = transform.Transform(run.BaselineOrigin);
            // Blink: the box's layout position in 1/64 pixel units, its centre (top: 50%) snapped to a whole pixel; the line box centred on it (translate -50%);
            // the face's ascent and descent rounded, the half-leading floored; the glyphs on the rounded baseline.
            var centreY = m.TransY + scale * box.Center.Y;
            var lineBox = DesignLineHeight * em;
            var ascent = Math.Floor(Math.Abs(metrics.Ascent) * em / metrics.DesignEmHeight + 0.5);
            var descent = Math.Floor(Math.Abs(metrics.Descent) * em / metrics.DesignEmHeight + 0.5);
            var baseline = (int)Math.Floor(Math.Floor(Math.Round(centreY * 64) / 64 + 0.5) - lineBox / 2 + Math.Floor((lineBox - ascent - descent) / 2) + ascent + 0.5);
            var left = Math.Floor((m.TransX + scale * origin.X) * 64) / 64;
            long pen = 0; // 16.16, as Blink's HarfBuzz positions
            foreach (var info in run.GlyphInfos)
            {
                var x = left + (float)((pen + (long)(info.GlyphOffset.X * scale * 65536)) / 65536.0) + 0.125;
                var whole = (int)Math.Floor(x);
                var quarter = (int)Math.Floor((x - whole) * 4) & 3;
                var mask = VoicePillGlyphMasks.Mask(typeface, em, info.GlyphIndex, quarter);
                if (mask is null) return false;
                if (mask.Width > 0) placed.Add((whole + mask.Left, baseline + (int)Math.Round(info.GlyphOffset.Y * scale) + mask.Top, mask, alpha));
                pen += (long)(info.GlyphAdvance * scale * 65536);
            }
        }
        if (placed.Count == 0) return true;
        var x0 = placed.Min(p => p.X);
        var y0 = placed.Min(p => p.Y);
        var width = placed.Max(p => p.X + p.Mask.Width) - x0;
        var height = placed.Max(p => p.Y + p.Mask.Height) - y0;
        var coverage = new float[width * height];
        foreach (var (px, py, mask, alpha) in placed)
            for (var r = 0; r < mask.Height; r++)
            for (var c = 0; c < mask.Width; c++)
            {
                var at = (py - y0 + r) * width + px - x0 + c;
                var a = (float)(mask.Alpha[r * mask.Width + c] / 255.0 * alpha);
                coverage[at] += a * (1 - coverage[at]); // the glyphs composite source-over on the layer, as Skia blits them
            }
        var bytes = new byte[width * height];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)Math.Clamp((int)Math.Floor(coverage[i] * 255 + 0.5), 0, 255);
        using var image = SKImage.FromPixelCopy(new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul), bytes, width);
        if (image is null) return false;
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        paint.ColorF = new SKColorF(0, 0, 0, (float)Math.Clamp(opacity, 0, 1));
        canvas.Save();
        canvas.ResetMatrix();
        canvas.DrawImage(image, x0, y0, paint);
        canvas.Restore();
        return true;
    }
}
