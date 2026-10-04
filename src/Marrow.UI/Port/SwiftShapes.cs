// PORT-ONLY: SwiftUI's RoundedRectangle(cornerRadius:style:), Capsule(style:) and UnevenRoundedRectangle take a
// RoundedCornerStyle that Avalonia has no counterpart for; this is the one shared helper every `.continuous` site goes
// through (ED-181, closed by the continuous-corners build spec, ED-275).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.UI.Port;

/// <summary>SwiftUI <c>RoundedCornerStyle</c>: <c>.circular</c> and <c>.continuous</c>.</summary>
public enum RoundedCornerStyle
{
    Circular,
    Continuous,
}

/// <summary>How one corner of a shape is drawn.</summary>
public enum CornerKind
{
    /// <summary>No rounding.</summary>
    Square,

    /// <summary>A circular arc.</summary>
    Arc,

    /// <summary>Apple's continuous corner: three cubic Beziers (<see cref="ContinuousCorner"/>).</summary>
    Continuous,
}

/// <summary>One drawing step of a <see cref="ShapeOutline"/>.</summary>
public enum PathVerb
{
    Move,
    Line,
    Cubic,

    /// <summary>A clockwise circular arc of <see cref="PathSegment.ArcRadius"/> to <see cref="PathSegment.End"/>.</summary>
    Arc,
}

/// <summary>A drawing step: where it ends, and for a cubic its two control points or for an arc its radius.</summary>
public readonly record struct PathSegment(PathVerb Verb, Point End, Point Control1 = default, Point Control2 = default, double ArcRadius = 0);

/// <summary>A closed rounded-rectangle path as data, in clockwise order from the end of the top-left corner, and how each corner was drawn.</summary>
public sealed record ShapeOutline(Rect Rect, IReadOnlyList<PathSegment> Segments, IReadOnlyList<CornerKind> Corners, IReadOnlyList<double> Radii)
{
    /// <summary>
    /// The radius each corner was really drawn at (index in the order top-left, top-right, bottom-right, bottom-left): 0 for a square corner, the circular
    /// arc's radius (held to half the shorter side) for an arc, and for Apple's corner the radius of its curve, which is the requested radius where it
    /// fits and, in ED-275 (2)'s band, the largest radius that fits (<see cref="ContinuousCorner.LargestRadius"/>, OD-0102).
    /// </summary>
    public double DrawnRadius(int index) => Radii[index];

    /// <summary>The corners in the order top-left, top-right, bottom-right, bottom-left.</summary>
    public CornerKind Corner(int index) => Corners[index];

    /// <summary>Whether any corner is Apple's continuous corner.</summary>
    public bool HasContinuousCorner => Corners.Contains(CornerKind.Continuous);
}

/// <summary>SwiftUI <c>RoundedRectangle</c>, <c>Capsule</c> and <c>UnevenRoundedRectangle</c> with their corner style (ED-181).</summary>
public static class SwiftShapes
{
    /// <summary>The corner style a shape was drawn with; null on a view that is not one of these shapes.</summary>
    public static readonly AttachedProperty<RoundedCornerStyle?> CornerStyleProperty =
        AvaloniaProperty.RegisterAttached<Control, RoundedCornerStyle?>("CornerStyle", typeof(SwiftShapes));

    public static RoundedCornerStyle? GetCornerStyle(Control control) => control.GetValue(CornerStyleProperty);

    /// <summary>
    /// The one exception to OD-0102's in-between corners: a `.continuous` shape that a reference frame MEASURES as the circular arc where Apple's corner
    /// does not fit at the stated radius (ED-275 (2): the composer pill, r 22 on 46 pt, circular 0.156 px rms against the clamped continuous corner's 1.148).
    /// Such a shape keeps the circular arc in that band; every other `.continuous` shape takes Apple's corner at the largest radius that fits.
    /// </summary>
    public static readonly AttachedProperty<bool> CircularInBandProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("CircularInBand", typeof(SwiftShapes));

    public static bool GetCircularInBand(Control control) => control.GetValue(CircularInBandProperty);

    /// <summary>Marks a `.continuous` shape as measured circular in ED-275 (2)'s band (see <see cref="CircularInBandProperty"/>).</summary>
    public static T CircularInBand<T>(this T control, bool value = true) where T : Control
    {
        control.SetValue(CircularInBandProperty, value);
        return control;
    }

