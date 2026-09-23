// PORT-ONLY: SwiftUI draws SF Symbols and asset-catalogue vectors natively; the port draws the Lucide icons of OD-0010
// and the two vector assets through this converter (ED-179 (1)). No NuGet package: only the SVG subset those files use.
using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;

namespace Marrow.UI.Port;

// Decisions: ED-179 (1) (the elements: path d, circle, ellipse, line, polyline, polygon, rect with rx and ry; Lucide's
// stroke: width 2, round caps and joins, current colour), OD-0010, PA-0012, porting/icons/README.md "Rendering rules".
// Paint, stroke width, caps, joins and fill rule are read from the root, from <g> and from the element, the nearest
// one winning, as SVG presentation attributes inherit. SVG initial values apply where no one sets them: fill black,
// stroke none, stroke width 1, butt caps, miter joins, nonzero.

/// <summary>What an SVG paint attribute says.</summary>
public enum SvgPaintKind
{
    None,
    CurrentColor,
    Color,
}

/// <summary>An SVG paint: none, currentColor, or a fixed colour (<see cref="Color"/> is only meaningful for Color).</summary>
public readonly record struct SvgPaint(SvgPaintKind Kind, Color Color)
{
    public static SvgPaint None { get; } = new(SvgPaintKind.None, default);

    public static SvgPaint CurrentColor { get; } = new(SvgPaintKind.CurrentColor, default);

    internal static SvgPaint Parse(string value)
    {
        var text = value.Trim();
        if (text == "none") return None;
        if (text == "currentColor") return CurrentColor;
        return Color.TryParse(text, out var color)
            ? new SvgPaint(SvgPaintKind.Color, color)
            : throw new FormatException($"Unsupported SVG paint \"{value}\"");
    }
}

/// <summary>One drawable SVG element, as geometry in view-box units and the paint it is drawn with. <see cref="IsClosed"/>
/// is true for circle, ellipse, rect and polygon, and for a path whose every subpath is closed by Z or ends on its own
/// start point (ED-182 (Q4): only a closed shape can be the container a .fill name fills).</summary>
public sealed record SvgShape(
    string Element,
    Geometry Geometry,
    SvgPaint Fill,
    SvgPaint Stroke,
    double StrokeWidth,
    PenLineCap LineCap,
    PenLineJoin LineJoin,
    FillRule FillRule,
    bool IsClosed = false);

/// <summary>An SVG document reduced to its view box and its drawable shapes in document order.</summary>
public sealed class SvgIcon
{
    private SvgIcon(Rect viewBox, IReadOnlyList<SvgShape> shapes)
    {
        ViewBox = viewBox;
        Shapes = shapes;
    }

    public Rect ViewBox { get; }

    public IReadOnlyList<SvgShape> Shapes { get; }

    private sealed record Style(SvgPaint Fill, SvgPaint Stroke, double StrokeWidth, PenLineCap LineCap, PenLineJoin LineJoin, FillRule FillRule)
    {
        public static Style Initial { get; } = new(new SvgPaint(SvgPaintKind.Color, Colors.Black), SvgPaint.None, 1,
            PenLineCap.Flat, PenLineJoin.Miter, FillRule.NonZero);

        public Style With(XElement element)
        {
            var style = this;
            if (Attr(element, "fill") is { } fill) style = style with { Fill = SvgPaint.Parse(fill) };
            if (Attr(element, "stroke") is { } stroke) style = style with { Stroke = SvgPaint.Parse(stroke) };
            if (Attr(element, "stroke-width") is { } width) style = style with { StrokeWidth = Number(width) };
            if (Attr(element, "stroke-linecap") is { } cap)
                style = style with
                {
                    LineCap = cap switch
                    {
                        "round" => PenLineCap.Round,
                        "square" => PenLineCap.Square,
                        "butt" => PenLineCap.Flat,
                        _ => throw new FormatException($"Unsupported stroke-linecap \"{cap}\""),
                    },
                };
            if (Attr(element, "stroke-linejoin") is { } join)
                style = style with
                {
                    LineJoin = join switch
                    {
                        "round" => PenLineJoin.Round,
                        "bevel" => PenLineJoin.Bevel,
                        "miter" => PenLineJoin.Miter,
                        _ => throw new FormatException($"Unsupported stroke-linejoin \"{join}\""),
                    },
                };
            if (Attr(element, "fill-rule") is { } rule)
                style = style with
                {
                    FillRule = rule switch
                    {
                        "nonzero" => FillRule.NonZero,
                        "evenodd" => FillRule.EvenOdd,
                        _ => throw new FormatException($"Unsupported fill-rule \"{rule}\""),
                    },
                };
            return style;
        }
    }

