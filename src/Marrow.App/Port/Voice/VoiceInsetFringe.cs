// PORT-ONLY, T33 / OD-0121: the design's inset dispersion shadows, in CSS geometry and sigma.
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The two dispersion-fringe insets, <c>inset offset 0 blur -.1px</c> in blue (negative offset) and pink (positive offset): the padding-box rounded rectangle minus the same
/// rectangle grown by the spread -0.1 and moved by the offset, painted through a Gaussian of the CSS blur's half-radius sigma (CSS blur .8 / 2) and clipped to the padding box.
/// Blue first, pink over it (the stack lists pink first).
/// </summary>
internal sealed class VoiceInsetFringe(Rect box, Color first, double firstOffset, Color second, double secondOffset, float sigma) : ICustomDrawOperation
{
    private const float Spread = -0.1f;

    public Rect Bounds => box;

    public bool HitTest(Point point) => false;

    public bool Equals(ICustomDrawOperation? other) => false;

    public void Dispose() { }

    public void Render(ImmediateDrawingContext context)
    {
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature is null) return;
        using var lease = feature.Lease();
        var canvas = lease.SkCanvas;
        var rect = new SKRect((float)box.Left, (float)box.Top, (float)box.Right, (float)box.Bottom);
        var radius = rect.Height / 2;
        using var clip = new SKRoundRect(rect, radius, radius);
        var saved = canvas.Save();
        try
        {
            canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
            Draw(canvas, rect, first, (float)firstOffset, (float)lease.CurrentOpacity);
            Draw(canvas, rect, second, (float)secondOffset, (float)lease.CurrentOpacity);
        }
        finally { canvas.RestoreToCount(saved); }
    }

    private void Draw(SKCanvas canvas, SKRect rect, Color color, float offset, float opacity)
    {
        var hole = new SKRect(rect.Left + Spread + offset, rect.Top + Spread, rect.Right - Spread + offset, rect.Bottom - Spread);
        var holeRadius = hole.Height / 2;
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        path.AddRect(SKRect.Inflate(rect, 8, 8));
        path.AddRoundRect(hole, holeRadius, holeRadius);
        using var filter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(color.R, color.G, color.B, (byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1))),
            MaskFilter = filter,
        };
        canvas.DrawPath(path, paint);
    }
}