    /// <summary>
    /// Marks a plain `.continuous` Border whose content the source draws BEYOND the shape's sides (SwiftUI's <c>.background(shape)</c> does not clip its content: the
    /// effort title's leaving letters carried past the chevron, EffortSlider.swift:589 <c>.buttonStyle(.plain)</c>). Such a Border keeps the slack clip of ED-275 (3),
    /// which runs past the straight sides; every other Border is cut by the shape's exact outline, which Skia anti-aliases exactly (ED-317).
    /// </summary>
    public static readonly AttachedProperty<bool> ContentBeyondSidesProperty =
        AvaloniaProperty.RegisterAttached<Border, bool>("ContentBeyondSides", typeof(SwiftShapes));

    public static bool GetContentBeyondSides(Border border) => border.GetValue(ContentBeyondSidesProperty);

    /// <summary>Marks a `.continuous` Border as one whose content is drawn beyond its sides (see <see cref="ContentBeyondSidesProperty"/>).</summary>
    public static T ContentBeyondSides<T>(this T border, bool value = true) where T : Border
    {
        border.SetValue(ContentBeyondSidesProperty, value);
        return border;
    }

    /// <summary>
    /// <c>RoundedRectangle(cornerRadius:style:)</c> as the corners of a Border that fills its bounds: its Background is
    /// the shape's <c>.fill</c>, its border the shape's <c>.strokeBorder</c> (a Border draws its border inside its
    /// bounds, the inner stroke strokeBorder draws). A `.continuous` shape keeps its Border (its CornerRadius is the circular corner of the same radius, and
    /// the stated radius, <see cref="StatedCornerRadius"/>, wherever Apple's corner fits) and is cut to Apple's curve by <see cref="Visual.Clip"/>
    /// (<see cref="ContinuousClip"/>): Avalonia's Border draws circular corners only and its Render is sealed. In OD-0102's band the Border's own CornerRadius is
    /// set to what draws the corner (<see cref="DrawnCornerRadius"/>), because a clip can only remove fill.
    /// </summary>
    public static T RoundedRectangle<T>(this T border, double cornerRadius, RoundedCornerStyle style) where T : Border
    {
        border.SetValue(CornerStyleProperty, style);
        border.CornerRadius = new CornerRadius(cornerRadius);
        if (style == RoundedCornerStyle.Continuous) ContinuousClip.Track(border);
        return border;
    }

    /// <summary>
    /// <c>UnevenRoundedRectangle(topLeadingRadius:bottomLeadingRadius:bottomTrailingRadius:topTrailingRadius:style:)</c>
    /// (left to right) as the corners of a Border, like <see cref="RoundedRectangle{T}"/>.
    /// </summary>
    public static T UnevenRoundedRectangle<T>(this T border, CornerRadius cornerRadii, RoundedCornerStyle style) where T : Border
    {
        border.SetValue(CornerStyleProperty, style);
        border.CornerRadius = cornerRadii;
        if (style == RoundedCornerStyle.Continuous) ContinuousClip.Track(border);
        return border;
    }

    /// <summary>
    /// <c>Capsule(style:)</c> as the corners of a Border: rounded by half its shorter side at every size. A capsule is
    /// always past <see cref="ContinuousCorner.LargestRadius"/>, where the reference video shows circular ends (SPEC.md
    /// 2.3), so both styles draw the same arcs and the style is only recorded.
    /// </summary>
    public static T Capsule<T>(this T border, RoundedCornerStyle style) where T : Border
    {
        border.SetValue(CornerStyleProperty, style);
        border.CornerRadius = new CornerRadius(CapsuleCornerRadius(border.Bounds.Size));
        border.SizeChanged += (_, e) => border.CornerRadius = new CornerRadius(CapsuleCornerRadius(e.NewSize));
        return border;
    }

    /// <summary>A capsule's corner radius: half its shorter side.</summary>
    public static double CapsuleCornerRadius(Size size) => Math.Min(size.Width, size.Height) / 2;

    /// <summary><c>RoundedRectangle(cornerRadius:style:)</c> as a path in <paramref name="rect"/>. The geometry is shared: do not change it.</summary>
    public static Geometry RoundedRectangleGeometry(Rect rect, double cornerRadius, RoundedCornerStyle style, bool circularInBand = false) =>
        Geometry(rect, new CornerRadius(cornerRadius), style, clip: false, circularInBand);

