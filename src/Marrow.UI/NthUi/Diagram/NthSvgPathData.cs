// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 sanitised SVG "shapes, paths, text and gradients only"; ED-NEW-web-w16c-1). No
// Swift counterpart. SVG path data read by nth's own reader: every command becomes an absolute move, line, cubic, quadratic,
// arc or close, so the painter, the marker maths and the writer all work from one short list; a number that is not finite or
// not sane stops the path where it is (the spec's own rule for an error), and a path longer than the limits is cut. The text is
// never handed to a library to interpret.
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Media;

namespace Marrow.UI.NthUi.Diagram;

public enum NthSvgPathKind : byte
{
    Move,
    Line,
    Cubic,
    Quad,
    Arc,
    Close,
}

/// <summary>One absolute path segment. For a cubic (X1,Y1) and (X2,Y2) are its control points, for a quad (X1,Y1) is; an
/// arc carries its radii, rotation and flags.</summary>
public readonly record struct NthSvgPathSegment(
    NthSvgPathKind Kind,
    double X,
    double Y,
    double X1 = 0,
    double Y1 = 0,
    double X2 = 0,
    double Y2 = 0,
    double Rx = 0,
    double Ry = 0,
    double Rotation = 0,
    bool Large = false,
    bool Sweep = false);

/// <summary>A point where a marker may sit: the vertex, and the direction the path arrives and leaves along.</summary>
public readonly record struct NthSvgPathVertex(Point Point, Vector? Arrive, Vector? Leave);

/// <summary>A parsed path.</summary>
public sealed class NthSvgPathData
{
    /// <summary>The most segments one path may have.</summary>
    public const int MaxSegments = 100_000;

    /// <summary>The longest path text read.</summary>
    public const int MaxLength = 400_000;

    /// <summary>The largest coordinate magnitude accepted.</summary>
    public const double MaxCoordinate = 1e7;

    public static NthSvgPathData Empty { get; } = new([], true);

    private NthSvgPathData(IReadOnlyList<NthSvgPathSegment> segments, bool complete)
    {
        Segments = segments;
        IsComplete = complete;
    }

    public IReadOnlyList<NthSvgPathSegment> Segments { get; }

    /// <summary>False when the text stopped making sense (or was cut) and the segments are what came before.</summary>
    public bool IsComplete { get; }

    public bool IsEmpty => Segments.Count == 0;

