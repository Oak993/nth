// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.5 "The Avalonia port"; ED-NEW-dextra-as8-1). No Swift twin.
// Armilla's drawing calls on a Skia canvas, as the canvas's 2D context draws them: arcs from twelve o'clock clockwise, butt or
// round caps, the core haze as a radial gradient, the six node shapes with one radius, and the three centre glyphs. The paints
// and paths are kept and reused, so a frame allocates nothing; one instance per mark, used on one thread at a time.
using SkiaSharp;

namespace Marrow.UI.Port.Armilla;

internal sealed class ArmillaSkiaCanvas : IArmillaCanvas, IDisposable
{
    private readonly SKPaint stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath path = new();
    private SKCanvas? canvas;

    /// <summary>Points the drawing at <paramref name="target"/>, whose origin the caller has put at the slot's centre.</summary>
    public void Begin(SKCanvas target) => canvas = target;

    private SKCanvas C => canvas ?? throw new InvalidOperationException("Begin was not called.");

    private static SKColor Color(ArmillaRgb c, double alpha) =>
        new((byte)Math.Clamp((int)c.R, 0, 255), (byte)Math.Clamp((int)c.G, 0, 255), (byte)Math.Clamp((int)c.B, 0, 255), (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255));

    public void Save() => C.Save();

    public void Restore() => C.Restore();

    public void Translate(double dx, double dy) => C.Translate((float)dx, (float)dy);

    public void Rotate(double degrees) => C.RotateDegrees((float)degrees);

    public void StrokeArc(double r, double fromDeg, double toDeg, double width, ArmillaRgb color, double alpha, bool roundCap)
    {
        stroke.Color = Color(color, alpha);
        stroke.StrokeWidth = (float)width;
        stroke.StrokeCap = roundCap ? SKStrokeCap.Round : SKStrokeCap.Butt;
        var sweep = toDeg - fromDeg;
        var rect = new SKRect((float)-r, (float)-r, (float)r, (float)r);
        path.Reset();
        if (sweep >= 360) path.AddOval(rect);
        else path.AddArc(rect, (float)(fromDeg - 90), (float)sweep);
        C.DrawPath(path, stroke);
    }

    public void StrokeLine(double x0, double y0, double x1, double y1, double width, ArmillaRgb color, double alpha, bool roundCap)
    {
        stroke.Color = Color(color, alpha);
        stroke.StrokeWidth = (float)width;
        stroke.StrokeCap = roundCap ? SKStrokeCap.Round : SKStrokeCap.Butt;
        C.DrawLine((float)x0, (float)y0, (float)x1, (float)y1, stroke);
    }

    public void FillCircle(double x, double y, double r, ArmillaRgb color, double alpha)
    {
        if (r <= 0) return;
        fill.Shader = null;
        fill.Color = Color(color, alpha);
        C.DrawCircle((float)x, (float)y, (float)r, fill);
    }

    public void FillHaze(double r, ArmillaRgb color, double centreAlpha)
    {
        // one gradient per ink and radius, kept: the paint's alpha carries the frame's strength, so a frame allocates nothing
        if (haze is null || hazeRadius != r || hazeInk != color)
        {
            haze?.Dispose();
            haze = SKShader.CreateRadialGradient(SKPoint.Empty, (float)r, [Color(color, 1), Color(color, .55), Color(color, 0)], [0f, .5f, 1f], SKShaderTileMode.Clamp);
            hazeRadius = r;
            hazeInk = color;
        }
        fill.Shader = haze;
        fill.Color = new SKColor(255, 255, 255, (byte)Math.Round(Math.Clamp(centreAlpha, 0, 1) * 255));
        C.DrawCircle(0, 0, (float)r, fill);
        fill.Shader = null;
    }

    private SKShader? haze;
    private double hazeRadius;
    private ArmillaRgb hazeInk;

