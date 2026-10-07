// PORT-ONLY, T33 / OD-0123/0125: CSS background-clip:text paints through a black glyph mask.
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Marrow.App.Port.Voice;

/// <summary>
/// Rasterizes the existing shaped runs in black on the current target, then colors their alpha with the sweep.
/// Direct gradient glyph paint changes Skia's luminance-dependent coverage correction. The same-target layer
/// preserves the real device transform and subpixel positions; no bitmap or picture-shader resampling is used.
/// </summary>
internal sealed class VoicePillTextMask : ICustomDrawOperation
{
    private readonly (IImmutableGlyphRunReference Glyph, Matrix Transform, IImmutableBrush Brush)[] runs;
    private readonly ImmutableLinearGradientBrush gradient;

    public VoicePillTextMask(DrawingGroup drawing, Rect box, LinearGradientBrush gradient)
    {
        var captured = new List<(IImmutableGlyphRunReference, Matrix, IImmutableBrush)>();
        Capture(drawing, Matrix.Identity, captured);
        runs = captured.ToArray();
        Bounds = box.Union(drawing.GetBounds());
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
        List<(IImmutableGlyphRunReference, Matrix, IImmutableBrush)> captured)
    {
        if (drawing is DrawingGroup group)
        {
            var next = (group.Transform?.Value ?? Matrix.Identity) * transform;
            foreach (var child in group.Children) Capture(child, next, captured);
        }
        else if (drawing is GlyphRunDrawing { GlyphRun: { } glyph, Foreground: { } brush }
            && glyph.TryCreateImmutableGlyphRunReference() is { } reference)
            captured.Add((reference, transform, brush.ToImmutable()));
    }

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
        int saveCount;
        using (var lease = feature.Lease())
        {
            saveCount = lease.SkCanvas.SaveCount;
            lease.SkCanvas.SaveLayer(new SKRect((float)Bounds.Left, (float)Bounds.Top, (float)Bounds.Right, (float)Bounds.Bottom), null);
        }
        try
        {
            // End the lease before Avalonia draws; its normal font path consumes the caller's text options.
            foreach (var run in runs)
            {
                using var transform = context.PushPreTransform(run.Transform);
                context.DrawGlyphRun(run.Brush, run.Glyph);
            }
            using var lease = feature.Lease();
            var start = gradient.StartPoint.Point;
            var end = gradient.EndPoint.Point;
            using var shader = SKShader.CreateLinearGradient(new SKPoint((float)start.X, (float)start.Y), new SKPoint((float)end.X, (float)end.Y),
                gradient.GradientStops.Select(s => new SKColor(s.Color.R, s.Color.G, s.Color.B, s.Color.A)).ToArray(),
                gradient.GradientStops.Select(s => (float)s.Offset).ToArray(), SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.SrcIn, IsAntialias = false };
            // The glyph draw has already consumed the current opacity, once.
            lease.SkCanvas.DrawPaint(paint);
        }
        finally
        {
            using var lease = feature.Lease();
            lease.SkCanvas.RestoreToCount(saveCount);
        }
    }
}
