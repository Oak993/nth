// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 "drawn natively by the svg component ... native vector, themes with the thread and
// never needs a live web view"; 10 "text 4.5:1"; ED-NEW-web-w16c-1). No Swift counterpart. Draws a sanitised document with
// Avalonia's own drawing context: shapes, paths (their geometry built once), gradients, dashes, markers (arrowheads that follow
// the path), use, and text with Avalonia's text engine (anchor, baseline, tspan positions, weight, style, decoration). The
// picture is fitted into its box without stretching. For a model's line art the painter can adapt a stroke or a text colour
// that would be lost on the surface it is drawn over (black on the dark thread): the surface is the nearest opaque fill drawn
// before it, else the thread's colour, and the colour moves along its own hue until it keeps the contrast (3:1 for strokes, 4.5:1
// for text). Mermaid's diagrams are drawn in the thread's own palette already and are not adapted.
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Marrow.UI.NthUi.Diagram;

/// <summary>What the painter needs from the theme.</summary>
public sealed class NthSvgPaintContext
{
    /// <summary>The colour <c>currentColor</c> draws in.</summary>
    public Color CurrentColor { get; init; } = Colors.Black;

    /// <summary>The surface the picture sits on (the thread's colour), for adaptation.</summary>
    public Color Backdrop { get; init; } = Colors.White;

    /// <summary>Whether strokes and text are adapted to keep their contrast.</summary>
    public bool Adapt { get; init; }

    /// <summary>The family a text with no family of its own (or only "sans-serif") is set in: the thread's.</summary>
    public FontFamily Fallback { get; init; } = FontFamily.Default;

    /// <summary>The family "monospace" is set in.</summary>
    public FontFamily Mono { get; init; } = FontFamily.Default;
}

/// <summary>Draws one <see cref="NthSvgDocument"/>. Keeps the geometry and text layouts it builds, so redrawing is cheap.</summary>
public sealed class NthSvgPainter(NthSvgDocument document)
{
    public const double StrokeContrast = 3;
    public const double TextContrast = 4.5;
    private const int MaxDepth = 24;

    private readonly Dictionary<NthSvgNode, Geometry> geometries = [];
    private readonly Dictionary<NthSvgTextRun, CachedText> texts = [];
    private readonly Dictionary<(Color, double, PenLineCap, PenLineJoin, double, string), IPen> pens = [];
    private readonly Dictionary<Color, IBrush> brushes = [];
    private readonly Dictionary<string, FontFamily> families = new(StringComparer.Ordinal);
    private readonly List<(Rect Bounds, Color Fill)> opaque = [];
    private NthSvgPaintContext context = new();
    private int useCount;
    private int visited;

    public NthSvgDocument Document { get; } = document;

    /// <summary>Shapes and texts drawn by the last <see cref="Draw"/> (tests).</summary>
    public int Drawn { get; private set; }

    /// <summary>Nodes visited by the last <see cref="Draw"/>: never more than <see cref="NthSvgLimits.MaxPaintNodes"/> (tests).</summary>
    public int Visited => visited;

    // ED-NEW-secfinal-41: one paint visits at most MaxPaintNodes nodes; past it the rest is not drawn.
    private bool OverBudget => visited >= NthSvgLimits.MaxPaintNodes;

    private sealed class CachedText
    {
        public FormattedText? Formatted;
        public string Key = "";
        public Color Brush;
    }

