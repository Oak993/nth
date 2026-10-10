// PORT-ONLY: OD-0136 amendment 2, step 2 option B "Breathing letters" glyph (ED-NEW-nth-motion). Three short strokes on one
// circle (14 px box, stroke 1.7, round caps, --mark) turn together 360 degrees per 2.2 s; every 1.4 s they gather close and
// spread out evenly, like a slow breath. Calm: still and spread, opacity 0.6 to 1 every 2.4 s.
using Avalonia;
using SkiaSharp;

namespace Marrow.UI.Port.Nth;

/// <summary>Step 2 option B's glyph, 14 px.</summary>
public sealed class NthThinkingGlyph : NthMotionControl
{
    public NthThinkingGlyph() { Width = 14; Height = 14; }

    protected override object? State => null;

    protected override NthPainter CreatePainter() => new NthThinkingGlyphPainter();
}

public sealed class NthThinkingGlyphPainter : NthSkiaPainter
{
    private readonly SKPaint paint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeWidth = (float)NthMotion.GlyphStroke };
    private readonly SKPath path = new();

    public override bool Moving(NthFrame frame, double now) => true;

    public override void Draw(SKCanvas canvas, NthFrame frame, double now)
    {
        var size = Math.Min(frame.Size.Width, frame.Size.Height);
        if (size <= 0) return;
        var arcs = NthMotion.Glyph(now - frame.Started, frame.Calm);
        var radius = (float)((size - NthMotion.GlyphStroke) / 2 - .5);
        float cx = (float)(frame.Size.Width / 2), cy = (float)(frame.Size.Height / 2);
        var mark = frame.Palette.Mark;
        paint.Color = mark.WithAlpha((byte)Math.Round(mark.Alpha * NthEase.Clamp01(arcs.Alpha)));
        path.Rewind();
        var oval = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
        for (var i = 0; i < 3; i++)
        {
            var centre = arcs.Rotation + i * arcs.Spacing;
            path.AddArc(oval, (float)((centre - arcs.Sweep / 2) * 180 / Math.PI), (float)(arcs.Sweep * 180 / Math.PI));
        }
        canvas.DrawPath(path, paint);
    }

    public override void Dispose()
    {
        paint.Dispose(); path.Dispose();
        base.Dispose();
    }
}
