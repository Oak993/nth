// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 "the SVG is sanitised (shapes, paths, text and gradients only; no script, no
// foreignObject, no external references) and drawn natively by the svg component"; ED-NEW-web-w16c-1). No Swift counterpart.
// The sanitised form of an SVG: a small tree of shapes, paths, text, gradients and markers, with nothing else in it. The
// sanitiser (NthSvgSanitizer) builds it from untrusted markup and only ever puts values here that it checked, so a document
// of this type cannot carry a script, a foreign object, a stylesheet, an event handler or an address. The painter and the
// writer read it without parsing anything again.
using Avalonia;
using Avalonia.Media;

namespace Marrow.UI.NthUi.Diagram;

/// <summary>What an SVG paint says.</summary>
public enum NthSvgPaintKind
{
    None,
    Color,
    CurrentColor,

    /// <summary>A gradient of the same document, by id (<see cref="NthSvgPaint.Reference"/>).</summary>
    Reference,
}

/// <summary>A fill or stroke paint.</summary>
public readonly record struct NthSvgPaint(NthSvgPaintKind Kind, Color Color, string? Reference = null)
{
    public static NthSvgPaint None { get; } = new(NthSvgPaintKind.None, default);

    public static NthSvgPaint CurrentColor { get; } = new(NthSvgPaintKind.CurrentColor, default);

    public static NthSvgPaint Of(Color color) => new(NthSvgPaintKind.Color, color);
}

public enum NthSvgAnchor
{
    Start,
    Middle,
    End,
}

public enum NthSvgBaseline
{
    Alphabetic,
    Central,
    Hanging,
    TextBottom,
}

/// <summary>The presentation properties one element specifies. A null property is not specified: it inherits from the
/// parent (fill, stroke and the text properties) or takes the SVG initial value.</summary>
public sealed class NthSvgStyle
{
    public static NthSvgStyle Empty { get; } = new();

    public NthSvgPaint? Fill { get; set; }

    public NthSvgPaint? Stroke { get; set; }

    public double? FillOpacity { get; set; }

    public double? StrokeOpacity { get; set; }

    public double? Opacity { get; set; }

    public double? StrokeWidth { get; set; }

    public double? MiterLimit { get; set; }

    public bool? EvenOdd { get; set; }

    public PenLineCap? LineCap { get; set; }

    public PenLineJoin? LineJoin { get; set; }

    public double[]? DashArray { get; set; }

    public double? DashOffset { get; set; }

    public string? FontFamily { get; set; }

    public double? FontSize { get; set; }

    public int? FontWeight { get; set; }

    public bool? Italic { get; set; }

    public NthSvgAnchor? Anchor { get; set; }

    public NthSvgBaseline? Baseline { get; set; }

    public double? LetterSpacing { get; set; }

    public bool? Underline { get; set; }

    public bool? LineThrough { get; set; }

    /// <summary><c>visibility: hidden</c> hides the element but not children that say <c>visible</c>.</summary>
    public bool? Visible { get; set; }

    public string? MarkerStart { get; set; }

    public string? MarkerMid { get; set; }

    public string? MarkerEnd { get; set; }

    public bool IsEmpty =>
        Fill is null && Stroke is null && FillOpacity is null && StrokeOpacity is null && Opacity is null && StrokeWidth is null
        && MiterLimit is null && EvenOdd is null && LineCap is null && LineJoin is null && DashArray is null && DashOffset is null
        && FontFamily is null && FontSize is null && FontWeight is null && Italic is null && Anchor is null && Baseline is null
        && LetterSpacing is null && Underline is null && LineThrough is null && Visible is null && MarkerStart is null
        && MarkerMid is null && MarkerEnd is null;
}

/// <summary>One node of the drawing.</summary>
public abstract class NthSvgNode
{
    /// <summary>The element's id when something refers to it (a gradient, a marker, a use).</summary>
    public string? Id { get; set; }

    public Matrix Transform { get; set; } = Matrix.Identity;

    public bool HasTransform { get; set; }

    public NthSvgStyle Style { get; set; } = NthSvgStyle.Empty;
}

/// <summary>A group (g, a, a nested svg, or a symbol that a use draws).</summary>
public sealed class NthSvgGroup : NthSvgNode
{
    public List<NthSvgNode> Children { get; } = [];

    /// <summary>False for a symbol: it is drawn only where a use refers to it.</summary>
    public bool Rendered { get; set; } = true;
}

public sealed class NthSvgRect : NthSvgNode
{
    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public double Rx { get; init; }

    public double Ry { get; init; }
}

/// <summary>A circle or an ellipse.</summary>
public sealed class NthSvgEllipse : NthSvgNode
{
    public double Cx { get; init; }

    public double Cy { get; init; }

    public double Rx { get; init; }

    public double Ry { get; init; }

    /// <summary>True when the markup said circle (written back as a circle).</summary>
    public bool IsCircle { get; init; }
}

public sealed class NthSvgLine : NthSvgNode
{
    public double X1 { get; init; }

    public double Y1 { get; init; }

    public double X2 { get; init; }

    public double Y2 { get; init; }
}

/// <summary>A polyline or a polygon.</summary>
public sealed class NthSvgPoly : NthSvgNode
{
    public IReadOnlyList<Point> Points { get; init; } = [];

    public bool Closed { get; init; }
}

