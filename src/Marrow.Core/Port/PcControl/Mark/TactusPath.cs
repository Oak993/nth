// PORT-ONLY: Tactus's mark (OD-0146, ED-NEW-pcctl-11; DESIGN.md 4.5). A drawn outline as plain segments (move, line,
// circular arc, close), so every surface can draw the mark without parsing text: the pill and the other Avalonia surfaces
// build their geometry from the segments, the tray and notification images hand the SVG text to Skia, and tests compare
// the text with the owner-locked SVG files. Numbers print as the design pages print them: rounded to 0.001, no -0.
using System.Globalization;
using System.Text;

namespace Marrow.Core.Port.PcControl.Mark;

/// <summary>One segment of an outline: a move, a line, an arc (radius, sweep flag 1 = clockwise on screen) or a close.</summary>
public readonly record struct TactusSegment(TactusSegmentKind Kind, double X, double Y, double Radius = 0, bool Clockwise = false);

public enum TactusSegmentKind
{
    Move,
    Line,
    Arc,
    Close,
}

/// <summary>An outline of the mark in its own units (a 24 or 16 unit square), filled with one colour.</summary>
public sealed class TactusPath
{
    private readonly List<TactusSegment> segments = [];

    public IReadOnlyList<TactusSegment> Segments => segments;

    public TactusPath MoveTo(double x, double y) => Add(new TactusSegment(TactusSegmentKind.Move, x, y));

    public TactusPath LineTo(double x, double y) => Add(new TactusSegment(TactusSegmentKind.Line, x, y));

    public TactusPath ArcTo(double x, double y, double radius, bool clockwise) => Add(new TactusSegment(TactusSegmentKind.Arc, x, y, radius, clockwise));

    public TactusPath Close() => Add(new TactusSegment(TactusSegmentKind.Close, 0, 0));

    /// <summary>Appends every segment of another outline (a second shape of the same mark).</summary>
    public TactusPath Append(TactusPath other)
    {
        segments.AddRange(other.segments);
        return this;
    }

    /// <summary>The outline scaled by <paramref name="scale"/> about (<paramref name="cx"/>, <paramref name="cy"/>), radii included.</summary>
    public TactusPath Scaled(double cx, double cy, double scale)
    {
        var scaled = new TactusPath();
        foreach (var s in segments)
        {
            scaled.Add(s.Kind == TactusSegmentKind.Close ? s : s with
            {
                X = cx + ((s.X - cx) * scale),
                Y = cy + ((s.Y - cy) * scale),
                Radius = s.Radius * scale,
            });
        }
        return scaled;
    }

    /// <summary>SVG path data, in the design pages' own form ("M7 4.652L7 19.756A0.75 0.75 0 0 0 8.181 20.369 ... Z").</summary>
    public string ToSvg()
    {
        var text = new StringBuilder(segments.Count * 24);
        foreach (var s in segments)
        {
            switch (s.Kind)
            {
                case TactusSegmentKind.Move:
                    text.Append('M').Append(Number(s.X)).Append(' ').Append(Number(s.Y));
                    break;
                case TactusSegmentKind.Line:
                    text.Append('L').Append(Number(s.X)).Append(' ').Append(Number(s.Y));
                    break;
                case TactusSegmentKind.Arc:
                    text.Append('A').Append(Number(s.Radius)).Append(' ').Append(Number(s.Radius)).Append(" 0 0 ").Append(s.Clockwise ? '1' : '0')
                        .Append(' ').Append(Number(s.X)).Append(' ').Append(Number(s.Y));
                    break;
                default:
                    text.Append('Z');
                    break;
            }
        }
        return text.ToString();
    }

    /// <summary>A number as the design pages print it: rounded to 0.001, trailing zeros dropped, never "-0".</summary>
    public static string Number(double value)
    {
        // As JavaScript's Math.round: halves round up, toward positive infinity.
        var rounded = Math.Floor((value * 1000) + 0.5) / 1000;
        if (rounded == 0) rounded = 0;
        return rounded.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private TactusPath Add(TactusSegment segment)
    {
        segments.Add(segment);
        return this;
    }
}
