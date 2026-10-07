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
    private readonly Dictionary<int, SKShader> radial = [];
    private readonly SKMaskFilter blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4);
    private readonly SKColor middle;
    private readonly double width, height;
    private readonly (byte R, byte G, byte B) outgoing, incoming;
    internal HandshakePainter(double width, double height, (byte R, byte G, byte B) outgoing, (byte R, byte G, byte B) incoming)
    {
        this.width = width; this.height = height; this.outgoing = outgoing; this.incoming = incoming;
        middle = new SKColor((byte)((outgoing.R + incoming.R) / 2), (byte)((outgoing.G + incoming.G) / 2), (byte)((outgoing.B + incoming.B) / 2));
        gradient = SKShader.CreateLinearGradient(new SKPoint((float)width - 46, (float)height - 26), new SKPoint((float)width - 118, (float)height - 82), [Color(outgoing), Color(incoming)], [0, 1], SKShaderTileMode.Clamp);
        // The page rounds each mixed RGB channel. Cache every interval and rounding boundary, rather than
        // quantizing the mixing fraction (which can shift a channel by one). At most 766 endpoint blends.
        var stops = new List<double> { 0, 1 }; AddStops(outgoing.R, incoming.R); AddStops(outgoing.G, incoming.G); AddStops(outgoing.B, incoming.B); stops.Sort();
        for (var i = 0; i < stops.Count; i++)
        {
            AddBlend(stops[i]); if (i > 0) AddBlend((stops[i] + stops[i - 1]) / 2);
        }
        Add(SKColors.White);
        strand.Shader = gradient; shadow.MaskFilter = blur;
        void AddStops(byte a, byte b) { if (a == b) return; for (var n = Math.Min(a, b); n < Math.Max(a, b); n++) stops.Add((n + .5 - a) / (b - a)); }
        void AddBlend(double s) => Add(new SKColor(Round(outgoing.R + (incoming.R - outgoing.R) * s), Round(outgoing.G + (incoming.G - outgoing.G) * s), Round(outgoing.B + (incoming.B - outgoing.B) * s)));
        void Add(SKColor c) { var key = Key(c.Red, c.Green, c.Blue); if (!radial.ContainsKey(key)) radial.Add(key, SKShader.CreateRadialGradient(SKPoint.Empty, 1, [c, c.WithAlpha(0)], [0, 1], SKShaderTileMode.Clamp)); }
    }
    // The live path consumes a value snapshot and emits exactly the oracle's points into the retained path.
    // Arc/Normal/Smooth/Ease are the Core oracle's allocation-free value functions; no frame lists are built.
    internal void Draw(SKCanvas canvas, HandshakeFrameSnapshot frame, bool dark, double opacity = 1)
    {
        var t = frame.VirtualMilliseconds; var sb = HandshakeGeometry.Smooth(1000, 1600, t); var lk = HandshakeGeometry.Smooth(2000, 3000, t);
        var lkl = Math.Clamp((t - 2000) / 1000, 0, 1); var sy = HandshakeGeometry.Smooth(3000, 3800, t); var sw = Math.Clamp((t - 4400) / 620, 0, 1);
        var post = HandshakeGeometry.Smooth(4900, 5700, t); var end = 1 - HandshakeGeometry.Ease(sw);
        var mode = dark ? SKBlendMode.Plus : SKBlendMode.SrcOver; strand.BlendMode = glow.BlendMode = shadow.BlendMode = mode;
        if (frame.UseCrossfade)
        {
            var alpha = .25 * lk + .5 * sy;
            if (alpha > .01 && sw < 1) { ArcPath(1); Stroke(canvas, 1.4, (1 - sw) * opacity * alpha); }
            return;
        }
        GlowAt(canvas, HandshakeGeometry.Arc(width, height, 0), 24, outgoing.R + (incoming.R - outgoing.R) * sy, outgoing.G + (incoming.G - outgoing.G) * sy, outgoing.B + (incoming.B - outgoing.B) * sy, .16 * (lk + sy) * (1 - post) * opacity);
        var grow = Math.Max(.14 * sb, HandshakeGeometry.Smooth(0, .55, lkl)); var braid = HandshakeGeometry.Smooth(.3, 1, lkl); var pres = sb * (.5 + .5 * lk) * (1 - HandshakeGeometry.Smooth(.85, 1, sw));
        for (var i = 0; i < 9; i++)
        {
            var ph = i * Math.PI * 2 / 9; var fq = 2.1 + i * .37; var pen = false; path.Rewind();
            for (var k = 0; k <= 48; k++)
            {
                var s = k / 48.0; var visible = (s <= grow * .5 || s >= 1 - grow * .5) && s <= end + .001;
                if (!visible) { pen = false; continue; }
                var env = Math.Sin(Math.PI * s);
                var loose = (1 - braid) * 11 * env * Math.Sin(fq * s * 6.283 + t * .0021 * (1 + i * .13) + ph) * (.6 + .4 * Math.Sin(t * .0013 + i));
                var tight = braid * (2.4 - 1.4 * sy) * env * Math.Sin(s * 24 - t * .0065 + ph); var b = HandshakeGeometry.Arc(width, height, s); var n = HandshakeGeometry.Normal(s);
                var x = (float)(b.X + n.X * (loose + tight)); var y = (float)(b.Y + n.Y * (loose + tight));
                if (pen) path.LineTo(x, y); else path.MoveTo(x, y); pen = true;
            }
            Stroke(canvas, dark ? .6 : .75, (dark ? .42 : .55) * pres * (.55 + .45 * Math.Sin(t / 900 + i)) * opacity);
        }
        var core = HandshakeGeometry.Smooth(.6, 1, lkl) * (1 - HandshakeGeometry.Smooth(.8, 1, sw));
        if (core > .01)
        {
            var beat = .75 + .25 * Math.Sin(t / 170); var alpha = (.35 + .35 * sy * beat) * core; var thickness = 1.2 + .6 * sy; ArcPath(end);
            shadow.Color = middle.WithAlpha(Alpha(.7 * core * alpha * opacity)); shadow.StrokeWidth = (float)thickness; canvas.DrawPath(path, shadow); Stroke(canvas, thickness, alpha * opacity);
        }
        var pa = HandshakeGeometry.Smooth(.3, .6, lkl) * (1 - HandshakeGeometry.Smooth(0, .4, sw));
        if (pa <= .01) return;
        var locked = HandshakeGeometry.Smooth(.55, 1, lkl); var drift = (1 - locked) * .37;
        for (var j = 0; j < 2; j++)
        {
            var f = t / 720 + j / 2.0; var so = f - Math.Floor(f); f += drift; var si = 1 - (f - Math.Floor(f)); var meet = Math.Max(0, 1 - Math.Abs(so - si) / .08) * locked;
            var p = HandshakeGeometry.Arc(width, height, so); GlowAt(canvas, p, 8 + meet * 6, outgoing.R, outgoing.G, outgoing.B, (.55 + .4 * meet) * pa * opacity); GlowAt(canvas, p, 2.2, 255, 255, 255, .85 * pa * opacity);
            p = HandshakeGeometry.Arc(width, height, si); GlowAt(canvas, p, 8 + meet * 6, incoming.R, incoming.G, incoming.B, (.55 + .4 * meet) * pa * opacity); GlowAt(canvas, p, 2.2, 255, 255, 255, .85 * pa * opacity);
        }
    }
    private void ArcPath(double end)
    {
        path.Rewind(); for (var k = 0; k <= 30; k++) { var p = HandshakeGeometry.Arc(width, height, k / 30.0 * end); if (k == 0) path.MoveTo((float)p.X, (float)p.Y); else path.LineTo((float)p.X, (float)p.Y); }
    }
    private void Stroke(SKCanvas canvas, double thickness, double alpha) { strand.Color = SKColors.White.WithAlpha(Alpha(alpha)); strand.StrokeWidth = (float)thickness; canvas.DrawPath(path, strand); }
    private void GlowAt(SKCanvas canvas, HandshakePoint p, double radius, double r, double g, double b, double alpha)
    {
        if (alpha <= .01) return; glow.Shader = radial[Key(Round(r), Round(g), Round(b))]; glow.Color = SKColors.White.WithAlpha(Alpha(alpha));
        canvas.Save(); canvas.Translate((float)p.X, (float)p.Y); canvas.Scale((float)radius); canvas.DrawRect(new SKRect(-1, -1, 1, 1), glow); canvas.Restore();
    }
    internal void Draw(SKCanvas canvas, HandshakeFrame frame, double milliseconds, bool dark, bool reduced, double opacity = 1)
    {
        var mode = dark ? SKBlendMode.Plus : SKBlendMode.SrcOver; strand.BlendMode = glow.BlendMode = shadow.BlendMode = mode;
        var lk = HandshakeGeometry.Smooth(2000, 3000, milliseconds); var sy = HandshakeGeometry.Smooth(3000, 3800, milliseconds);
        if (!reduced && frame.Glows.Count > 0) { var g = frame.Glows[0]; GlowAt(canvas, new HandshakePoint(g.X, g.Y), g.Radius, g.R, g.G, g.B, g.Alpha * opacity); }
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
        for (var i = 1; i < frame.Glows.Count; i++)
        {
            var g = frame.Glows[i]; glow.Shader = radial[Key(Round(g.R), Round(g.G), Round(g.B))]; glow.Color = SKColors.White.WithAlpha(Alpha(g.Alpha * opacity));
            canvas.Save(); canvas.Translate((float)g.X, (float)g.Y); canvas.Scale((float)g.Radius); canvas.DrawRect(new SKRect(-1, -1, 1, 1), glow); canvas.Restore();
        }
    }
    private static byte Round(double value) => (byte)Math.Clamp(Math.Floor(value + .5), 0, 255);
    private static int Key(byte r, byte g, byte b) => r << 16 | g << 8 | b;
    private static SKColor Color((byte R, byte G, byte B) value) => new(value.R, value.G, value.B);
    private static byte Alpha(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    public void Dispose() { path.Dispose(); strand.Dispose(); glow.Dispose(); shadow.Dispose(); gradient.Dispose(); blur.Dispose(); foreach (var shader in radial.Values) shader.Dispose(); }
}
