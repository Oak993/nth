// PORT-ONLY: Dextra AS-17 (DESIGN.md rev 5, 2.5 "The Avalonia port"; ED-NEW-dextra-as17-2). No Swift twin.
// An Astur's mark on a Skia canvas: Armilla's own drawing calls (the six shapes, the glyphs, circles and lines) through ArmillaSkiaCanvas, and the two it adds: a layer
// the shape is drawn into and composited with one opacity, and a cut that clears a disc inside that layer (the ring round the badge) without touching the card behind
// the mark. One instance per mark, used on one thread at a time; the paints are kept, so a frame allocates nothing.
using SkiaSharp;

namespace Marrow.UI.Port.Armilla;

internal sealed class AsturSkiaCanvas : IAsturCanvas, IDisposable
{
    private readonly ArmillaSkiaCanvas inner = new();
    private readonly SKPaint layer = new();
    private readonly SKPaint clear = new() { IsAntialias = true, Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.Clear };
    private SKCanvas? canvas;

    /// <summary>Points the drawing at <paramref name="target"/>, whose origin the caller has put at the slot's centre.</summary>
    public void Begin(SKCanvas target)
    {
        canvas = target;
        inner.Begin(target);
    }

    private SKCanvas C => canvas ?? throw new InvalidOperationException("Begin was not called.");

    public void Save() => inner.Save();

    public void Restore() => inner.Restore();

    public void Translate(double dx, double dy) => inner.Translate(dx, dy);

    public void Rotate(double degrees) => inner.Rotate(degrees);

    public void StrokeArc(double r, double fromDeg, double toDeg, double width, ArmillaRgb color, double alpha, bool roundCap) =>
        inner.StrokeArc(r, fromDeg, toDeg, width, color, alpha, roundCap);

    public void StrokeLine(double x0, double y0, double x1, double y1, double width, ArmillaRgb color, double alpha, bool roundCap) =>
        inner.StrokeLine(x0, y0, x1, y1, width, color, alpha, roundCap);

    public void FillCircle(double x, double y, double r, ArmillaRgb color, double alpha) => inner.FillCircle(x, y, r, color, alpha);

    public void FillHaze(double r, ArmillaRgb color, double centreAlpha) => inner.FillHaze(r, color, centreAlpha);

    public void FillShape(ArmillaShape shape, double x, double y, double r, ArmillaRgb color, double alpha) => inner.FillShape(shape, x, y, r, color, alpha);

    public void Glyph(ArmillaGlyph glyph, double unit, double lineWidth, ArmillaRgb color, double alpha) => inner.Glyph(glyph, unit, lineWidth, color, alpha);

    public void BeginLayer(double opacity, double half)
    {
        layer.Color = new SKColor(255, 255, 255, (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255));
        // the layer is the slot and a pixel round it, not the whole clip: a mark in a long list costs a mark's worth of memory
        var h = (float)half + 1;
        C.SaveLayer(new SKRect(-h, -h, h, h), layer);
    }

    public void Cut(double x, double y, double r)
    {
        if (r <= 0) return;
        C.DrawCircle((float)x, (float)y, (float)r, clear);
    }

    public void EndLayer() => C.Restore();

    public void Dispose()
    {
        inner.Dispose();
        layer.Dispose();
        clear.Dispose();
    }
}
