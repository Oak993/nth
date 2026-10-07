// PORT-ONLY: MA-4-2, locked drawA :3098-3160. Native Skia objects are retained; Draw allocates nothing managed.
using Marrow.Core.Port.MultiAccount.Handshake;
using SkiaSharp;
namespace Marrow.App.Port.MultiAccount.Handshake;
internal sealed class HandshakePainter : IDisposable
{
    private readonly SKPath path = new();
    private readonly SKPaint strand = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint glow = new() { IsAntialias = true };
    private readonly SKPaint shadow = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKShader gradient;
    private readonly SKShader[] radial = new SKShader[257];
    private readonly SKMaskFilter blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4);
    private readonly (byte R, byte G, byte B) outgoing, incoming;
    private readonly SKColor middle;
    internal HandshakePainter(double width, double height, (byte R, byte G, byte B) outgoing, (byte R, byte G, byte B) incoming)
    {
        this.outgoing = outgoing; this.incoming = incoming;
        middle = new SKColor((byte)((outgoing.R + incoming.R) / 2), (byte)((outgoing.G + incoming.G) / 2), (byte)((outgoing.B + incoming.B) / 2));
        gradient = SKShader.CreateLinearGradient(new SKPoint((float)width - 46, (float)height - 26), new SKPoint((float)width - 118, (float)height - 82), [Color(outgoing), Color(incoming)], [0, 1], SKShaderTileMode.Clamp);
        for (var i = 0; i < radial.Length; i++)
        {
            var s = i / 255.0; var c = i == 256 ? SKColors.White : new SKColor((byte)Math.Round(outgoing.R + (incoming.R - outgoing.R) * s), (byte)Math.Round(outgoing.G + (incoming.G - outgoing.G) * s), (byte)Math.Round(outgoing.B + (incoming.B - outgoing.B) * s));
            radial[i] = SKShader.CreateRadialGradient(SKPoint.Empty, 1, [c, c.WithAlpha(0)], [0, 1], SKShaderTileMode.Clamp);
        }
        strand.Shader = gradient; shadow.MaskFilter = blur;
    }
    internal void Draw(SKCanvas canvas, HandshakeFrame frame, double milliseconds, bool dark, bool reduced, double opacity = 1)
    {
        var mode = dark ? SKBlendMode.Plus : SKBlendMode.SrcOver; strand.BlendMode = glow.BlendMode = shadow.BlendMode = mode;
        var lk = HandshakeGeometry.Smooth(2000, 3000, milliseconds); var sy = HandshakeGeometry.Smooth(3000, 3800, milliseconds);
        for (var i = 0; i < frame.Strokes.Count; i++)
        {
            var stroke = frame.Strokes[i]; path.Rewind();
            for (var k = 0; k < stroke.Points.Count; k++) { var p = stroke.Points[k]; if (p.Move) path.MoveTo((float)p.X, (float)p.Y); else path.LineTo((float)p.X, (float)p.Y); }
            if (!reduced && stroke.Width >= 1.2)
            {
                var lkl = Math.Clamp((milliseconds - 2000) / 1000, 0, 1); var sw = Math.Clamp((milliseconds - 4400) / 620, 0, 1);
                var core = HandshakeGeometry.Smooth(.6, 1, lkl) * (1 - HandshakeGeometry.Smooth(.8, 1, sw));
                shadow.Color = middle.WithAlpha(Alpha(.7 * core * stroke.Alpha * opacity)); shadow.StrokeWidth = (float)stroke.Width; canvas.DrawPath(path, shadow);
            }
            strand.Color = SKColors.White.WithAlpha(Alpha(stroke.Alpha * opacity * (reduced ? .25 * lk + .5 * sy : 1))); strand.StrokeWidth = (float)stroke.Width; canvas.DrawPath(path, strand);
        }
        if (reduced) return;
        for (var i = 0; i < frame.Glows.Count; i++)
        {
            var g = frame.Glows[i]; glow.Shader = radial[Index(g.R, g.G, g.B)]; glow.Color = SKColors.White.WithAlpha(Alpha(g.Alpha * opacity));
            canvas.Save(); canvas.Translate((float)g.X, (float)g.Y); canvas.Scale((float)g.Radius); canvas.DrawRect(new SKRect(-1, -1, 1, 1), glow); canvas.Restore();
        }
    }
    private int Index(double r, double g, double b)
    {
        if (r == 255 && g == 255 && b == 255) return 256;
        var s = incoming.R != outgoing.R ? (r - outgoing.R) / (incoming.R - outgoing.R) : incoming.G != outgoing.G ? (g - outgoing.G) / (incoming.G - outgoing.G) : incoming.B != outgoing.B ? (b - outgoing.B) / (incoming.B - outgoing.B) : 0;
        return (int)Math.Round(Math.Clamp(s, 0, 1) * 255);
    }
    private static SKColor Color((byte R, byte G, byte B) value) => new(value.R, value.G, value.B);
    private static byte Alpha(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    public void Dispose() { path.Dispose(); strand.Dispose(); glow.Dispose(); shadow.Dispose(); gradient.Dispose(); blur.Dispose(); foreach (var shader in radial) shader.Dispose(); }
}