    /// <summary><c>UnevenRoundedRectangle</c> as a path in <paramref name="rect"/>. The geometry is shared: do not change it.</summary>
    public static Geometry UnevenRoundedRectangleGeometry(Rect rect, CornerRadius cornerRadii, RoundedCornerStyle style, bool circularInBand = false) =>
        Geometry(rect, cornerRadii, style, clip: false, circularInBand);

    /// <summary><c>Capsule(style:)</c> as a path in <paramref name="rect"/>. The geometry is shared: do not change it.</summary>
    public static Geometry CapsuleGeometry(Rect rect, RoundedCornerStyle style) =>
        Geometry(rect, new CornerRadius(CapsuleCornerRadius(rect.Size)), style, clip: false, circularInBand: false);

    // ---- The outline as data ---------------------------------------------------------------------------------------

    /// <summary>
    /// How far outside its bounds a clip path reaches along the straight sides: far enough that it never cuts what the shape or a child draws
    /// beyond a side (SwiftUI's `.background(shape)` does not clip its content: the effort title's letters leaving past the chevron are drawn).
    /// It holds along the sides between the corners' run-ins: within <see cref="ContinuousCorner.Extent"/> radii of an Apple-regime corner the path
    /// runs in to the curve's ends, so the whole quadrant beyond the bounds there is cut too (on a 26 pt row of radius 6 a child drawing past a
    /// side survives only for 9.2 &lt; y &lt; 16.8). No site is known to draw there (ED-275 (3)).
    /// </summary>
    internal const double ClipSlack = 4096;

    /// <summary>
    /// The closed path of a rounded rectangle, as data. Per corner (ENGINEERING DECISION, SPEC.md 4.2, backed by the
    /// oracle for capsules and large radii): radius 0 is square; <see cref="RoundedCornerStyle.Circular"/> is a circular
    /// arc of the radius held to half the shorter side (so the path never folds over itself; a capsule's radius is exactly
    /// that); <see cref="RoundedCornerStyle.Continuous"/> is Apple's corner while the corner's run-in
    /// (<see cref="ContinuousCorner.Extent"/> radii) and its neighbour's fit along each side they share
    /// (<c>2 * Extent * r &lt;= min(w, h)</c> for a uniform radius), else the circular arc.
    /// </summary>
    public static ShapeOutline Outline(Rect rect, CornerRadius radii, RoundedCornerStyle style, bool circularInBand = false) => Build(rect, radii, style, slack: 0, circularInBand);

    // The clip variant of the outline: the same corners, but every straight side and every corner that is not Apple's is
    // pushed ClipSlack outside, so the clip cuts along the continuous curves and not along any straight side (the Border's own fill, with its own
    // anti-aliased edge, stays what draws every straight side, and a child that draws beyond a side is not clipped, except within the run-ins of an
    // Apple-regime corner, where the quadrant outside the bounds is cut with the sliver: see ClipSlack).
    // With exact: true the clip is the shape's own outline (no slack), a CONVEX path when the corners are Apple's or square: see ExactClipFits.
    internal static ShapeOutline ClipOutline(Rect rect, CornerRadius radii, bool circularInBand = false, bool exact = false) => Build(rect, radii, RoundedCornerStyle.Continuous, exact ? 0 : ClipSlack, circularInBand);

    /// <summary>
    /// Whether a Border of this size and radii may be cut by the exact outline instead of the slack clip: every rounded corner is Apple's (a corner that is an arc is
    /// the Border's own, and the same arc as a clip would be anti-aliased twice, ED-297 (8)). The outline is then convex, which is what makes Skia
    /// anti-aliase it exactly: ANY non-convex path (the slack clip's walls, or a path with a notch of 0.001 pt in a straight side) is anti-aliased by Skia's coarser
    /// walker, 49 of 255 off the path fill at the worst pixel of a 200 x 40 shape and 102 at 380 x 46 (ED-297 (9)).
    /// </summary>
    internal static bool ExactClipFits(Size size, CornerRadius radii, bool circularInBand = false)
    {
        if (!(size.Width > 0) || !(size.Height > 0)) return false;
        var kinds = new CornerKind[4];
        var drawn = new double[4];
        ResolveCorners(size.Width, size.Height, [Math.Max(0, radii.TopLeft), Math.Max(0, radii.TopRight), Math.Max(0, radii.BottomRight), Math.Max(0, radii.BottomLeft)],
            RoundedCornerStyle.Continuous, circularInBand, kinds, drawn);
        return !kinds.Contains(CornerKind.Arc);
    }

