// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 "the SVG is sanitised (shapes, paths, text and gradients only; no script, no
// foreignObject, no external references) and drawn natively by the svg component"; 6.5 for the threat; ED-NEW-web-w16c-1). No
// Swift counterpart. The sanitiser reads untrusted SVG markup (the page's output for a diagram, or the markup a model wrote
// for an svg line) with an XML reader that allows no DTD and no entity, and builds NthSvgDocument from an ALLOW-LIST: the
// elements svg, g, a, defs, symbol, use, rect, circle, ellipse, line, polyline, polygon, path, text, tspan, title, desc,
// linearGradient, radialGradient, stop and marker, and for each only the presentation attributes below, each checked. Everything
// else is not copied: scripts, foreign objects, stylesheets, images, filters, clips, masks, patterns, animation, event
// handlers, classes, and every reference that is not "#id" of the same document. Nothing is blacklisted, so nothing new a
// browser learns can pass. Limits on length, nodes, depth and text keep a hostile document from costing more than it is worth.
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;

namespace Marrow.UI.NthUi.Diagram;

/// <summary>How much of a document the sanitiser reads.</summary>
public sealed record NthSvgLimits(int MaxLength, int MaxNodes, int MaxDepth, int MaxTextLength)
{
    /// <summary>The markup a model wrote in an svg line (docs/nth-ui.md: at most 262,144 characters).</summary>
    public static NthSvgLimits Model { get; } = new(262_144, 8_000, 32, 60_000);

    /// <summary>The output of the hidden Mermaid renderer.</summary>
    public static NthSvgLimits Renderer { get; } = new(Marrow.Core.Web.DiagramLimits.MaxSvg, 60_000, 64, 600_000);
}

/// <summary>The outcome of reading a markup: a document, or why there is none.</summary>
public sealed record NthSvgParseResult(NthSvgDocument? Document, string? Error)
{
    public bool Ok => Document is not null;
}