    /// <summary>Parses SVG path data.</summary>
    public static NthSvgPathData Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Empty;
        var reader = new Reader(text.Length > MaxLength ? text[..MaxLength] : text);
        var cut = text.Length > MaxLength;
        var segments = new List<NthSvgPathSegment>();
        double cx = 0, cy = 0, sx = 0, sy = 0;
        double lastCx = 0, lastCy = 0;
        var lastWasCubic = false;
        var lastWasQuad = false;
        var ok = true;
        var started = false;
        var command = '\0';
        while (true)
        {
            reader.SkipSeparators();
            if (reader.AtEnd) break;
            if (reader.NextIsCommand)
            {
                command = reader.ReadCommand();
                if (command is 'Z' or 'z')
                {
                    if (!started) { ok = false; break; }
                    if (segments.Count > 0 && segments[^1].Kind != NthSvgPathKind.Close) segments.Add(new NthSvgPathSegment(NthSvgPathKind.Close, sx, sy));
                    cx = sx;
                    cy = sy;
                    lastWasCubic = lastWasQuad = false;
                    command = '\0';
                    continue;
                }
                if (!started && command is not ('M' or 'm')) { ok = false; break; }
            }
            else if (command == '\0')
            {
                ok = false;
                break;
            }

            var relative = char.IsLower(command);
            var upper = char.ToUpperInvariant(command);
            if (segments.Count >= MaxSegments) { ok = false; break; }
            switch (upper)
            {
                case 'M':
                {
                    if (!reader.TryNumbers(2, out var n)) { ok = false; goto done; }
                    var x = relative && started ? cx + n[0] : n[0];
                    var y = relative && started ? cy + n[1] : n[1];
                    if (!Sane(x) || !Sane(y)) { ok = false; goto done; }
                    segments.Add(new NthSvgPathSegment(NthSvgPathKind.Move, x, y));
                    cx = sx = x;
                    cy = sy = y;
                    started = true;
                    command = relative ? 'l' : 'L'; // the following pairs are lines
                    lastWasCubic = lastWasQuad = false;
                    break;
                }
                case 'L':
                case 'H':
                case 'V':
                {
                    double x = cx, y = cy;
                    if (upper == 'L')
                    {
                        if (!reader.TryNumbers(2, out var n)) { ok = false; goto done; }
                        x = relative ? cx + n[0] : n[0];
                        y = relative ? cy + n[1] : n[1];
                    }
                    else if (upper == 'H')
                    {
                        if (!reader.TryNumbers(1, out var n)) { ok = false; goto done; }
                        x = relative ? cx + n[0] : n[0];
                    }
                    else
                    {
                        if (!reader.TryNumbers(1, out var n)) { ok = false; goto done; }
                        y = relative ? cy + n[0] : n[0];
                    }
                    if (!Sane(x) || !Sane(y)) { ok = false; goto done; }
                    segments.Add(new NthSvgPathSegment(NthSvgPathKind.Line, x, y));
                    cx = x;
                    cy = y;
                    lastWasCubic = lastWasQuad = false;
                    break;
                }
                case 'C':
                case 'S':
                {
                    double x1, y1, x2, y2, x, y;
                    if (upper == 'C')
                    {
                        if (!reader.TryNumbers(6, out var n)) { ok = false; goto done; }
                        (x1, y1, x2, y2, x, y) = relative
                            ? (cx + n[0], cy + n[1], cx + n[2], cy + n[3], cx + n[4], cy + n[5])
                            : (n[0], n[1], n[2], n[3], n[4], n[5]);
                    }
                    else
                    {
                        if (!reader.TryNumbers(4, out var n)) { ok = false; goto done; }
                        (x1, y1) = lastWasCubic ? (2 * cx - lastCx, 2 * cy - lastCy) : (cx, cy);
                        (x2, y2, x, y) = relative ? (cx + n[0], cy + n[1], cx + n[2], cy + n[3]) : (n[0], n[1], n[2], n[3]);
                    }
                    if (!Sane(x1) || !Sane(y1) || !Sane(x2) || !Sane(y2) || !Sane(x) || !Sane(y)) { ok = false; goto done; }
                    segments.Add(new NthSvgPathSegment(NthSvgPathKind.Cubic, x, y, x1, y1, x2, y2));
                    lastCx = x2;
                    lastCy = y2;
                    cx = x;
                    cy = y;
                    lastWasCubic = true;
                    lastWasQuad = false;
                    break;
                }
                case 'Q':
                case 'T':
                {
                    double x1, y1, x, y;
                    if (upper == 'Q')
                    {
                        if (!reader.TryNumbers(4, out var n)) { ok = false; goto done; }
                        (x1, y1, x, y) = relative ? (cx + n[0], cy + n[1], cx + n[2], cy + n[3]) : (n[0], n[1], n[2], n[3]);
                    }
                    else
                    {
                        if (!reader.TryNumbers(2, out var n)) { ok = false; goto done; }
                        (x1, y1) = lastWasQuad ? (2 * cx - lastCx, 2 * cy - lastCy) : (cx, cy);
                        (x, y) = relative ? (cx + n[0], cy + n[1]) : (n[0], n[1]);
                    }
                    if (!Sane(x1) || !Sane(y1) || !Sane(x) || !Sane(y)) { ok = false; goto done; }
                    segments.Add(new NthSvgPathSegment(NthSvgPathKind.Quad, x, y, x1, y1));
                    lastCx = x1;
                    lastCy = y1;
                    cx = x;
                    cy = y;
                    lastWasQuad = true;
                    lastWasCubic = false;
                    break;
                }
                case 'A':
                {
                    if (!reader.TryNumbers(3, out var n) || !reader.TryFlag(out var large) || !reader.TryFlag(out var sweep) || !reader.TryNumbers(2, out var end))
                    {
                        ok = false;
                        goto done;
                    }
                    var x = relative ? cx + end[0] : end[0];
                    var y = relative ? cy + end[1] : end[1];
                    if (!Sane(x) || !Sane(y) || !Sane(n[0]) || !Sane(n[1]) || !Sane(n[2])) { ok = false; goto done; }
                    segments.Add(new NthSvgPathSegment(NthSvgPathKind.Arc, x, y, Rx: Math.Abs(n[0]), Ry: Math.Abs(n[1]), Rotation: n[2], Large: large, Sweep: sweep));
                    cx = x;
                    cy = y;
                    lastWasCubic = lastWasQuad = false;
                    break;
                }
                default:
                    ok = false;
                    goto done;
            }
        }