    // Whether a corner's run-in fits: the corner and its neighbour on each side it shares need E r each along that side (the top side
    // joins corners 0 and 1, the bottom side 2 and 3, the left side 0 and 3, the right side 1 and 2; corners clockwise from the top-left).
    private static bool FitsContinuous(int corner, double w, double h, double tl, double tr, double br, double bl)
    {
        static bool Fits(double a, double b, double side) => ContinuousCorner.Extent * a + ContinuousCorner.Extent * b <= side + 1e-9;
        var horizontal = corner is 0 or 1 ? Fits(tl, tr, w) : Fits(br, bl, w);
        var vertical = corner is 0 or 3 ? Fits(tl, bl, h) : Fits(tr, br, h);
        return horizontal && vertical;
    }

    /// <summary>Whether any corner of this shape is Apple's continuous corner at this size (what <see cref="Outline"/> reports, without building the path).</summary>
    public static bool HasContinuousCorner(Size size, CornerRadius radii, RoundedCornerStyle style, bool circularInBand = false)
    {
        if (style != RoundedCornerStyle.Continuous || !(size.Width > 0) || !(size.Height > 0)) return false;
        var kinds = new CornerKind[4];
        var drawn = new double[4];
        ResolveCorners(size.Width, size.Height, [Math.Max(0, radii.TopLeft), Math.Max(0, radii.TopRight), Math.Max(0, radii.BottomRight), Math.Max(0, radii.BottomLeft)],
            style, circularInBand, kinds, drawn);
        return kinds.Contains(CornerKind.Continuous);
    }

    // How each corner is drawn and at what radius (ENGINEERING DECISION backed by the oracle for capsules and large radii, ED-275 (2); the band is the
    // owner's, OD-0102). Per corner, in order top-left, top-right, bottom-right, bottom-left:
    //  - radius 0: square.
    //  - `.circular`: the circular arc, the radius held to half the shorter side (so the path never folds over itself; a capsule's radius is exactly that).
    //  - `.continuous` where the corner's run-in and its neighbour's fit along each side they share (2 * Extent * r <= min(w, h) for a uniform radius):
    //    Apple's corner at the requested radius.
    //  - `.continuous` in the BAND (it does not fit at the stated radius, yet the radius is under half the shorter side: for r 18 that is 36 < h < 55):
    //    Apple's corner at the largest radius that fits, ContinuousCorner.LargestRadius(size) = min(w, h) / (2 * Extent), or the stated radius if
    //    that is smaller (OD-0102, "corner-band: A"); a neighbour that then still does not fit leaves the corner circular. EXCEPT a shape marked
    //    circularInBand (the composer pill, measured circular), which takes the circular arc as before.
    //  - `.continuous` at half the shorter side or more (a capsule): the circular arc, held to half (the video shows circular ends, SPEC.md 2.3).
    private static void ResolveCorners(double w, double h, double[] requested, RoundedCornerStyle style, bool circularInBand, CornerKind[] kinds, double[] drawn)
    {
        var half = Math.Min(w, h) / 2;
        var largest = ContinuousCorner.LargestRadius(new Size(w, h));
        var effective = (double[])requested.Clone();
        var inBand = new bool[4];
        for (var i = 0; i < 4; i++)
        {
            var r = requested[i];
            if (r <= 0 || style != RoundedCornerStyle.Continuous || FitsContinuous(i, w, h, requested[0], requested[1], requested[2], requested[3])) continue;
            if (!circularInBand && r < half - 1e-9)
            {
                inBand[i] = true;
                effective[i] = Math.Min(r, largest);
            }
        }
        for (var i = 0; i < 4; i++)
        {
            var r = requested[i];
            if (r <= 0) { kinds[i] = CornerKind.Square; drawn[i] = 0; continue; }
            var fits = style == RoundedCornerStyle.Continuous && (inBand[i] || FitsContinuous(i, w, h, requested[0], requested[1], requested[2], requested[3]))
                && FitsContinuous(i, w, h, effective[0], effective[1], effective[2], effective[3]);
            if (fits) { kinds[i] = CornerKind.Continuous; drawn[i] = effective[i]; }
            else { kinds[i] = CornerKind.Arc; drawn[i] = Math.Min(r, half); }
        }
    }