public static class NthSvgSanitizer
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    private const string XlinkNamespace = "http://www.w3.org/1999/xlink";
    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";

    /// <summary>Reads a complete markup.</summary>
    public static NthSvgParseResult Parse(string? markup, NthSvgLimits? limits = null)
    {
        limits ??= NthSvgLimits.Model;
        if (string.IsNullOrWhiteSpace(markup)) return new NthSvgParseResult(null, "The picture is empty.");
        if (markup.Length > limits.MaxLength) return new NthSvgParseResult(null, "The picture is larger than nth draws.");
        XDocument xml;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = limits.MaxLength + 4096L,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                CloseInput = true,
            };
            using var reader = XmlReader.Create(new StringReader(markup), settings);
            xml = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException error)
        {
            return new NthSvgParseResult(null, "The picture is not well-formed: " + error.Message);
        }

        if (xml.Root is not { } root || !IsSvg(root, "svg")) return new NthSvgParseResult(null, "The picture is not an svg.");
        var builder = new Builder(limits);
        try
        {
            return new NthSvgParseResult(builder.Build(root), null);
        }
        catch (LimitException error)
        {
            return new NthSvgParseResult(null, error.Message);
        }
    }

    /// <summary>
    /// Reads a markup that is still arriving: an element cut in the middle is dropped, and every element still open is
    /// closed, so the picture drawn so far shows. Error while the root tag has not arrived.
    /// </summary>
    public static NthSvgParseResult ParsePartial(string? markup, NthSvgLimits? limits = null)
    {
        if (string.IsNullOrWhiteSpace(markup)) return new NthSvgParseResult(null, "Waiting for the picture.");
        var closed = Close(markup, out var started);
        if (!started) return new NthSvgParseResult(null, "Waiting for the picture.");
        return Parse(closed, limits);
    }

    /// <summary>The markup cut at its last complete tag, with the end tags of the elements still open added.</summary>
    internal static string Close(string markup, out bool rootStarted)
    {
        var open = new List<string>();
        var end = 0;
        var i = 0;
        rootStarted = false;
        while (i < markup.Length)
        {
            var lt = markup.IndexOf('<', i);
            if (lt < 0)
            {
                end = markup.Length;
                break;
            }
            end = lt;
            if (string.CompareOrdinal(markup, lt, "<!--", 0, 4) == 0)
            {
                var stop = markup.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                if (stop < 0) break;
                i = stop + 3;
                end = i;
                continue;
            }
            if (string.CompareOrdinal(markup, lt, "<![CDATA[", 0, 9) == 0)
            {
                var stop = markup.IndexOf("]]>", lt + 9, StringComparison.Ordinal);
                if (stop < 0) break;
                i = stop + 3;
                end = i;
                continue;
            }
            // The tag's end, past any '>' inside a quoted value.
            var j = lt + 1;
            char quote = '\0';
            var complete = false;
            for (; j < markup.Length; j++)
            {
                var c = markup[j];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                }
                else if (c is '"' or '\'') quote = c;
                else if (c == '>')
                {
                    complete = true;
                    break;
                }
            }
            if (!complete) break;
            var tag = markup.Substring(lt + 1, j - lt - 1);
            i = j + 1;
            end = i;
            if (tag.Length == 0 || tag[0] is '?' or '!') continue;
            if (tag[0] == '/')
            {
                if (open.Count > 0) open.RemoveAt(open.Count - 1);
                continue;
            }
            var nameEnd = 0;
            while (nameEnd < tag.Length && !char.IsWhiteSpace(tag[nameEnd]) && tag[nameEnd] != '/') nameEnd++;
            var name = tag[..nameEnd];
            if (name.Length == 0) continue;
            rootStarted = true;
            if (tag[^1] != '/') open.Add(name);
        }

        var builder = new StringBuilder(markup[..Math.Min(end, markup.Length)]);
        for (var k = open.Count - 1; k >= 0; k--) builder.Append("</").Append(open[k]).Append('>');
        return builder.ToString();
    }

    private static bool IsSvg(XElement element, string name) =>
        element.Name.LocalName == name && (element.Name.NamespaceName.Length == 0 || element.Name.NamespaceName == SvgNamespace);

    private sealed class LimitException(string message) : Exception(message);

    private sealed class Builder(NthSvgLimits limits)
    {
        private static readonly HashSet<string> Dropped = new(StringComparer.Ordinal)
        {
            "script", "foreignObject", "style", "image", "filter", "clipPath", "mask", "pattern", "animate", "animateMotion",
            "animateTransform", "set", "iframe", "object", "embed", "video", "audio", "canvas", "switch", "textPath", "view",
            "cursor", "font", "font-face", "feImage", "link", "meta", "metadata", "html", "body", "div", "span", "p",
        };

        private readonly SortedSet<string> removed = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NthSvgGradient> gradients = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NthSvgMarker> markers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NthSvgNode> targets = new(StringComparer.Ordinal);
        private readonly NthSvgGroup defs = new() { Rendered = false };
        private int nodes;
        private int textLength;
        private string? title;
        private string? description;

        public NthSvgDocument Build(XElement root)
        {
            var group = new NthSvgGroup();
            group.Style = ReadStyle(root, InlineStyle(root));
            NoteHandlers(root);
            Fill(group, root, 0, preserve: false);
            var viewBox = ReadViewBox(root, out var declared);
            return new NthSvgDocument(viewBox, group, defs, gradients, markers, targets, title, description, nodes, [.. removed], declared);
        }

        private void Note(string text) => removed.Add(text);

        private void Count()
        {
            if (++nodes > limits.MaxNodes) throw new LimitException("The picture has more parts than nth draws.");
        }

        private Rect ReadViewBox(XElement root, out Size? declared)
        {
            declared = null;
            double? width = Attribute(root, "width") is { } w && Length(w, out var wl, out _) ? wl : null;
            double? height = Attribute(root, "height") is { } h && Length(h, out var hl, out _) ? hl : null;
            if (width is > 0 and <= 20_000 && height is > 0 and <= 20_000) declared = new Size(width.Value, height.Value);
            if (Attribute(root, "viewBox") is { } text)
            {
                var numbers = Numbers(text);
                if (numbers.Count == 4 && numbers[2] > 0 && numbers[3] > 0 && numbers[2] <= 20_000 && numbers[3] <= 20_000)
                    return new Rect(numbers[0], numbers[1], numbers[2], numbers[3]);
            }
            if (declared is { } size) return new Rect(0, 0, size.Width, size.Height);
            return new Rect(0, 0, 300, 150);
        }

        // ---- elements ----------------------------------------------------------------------------------------------------

        private void Fill(NthSvgGroup parent, XElement element, int depth, bool preserve)
        {
            if (depth > limits.MaxDepth) throw new LimitException("The picture is nested deeper than nth draws.");
            preserve |= (string?)element.Attribute(XName.Get("space", XmlNamespace)) == "preserve";
            foreach (var child in element.Elements())
            {
                var ns = child.Name.NamespaceName;
                var name = child.Name.LocalName;
                if (ns.Length != 0 && ns != SvgNamespace)
                {
                    Note("element in another namespace");
                    continue;
                }
                if (Dropped.Contains(name))
                {
                    Note("<" + name + "> removed");
                    continue;
                }
                var node = Element(child, name, depth + 1, preserve);
                if (node is null) continue;
                parent.Children.Add(node);
            }
        }

        private NthSvgNode? Element(XElement e, string name, int depth, bool preserve)
        {
            switch (name)
            {
                case "title":
                case "desc":
                {
                    var caption = Collapse(e.Value);
                    if (caption.Length > 0 && caption.Length <= 400)
                    {
                        if (name == "title") title ??= caption;
                        else description ??= caption;
                    }
                    return null;
                }
                case "linearGradient":
                case "radialGradient":
                    Gradient(e, name == "radialGradient");
                    return null;
                case "marker":
                    Marker(e, depth, preserve);
                    return null;
                case "defs":
                {
                    Fill(defs, e, depth, preserve);
                    return null;
                }
                case "g":
                case "a":
                case "svg":
                case "symbol":
                {
                    Count();
                    var group = new NthSvgGroup { Rendered = name != "symbol" };
                    Common(group, e);
                    if (Hidden(e)) return null;
                    if (name == "svg")
                    {
                        var x = Number(e, "x");
                        var y = Number(e, "y");
                        if (x != 0 || y != 0)
                        {
                            group.Transform = Matrix.CreateTranslation(x, y) * group.Transform;
                            group.HasTransform = true;
                        }
                    }
                    Fill(group, e, depth, preserve);
                    Register(group);
                    return group;
                }
                case "use":
                {
                    Count();
                    var href = Href(e);
                    if (href is null)
                    {
                        Note("external reference");
                        return null;
                    }
                    var use = new NthSvgUse { Target = href, X = Number(e, "x"), Y = Number(e, "y") };
                    Common(use, e);
                    return Hidden(e) ? null : use;
                }
                case "rect":
                {
                    Count();
                    var rx = Number(e, "rx");
                    var ry = Number(e, "ry");
                    if (Attribute(e, "rx") is null) rx = ry;
                    if (Attribute(e, "ry") is null) ry = rx;
                    var rect = new NthSvgRect { X = Number(e, "x"), Y = Number(e, "y"), Width = Number(e, "width"), Height = Number(e, "height"), Rx = rx, Ry = ry };
                    Common(rect, e);
                    if (Hidden(e) || rect.Width <= 0 || rect.Height <= 0) return null;
                    Register(rect);
                    return rect;
                }
                case "circle":
                case "ellipse":
                {
                    Count();
                    var circle = name == "circle";
                    var r = Number(e, "r");
                    var ellipse = new NthSvgEllipse
                    {
                        Cx = Number(e, "cx"),
                        Cy = Number(e, "cy"),
                        Rx = circle ? r : Number(e, "rx"),
                        Ry = circle ? r : Number(e, "ry"),
                        IsCircle = circle,
                    };
                    Common(ellipse, e);
                    if (Hidden(e) || ellipse.Rx <= 0 || ellipse.Ry <= 0) return null;
                    Register(ellipse);
                    return ellipse;
                }
                case "line":
                {
                    Count();
                    var line = new NthSvgLine { X1 = Number(e, "x1"), Y1 = Number(e, "y1"), X2 = Number(e, "x2"), Y2 = Number(e, "y2") };
                    Common(line, e);
                    if (Hidden(e)) return null;
                    Register(line);
                    return line;
                }
                case "polyline":
                case "polygon":
                {
                    Count();
                    var numbers = Numbers(Attribute(e, "points") ?? "");
                    if (numbers.Count > 2 * NthSvgPathData.MaxSegments) numbers = numbers.GetRange(0, 2 * NthSvgPathData.MaxSegments);
                    var points = new List<Point>(numbers.Count / 2);
                    for (var i = 0; i + 1 < numbers.Count; i += 2) points.Add(new Point(numbers[i], numbers[i + 1]));
                    var poly = new NthSvgPoly { Points = points, Closed = name == "polygon" };
                    Common(poly, e);
                    if (Hidden(e) || points.Count < 2) return null;
                    Register(poly);
                    return poly;
                }
                case "path":
                {
                    Count();
                    var path = new NthSvgPathShape { Data = NthSvgPathData.Parse(Attribute(e, "d")) };
                    Common(path, e);
                    if (Hidden(e) || path.Data.IsEmpty) return null;
                    Register(path);
                    return path;
                }
                case "text":
                    return Text(e, preserve);
                default:
                    Note("<" + name + "> not drawn");
                    return null;
            }
        }

        private void Register(NthSvgNode node)
        {
            if (node.Id is { } id && targets.Count < 4000) targets.TryAdd(id, node);
        }

        private static bool Hidden(XElement e)
        {
            if (Attribute(e, "display") is { } attribute && attribute.Trim() == "none") return true;
            return InlineStyle(e).TryGetValue("display", out var value) && value == "none";
        }

        /// <summary>The id, the transform and the presentation properties every drawable carries.</summary>
        private void Common(NthSvgNode node, XElement e)
        {
            if (Attribute(e, "id") is { } id && IsId(id)) node.Id = id;
            var transformText = Attribute(e, "transform");
            var inline = InlineStyle(e);
            if (inline.TryGetValue("transform", out var styled)) transformText = styled;
            if (transformText is not null)
            {
                if (Transform(transformText, out var matrix))
                {
                    node.Transform = matrix;
                    node.HasTransform = true;
                }
                else Note("transform not read");
            }
            node.Style = ReadStyle(e, inline);
            NoteHandlers(e);
        }

        private void NoteHandlers(XElement e)
        {
            foreach (var attribute in e.Attributes())
            {
                var local = attribute.Name.LocalName;
                if (local.StartsWith("on", StringComparison.OrdinalIgnoreCase)) Note("event handler " + local.ToLowerInvariant());
            }
        }

        // ---- text --------------------------------------------------------------------------------------------------------

        private NthSvgText? Text(XElement e, bool preserve)
        {
            Count();
            preserve |= (string?)e.Attribute(XName.Get("space", XmlNamespace)) == "preserve";
            var pieces = new List<Piece>();
            Walk(e, null, pieces, 0, isRoot: true);
            if (Hidden(e)) return null;
            var runs = Layout(pieces, preserve);
            if (runs.Count == 0) return null;
            var text = new NthSvgText { X = FirstNumber(e, "x"), Y = FirstNumber(e, "y"), Runs = runs };
            Common(text, e);
            Register(text);
            return text;
        }

        private sealed class Piece
        {
            public string Text = "";
            public double? X;
            public double? Y;
            public double Dx;
            public double Dy;
            public bool DxEm;
            public bool DyEm;
            public NthSvgStyle Style = NthSvgStyle.Empty;
        }

        private void Walk(XElement element, NthSvgStyle? inherited, List<Piece> pieces, int depth, bool isRoot)
        {
            if (depth > 16) throw new LimitException("The text is nested deeper than nth draws.");
            NthSvgStyle own = isRoot ? NthSvgStyle.Empty : ReadStyle(element, InlineStyle(element));
            var style = isRoot ? NthSvgStyle.Empty : Overlay(inherited, own);
            var first = true;
            foreach (var node in element.Nodes())
            {
                if (node is XText t)
                {
                    if (t.Value.Length == 0) continue;
                    textLength += t.Value.Length;
                    if (textLength > limits.MaxTextLength) throw new LimitException("The text is longer than nth draws.");
                    var piece = new Piece { Text = t.Value, Style = style };
                    if (first && !isRoot)
                    {
                        piece.X = OptionalNumber(element, "x");
                        piece.Y = OptionalNumber(element, "y");
                        (piece.Dx, piece.DxEm) = Delta(element, "dx");
                        (piece.Dy, piece.DyEm) = Delta(element, "dy");
                    }
                    first = false;
                    pieces.Add(piece);
                }
                else if (node is XElement child && IsSvg(child, "tspan"))
                {
                    Count();
                    var before = pieces.Count;
                    Walk(child, style, pieces, depth + 1, isRoot: false);
                    if (pieces.Count == before && (child.Attribute("x") is not null || child.Attribute("y") is not null))
                    {
                        // An empty tspan that moves the pen still counts (a blank line).
                        var empty = new Piece { Text = "", X = OptionalNumber(child, "x"), Y = OptionalNumber(child, "y"), Style = Overlay(style, ReadStyle(child, InlineStyle(child))) };
                        (empty.Dx, empty.DxEm) = Delta(child, "dx");
                        (empty.Dy, empty.DyEm) = Delta(child, "dy");
                        pieces.Add(empty);
                    }
                    first = false;
                }
                else if (node is XElement other)
                {
                    Note("<" + other.Name.LocalName + "> in text not drawn");
                }
            }
        }

        private static List<NthSvgTextRun> Layout(List<Piece> pieces, bool preserve)
        {
            // XML white space: line breaks and tabs are spaces; runs of spaces collapse to one, and the ends are trimmed,
            // unless xml:space says preserve.
            var runs = new List<NthSvgTextRun>(pieces.Count);
            var previousSpace = true;
            for (var i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                var builder = new StringBuilder(piece.Text.Length);
                foreach (var c in piece.Text)
                {
                    var ch = c is '\n' or '\r' or '\t' ? ' ' : c;
                    if (char.IsControl(ch)) continue;
                    if (!preserve && ch == ' ')
                    {
                        if (previousSpace) continue;
                        previousSpace = true;
                    }
                    else previousSpace = false;
                    builder.Append(ch);
                }
                runs.Add(new NthSvgTextRun { Text = builder.ToString(), X = piece.X, Y = piece.Y, Dx = piece.Dx, Dy = piece.Dy, DxEm = piece.DxEm, DyEm = piece.DyEm, Style = piece.Style });
            }
            if (!preserve)
            {
                // Trim the start of the first and the end of the last run that has characters.
                var firstIndex = runs.FindIndex(r => r.Text.Length > 0);
                if (firstIndex >= 0 && runs[firstIndex].Text[0] == ' ') runs[firstIndex] = Copy(runs[firstIndex], runs[firstIndex].Text.TrimStart(' '));
                var lastIndex = runs.FindLastIndex(r => r.Text.Length > 0);
                if (lastIndex >= 0 && runs[lastIndex].Text.Length > 0 && runs[lastIndex].Text[^1] == ' ') runs[lastIndex] = Copy(runs[lastIndex], runs[lastIndex].Text.TrimEnd(' '));
            }
            // A text that has no characters at all draws nothing.
            return runs.Any(r => r.Text.Length > 0) ? runs : [];
        }

        private static NthSvgTextRun Copy(NthSvgTextRun run, string text) =>
            new() { Text = text, X = run.X, Y = run.Y, Dx = run.Dx, Dy = run.Dy, DxEm = run.DxEm, DyEm = run.DyEm, Style = run.Style };

        private static (double Value, bool Em) Delta(XElement e, string name)
        {
            if (Attribute(e, name) is not { } text) return (0, false);
            var parts = text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !Length(parts[0], out var value, out var em)) return (0, false);
            return (Math.Clamp(value, -10_000, 10_000), em);
        }

        private static double? OptionalNumber(XElement e, string name) =>
            Attribute(e, name) is { } text && text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts && Length(parts[0], out var value, out _) ? value : null;

        private static double FirstNumber(XElement e, string name) => OptionalNumber(e, name) ?? 0;

        // ---- gradients and markers ---------------------------------------------------------------------------------------

        private void Gradient(XElement e, bool radial)
        {
            if (Attribute(e, "id") is not { } id || !IsId(id) || gradients.Count >= 400) return;
            Count();
            var userSpace = Attribute(e, "gradientUnits") == "userSpaceOnUse";
            var stops = new List<NthSvgStop>();
            foreach (var stop in e.Elements().Where(c => IsSvg(c, "stop")))
            {
                if (stops.Count >= 64) break;
                var inline = InlineStyle(stop);
                var colorText = inline.GetValueOrDefault("stop-color") ?? Attribute(stop, "stop-color") ?? "black";
                var opacityText = inline.GetValueOrDefault("stop-opacity") ?? Attribute(stop, "stop-opacity");
                if (!NthSvgColor.TryParse(colorText, out var color)) color = Colors.Black;
                if (opacityText is not null && Fraction(opacityText, out var alpha)) color = Color.FromArgb((byte)Math.Round(color.A * alpha), color.R, color.G, color.B);
                var offset = Attribute(stop, "offset") is { } o && Fraction(o, out var f) ? f : 0;
                if (stops.Count > 0) offset = Math.Max(offset, stops[^1].Offset);
                stops.Add(new NthSvgStop(offset, color));
            }
            double Coordinate(string name, double fallback) =>
                Attribute(e, name) is { } text && Coordinate1(text, userSpace, out var value) ? value : fallback;
            Matrix matrix = Matrix.Identity;
            var has = false;
            if (Attribute(e, "gradientTransform") is { } t && Transform(t, out var parsed))
            {
                matrix = parsed;
                has = true;
            }
            gradients[id] = new NthSvgGradient
            {
                Id = id,
                Radial = radial,
                UserSpace = userSpace,
                X1 = Coordinate("x1", 0),
                Y1 = Coordinate("y1", 0),
                X2 = Coordinate("x2", userSpace ? 0 : 1),
                Y2 = Coordinate("y2", 0),
                Cx = Coordinate("cx", userSpace ? 0 : .5),
                Cy = Coordinate("cy", userSpace ? 0 : .5),
                R = Coordinate("r", userSpace ? 0 : .5),
                Fx = Coordinate("fx", Coordinate("cx", userSpace ? 0 : .5)),
                Fy = Coordinate("fy", Coordinate("cy", userSpace ? 0 : .5)),
                Transform = matrix,
                HasTransform = has,
                Stops = stops,
            };
        }

        private static bool Coordinate1(string text, bool userSpace, out double value)
        {
            value = 0;
            var trimmed = text.Trim();
            if (trimmed.EndsWith('%'))
            {
                if (!double.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) || !double.IsFinite(percent)) return false;
                value = userSpace ? percent : percent / 100;
                return true;
            }
            if (!Length(trimmed, out value, out _)) return false;
            return true;
        }

        private void Marker(XElement e, int depth, bool preserve)
        {
            if (Attribute(e, "id") is not { } id || !IsId(id) || markers.Count >= 100) return;
            Count();
            var content = new NthSvgGroup();
            Fill(content, e, depth, preserve);
            Rect? viewBox = null;
            if (Attribute(e, "viewBox") is { } vb)
            {
                var n = Numbers(vb);
                if (n.Count == 4 && n[2] > 0 && n[3] > 0) viewBox = new Rect(n[0], n[1], n[2], n[3]);
            }
            var orient = Attribute(e, "orient")?.Trim();
            var auto = orient is "auto" or "auto-start-reverse";
            var angle = 0.0;
            if (!auto && orient is not null && double.TryParse(orient.Replace("deg", "", StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees) && double.IsFinite(degrees)) angle = degrees;
            markers[id] = new NthSvgMarker
            {
                Id = id,
                RefX = Number(e, "refX"),
                RefY = Number(e, "refY"),
                Width = Attribute(e, "markerWidth") is not null ? Math.Clamp(Number(e, "markerWidth"), 0, 1000) : 3,
                Height = Attribute(e, "markerHeight") is not null ? Math.Clamp(Number(e, "markerHeight"), 0, 1000) : 3,
                Auto = auto,
                Angle = angle,
                UserSpace = Attribute(e, "markerUnits") == "userSpaceOnUse",
                ViewBox = viewBox,
                Content = content,
            };
        }

        // ---- presentation properties -------------------------------------------------------------------------------------

        private static readonly string[] Presentation =
        [
            "fill", "stroke", "fill-opacity", "stroke-opacity", "opacity", "stroke-width", "stroke-linecap", "stroke-linejoin",
            "stroke-miterlimit", "stroke-dasharray", "stroke-dashoffset", "fill-rule", "font-family", "font-size", "font-weight",
            "font-style", "text-anchor", "dominant-baseline", "alignment-baseline", "letter-spacing", "text-decoration",
            "text-decoration-line", "visibility", "marker-start", "marker-mid", "marker-end", "marker",
        ];

        private NthSvgStyle ReadStyle(XElement e, Dictionary<string, string> inline)
        {
            var style = new NthSvgStyle();
            foreach (var name in Presentation)
            {
                if (Attribute(e, name) is { } value) Apply(style, name, value);
            }
            foreach (var (name, value) in inline) Apply(style, name, value);
            return style.IsEmpty ? NthSvgStyle.Empty : style;
        }

        private void Apply(NthSvgStyle s, string name, string raw)
        {
            var value = raw.Trim();
            if (value.Length == 0 || value.Length > 600 || value == "inherit") return;
            switch (name)
            {
                case "fill":
                    if (Paint(value, out var fill)) s.Fill = fill;
                    break;
                case "stroke":
                    if (Paint(value, out var stroke)) s.Stroke = stroke;
                    break;
                case "fill-opacity":
                    if (Fraction(value, out var fo)) s.FillOpacity = fo;
                    break;
                case "stroke-opacity":
                    if (Fraction(value, out var so)) s.StrokeOpacity = so;
                    break;
                case "opacity":
                    if (Fraction(value, out var op)) s.Opacity = op;
                    break;
                case "stroke-width":
                    if (Length(value, out var sw, out _) && sw >= 0) s.StrokeWidth = Math.Min(sw, 1000);
                    break;
                case "stroke-miterlimit":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ml) && double.IsFinite(ml) && ml >= 1) s.MiterLimit = Math.Min(ml, 100);
                    break;
                case "stroke-linecap":
                    s.LineCap = value switch { "round" => PenLineCap.Round, "square" => PenLineCap.Square, "butt" => PenLineCap.Flat, _ => s.LineCap };
                    break;
                case "stroke-linejoin":
                    s.LineJoin = value switch { "round" => PenLineJoin.Round, "bevel" => PenLineJoin.Bevel, "miter" or "miter-clip" or "arcs" => PenLineJoin.Miter, _ => s.LineJoin };
                    break;
                case "stroke-dasharray":
                    if (value == "none") s.DashArray = [];
                    else if (Dashes(value) is { } dashes) s.DashArray = dashes;
                    break;
                case "stroke-dashoffset":
                    if (Length(value, out var offset, out _)) s.DashOffset = Math.Clamp(offset, -100_000, 100_000);
                    break;
                case "fill-rule":
                    if (value is "evenodd" or "nonzero") s.EvenOdd = value == "evenodd";
                    break;
                case "font-family":
                    if (Family(value) is { } family) s.FontFamily = family;
                    break;
                case "font-size":
                    if (FontSize(value, out var size)) s.FontSize = size;
                    break;
                case "font-weight":
                    if (Weight(value, out var weight)) s.FontWeight = weight;
                    break;
                case "font-style":
                    if (value is "italic" or "oblique" or "normal") s.Italic = value != "normal";
                    break;
                case "text-anchor":
                    s.Anchor = value switch { "start" => NthSvgAnchor.Start, "middle" => NthSvgAnchor.Middle, "end" => NthSvgAnchor.End, _ => s.Anchor };
                    break;
                case "dominant-baseline":
                case "alignment-baseline":
                    s.Baseline = value switch
                    {
                        "alphabetic" or "auto" or "ideographic" or "baseline" => NthSvgBaseline.Alphabetic,
                        "central" or "middle" or "mathematical" => NthSvgBaseline.Central,
                        "hanging" or "text-before-edge" or "before-edge" or "text-top" => NthSvgBaseline.Hanging,
                        "text-after-edge" or "after-edge" or "text-bottom" => NthSvgBaseline.TextBottom,
                        _ => s.Baseline,
                    };
                    break;
                case "letter-spacing":
                    if (value == "normal") s.LetterSpacing = 0;
                    else if (Length(value, out var spacing, out var em)) s.LetterSpacing = Math.Clamp(em ? spacing * 16 : spacing, -5, 50);
                    break;
                case "text-decoration":
                case "text-decoration-line":
                    s.Underline = value.Contains("underline", StringComparison.Ordinal) ? true : s.Underline;
                    s.LineThrough = value.Contains("line-through", StringComparison.Ordinal) ? true : s.LineThrough;
                    break;
                case "visibility":
                    if (value is "visible" or "hidden" or "collapse") s.Visible = value == "visible";
                    break;
                case "marker-start":
                    s.MarkerStart = MarkerRef(value) ?? s.MarkerStart;
                    break;
                case "marker-mid":
                    s.MarkerMid = MarkerRef(value) ?? s.MarkerMid;
                    break;
                case "marker-end":
                    s.MarkerEnd = MarkerRef(value) ?? s.MarkerEnd;
                    break;
                case "marker":
                    if (MarkerRef(value) is { } all) s.MarkerStart = s.MarkerMid = s.MarkerEnd = all;
                    break;
            }
        }

        private bool Paint(string value, out NthSvgPaint paint)
        {
            paint = default;
            if (value == "none")
            {
                paint = NthSvgPaint.None;
                return true;
            }
            if (value.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
            {
                paint = NthSvgPaint.CurrentColor;
                return true;
            }
            if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            {
                var id = UrlId(value, out var rest);
                if (id is null)
                {
                    Note("external reference");
                    return false;
                }
                // "url(#g) red": the fallback is used when the reference is missing; a missing gradient draws nothing here.
                paint = new NthSvgPaint(NthSvgPaintKind.Reference, default, id);
                if (rest.Length > 0 && NthSvgColor.TryParse(rest, out var fallback)) paint = paint with { Color = fallback };
                return true;
            }
            if (NthSvgColor.TryParse(value, out var color))
            {
                paint = NthSvgPaint.Of(color);
                return true;
            }
            return false;
        }

        /// <summary>The id of a "url(#id)" (quotes allowed), or null for any other address.</summary>
        private static string? UrlId(string value, out string rest)
        {
            rest = "";
            var close = value.IndexOf(')', StringComparison.Ordinal);
            if (close < 0) return null;
            var inside = value[4..close].Trim().Trim('"', '\'').Trim();
            rest = value[(close + 1)..].Trim();
            if (inside.Length < 2 || inside[0] != '#') return null;
            var id = inside[1..];
            return IsId(id) ? id : null;
        }

        private string? MarkerRef(string value)
        {
            if (value == "none") return null;
            if (!value.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) return null;
            var id = UrlId(value, out _);
            if (id is null) Note("external reference");
            return id;
        }

        private static double[]? Dashes(string value)
        {
            var parts = value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is 0 or > 16) return null;
            var result = new double[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!Length(parts[i], out var length, out _) || length < 0) return null;
                result[i] = Math.Min(length, 100_000);
            }
            return result.Sum() > 0 ? result : null;
        }

        private static string? Family(string value)
        {
            // Names, commas, quotes, dots and hyphens only: no function, no address, no escape.
            var builder = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                if (char.IsAsciiLetterOrDigit(c) || c is ' ' or ',' or '-' or '_' or '.' or '\'' or '"') builder.Append(c);
                else return null;
            }
            var family = builder.ToString().Trim();
            return family.Length is > 0 and <= 200 ? family : null;
        }

        private static bool FontSize(string value, out double size)
        {
            size = 0;
            switch (value)
            {
                case "xx-small": size = 9; return true;
                case "x-small": size = 10; return true;
                case "small": size = 13; return true;
                case "medium": size = 16; return true;
                case "large": size = 18; return true;
                case "x-large": size = 24; return true;
                case "xx-large": size = 32; return true;
            }
            if (value.EndsWith('%'))
            {
                if (!double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) || !double.IsFinite(percent)) return false;
                size = Math.Clamp(16 * percent / 100, 1, 400);
                return true;
            }
            if (!Length(value, out var length, out var em)) return false;
            size = Math.Clamp(em ? length * 16 : length, 1, 400);
            return true;
        }

        private static bool Weight(string value, out int weight)
        {
            weight = 400;
            switch (value)
            {
                case "normal": return true;
                case "bold" or "bolder": weight = 700; return true;
                case "lighter": weight = 300; return true;
            }
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) return false;
            weight = (int)Math.Clamp(Math.Round(number / 100) * 100, 100, 900);
            return true;
        }

        // ---- values ------------------------------------------------------------------------------------------------------

        private static string? Attribute(XElement e, string name)
        {
            var attribute = e.Attribute(name);
            return attribute?.Value;
        }

        private static string? Href(XElement e)
        {
            var value = e.Attribute("href")?.Value ?? e.Attribute(XName.Get("href", XlinkNamespace))?.Value;
            if (value is null) return null;
            var trimmed = value.Trim();
            return trimmed.Length > 1 && trimmed[0] == '#' && IsId(trimmed[1..]) ? trimmed[1..] : null;
        }

        private static Dictionary<string, string> InlineStyle(XElement e)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (e.Attribute("style")?.Value is not { Length: > 0 } text || text.Length > 4000) return result;
            var depth = 0;
            var begin = 0;
            for (var i = 0; i <= text.Length; i++)
            {
                if (i < text.Length)
                {
                    if (text[i] == '(') depth++;
                    else if (text[i] == ')' && depth > 0) depth--;
                    if (text[i] != ';' || depth > 0) continue;
                }
                var declaration = text[begin..i];
                begin = i + 1;
                var colon = declaration.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0) continue;
                var name = declaration[..colon].Trim().ToLowerInvariant();
                var value = declaration[(colon + 1)..].Replace("!important", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (name.Length > 0 && value.Length > 0 && result.Count < 64) result[name] = value;
            }
            return result;
        }

        private static NthSvgStyle Overlay(NthSvgStyle? under, NthSvgStyle over)
        {
            if (under is null || under.IsEmpty) return over;
            if (over.IsEmpty) return under;
            return new NthSvgStyle
            {
                Fill = over.Fill ?? under.Fill,
                Stroke = over.Stroke ?? under.Stroke,
                FillOpacity = over.FillOpacity ?? under.FillOpacity,
                StrokeOpacity = over.StrokeOpacity ?? under.StrokeOpacity,
                Opacity = over.Opacity ?? under.Opacity,
                StrokeWidth = over.StrokeWidth ?? under.StrokeWidth,
                MiterLimit = over.MiterLimit ?? under.MiterLimit,
                EvenOdd = over.EvenOdd ?? under.EvenOdd,
                LineCap = over.LineCap ?? under.LineCap,
                LineJoin = over.LineJoin ?? under.LineJoin,
                DashArray = over.DashArray ?? under.DashArray,
                DashOffset = over.DashOffset ?? under.DashOffset,
                FontFamily = over.FontFamily ?? under.FontFamily,
                FontSize = over.FontSize ?? under.FontSize,
                FontWeight = over.FontWeight ?? under.FontWeight,
                Italic = over.Italic ?? under.Italic,
                Anchor = over.Anchor ?? under.Anchor,
                Baseline = over.Baseline ?? under.Baseline,
                LetterSpacing = over.LetterSpacing ?? under.LetterSpacing,
                Underline = over.Underline ?? under.Underline,
                LineThrough = over.LineThrough ?? under.LineThrough,
                Visible = over.Visible ?? under.Visible,
                MarkerStart = over.MarkerStart ?? under.MarkerStart,
                MarkerMid = over.MarkerMid ?? under.MarkerMid,
                MarkerEnd = over.MarkerEnd ?? under.MarkerEnd,
            };
        }

        private static bool IsId(string id)
        {
            if (id.Length is 0 or > 128) return false;
            if (!(char.IsAsciiLetter(id[0]) || id[0] == '_')) return false;
            foreach (var c in id)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':')) return false;
            }
            return true;
        }

        private static double Number(XElement e, string name) =>
            Attribute(e, name) is { } text && Length(text, out var value, out var em) ? Math.Clamp(em ? value * 16 : value, -NthSvgPathData.MaxCoordinate, NthSvgPathData.MaxCoordinate) : 0;

        private static List<double> Numbers(string text)
        {
            var result = new List<double>();
            var parts = text.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (result.Count >= 400_000) break;
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || Math.Abs(value) > NthSvgPathData.MaxCoordinate) break;
                result.Add(value);
            }
            return result;
        }

        /// <summary>A length in pixels; <paramref name="em"/> is true (and the value in em) for "1.2em".</summary>
        internal static bool Length(string text, out double value, out bool em)
        {
            em = false;
            value = 0;
            var trimmed = text.Trim();
            if (trimmed.Length == 0 || trimmed.Length > 40) return false;
            var unit = 1.0;
            var number = trimmed;
            foreach (var (suffix, factor) in Units)
            {
                if (!trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                number = trimmed[..^suffix.Length];
                unit = factor;
                em = suffix is "em" or "rem";
                break;
            }
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed)) return false;
            value = em ? parsed : parsed * unit;
            return true;
        }

        private static readonly (string Suffix, double Factor)[] Units =
        [
            ("px", 1), ("rem", 16), ("em", 16), ("ex", 8), ("pt", 4.0 / 3), ("pc", 16), ("mm", 96 / 25.4), ("cm", 96 / 2.54), ("in", 96),
        ];

        private static bool Fraction(string text, out double value)
        {
            value = 1;
            var trimmed = text.Trim();
            var percent = trimmed.EndsWith('%');
            if (!double.TryParse(percent ? trimmed[..^1] : trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed)) return false;
            value = Math.Clamp(percent ? parsed / 100 : parsed, 0, 1);
            return true;
        }

        private static bool Transform(string text, out Matrix matrix)
        {
            matrix = Matrix.Identity;
            var rest = text.Trim();
            if (rest.Length == 0 || rest.Length > 600) return false;
            var count = 0;
            var result = Matrix.Identity;
            while (rest.Length > 0)
            {
                if (++count > 16) return false;
                var open = rest.IndexOf('(', StringComparison.Ordinal);
                var close = rest.IndexOf(')', StringComparison.Ordinal);
                if (open <= 0 || close < open) return false;
                var name = rest[..open].Trim();
                var numbers = Numbers(rest[(open + 1)..close]);
                rest = rest[(close + 1)..].TrimStart(' ', ',', '\t', '\r', '\n');
                Matrix next;
                switch (name)
                {
                    case "translate" when numbers.Count is 1 or 2:
                        next = Matrix.CreateTranslation(numbers[0], numbers.Count == 2 ? numbers[1] : 0);
                        break;
                    case "scale" when numbers.Count is 1 or 2:
                        next = Matrix.CreateScale(numbers[0], numbers.Count == 2 ? numbers[1] : numbers[0]);
                        break;
                    case "rotate" when numbers.Count is 1 or 3:
                    {
                        var radians = numbers[0] * Math.PI / 180;
                        next = Matrix.CreateRotation(radians);
                        if (numbers.Count == 3) next = Matrix.CreateTranslation(-numbers[1], -numbers[2]) * next * Matrix.CreateTranslation(numbers[1], numbers[2]);
                        break;
                    }
                    case "skewX" when numbers.Count == 1:
                        next = Matrix.CreateSkew(numbers[0] * Math.PI / 180, 0);
                        break;
                    case "skewY" when numbers.Count == 1:
                        next = Matrix.CreateSkew(0, numbers[0] * Math.PI / 180);
                        break;
                    case "matrix" when numbers.Count == 6:
                        next = new Matrix(numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5]);
                        break;
                    default:
                        return false;
                }
                // SVG applies the list left to right as nested coordinate systems: M = T1 * T2 * ... in matrix-on-the-left
                // notation, which Avalonia's row-vector Matrix writes as next * result.
                result = next * result;
            }
            matrix = result;
            return true;
        }

        private static string Collapse(string text)
        {
            var builder = new StringBuilder(text.Length);
            var space = true;
            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!space) builder.Append(' ');
                    space = true;
                }
                else if (!char.IsControl(c))
                {
                    builder.Append(c);
                    space = false;
                }
            }
            return builder.ToString().Trim();
        }
    }
}