    private readonly record struct State(
        NthSvgPaint Fill,
        NthSvgPaint Stroke,
        double FillOpacity,
        double StrokeOpacity,
        double StrokeWidth,
        double MiterLimit,
        bool EvenOdd,
        PenLineCap Cap,
        PenLineJoin Join,
        double[]? Dash,
        double DashOffset,
        string? Family,
        double FontSize,
        int Weight,
        bool Italic,
        NthSvgAnchor Anchor,
        NthSvgBaseline Baseline,
        bool Underline,
        bool LineThrough,
        bool Visible,
        string? MarkerStart,
        string? MarkerMid,
        string? MarkerEnd)
    {
        public static State Initial { get; } = new(NthSvgPaint.Of(Colors.Black), NthSvgPaint.None, 1, 1, 1, 4, false, PenLineCap.Flat, PenLineJoin.Miter,
            null, 0, null, 16, 400, false, NthSvgAnchor.Start, NthSvgBaseline.Alphabetic, false, false, true, null, null, null);

        public State With(NthSvgStyle s) => s.IsEmpty ? this : this with
        {
            Fill = s.Fill ?? Fill,
            Stroke = s.Stroke ?? Stroke,
            FillOpacity = s.FillOpacity ?? FillOpacity,
            StrokeOpacity = s.StrokeOpacity ?? StrokeOpacity,
            StrokeWidth = s.StrokeWidth ?? StrokeWidth,
            MiterLimit = s.MiterLimit ?? MiterLimit,
            EvenOdd = s.EvenOdd ?? EvenOdd,
            Cap = s.LineCap ?? Cap,
            Join = s.LineJoin ?? Join,
            Dash = s.DashArray ?? Dash,
            DashOffset = s.DashOffset ?? DashOffset,
            Family = s.FontFamily ?? Family,
            FontSize = s.FontSize ?? FontSize,
            Weight = s.FontWeight ?? Weight,
            Italic = s.Italic ?? Italic,
            Anchor = s.Anchor ?? Anchor,
            Baseline = s.Baseline ?? Baseline,
            Underline = s.Underline ?? Underline,
            LineThrough = s.LineThrough ?? LineThrough,
            Visible = s.Visible ?? Visible,
            MarkerStart = s.MarkerStart ?? MarkerStart,
            MarkerMid = s.MarkerMid ?? MarkerMid,
            MarkerEnd = s.MarkerEnd ?? MarkerEnd,
        };
    }