    /// <summary>
    /// The radii a plain Border must carry as its own <see cref="Border.CornerRadius"/> in OD-0102's band (ED-275 (2)): Avalonia fills the circle of that radius
    /// and a clip can only remove fill, but Apple's corner at the largest radius that fits lies OUTSIDE the circle of the stated radius near the diagonal
    /// (2.06 pt at h 40, r 18), so the stated radius cannot stay. A corner that is Apple's in the band carries
    /// <list type="bullet">
    /// <item><description>0 when <paramref name="clipOnly"/>: the Border fills its whole rectangle (pixel aligned, no edge of its own) and the clip, which is Apple's
    /// path, is the only anti-aliased edge, so the corner is exactly <c>SwiftShapeBorder</c>'s fill of the same path (a circular fill under the clip would multiply
    /// two anti-aliased edges, 0.5 x 0.5 coverage at an edge pixel);</description></item>
    /// <item><description>otherwise the radius it is drawn at, <see cref="ContinuousCorner.LargestRadius"/>: the circle of that radius is Apple's curve to
    /// 0.0138 r inside and 0.002 r outside (<see cref="ContinuousCorner"/>), what a border ring or a shadow needs, which a clip cannot carry.</description></item>
    /// </list>
    /// Every other corner keeps its stated radius (a corner that fits is the existing ED-275 (3) mechanism).
    /// </summary>
    internal static CornerRadius DrawnCornerRadius(Size size, CornerRadius stated, bool circularInBand, bool clipOnly)
    {
        if (!(size.Width > 0) || !(size.Height > 0)) return stated;
        var requested = new[] { Math.Max(0, stated.TopLeft), Math.Max(0, stated.TopRight), Math.Max(0, stated.BottomRight), Math.Max(0, stated.BottomLeft) };
        var kinds = new CornerKind[4];
        var drawn = new double[4];
        ResolveCorners(size.Width, size.Height, requested, RoundedCornerStyle.Continuous, circularInBand, kinds, drawn);
        double Pick(int i, double was) => kinds[i] == CornerKind.Continuous && drawn[i] < requested[i] - 1e-9 ? (clipOnly ? 0 : drawn[i]) : was;
        return new CornerRadius(Pick(0, stated.TopLeft), Pick(1, stated.TopRight), Pick(2, stated.BottomRight), Pick(3, stated.BottomLeft));
    }

    /// <summary>
    /// The corner radius a site stated for a Border (what <c>.RoundedRectangle(r, .continuous)</c> was given), which is <see cref="Border.CornerRadius"/> everywhere
    /// except where <see cref="ContinuousClip"/> has set the drawn radius there (ED-275 (2)'s band, OD-0102). The glass reads this, not the drawn radius.
    /// </summary>
    internal static CornerRadius StatedCornerRadius(Border border) => ContinuousClip.Stated(border);

