using System.Globalization;
using System.Text;
using Marrow.UI.Port.Armilla;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Armilla;

/// <summary>Dextra AS-17: writes every drawing call of an Astur's mark down, rounded, layers and cuts included, so two still poses can be compared call by call.</summary>
internal sealed class RecordingAsturCanvas : IAsturCanvas
{
    private readonly StringBuilder log = new();

    public string Log => log.ToString();

    public int Calls { get; private set; }

    public int Layers { get; private set; }

    public int Cuts { get; private set; }

    private void Add(string op, params object[] values)
    {
        Calls++;
        log.Append(op);
        foreach (var v in values)
            log.Append(' ').Append(v switch
            {
                double d => Math.Round(d, 2).ToString(CultureInfo.InvariantCulture),
                ArmillaRgb c => $"{Math.Round(c.R)},{Math.Round(c.G)},{Math.Round(c.B)}",
                _ => Convert.ToString(v, CultureInfo.InvariantCulture),
            });
        log.Append('\n');
    }

    public void Save() => Add("save");

    public void Restore() => Add("restore");

    public void Translate(double dx, double dy) => Add("translate", dx, dy);

    public void Rotate(double degrees) => Add("rotate", degrees);

    public void StrokeArc(double r, double fromDeg, double toDeg, double width, ArmillaRgb color, double alpha, bool roundCap) => Add("arc", r, fromDeg, toDeg, width, color, alpha, roundCap);

    public void StrokeLine(double x0, double y0, double x1, double y1, double width, ArmillaRgb color, double alpha, bool roundCap) => Add("line", x0, y0, x1, y1, width, color, alpha, roundCap);

    public void FillCircle(double x, double y, double r, ArmillaRgb color, double alpha) => Add("circle", x, y, r, color, alpha);

    public void FillHaze(double r, ArmillaRgb color, double centreAlpha) => Add("haze", r, color, centreAlpha);

    public void FillShape(ArmillaShape shape, double x, double y, double r, ArmillaRgb color, double alpha) => Add("shape", shape, x, y, r, color, alpha);

    public void Glyph(ArmillaGlyph glyph, double unit, double lineWidth, ArmillaRgb color, double alpha) => Add("glyph", glyph, unit, lineWidth, color, alpha);

    public void BeginLayer(double opacity, double half)
    {
        Layers++;
        Add("layer", opacity, half);
    }

    public void Cut(double x, double y, double r)
    {
        Cuts++;
        Add("cut", x, y, r);
    }

    public void EndLayer() => Add("endlayer");
}

/// <summary>Dextra AS-17: drives an Astur's mark the way the render thread does (a frame every 1/60 s) and draws it for real into a bitmap.</summary>
internal static class AsturDrive
{
    public const double Frame = 1.0 / 60;

    /// <summary>Ticks for <paramref name="seconds"/>, drawing each frame into a null canvas; returns the worst extent (a fraction of the half-box).</summary>
    public static double Hold(AsturMarkEngine mark, double seconds)
    {
        var worst = 0.0;
        var frames = (int)Math.Ceiling(seconds / Frame);
        for (var i = 0; i < frames; i++)
        {
            mark.Tick(Frame);
            mark.Draw(NullAsturCanvas.Instance);
            worst = Math.Max(worst, mark.MaxExtent);
        }
        return worst;
    }

    public static string Still(double size, ArmillaShape shape, AsturMarkState state, bool dark = true)
    {
        var mark = new AsturMarkEngine(new AsturMarkLook { Size = size, Shape = shape, State = state, Dark = dark, Reduce = true });
        var canvas = new RecordingAsturCanvas();
        mark.Draw(canvas);
        return canvas.Log;
    }

    /// <summary>
    /// The mark's pose drawn with Skia into a <paramref name="scale"/>-times bitmap on a transparent ground, with <paramref name="margin"/> logical px of ground
    /// all round the slot, so paint that runs past the slot shows up instead of being cut off by the bitmap's edge.
    /// </summary>
    public static SKBitmap Render(AsturMarkEngine mark, double scale = 1, double margin = 4)
    {
        var px = (int)Math.Round((mark.Look.Size + 2 * margin) * scale);
        var bitmap = new SKBitmap(px, px, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = new SKCanvas(bitmap);
        surface.Clear(SKColors.Transparent);
        surface.Scale((float)scale);
        surface.Translate((float)(margin + mark.Look.Size / 2), (float)(margin + mark.Look.Size / 2));
        using var canvas = new AsturSkiaCanvas();
        canvas.Begin(surface);
        mark.Draw(canvas);
        return bitmap;
    }

    /// <summary>How many pixels outside the slot hold any paint (the slot is the middle square, <paramref name="margin"/> logical px in from every edge).</summary>
    public static int PaintedOutsideSlot(SKBitmap bitmap, double size, double scale = 1, double margin = 4)
    {
        var from = (int)Math.Ceiling(margin * scale - 1e-6);
        var to = (int)Math.Floor((margin + size) * scale + 1e-6);
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            if ((x < from || x >= to || y < from || y >= to) && bitmap.GetPixel(x, y).Alpha > 0) count++;
        return count;
    }

    /// <summary>The pixel at (<paramref name="x"/>, <paramref name="y"/>) logical px from the slot's centre.</summary>
    public static SKColor At(SKBitmap bitmap, double size, double x, double y, double scale = 1, double margin = 4) =>
        bitmap.GetPixel((int)Math.Floor((margin + size / 2 + x) * scale), (int)Math.Floor((margin + size / 2 + y) * scale));
}