    /// <summary>The matrix that fits the view box into <paramref name="target"/>, centred, uniformly (meet).</summary>
    public Matrix Fit(Rect target)
    {
        var box = Document.ViewBox;
        if (box.Width <= 0 || box.Height <= 0) return Matrix.Identity;
        var scale = Math.Min(target.Width / box.Width, target.Height / box.Height);
        var x = target.X + (target.Width - box.Width * scale) / 2;
        var y = target.Y + (target.Height - box.Height * scale) / 2;
        return Matrix.CreateTranslation(-box.X, -box.Y) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x, y);
    }

    public void Draw(DrawingContext drawing, Rect target, NthSvgPaintContext paint)
    {
        context = paint;
        Drawn = 0;
        useCount = 0;
        visited = 0;
        opaque.Clear();
        if (target.Width <= 0 || target.Height <= 0) return;
        var fit = Fit(target);
        using (drawing.PushClip(target))
        using (drawing.PushTransform(fit))
        {
            var state = State.Initial.With(Document.Root.Style);
            Children(drawing, Document.Root.Children, state, fit, 0);
        }
    }

    private void Children(DrawingContext d, IReadOnlyList<NthSvgNode> children, State state, Matrix world, int depth)
    {
        foreach (var child in children)
        {
            if (OverBudget) return;
            Node(d, child, state, world, depth);
        }
    }

    private void Node(DrawingContext d, NthSvgNode node, State inherited, Matrix world, int depth)
    {
        if (depth > MaxDepth || OverBudget) return;
        visited++;
        var state = inherited.With(node.Style);
        var opacity = node.Style.Opacity ?? 1;
        var local = node.HasTransform ? node.Transform : Matrix.Identity;
        var here = node.HasTransform ? local * world : world;
        using var transformed = node.HasTransform ? d.PushTransform(local) : default;
        using var layered = opacity < 1 ? d.PushOpacity(opacity) : default;
        switch (node)
        {
            case NthSvgGroup group:
                if (group.Rendered) Children(d, group.Children, state, here, depth + 1);
                break;
            case NthSvgUse use:
                Use(d, use, state, here, depth);
                break;
            case NthSvgText text:
                Text(d, text, state, here);
                break;
            default:
                Shape(d, node, state, here, depth);
                break;
        }
    }

    private void Use(DrawingContext d, NthSvgUse use, State state, Matrix world, int depth)
    {
        if (++useCount > 2000 || OverBudget || !Document.Targets.TryGetValue(use.Target, out var target)) return;
        var shift = Matrix.CreateTranslation(use.X, use.Y);
        var here = use.X != 0 || use.Y != 0 ? shift * world : world;
        using var moved = use.X != 0 || use.Y != 0 ? d.PushTransform(shift) : default;
        if (target is NthSvgGroup { Rendered: false } symbol) Children(d, symbol.Children, state, here, depth + 1);
        else Node(d, target, state, here, depth + 1);
    }

    // ---- shapes -----------------------------------------------------------------------------------------------------

    private void Shape(DrawingContext d, NthSvgNode node, State state, Matrix world, int depth)
    {
        if (!state.Visible) return;
        Rect bounds;
        Geometry? geometry = null;
        switch (node)
        {
            case NthSvgRect rect:
                bounds = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
                break;
            case NthSvgEllipse ellipse:
                bounds = new Rect(ellipse.Cx - ellipse.Rx, ellipse.Cy - ellipse.Ry, ellipse.Rx * 2, ellipse.Ry * 2);
                break;
            case NthSvgLine line:
                bounds = new Rect(new Point(Math.Min(line.X1, line.X2), Math.Min(line.Y1, line.Y2)), new Point(Math.Max(line.X1, line.X2), Math.Max(line.Y1, line.Y2)));
                break;
            case NthSvgPoly poly:
                geometry = Cached(poly, () => PolyGeometry(poly));
                bounds = geometry.Bounds;
                break;
            case NthSvgPathShape path:
                geometry = path.Geometry ??= path.Data.ToGeometry();
                bounds = geometry.Bounds;
                break;
            default:
                return;
        }

        var fill = Brush(state.Fill, state.FillOpacity, bounds, role: 0, world);
        var stroke = state.StrokeWidth > 0 ? StrokePen(state, bounds, world) : null;
        if (fill is null && stroke is null && !HasMarkers(state)) return;
        Drawn++;
        switch (node)
        {
            case NthSvgRect rect:
                if (rect.Rx > 0 || rect.Ry > 0) d.DrawRectangle(fill, stroke, new RoundedRect(bounds, Math.Min(rect.Rx, rect.Width / 2), Math.Min(rect.Ry, rect.Height / 2)));
                else d.DrawRectangle(fill, stroke, bounds);
                break;
            case NthSvgEllipse ellipse:
                d.DrawEllipse(fill, stroke, new Point(ellipse.Cx, ellipse.Cy), ellipse.Rx, ellipse.Ry);
                break;
            case NthSvgLine line:
                if (stroke is not null) d.DrawLine(stroke, new Point(line.X1, line.Y1), new Point(line.X2, line.Y2));
                break;
            default:
                if (geometry is not null) d.DrawGeometry(fill, stroke, geometry);
                break;
        }

        if (fill is not null && state.Fill.Kind != NthSvgPaintKind.Reference && state.FillOpacity * AlphaOf(state.Fill) >= .98 && opaque.Count < 512)
            opaque.Add((bounds.TransformToAABB(world), FillColor(state.Fill)));

        if (HasMarkers(state)) Markers(d, node, state, world, depth);
    }

    private static bool HasMarkers(State s) => s.MarkerStart is not null || s.MarkerMid is not null || s.MarkerEnd is not null;

    private Geometry Cached(NthSvgNode node, Func<Geometry> make)
    {
        if (geometries.TryGetValue(node, out var geometry)) return geometry;
        return geometries[node] = make();
    }

    private static Geometry PolyGeometry(NthSvgPoly poly)
    {
        var geometry = new StreamGeometry();
        using (var c = geometry.Open())
        {
            c.BeginFigure(poly.Points[0], true);
            for (var i = 1; i < poly.Points.Count; i++) c.LineTo(poly.Points[i]);
            c.EndFigure(poly.Closed);
        }
        return geometry;
    }

    // ---- paint -------------------------------------------------------------------------------------------------------

    private static double AlphaOf(NthSvgPaint paint) => paint.Kind == NthSvgPaintKind.Color ? paint.Color.A / 255.0 : 1;

    private Color FillColor(NthSvgPaint paint) => paint.Kind == NthSvgPaintKind.CurrentColor ? context.CurrentColor : paint.Color;

    private IBrush? Brush(NthSvgPaint paint, double opacity, Rect bounds, int role, Matrix world)
    {
        switch (paint.Kind)
        {
            case NthSvgPaintKind.None:
                return null;
            case NthSvgPaintKind.Reference:
                if (paint.Reference is { } id && Document.Gradients.TryGetValue(id, out var gradient) && gradient.Stops.Count > 0) return GradientBrush(gradient, opacity, bounds);
                if (paint.Color.A > 0) return Solid(WithOpacity(paint.Color, opacity));
                return null;
            default:
            {
                var color = FillColor(paint);
                if (role == 1 && context.Adapt) color = NthSvgColor.Adapt(color, BackdropAt(bounds, world), StrokeContrast);
                return color.A == 0 || opacity <= 0 ? null : Solid(WithOpacity(color, opacity));
            }
        }
    }

    private static Color WithOpacity(Color color, double opacity) =>
        opacity >= 1 ? color : Color.FromArgb((byte)Math.Round(Math.Clamp(color.A * opacity, 0, 255)), color.R, color.G, color.B);

    private IBrush Solid(Color color)
    {
        if (brushes.TryGetValue(color, out var brush)) return brush;
        return brushes[color] = new ImmutableSolidColorBrush(color);
    }

    private static IBrush GradientBrush(NthSvgGradient g, double opacity, Rect bounds)
    {
        var stops = g.Stops.Select(s => new GradientStop(WithOpacity(s.Color, opacity), Math.Clamp(s.Offset, 0, 1))).ToList();
        RelativePoint Point(double x, double y) => g.UserSpace
            ? new RelativePoint(x - bounds.X, y - bounds.Y, RelativeUnit.Absolute)
            : new RelativePoint(x, y, RelativeUnit.Relative);
        if (!g.Radial)
        {
            var brush = new LinearGradientBrush { StartPoint = Point(g.X1, g.Y1), EndPoint = Point(g.X2, g.Y2) };
            foreach (var stop in stops) brush.GradientStops.Add(stop);
            return brush;
        }
        var radial = new RadialGradientBrush { Center = Point(g.Cx, g.Cy), GradientOrigin = Point(g.Fx, g.Fy) };
        if (g.UserSpace)
        {
            radial.RadiusX = new RelativeScalar(g.R, RelativeUnit.Absolute);
            radial.RadiusY = new RelativeScalar(g.R, RelativeUnit.Absolute);
        }
        else
        {
            radial.RadiusX = new RelativeScalar(g.R, RelativeUnit.Relative);
            radial.RadiusY = new RelativeScalar(g.R, RelativeUnit.Relative);
        }
        foreach (var stop in stops) radial.GradientStops.Add(stop);
        return radial;
    }

    private IPen? StrokePen(State state, Rect bounds, Matrix world)
    {
        var brush = Brush(state.Stroke, state.StrokeOpacity, bounds, role: 1, world);
        if (brush is null) return null;
        var width = state.StrokeWidth;
        var key = (brush is ISolidColorBrush solid ? solid.Color : default, width, state.Cap, state.Join, state.MiterLimit,
            state.Dash is null ? "" : string.Join(',', state.Dash.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))) + "@" + state.DashOffset.ToString("0.###", CultureInfo.InvariantCulture));
        if (brush is ISolidColorBrush && pens.TryGetValue(key, out var cached)) return cached;
        IDashStyle? dashes = null;
        if (state.Dash is { Length: > 0 } list && list.Sum() > 0)
        {
            var even = list.Length % 2 == 0 ? list : [.. list, .. list];
            // A gap of nothing (Mermaid's "1, 0") is a solid line.
            var gaps = even.Where((_, i) => i % 2 == 1).Sum();
            if (gaps > 0) dashes = new ImmutableDashStyle(even.Select(v => v / width), state.DashOffset / width);
        }
        var pen = new Pen(brush, width, dashes, state.Cap, state.Join, Math.Max(1, state.MiterLimit));
        if (brush is ISolidColorBrush && pens.Count < 512) pens[key] = pen;
        return pen;
    }

    /// <summary>The surface a shape or text over <paramref name="bounds"/> sits on: the latest opaque fill drawn before it that
    /// holds its centre, else the thread's colour.</summary>
    private Color BackdropAt(Rect bounds, Matrix world)
    {
        var centre = bounds.TransformToAABB(world).Center;
        for (var i = opaque.Count - 1; i >= 0; i--)
        {
            if (opaque[i].Bounds.Contains(centre)) return NthUiTokens.Over(opaque[i].Fill, context.Backdrop);
        }
        return context.Backdrop;
    }

    // ---- markers -----------------------------------------------------------------------------------------------------

    private void Markers(DrawingContext d, NthSvgNode node, State state, Matrix world, int depth)
    {
        IReadOnlyList<NthSvgPathVertex> vertices;
        switch (node)
        {
            case NthSvgPathShape path:
                vertices = path.Data.Vertices();
                break;
            case NthSvgLine line:
                vertices = [new NthSvgPathVertex(new Point(line.X1, line.Y1), null, new Point(line.X2, line.Y2) - new Point(line.X1, line.Y1)),
                    new NthSvgPathVertex(new Point(line.X2, line.Y2), new Point(line.X2, line.Y2) - new Point(line.X1, line.Y1), null)];
                break;
            case NthSvgPoly poly:
            {
                var list = new List<NthSvgPathVertex>();
                var pts = poly.Points;
                for (var i = 0; i < pts.Count; i++)
                {
                    Vector? arrive = i > 0 ? pts[i] - pts[i - 1] : null;
                    Vector? leave = i + 1 < pts.Count ? pts[i + 1] - pts[i] : null;
                    list.Add(new NthSvgPathVertex(pts[i], arrive, leave));
                }
                vertices = list;
                break;
            }
            default:
                return;
        }
        if (vertices.Count == 0 || OverBudget) return;
        if (state.MarkerStart is { } start) DrawMarker(d, start, vertices[0], vertices[0].Leave ?? vertices[0].Arrive, state, depth);
        if (state.MarkerMid is { } mid)
        {
            for (var i = 1; i < vertices.Count - 1 && !OverBudget; i++) DrawMarker(d, mid, vertices[i], Bisect(vertices[i].Arrive, vertices[i].Leave), state, depth);
        }
        if (state.MarkerEnd is { } end)
        {
            var last = vertices[^1];
            DrawMarker(d, end, last, last.Arrive ?? last.Leave, state, depth);
        }
    }

    private static Vector? Bisect(Vector? a, Vector? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        var angle = (Math.Atan2(a.Value.Y, a.Value.X) + Math.Atan2(b.Value.Y, b.Value.X)) / 2;
        // Averaging angles across the +/- pi seam would flip the direction; use the vector sum instead.
        var sum = Normalize(a.Value) + Normalize(b.Value);
        return Math.Abs(sum.X) < 1e-9 && Math.Abs(sum.Y) < 1e-9 ? new Vector(Math.Cos(angle), Math.Sin(angle)) : sum;
    }

    private static Vector Normalize(Vector v)
    {
        var length = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        return length < 1e-12 ? default : new Vector(v.X / length, v.Y / length);
    }

    // ED-NEW-secfinal-41: a marker's content is drawn one level deeper than the shape that carries the marker (it was a fixed 1), so
    // a marker whose content points at the marker again ends at the depth limit instead of at the end of the stack.
    private void DrawMarker(DrawingContext d, string id, NthSvgPathVertex vertex, Vector? direction, State state, int depth)
    {
        if (OverBudget || !Document.Markers.TryGetValue(id, out var marker) || marker.Width <= 0 || marker.Height <= 0) return;
        var angle = marker.Auto ? (direction is { } v ? Math.Atan2(v.Y, v.X) : 0) : marker.Angle * Math.PI / 180;
        var unit = marker.UserSpace ? 1 : state.StrokeWidth;
        var box = marker.ViewBox ?? new Rect(0, 0, marker.Width, marker.Height);
        var scaleX = marker.Width / box.Width;
        var scaleY = marker.Height / box.Height;
        var scale = marker.ViewBox is null ? 1 : Math.Min(scaleX, scaleY);
        // Marker space -> path space: the reference point sits on the vertex, turned to the path, scaled by the stroke width.
        var matrix = Matrix.CreateTranslation(-marker.RefX, -marker.RefY)
            * Matrix.CreateScale(scale * unit, scale * unit)
            * Matrix.CreateRotation(angle)
            * Matrix.CreateTranslation(vertex.Point.X, vertex.Point.Y);
        using var moved = d.PushTransform(matrix);
        var saved = opaque.Count;
        Children(d, marker.Content.Children, State.Initial.With(marker.Content.Style), matrix, depth + 1);
        if (opaque.Count > saved) opaque.RemoveRange(saved, opaque.Count - saved);
    }

    // ---- text --------------------------------------------------------------------------------------------------------

    private sealed class Placed(NthSvgTextRun run, FormattedText formatted, double x, double y, int chunk, NthSvgAnchor anchor)
    {
        public NthSvgTextRun Run { get; } = run;
        public FormattedText Formatted { get; } = formatted;
        public double X { get; set; } = x;
        public double Y { get; } = y;
        public int Chunk { get; } = chunk;
        public NthSvgAnchor Anchor { get; } = anchor;
    }

    private void Text(DrawingContext d, NthSvgText text, State state, Matrix world)
    {
        if (!state.Visible) return;
        var placed = new List<Placed>(text.Runs.Count);
        var x = text.X;
        var y = text.Y;
        var chunk = 0;
        var started = false;
        foreach (var run in text.Runs)
        {
            var runState = state.With(run.Style);
            if (run.X is not null || run.Y is not null)
            {
                if (started) chunk++;
                if (run.X is { } ax) x = ax;
                if (run.Y is { } ay) y = ay;
            }
            x += run.DxEm ? run.Dx * runState.FontSize : run.Dx;
            y += run.DyEm ? run.Dy * runState.FontSize : run.Dy;
            if (run.Text.Length == 0) continue;
            var formatted = Layout(run, runState, new Rect(x, y, 1, 1), world);
            placed.Add(new Placed(run, formatted, x, y, chunk, runState.Anchor));
            x += formatted.WidthIncludingTrailingWhitespace;
            started = true;
        }

        // text-anchor shifts a whole chunk, by the chunk's width.
        foreach (var group in placed.GroupBy(p => p.Chunk))
        {
            var width = group.Sum(p => p.Formatted.WidthIncludingTrailingWhitespace);
            var shift = group.First().Anchor switch { NthSvgAnchor.Middle => -width / 2, NthSvgAnchor.End => -width, _ => 0 };
            if (shift == 0) continue;
            foreach (var p in group) p.X += shift;
        }

        foreach (var p in placed)
        {
            var runState = state.With(p.Run.Style);
            var top = runState.Baseline switch
            {
                NthSvgBaseline.Central => p.Y - p.Formatted.Height / 2,
                NthSvgBaseline.Hanging => p.Y,
                NthSvgBaseline.TextBottom => p.Y - p.Formatted.Height,
                _ => p.Y - p.Formatted.Baseline,
            };
            Drawn++;
            d.DrawText(p.Formatted, new Point(p.X, top));
        }
    }

    private FormattedText Layout(NthSvgTextRun run, State state, Rect where, Matrix world)
    {
        var fill = state.Fill.Kind == NthSvgPaintKind.Reference ? (state.Fill.Color.A > 0 ? state.Fill.Color : context.CurrentColor) : state.Fill.Kind == NthSvgPaintKind.None ? Color.FromArgb(0, 0, 0, 0) : FillColor(state.Fill);
        if (context.Adapt && fill.A > 0) fill = NthSvgColor.Adapt(fill, BackdropAt(where, world), TextContrast);
        fill = WithOpacity(fill, state.FillOpacity);
        var key = string.Create(CultureInfo.InvariantCulture, $"{state.Family}|{state.FontSize:0.##}|{state.Weight}|{state.Italic}|{state.Underline}|{state.LineThrough}");
        if (!texts.TryGetValue(run, out var cached)) texts[run] = cached = new CachedText();
        if (cached.Formatted is null || cached.Key != key)
        {
            var typeface = new Typeface(Family(state.Family), state.Italic ? FontStyle.Italic : FontStyle.Normal, (FontWeight)Math.Clamp(state.Weight, 100, 900));
            cached.Formatted = new FormattedText(run.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, Math.Max(1, state.FontSize), Solid(fill));
            if (state.Underline) cached.Formatted.SetTextDecorations(TextDecorations.Underline);
            else if (state.LineThrough) cached.Formatted.SetTextDecorations(TextDecorations.Strikethrough);
            cached.Key = key;
            cached.Brush = fill;
        }
        else if (cached.Brush != fill)
        {
            cached.Formatted.SetForegroundBrush(Solid(fill));
            cached.Brush = fill;
        }
        return cached.Formatted;
    }

    private FontFamily Family(string? list)
    {
        var key = list ?? "";
        if (families.TryGetValue(key, out var family)) return family;
        var names = new List<string>();
        var mono = false;
        foreach (var part in key.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = part.Trim('"', '\'', ' ');
            if (name.Length == 0) continue;
            switch (name.ToLowerInvariant())
            {
                case "monospace" or "ui-monospace":
                    mono = true;
                    break;
                case "sans-serif" or "system-ui" or "ui-sans-serif" or "serif" or "ui-serif" or "cursive" or "fantasy":
                    break;
                default:
                    names.Add(name);
                    break;
            }
        }
        // Named families first (a text keeps the face it asked for); the generic ones are the thread's own faces.
        return families[key] = names.Count > 0 ? new FontFamily(string.Join(',', names)) : mono ? context.Mono : context.Fallback;
    }
}