    private static ShapeOutline Build(Rect rect, CornerRadius radii, RoundedCornerStyle style, double slack, bool circularInBand)
    {
        double x0 = rect.Left, y0 = rect.Top, x1 = rect.Right, y1 = rect.Bottom;
        var w = rect.Width;
        var h = rect.Height;
        var segments = new List<PathSegment>();
        if (!(w > 0) || !(h > 0))
        {
            segments.Add(new PathSegment(PathVerb.Move, new Point(x0, y0)));
            segments.Add(new PathSegment(PathVerb.Line, new Point(x1, y0)));
            segments.Add(new PathSegment(PathVerb.Line, new Point(x1, y1)));
            segments.Add(new PathSegment(PathVerb.Line, new Point(x0, y1)));
            return new ShapeOutline(rect, segments, [CornerKind.Square, CornerKind.Square, CornerKind.Square, CornerKind.Square], [0, 0, 0, 0]);
        }
        const double e = ContinuousCorner.Extent;
        // Clockwise corner order: top-left, top-right, bottom-right, bottom-left.
        var requested = new[] { Math.Max(0, radii.TopLeft), Math.Max(0, radii.TopRight), Math.Max(0, radii.BottomRight), Math.Max(0, radii.BottomLeft) };
        var kinds = new CornerKind[4];
        var drawn = new double[4];
        ResolveCorners(w, h, requested, style, circularInBand, kinds, drawn);
        var ext = new double[4];
        var arc = new double[4];
        for (var i = 0; i < 4; i++)
        {
            if (kinds[i] == CornerKind.Continuous) ext[i] = e * drawn[i];
            else if (kinds[i] == CornerKind.Arc) { arc[i] = drawn[i]; ext[i] = drawn[i]; }
        }

        // Where each corner starts and ends, clockwise (TL: left edge to top edge; TR: top to right; BR: right to bottom;
        // BL: bottom to left), and the same points pushed outward for a clip.
        var start = new[] { new Point(x0, y0 + ext[0]), new Point(x1 - ext[1], y0), new Point(x1, y1 - ext[2]), new Point(x0 + ext[3], y1) };
        var end = new[] { new Point(x0 + ext[0], y0), new Point(x1, y0 + ext[1]), new Point(x1 - ext[2], y1), new Point(x0, y1 - ext[3]) };
        var startOut = new[] { new Point(x0 - slack, y0 + ext[0]), new Point(x1 - ext[1], y0 - slack), new Point(x1 + slack, y1 - ext[2]), new Point(x0 + ext[3], y1 + slack) };
        var endOut = new[] { new Point(x0 + ext[0], y0 - slack), new Point(x1 + slack, y0 + ext[1]), new Point(x1 - ext[2], y1 + slack), new Point(x0 - slack, y1 - ext[3]) };
        var vertexOut = new[] { new Point(x0 - slack, y0 - slack), new Point(x1 + slack, y0 - slack), new Point(x1 + slack, y1 + slack), new Point(x0 - slack, y1 + slack) };

        var current = new Point();
        void Move(Point to) { segments.Add(new PathSegment(PathVerb.Move, to)); current = to; }
        void Line(Point to)
        {
            if (to == current) return;
            segments.Add(new PathSegment(PathVerb.Line, to));
            current = to;
        }
        // Whether this corner is one a clip leaves alone (its straight sides and its square or circular corners stay the shape's own).
        bool Cut(int i) => slack > 0 && kinds[i] == CornerKind.Continuous;

        // Map of a unit corner (u along the corner's first edge from the vertex, v into the shape) per corner, clockwise.
        Point Map(int corner, Point unit, double r) => corner switch
        {
            0 => new Point(x0 + unit.Y * r, y0 + unit.X * r),
            1 => new Point(x1 - unit.X * r, y0 + unit.Y * r),
            2 => new Point(x1 - unit.Y * r, y1 - unit.X * r),
            _ => new Point(x0 + unit.X * r, y1 - unit.Y * r),
        };

        void Body(int i)
        {
            switch (kinds[i])
            {
                case CornerKind.Continuous:
                    foreach (var cubic in ContinuousCorner.Cubics)
                    {
                        var r = drawn[i];
                        var p1 = Map(i, cubic.Control1, r);
                        var p2 = Map(i, cubic.Control2, r);
                        var p3 = Map(i, cubic.End, r);
                        segments.Add(new PathSegment(PathVerb.Cubic, p3, p1, p2));
                        current = p3;
                    }
                    break;
                case CornerKind.Arc:
                    segments.Add(new PathSegment(PathVerb.Arc, end[i], ArcRadius: arc[i]));
                    current = end[i];
                    break;
            }
        }

        // The path starts at the end of the top-left corner (or, for a clip, outside it) and ends there.
        var origin = slack > 0 ? (Cut(0) ? endOut[0] : vertexOut[0]) : end[0];
        Move(origin);
        for (var i = 1; i <= 4; i++)
        {
            var corner = i % 4;
            if (slack > 0)
            {
                if (Cut(corner))
                {
                    Line(startOut[corner]);
                    Line(start[corner]);
                    Body(corner);
                    Line(endOut[corner]);
                }
                else Line(vertexOut[corner]);
            }
            else
            {
                Line(start[corner]);
                Body(corner);
                if (corner == 0) break;
            }
        }
        return new ShapeOutline(rect, segments, kinds, drawn);
    }

    // ---- The cached geometry ---------------------------------------------------------------------------------------

    private readonly record struct ShapeKey(double X, double Y, double Width, double Height, double TopLeft, double TopRight,
        double BottomRight, double BottomLeft, RoundedCornerStyle Style, bool Clip, bool CircularInBand, bool Exact);

