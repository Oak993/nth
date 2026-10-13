using System.Globalization;
using System.Text;
using Marrow.UI.Port.Armilla;

namespace Marrow.UI.Tests.Port.Armilla;

/// <summary>Writes every drawing call down, rounded, so two still poses can be compared call by call.</summary>
internal sealed class RecordingArmillaCanvas : IArmillaCanvas
{
    private readonly StringBuilder log = new();

    public string Log => log.ToString();

    public int Calls { get; private set; }

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
}

/// <summary>Drives an engine the way the render thread does: a frame every 1/60 s on one clock.</summary>
internal static class ArmillaDrive
{
    public const double Frame = 1.0 / 60;

    /// <summary>Ticks and draws for <paramref name="seconds"/>; returns the worst extent drawn (a fraction of the half-box).</summary>
    public static double Hold(ArmillaEngine mark, ref double clock, double seconds, double? level = null)
    {
        var worst = 0.0;
        var frames = (int)Math.Ceiling(seconds / Frame);
        for (var i = 0; i < frames; i++)
        {
            clock += Frame;
            if (level is { } l) mark.SetLevel(i % 20 < 10 ? l : 0);
            mark.Tick(Frame, clock);
            mark.Draw(NullArmillaCanvas.Instance);
            worst = Math.Max(worst, mark.MaxExtent);
        }
        return worst;
    }

    public static IReadOnlyList<ArmillaNode> Asturs(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new ArmillaNode((ArmillaShape)(i % 6), Busy: i % 2 == 0, Tint: null))];
}