public sealed class NthSvgPathShape : NthSvgNode
{
    public NthSvgPathData Data { get; init; } = NthSvgPathData.Empty;

    /// <summary>The geometry, built once when first drawn.</summary>
    internal Geometry? Geometry { get; set; }
}

/// <summary>A piece of a text: its characters and where they start.</summary>
public sealed class NthSvgTextRun
{
    public string Text { get; init; } = "";

    /// <summary>An absolute x for the run, or null to continue where the last run ended.</summary>
    public double? X { get; init; }

    public double? Y { get; init; }

    public double Dx { get; init; }

    public double Dy { get; init; }

    /// <summary>True when <see cref="Dx"/> is in em (of the run's font size) rather than pixels.</summary>
    public bool DxEm { get; init; }

    /// <summary>True when <see cref="Dy"/> is in em (of the run's font size) rather than pixels.</summary>
    public bool DyEm { get; init; }

    public NthSvgStyle Style { get; init; } = NthSvgStyle.Empty;
}

public sealed class NthSvgText : NthSvgNode
{
    public double X { get; init; }

    public double Y { get; init; }

    public IReadOnlyList<NthSvgTextRun> Runs { get; init; } = [];

    /// <summary>All the characters, runs joined.</summary>
    public string Content => string.Concat(Runs.Select(r => r.Text));
}

/// <summary>A reference to an element of the document, drawn where the use is.</summary>
public sealed class NthSvgUse : NthSvgNode
{
    public string Target { get; init; } = "";

    public double X { get; init; }

    public double Y { get; init; }
}

public sealed record NthSvgStop(double Offset, Color Color);

/// <summary>A linear or radial gradient. Coordinates are fractions of the shape's box when <see cref="UserSpace"/> is false.</summary>
public sealed class NthSvgGradient
{
    public string Id { get; init; } = "";

    public bool Radial { get; init; }

    public bool UserSpace { get; init; }

    public double X1 { get; init; }

    public double Y1 { get; init; }

    public double X2 { get; init; } = 1;

    public double Y2 { get; init; }

    public double Cx { get; init; } = .5;

    public double Cy { get; init; } = .5;

    public double R { get; init; } = .5;

    public double Fx { get; init; } = .5;

    public double Fy { get; init; } = .5;

    public Matrix Transform { get; init; } = Matrix.Identity;

    public bool HasTransform { get; init; }

    public IReadOnlyList<NthSvgStop> Stops { get; init; } = [];
}

/// <summary>A marker (an arrowhead, a dot) that a path draws at its ends.</summary>
public sealed class NthSvgMarker
{
    public string Id { get; init; } = "";

    public double RefX { get; init; }

    public double RefY { get; init; }

    public double Width { get; init; } = 3;

    public double Height { get; init; } = 3;

    /// <summary>True when the marker follows the path's direction (orient=auto); otherwise <see cref="Angle"/> degrees.</summary>
    public bool Auto { get; init; }

    public double Angle { get; init; }

    /// <summary>False when the marker is scaled by the stroke width (the default markerUnits).</summary>
    public bool UserSpace { get; init; }

    public Rect? ViewBox { get; init; }

    public NthSvgGroup Content { get; init; } = new();
}

/// <summary>A sanitised SVG document.</summary>
public sealed class NthSvgDocument
{
    internal NthSvgDocument(Rect viewBox, NthSvgGroup root, NthSvgGroup defs, IReadOnlyDictionary<string, NthSvgGradient> gradients,
        IReadOnlyDictionary<string, NthSvgMarker> markers, IReadOnlyDictionary<string, NthSvgNode> targets, string? title, string? description,
        int nodeCount, IReadOnlyList<string> removed, Size? declaredSize = null)
    {
        DeclaredSize = declaredSize;
        ViewBox = viewBox;
        Root = root;
        Defs = defs;
        Gradients = gradients;
        Markers = markers;
        Targets = targets;
        Title = title;
        Description = description;
        NodeCount = nodeCount;
        Removed = removed;
    }

    /// <summary>The picture's own coordinate box; it is scaled to fit its place and never stretched.</summary>
    public Rect ViewBox { get; }

    public NthSvgGroup Root { get; }

    /// <summary>The shapes of the markup's defs: never drawn where they stand, only by a use.</summary>
    public NthSvgGroup Defs { get; }

    public IReadOnlyDictionary<string, NthSvgGradient> Gradients { get; }

    public IReadOnlyDictionary<string, NthSvgMarker> Markers { get; }

    /// <summary>Elements a use may draw, by id.</summary>
    public IReadOnlyDictionary<string, NthSvgNode> Targets { get; }

    /// <summary>The first title of the markup (an accessible name), when it has one.</summary>
    public string? Title { get; }

    public string? Description { get; }

    public int NodeCount { get; }

    /// <summary>What the sanitiser took out, one note per kind ("foreignObject", "script", "style", "event handler onclick",
    /// "external reference"): tests and the log read it; the reader never sees it.</summary>
    public IReadOnlyList<string> Removed { get; }

    /// <summary>The natural size in DIPs: the view box's.</summary>
    public Size Size => new(ViewBox.Width, ViewBox.Height);

    /// <summary>The size the markup's own width and height ask for (both in pixels), when it gives both.</summary>
    public Size? DeclaredSize { get; }
}