    private const int CacheLimit = 1024;
    private static readonly Dictionary<ShapeKey, StreamGeometry> cache = [];
    private static readonly object cacheLock = new();
    private static int geometryBuilds;

    /// <summary>How many geometries have been built (a test hook: a colour change builds none, a resize builds one).</summary>
    internal static int GeometryBuildCount => Volatile.Read(ref geometryBuilds);

    private static Geometry Geometry(Rect rect, CornerRadius radii, RoundedCornerStyle style, bool clip, bool circularInBand, bool exact = false)
    {
        var key = new ShapeKey(rect.X, rect.Y, rect.Width, rect.Height, radii.TopLeft, radii.TopRight, radii.BottomRight, radii.BottomLeft, style, clip, circularInBand, exact);
        lock (cacheLock)
        {
            if (cache.TryGetValue(key, out var found)) return found;
            if (cache.Count >= CacheLimit) cache.Clear();
            var outline = clip ? ClipOutline(rect, radii, circularInBand, exact) : Outline(rect, radii, style, circularInBand);
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var open = false;
                foreach (var segment in outline.Segments)
                {
                    switch (segment.Verb)
                    {
                        case PathVerb.Move:
                            context.BeginFigure(segment.End, isFilled: true);
                            open = true;
                            break;
                        case PathVerb.Line:
                            context.LineTo(segment.End);
                            break;
                        case PathVerb.Cubic:
                            context.CubicBezierTo(segment.Control1, segment.Control2, segment.End);
                            break;
                        case PathVerb.Arc:
                            context.ArcTo(segment.End, new Size(segment.ArcRadius, segment.ArcRadius), 0, false, SweepDirection.Clockwise);
                            break;
                    }
                }
                if (open) context.EndFigure(isClosed: true);
            }
            Interlocked.Increment(ref geometryBuilds);
            cache[key] = geometry;
            return geometry;
        }
    }

    /// <summary>
    /// The clip of a continuous shape of this size: Apple's corners cut, every other edge left to the shape's own fill (the slack clip), or with
    /// <paramref name="exact"/> the shape's own outline (<see cref="ExactClipFits"/>).
    /// </summary>
    internal static Geometry ClipGeometry(Rect rect, CornerRadius radii, bool circularInBand = false, bool exact = false) => Geometry(rect, radii, RoundedCornerStyle.Continuous, clip: true, circularInBand, exact);
}

/// <summary>
/// Keeps a `.continuous` Border cut to Apple's curve: <see cref="Visual.Clip"/> is set to the continuous path of the
/// Border's own size and corner radius, so its fill and its border's outer edge follow the curve, a child it clips follows
/// it, and hit testing follows it (the compositor hit-tests <see cref="Visual.Clip"/>). ENGINEERING DECISION (ED-275):
/// Avalonia's <c>Border.Render</c> is sealed and draws circular corners, and 286 tests and the glass, SettingsRise and
/// ChatView code treat these shapes as plain Borders, so the Border stays and its clip carries the curve instead of a
/// replacement control. The clip is set only where it can be seen: while the shape draws a fill or a border or clips its
/// children, and while it has no box shadow (a clip would cut the shadow); a Clip someone else set is never replaced.
/// </summary>
internal static class ContinuousClip
{
    private static readonly AttachedProperty<Tracker?> TrackerProperty =
        AvaloniaProperty.RegisterAttached<Border, Tracker?>("ContinuousClipTracker", typeof(ContinuousClip));

    public static void Track(Border border)
    {
        if (border.GetValue(TrackerProperty) is not null) return;
        border.SetValue(TrackerProperty, new Tracker(border));
    }

    /// <summary>The corner radius the site stated: <see cref="Border.CornerRadius"/> unless the tracker has set the drawn radius there (OD-0102's band).</summary>
    internal static CornerRadius Stated(Border border) => border.GetValue(TrackerProperty)?.Stated ?? border.CornerRadius;

    /// <summary>The clip a Border should carry now: null when the shape draws circular corners, nothing, or a shadow.</summary>
    internal static Geometry? Wanted(Border border) => Wanted(border, Stated(border));