    /// <summary>Parses an SVG document. Unsupported elements or attribute values are an error, never skipped.</summary>
    public static SvgIcon Parse(string svgText)
    {
        var root = XDocument.Parse(svgText).Root ?? throw new FormatException("Empty SVG document");
        if (root.Name.LocalName != "svg") throw new FormatException($"Root element is <{root.Name.LocalName}>, not <svg>");
        var viewBox = ViewBoxOf(root);
        var shapes = new List<SvgShape>();
        Walk(root, Style.Initial.With(root), shapes);
        return new SvgIcon(viewBox, shapes);
    }

    private static Rect ViewBoxOf(XElement root)
    {
        if (Attr(root, "viewBox") is { } box)
        {
            var n = Numbers(box);
            if (n.Count != 4) throw new FormatException($"viewBox \"{box}\" does not have four numbers");
            return new Rect(n[0], n[1], n[2], n[3]);
        }
        var width = Attr(root, "width") is { } w ? Number(w) : throw new FormatException("SVG has neither viewBox nor width");
        var height = Attr(root, "height") is { } h ? Number(h) : throw new FormatException("SVG has neither viewBox nor height");
        return new Rect(0, 0, width, height);
    }

    private static void Walk(XElement parent, Style style, List<SvgShape> shapes)
    {
        foreach (var element in parent.Elements())
        {
            var name = element.Name.LocalName;
            var own = style.With(element);
            if (name == "g")
            {
                Walk(element, own, shapes);
                continue;
            }
            var (geometry, closed) = name switch
            {
                "path" => ParsePath(Required(element, "d"), own.FillRule),
                "circle" => (Circle(element), true),
                "ellipse" => (Ellipse(element), true),
                "line" => (Line(element), false),
                "polyline" => (Poly(element, closed: false, own.FillRule), false),
                "polygon" => (Poly(element, closed: true, own.FillRule), true),
                "rect" => (RoundedRect(element), true),
                _ => throw new FormatException($"Unsupported SVG element <{name}>"),
            };
            shapes.Add(new SvgShape(name, geometry, own.Fill, own.Stroke, own.StrokeWidth, own.LineCap, own.LineJoin, own.FillRule, closed));
        }
    }

    private static Geometry Circle(XElement e)
    {
        var r = Length(e, "r");
        return new EllipseGeometry(new Rect(Length(e, "cx") - r, Length(e, "cy") - r, 2 * r, 2 * r));
    }

    private static Geometry Ellipse(XElement e)
    {
        var rx = Length(e, "rx");
        var ry = Length(e, "ry");
        return new EllipseGeometry(new Rect(Length(e, "cx") - rx, Length(e, "cy") - ry, 2 * rx, 2 * ry));
    }

    private static Geometry Line(XElement e) =>
        new LineGeometry(new Point(Length(e, "x1"), Length(e, "y1")), new Point(Length(e, "x2"), Length(e, "y2")));