    public void FillShape(ArmillaShape shape, double x, double y, double r, ArmillaRgb color, double alpha)
    {
        fill.Shader = null;
        fill.Color = Color(color, alpha);
        path.Reset();
        float fx = (float)x, fy = (float)y;
        void Poly(int k, double rot, double rad)
        {
            for (var i = 0; i < k; i++)
            {
                var a = rot + i * Math.PI * 2 / k;
                var px = (float)(x + Math.Cos(a) * rad);
                var py = (float)(y + Math.Sin(a) * rad);
                if (i == 0) path.MoveTo(px, py); else path.LineTo(px, py);
            }
            path.Close();
        }
        switch (shape)
        {
            case ArmillaShape.Squircle:
            {
                var s = (float)(r * 1.62);
                path.AddRoundRect(new SKRect(fx - s / 2, fy - s / 2, fx + s / 2, fy + s / 2), s * .34f, s * .34f);
                C.DrawPath(path, fill);
                return;
            }
            case ArmillaShape.Hexagon:
                Poly(6, 0, r * 1.02);
                break;
            case ArmillaShape.Diamond:
                path.MoveTo(fx, (float)(y - r * 1.18));
                path.LineTo((float)(x + r * .86), fy);
                path.LineTo(fx, (float)(y + r * 1.18));
                path.LineTo((float)(x - r * .86), fy);
                path.Close();
                break;
            case ArmillaShape.Triangle:
                Poly(3, -Math.PI / 2, r * 1.05);
                C.DrawPath(path, fill);
                // the rounded triangle: its own colour stroked round the corners
                stroke.Color = fill.Color;
                stroke.StrokeWidth = (float)(r * .42);
                stroke.StrokeJoin = SKStrokeJoin.Round;
                stroke.StrokeCap = SKStrokeCap.Butt;
                C.DrawPath(path, stroke);
                stroke.StrokeJoin = SKStrokeJoin.Miter;
                return;
            case ArmillaShape.Clover:
            {
                var rc = (float)(r * .5);
                var d = (float)(r * .5);
                path.AddCircle(fx + d, fy, rc);
                path.AddCircle(fx - d, fy, rc);
                path.AddCircle(fx, fy + d, rc);
                path.AddCircle(fx, fy - d, rc);
                break;
            }
            case ArmillaShape.Star:
                for (var i = 0; i < 10; i++)
                {
                    var a = -Math.PI / 2 + i * Math.PI / 5;
                    var rad = i % 2 == 1 ? r * .5 : r * 1.18;
                    var px = (float)(x + Math.Cos(a) * rad);
                    var py = (float)(y + Math.Sin(a) * rad);
                    if (i == 0) path.MoveTo(px, py); else path.LineTo(px, py);
                }
                path.Close();
                break;
            default:
                path.AddCircle(fx, fy, (float)r);
                break;
        }
        C.DrawPath(path, fill);
    }

    public void Glyph(ArmillaGlyph glyph, double unit, double lineWidth, ArmillaRgb color, double alpha)
    {
        var q = (float)(unit / 2);
        var u = (float)unit;
        switch (glyph)
        {
            case ArmillaGlyph.Stopped:
                fill.Shader = null;
                fill.Color = Color(color, alpha);
                C.DrawRoundRect(new SKRect(-q, -q, q, q), u * .2f, u * .2f, fill);
                break;
            case ArmillaGlyph.Stopping:
            {
                stroke.Color = Color(color, alpha);
                stroke.StrokeWidth = (float)lineWidth;
                stroke.StrokeCap = SKStrokeCap.Butt;
                var r = q - (float)lineWidth / 2;
                C.DrawRoundRect(new SKRect(-r, -r, r, r), u * .16f, u * .16f, stroke);
                break;
            }
            case ArmillaGlyph.Done:
                stroke.Color = Color(color, alpha);
                stroke.StrokeWidth = (float)lineWidth;
                stroke.StrokeCap = SKStrokeCap.Round;
                stroke.StrokeJoin = SKStrokeJoin.Round;
                path.Reset();
                path.MoveTo(-q * .82f, q * .02f);
                path.LineTo(-q * .22f, q * .6f);
                path.LineTo(q * .86f, -q * .56f);
                C.DrawPath(path, stroke);
                stroke.StrokeJoin = SKStrokeJoin.Miter;
                break;
        }
    }

    public void Dispose()
    {
        stroke.Dispose();
        fill.Dispose();
        path.Dispose();
        haze?.Dispose();
    }
}