    private static Geometry? Wanted(Border border, CornerRadius radii)
    {
        if (SwiftShapes.GetCornerStyle(border) != RoundedCornerStyle.Continuous) return null;
        var size = border.Bounds.Size;
        if (!(size.Width > 0) || !(size.Height > 0)) return null;
        if (border.BoxShadow.Count > 0) return null;
        var drawsFill = border.Background is not null;
        var drawsBorder = border.BorderBrush is not null && border.BorderThickness != default;
        if (!drawsFill && !drawsBorder && !border.ClipToBounds) return null;
        var circularInBand = SwiftShapes.GetCircularInBand(border);
        if (!SwiftShapes.HasContinuousCorner(size, radii, RoundedCornerStyle.Continuous, circularInBand)) return null;
        return SwiftShapes.ClipGeometry(new Rect(size), radii, circularInBand, exact: CutsExactly(border, size, radii, circularInBand));
    }

    /// <summary>
    /// Whether the Border is cut by its exact outline rather than the slack clip (ED-297 (9), ED-317): the exact outline is convex, and Skia anti-aliases
    /// only a convex path exactly (the slack clip's walls make it non-convex: 64 of 255 off the path fill at the one-step steps card, 700 x 45, 102 at 380 x 46), and no
    /// path that reaches beyond a straight side can be convex. So every Border is cut by the exact outline, with content or without, EXCEPT one whose site marks its
    /// content as drawn beyond its sides (<see cref="SwiftShapes.ContentBeyondSidesProperty"/>) while it has a child that is not clipped to its bounds: that one keeps the
    /// slack clip of ED-275 (3). A corner that is an arc stays on the slack clip (see <see cref="SwiftShapes.ExactClipFits"/>).
    /// </summary>
    private static bool CutsExactly(Border border, Size size, CornerRadius radii, bool circularInBand) =>
        (border.Child is null || border.ClipToBounds || !SwiftShapes.GetContentBeyondSides(border)) && SwiftShapes.ExactClipFits(size, radii, circularInBand);

    private sealed class Tracker
    {
        private readonly Border border;
        private Geometry? applied;
        private bool writing;

        // The radius the site stated. Avalonia's Border fills the circle of its CornerRadius and a clip can only take fill away, and Apple's corner at the largest
        // radius that fits (OD-0102, ED-275 (2)'s band) lies outside the circle of the stated radius near the diagonal; so where the corner is Apple's in the band
        // the Border's own CornerRadius is set to what draws it (SwiftShapes.DrawnCornerRadius) and the stated radius is kept here.
        public CornerRadius Stated { get; private set; }

        public Tracker(Border border)
        {
            this.border = border;
            Stated = border.CornerRadius;
            border.PropertyChanged += (_, e) =>
            {
                var p = e.Property;
                if (p == Border.CornerRadiusProperty)
                {
                    if (writing) return;
                    Stated = border.CornerRadius;
                    Update();
                }
                else if (p == Visual.BoundsProperty || p == Border.BackgroundProperty
                    || p == Border.BorderBrushProperty || p == Border.BorderThicknessProperty || p == Border.BoxShadowProperty
                    || p == Visual.ClipToBoundsProperty || p == Decorator.ChildProperty || p == SwiftShapes.CornerStyleProperty || p == SwiftShapes.CircularInBandProperty
                    || p == SwiftShapes.ContentBeyondSidesProperty)
                    Update();
            };
            Update();
        }

        private void Update()
        {
            var continuous = SwiftShapes.GetCornerStyle(border) == RoundedCornerStyle.Continuous;
            var wanted = Wanted(border, Stated);
            if (!ReferenceEquals(wanted, applied) && (border.Clip is null || ReferenceEquals(border.Clip, applied)))
            {
                // Only ever replace what this tracker set: a Clip the site set itself stays.
                border.Clip = wanted;
                applied = wanted;
            }
            // The band's corner is the clip alone (a square fill under Apple's path) only while this tracker's clip is what cuts the Border and the Border has no
            // border ring (the ring is a stroke along the Border's own corner, which a square corner would leave outside the curve); else the circle of the drawn radius.
            var clipOnly = applied is not null && ReferenceEquals(border.Clip, applied) && !(border.BorderBrush is not null && border.BorderThickness != default);
            var drawn = continuous ? SwiftShapes.DrawnCornerRadius(border.Bounds.Size, Stated, SwiftShapes.GetCircularInBand(border), clipOnly) : Stated;
            if (border.CornerRadius != drawn)
            {
                writing = true;
                try { border.SetCurrentValue(Border.CornerRadiusProperty, drawn); }
                finally { writing = false; }
            }
        }
    }
}