    private static Geometry Poly(XElement e, bool closed, FillRule fillRule)
    {
        var n = Numbers(Required(e, "points"));
        if (n.Count < 4 || n.Count % 2 != 0) throw new FormatException($"points \"{Required(e, "points")}\" is not a list of pairs");
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.SetFillRule(fillRule);
            context.BeginFigure(new Point(n[0], n[1]), closed);
            for (var i = 2; i < n.Count; i += 2) context.LineTo(new Point(n[i], n[i + 1]));
            context.EndFigure(closed);
        }
        return geometry;
    }

    private static Geometry RoundedRect(XElement e)
    {
        var rect = new Rect(Length(e, "x"), Length(e, "y"), Length(e, "width"), Length(e, "height"));
        // SVG 2 rect: an absent rx takes ry and an absent ry takes rx; each is clamped to half its side.
        double? rx = Attr(e, "rx") is { } x ? Number(x) : null;
        double? ry = Attr(e, "ry") is { } y ? Number(y) : null;
        var radiusX = Math.Min(rx ?? ry ?? 0, rect.Width / 2);
        var radiusY = Math.Min(ry ?? rx ?? 0, rect.Height / 2);
        return new RectangleGeometry(rect, radiusX, radiusY);
    }

    private static string? Attr(XElement e, string name) => e.Attribute(name)?.Value;

    private static string Required(XElement e, string name) =>
        Attr(e, name) ?? throw new FormatException($"<{e.Name.LocalName}> has no {name}");

    /// <summary>A coordinate attribute; absent means 0 as the SVG initial value.</summary>
    private static double Length(XElement e, string name) => Attr(e, name) is { } v ? Number(v) : 0;

    private static double Number(string text) =>
        double.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static List<double> Numbers(string text)
    {
        var reader = new PathReader(text);
        var list = new List<double>();
        reader.SkipSeparators();
        while (!reader.AtEnd)
        {
            list.Add(reader.ReadNumber());
            reader.SkipSeparators();
        }
        return list;
    }

    /// <summary>
    /// Parses SVG path data (every command of SVG 1.1 section 8.3: M L H V C S Q T A Z, absolute and relative, with
    /// implicit repeats) into a StreamGeometry. Malformed data is a <see cref="FormatException"/>.
    /// </summary>
    public static Geometry ParsePathData(string d) => ParsePath(d, FillRule.NonZero).Geometry;

    private static (Geometry Geometry, bool Closed) ParsePath(string d, FillRule fillRule)
    {
        var geometry = new StreamGeometry();
        PathBuilder builder;
        using (var context = geometry.Open())
        {
            context.SetFillRule(fillRule);
            builder = new PathBuilder(context);
            builder.Run(new PathReader(d));
        }
        return (geometry, builder.Closed);
    }

    private sealed class PathBuilder(StreamGeometryContext context)
    {
        private Point current;
        private Point start;
        private Point? lastCubicControl;
        private Point? lastQuadControl;
        private bool figureOpen;
        private bool anyFigure;
        private bool everyFigureClosed = true;

        /// <summary>True when the path has a subpath and every subpath is closed by Z or ends on its own start point.</summary>
        public bool Closed => anyFigure && everyFigureClosed;

        public void Run(PathReader reader)
        {
            reader.SkipSeparators();
            if (reader.AtEnd) return;
            var command = reader.ReadCommand();
            if (command is not ('M' or 'm')) throw new FormatException($"Path data must start with a moveto, not '{command}'");
            while (true)
            {
                Apply(command, reader);
                reader.SkipSeparators();
                if (reader.AtEnd) break;
                if (reader.NextIsCommand) command = reader.ReadCommand();
                else if (command is 'Z' or 'z') throw new FormatException("Numbers after closepath");
                else if (command == 'M') command = 'L';
                else if (command == 'm') command = 'l';
            }
            if (figureOpen) EndOpenFigure();
        }

        private void Apply(char command, PathReader r)
        {
            var relative = char.IsLower(command);
            Point P(double x, double y) => relative ? new Point(current.X + x, current.Y + y) : new Point(x, y);
            Point? cubic = null, quad = null;
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                {
                    var p = P(r.ReadNumber(), r.ReadNumber());
                    if (figureOpen) EndOpenFigure();
                    context.BeginFigure(p, true);
                    figureOpen = anyFigure = true;
                    current = start = p;
                    break;
                }
                case 'L':
                    RequireFigure();
                    current = P(r.ReadNumber(), r.ReadNumber());
                    context.LineTo(current);
                    break;
                case 'H':
                    RequireFigure();
                    current = new Point(relative ? current.X + r.ReadNumber() : r.ReadNumber(), current.Y);
                    context.LineTo(current);
                    break;
                case 'V':
                    RequireFigure();
                    current = new Point(current.X, relative ? current.Y + r.ReadNumber() : r.ReadNumber());
                    context.LineTo(current);
                    break;
                case 'C':
                {
                    RequireFigure();
                    var c1 = P(r.ReadNumber(), r.ReadNumber());
                    var c2 = P(r.ReadNumber(), r.ReadNumber());
                    var end = P(r.ReadNumber(), r.ReadNumber());
                    context.CubicBezierTo(c1, c2, end);
                    cubic = c2;
                    current = end;
                    break;
                }
                case 'S':
                {
                    RequireFigure();
                    var c1 = lastCubicControl is { } last ? new Point(2 * current.X - last.X, 2 * current.Y - last.Y) : current;
                    var c2 = P(r.ReadNumber(), r.ReadNumber());
                    var end = P(r.ReadNumber(), r.ReadNumber());
                    context.CubicBezierTo(c1, c2, end);
                    cubic = c2;
                    current = end;
                    break;
                }
                case 'Q':
                {
                    RequireFigure();
                    var c = P(r.ReadNumber(), r.ReadNumber());
                    var end = P(r.ReadNumber(), r.ReadNumber());
                    context.QuadraticBezierTo(c, end);
                    quad = c;
                    current = end;
                    break;
                }
                case 'T':
                {
                    RequireFigure();
                    var c = lastQuadControl is { } last ? new Point(2 * current.X - last.X, 2 * current.Y - last.Y) : current;
                    var end = P(r.ReadNumber(), r.ReadNumber());
                    context.QuadraticBezierTo(c, end);
                    quad = c;
                    current = end;
                    break;
                }
                case 'A':
                {
                    RequireFigure();
                    var rx = Math.Abs(r.ReadNumber());
                    var ry = Math.Abs(r.ReadNumber());
                    var rotation = r.ReadNumber();
                    var large = r.ReadFlag();
                    var sweep = r.ReadFlag();
                    var end = P(r.ReadNumber(), r.ReadNumber());
                    if (end == current) break; // SVG F.6.2: identical end points omit the arc
                    if (rx == 0 || ry == 0) context.LineTo(end); // SVG F.6.2: a zero radius is a straight line
                    else context.ArcTo(end, new Size(rx, ry), rotation, large,
                        sweep ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
                    current = end;
                    break;
                }
                case 'Z':
                    if (figureOpen) context.EndFigure(true);
                    figureOpen = false;
                    current = start;
                    break;
                default:
                    throw new FormatException($"Unknown path command '{command}'");
            }
            lastCubicControl = cubic;
            lastQuadControl = quad;
        }

        private void EndOpenFigure()
        {
            if (Math.Abs(current.X - start.X) > 1e-3 || Math.Abs(current.Y - start.Y) > 1e-3) everyFigureClosed = false;
            context.EndFigure(false);
        }

        private void RequireFigure()
        {
            if (figureOpen) return;
            if (!anyFigure) throw new FormatException("Drawing command before a moveto");
            // SVG 8.3.3: a drawing command right after closepath starts a new subpath at the closed subpath's start
            context.BeginFigure(current, true);
            figureOpen = true;
        }
    }

    private sealed class PathReader(string text)
    {
        private int index;

        public bool AtEnd => index >= text.Length;

        public bool NextIsCommand => !AtEnd && char.IsLetter(text[index]) && text[index] is not ('e' or 'E');

        public void SkipSeparators()
        {
            while (!AtEnd && (char.IsWhiteSpace(text[index]) || text[index] == ',')) index++;
        }

        public char ReadCommand()
        {
            SkipSeparators();
            if (AtEnd) throw new FormatException("Expected a path command");
            var c = text[index];
            if (!"MmLlHhVvCcSsQqTtAaZz".Contains(c, StringComparison.Ordinal)) throw new FormatException($"Unknown path command '{c}'");
            index++;
            return c;
        }

        public bool ReadFlag()
        {
            SkipSeparators();
            if (AtEnd || text[index] is not ('0' or '1')) throw new FormatException($"Expected an arc flag at {index} in \"{text}\"");
            return text[index++] == '1';
        }

        public double ReadNumber()
        {
            SkipSeparators();
            var begin = index;
            if (!AtEnd && text[index] is '+' or '-') index++;
            var digits = 0;
            while (!AtEnd && char.IsAsciiDigit(text[index])) { index++; digits++; }
            if (!AtEnd && text[index] == '.')
            {
                index++;
                while (!AtEnd && char.IsAsciiDigit(text[index])) { index++; digits++; }
            }
            if (digits == 0) throw new FormatException($"Expected a number at {begin} in \"{text}\"");
            if (!AtEnd && text[index] is 'e' or 'E')
            {
                var mark = index++;
                if (!AtEnd && text[index] is '+' or '-') index++;
                var exponentDigits = 0;
                while (!AtEnd && char.IsAsciiDigit(text[index])) { index++; exponentDigits++; }
                if (exponentDigits == 0) index = mark;
            }
            return double.Parse(text.AsSpan(begin, index - begin), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
