using SkiaSharp;

namespace Marrow.App.Views.Composer;

// Draw into the transparent bubble layer: destination-over must be beneath its white rings, not beneath opaque liquid.
internal sealed class StrikeBurstPainter : IDisposable
{
    private readonly StrikeBurstDrawing drawing = new();
    private readonly SKPath path = new();
    private readonly SKPaint paint = new() { Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, IsAntialias = true };
    public void Draw(SKCanvas canvas, StrikeContacts contacts, StrikeGeometry g, double time, StrikeInk ink)
    {
        for (var i = 0; i < contacts.RecentCount; i++)
        {
            drawing.Resolve(g, contacts.Recent[i], time, ink); if (drawing.RingCount == 0) continue;
            path.Rewind();
            for (var j = 0; j < drawing.ArcCount; j++)
            {
                var a = drawing.Arcs[j]; path.MoveTo((float)(a.X + a.Radius * Math.Cos(a.Start)), (float)(a.Y + a.Radius * Math.Sin(a.Start)));
                path.ArcTo(new SKRect((float)(a.X - a.Radius), (float)(a.Y - a.Radius), (float)(a.X + a.Radius), (float)(a.Y + a.Radius)), (float)(a.Start * 180 / Math.PI), (float)((a.End - a.Start) * 180 / Math.PI), false);
            }
            for (var ring = 0; ring < drawing.RingCount; ring++)
            {
                var c = ring == 0 ? SKColors.White : new SKColor(20, 22, 30); var alpha = ring == 0 ? drawing.RingAlpha : drawing.DarkRingAlpha;
                paint.StrokeWidth = (float)(drawing.RingWidth + (ring == 1 ? 1.4 : 0)); paint.ColorF = new SKColorF(c.Red / 255f, c.Green / 255f, c.Blue / 255f, (float)alpha); paint.BlendMode = ring == 0 ? SKBlendMode.SrcOver : SKBlendMode.DstOver; canvas.DrawPath(path, paint);
            }
        }
        paint.BlendMode = SKBlendMode.SrcOver;
    }
    public void Dispose() { path.Dispose(); paint.Dispose(); }
}