    done:
        return new NthSvgPathData(segments, ok && !cut);
    }

    private static bool Sane(double value) => double.IsFinite(value) && Math.Abs(value) <= MaxCoordinate;

    /// <summary>The path as absolute path data text, numbers rounded to four places.</summary>
    public string ToText()
    {
        var builder = new StringBuilder(Segments.Count * 18);
        foreach (var s in Segments)
        {
            if (builder.Length > 0) builder.Append(' ');
            switch (s.Kind)
            {
                case NthSvgPathKind.Move:
                    builder.Append('M').Append(N(s.X)).Append(' ').Append(N(s.Y));
                    break;
                case NthSvgPathKind.Line:
                    builder.Append('L').Append(N(s.X)).Append(' ').Append(N(s.Y));
                    break;
                case NthSvgPathKind.Cubic:
                    builder.Append('C').Append(N(s.X1)).Append(' ').Append(N(s.Y1)).Append(' ').Append(N(s.X2)).Append(' ').Append(N(s.Y2)).Append(' ').Append(N(s.X)).Append(' ').Append(N(s.Y));
                    break;
                case NthSvgPathKind.Quad:
                    builder.Append('Q').Append(N(s.X1)).Append(' ').Append(N(s.Y1)).Append(' ').Append(N(s.X)).Append(' ').Append(N(s.Y));
                    break;
                case NthSvgPathKind.Arc:
                    builder.Append('A').Append(N(s.Rx)).Append(' ').Append(N(s.Ry)).Append(' ').Append(N(s.Rotation)).Append(' ')
                        .Append(s.Large ? '1' : '0').Append(' ').Append(s.Sweep ? '1' : '0').Append(' ').Append(N(s.X)).Append(' ').Append(N(s.Y));
                    break;
                default:
                    builder.Append('Z');
                    break;
            }
        }
        return builder.ToString();
    }

    private static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>The box the path's points and control points span (a sound bound, not the tight box).</summary>
    public Rect Bounds()
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Add(double x, double y)
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
        foreach (var s in Segments)
        {
            switch (s.Kind)
            {
                case NthSvgPathKind.Cubic:
                    Add(s.X1, s.Y1);
                    Add(s.X2, s.Y2);
                    break;
                case NthSvgPathKind.Quad:
                    Add(s.X1, s.Y1);
                    break;
                case NthSvgPathKind.Arc:
                    Add(s.X - s.Rx, s.Y - s.Ry);
                    Add(s.X + s.Rx, s.Y + s.Ry);
                    break;
            }
            Add(s.X, s.Y);
        }
        return minX > maxX ? default : new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>The path as a geometry (filled, so an open figure fills as if closed, as SVG does).</summary>
    public Geometry ToGeometry()
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var open = false;
            var start = default(Point);
            foreach (var s in Segments)
            {
                switch (s.Kind)
                {
                    case NthSvgPathKind.Move:
                        if (open) context.EndFigure(false);
                        start = new Point(s.X, s.Y);
                        context.BeginFigure(start, true);
                        open = true;
                        break;
                    case NthSvgPathKind.Line:
                        context.LineTo(new Point(s.X, s.Y));
                        break;
                    case NthSvgPathKind.Cubic:
                        context.CubicBezierTo(new Point(s.X1, s.Y1), new Point(s.X2, s.Y2), new Point(s.X, s.Y));
                        break;
                    case NthSvgPathKind.Quad:
                        context.QuadraticBezierTo(new Point(s.X1, s.Y1), new Point(s.X, s.Y));
                        break;
                    case NthSvgPathKind.Arc:
                        if (s.Rx <= 0 || s.Ry <= 0) context.LineTo(new Point(s.X, s.Y));
                        else context.ArcTo(new Point(s.X, s.Y), new Size(s.Rx, s.Ry), s.Rotation, s.Large, s.Sweep ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
                        break;
                    default:
                        if (open)
                        {
                            context.EndFigure(true);
                            open = false;
                            // A segment after a close starts a new figure at the subpath's start.
                            context.BeginFigure(start, true);
                            open = true;
                        }
                        break;
                }
            }
            if (open) context.EndFigure(false);
        }
        return geometry;
    }

    /// <summary>Where markers sit, with the path's direction in and out of each.</summary>
    public IReadOnlyList<NthSvgPathVertex> Vertices()
    {
        var result = new List<NthSvgPathVertex>();
        var segments = Segments;
        var current = default(Point);
        var start = default(Point);
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            var end = s.Kind == NthSvgPathKind.Close ? start : new Point(s.X, s.Y);
            if (s.Kind == NthSvgPathKind.Move)
            {
                current = end;
                start = end;
                result.Add(new NthSvgPathVertex(end, null, LeaveAt(segments, i + 1, end)));
                continue;
            }
            var arrive = EndDirection(s, current, end);
            var leave = LeaveAt(segments, i + 1, end);
            result.Add(new NthSvgPathVertex(end, arrive, leave));
            current = end;
        }
        return result;
    }

    private static Vector? LeaveAt(IReadOnlyList<NthSvgPathSegment> segments, int next, Point from)
    {
        if (next >= segments.Count) return null;
        var s = segments[next];
        if (s.Kind == NthSvgPathKind.Move) return null;
        var end = s.Kind == NthSvgPathKind.Close ? default : new Point(s.X, s.Y);
        if (s.Kind == NthSvgPathKind.Close)
        {
            // Back to the subpath start: find it.
            for (var i = next - 1; i >= 0; i--)
            {
                if (segments[i].Kind != NthSvgPathKind.Move) continue;
                end = new Point(segments[i].X, segments[i].Y);
                break;
            }
        }
        return StartDirection(s, from, end);
    }

    private static Vector? StartDirection(NthSvgPathSegment s, Point from, Point end) => s.Kind switch
    {
        NthSvgPathKind.Cubic => First(new Point(s.X1, s.Y1) - from, new Point(s.X2, s.Y2) - from, end - from),
        NthSvgPathKind.Quad => First(new Point(s.X1, s.Y1) - from, end - from),
        _ => First(end - from),
    };

    private static Vector? EndDirection(NthSvgPathSegment s, Point from, Point end) => s.Kind switch
    {
        NthSvgPathKind.Cubic => First(end - new Point(s.X2, s.Y2), end - new Point(s.X1, s.Y1), end - from),
        NthSvgPathKind.Quad => First(end - new Point(s.X1, s.Y1), end - from),
        _ => First(end - from),
    };

    private static Vector? First(params Vector[] candidates)
    {
        foreach (var c in candidates)
        {
            if (Math.Abs(c.X) > 1e-9 || Math.Abs(c.Y) > 1e-9) return c;
        }
        return null;
    }

    private sealed class Reader(string text)
    {
        private int index;

        public bool AtEnd => index >= text.Length;

        public bool NextIsCommand => !AtEnd && char.IsAsciiLetter(text[index]) && text[index] is not ('e' or 'E');

        public void SkipSeparators()
        {
            while (index < text.Length && (char.IsWhiteSpace(text[index]) || text[index] == ',')) index++;
        }

        public char ReadCommand() => text[index++];

        public bool TryNumbers(int count, out double[] values)
        {
            values = new double[count];
            for (var i = 0; i < count; i++)
            {
                if (!TryNumber(out values[i])) return false;
            }
            return true;
        }

        public bool TryFlag(out bool flag)
        {
            SkipSeparators();
            flag = false;
            if (AtEnd || text[index] is not ('0' or '1')) return false;
            flag = text[index++] == '1';
            return true;
        }

        public bool TryNumber(out double value)
        {
            SkipSeparators();
            value = 0;
            var begin = index;
            var i = index;
            if (i < text.Length && text[i] is '+' or '-') i++;
            var digits = 0;
            while (i < text.Length && char.IsAsciiDigit(text[i])) { i++; digits++; }
            if (i < text.Length && text[i] == '.')
            {
                i++;
                while (i < text.Length && char.IsAsciiDigit(text[i])) { i++; digits++; }
            }
            if (digits == 0) return false;
            if (i < text.Length && text[i] is 'e' or 'E')
            {
                var j = i + 1;
                if (j < text.Length && text[j] is '+' or '-') j++;
                var exponent = 0;
                while (j < text.Length && char.IsAsciiDigit(text[j])) { j++; exponent++; }
                if (exponent > 0) i = j;
            }
            if (!double.TryParse(text.AsSpan(begin, i - begin), NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !double.IsFinite(value)) return false;
            index = i;
            return true;
        }
    }
}
