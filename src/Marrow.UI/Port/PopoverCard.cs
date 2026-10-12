// PORT-ONLY (OD-0095, owner ruling 2026-10-03). An NSPopover draws its panel with a small tip on the edge facing the
// control it hangs from; the port's popovers were rectangles touching their button. Every popover the presenter shows
// (Presentation, PopoverHost.Prepare) now hangs on this card: the panel's fill and edge (ED-238) drawn as ONE outline,
// the card's rectangle with the tip in it, so tip and card share one fill and one edge with no seam at any scale.
//
// SOURCE FACT, measured frame by frame in the reference runtime (E:\Droppy\work\reference-video\study\
// effort-title-2026-10-03\arrow and \review): the model chip's card (2-chat-use.mov 34.4 s), the diff panel's "..." menu
// (22.2 s) and the message menu (24.3 s), all above their anchors with the tip down, and a Hydra pair picker below its
// button with the tip up (1-settings-tour.mov 84.5 s): the tip stands 10-10.5 pt off the card's edge, 27-29 pt wide where
// it meets the card (its shoulders included), its point rounded (about 3 pt flat), its shoulders blending into the edge;
// its point 1-2 pt from the anchor, centred on the anchor's middle, the card's edge 13 pt from the anchor (OD-0095 said
// "about 11"; amended). Its sides run at 45 degrees: the tip's dark outline, row by row from 3 to 10 px under the card's
// edge, narrows by exactly 2 px a row in all four (review\video_tips_x8_reviewer.png). Cross-check: AppKit's NSPopover
// arrow is not published as a constant; the four popovers agree within a point, so they are the reference.
// ENGINEERING DECISION, the shape fitted to those numbers and checked side by side (arrow/sbs_tip_video_vs_port.png): a
// sharp height of 12 pt and a half base of 12 pt (sides at 45 degrees), the point rounded with a 3 pt radius (its outer
// edge 10.3 pt off the card's), the shoulders with 4 pt (27.3 pt across at the edge), and 1 pt left clear between the
// point and the anchor (the card's edge 13 pt from it, a whole number so the card stays on whole points). The card's
// corners stay square and its material plain until the glass pass (OD-0005); the tip keeps clear of the corners by its
// shoulder.
//
// THE GLASS (OD-0107 and its amendment, OD-0005; ED-357). On the Mac the panel is an NSPopover's Liquid Glass, not
// a flat card: the scene behind it seen blurred through it, a dark hairline round it, a thin light rim inside that, the tip
// part of the same glass. SOURCE FACT, measured on the reference frames (2-chat-use.mov 34.2 s, the effort popover; 151.0 s,
// the sidebar row menu; 1-settings-tour.mov 143.2 s, the theme list in Light; 1 px = 1 pt): the corners are round with a
// radius of about 20 pt (the outline leaves each straight side about 20 pt from the corner and passes 8.5 pt from it on the
// diagonal, a circle of 20.5); the outermost pixel is a near-black hairline (Dark 0-28 between a backdrop of 35-60 and a
// glass of 60-90; Light 146 between 194 and 215); inside it a pixel brighter than the glass (Dark +25 to +47, Light +8);
// the glass itself lighter than what is round it (Dark +20, Light +25). The card now draws that over the popup window's own
// see-through glass (PopoverGlass, which the card keeps in step with its outline, its grow-in and its fade): a translucent
// tint, the hairline and the rim, and its content clipped to the round corners. Where no glass can be had (the overlay
// host, a failed attach) it draws ED-238's opaque panel under the tint so its text stays legible. The corners are circular
// arcs, not Apple's continuous curve: the window manager's clip under them is a circular rounded rectangle, and the two
// must coincide or the blur would show past the rim.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;

namespace Marrow.UI.Port;

/// <summary>The card edge that carries a popover's tip: the edge facing its anchor.</summary>
public enum PopoverTipEdge
{
    Top,
    Bottom,
    Left,
    Right,
}

/// <summary>What is under a popover's card.</summary>
internal enum PopoverMaterial
{
    /// <summary>The popup window's see-through glass, or glass still to come (its first frame): the card draws only its
    /// translucent tint, hairline and rim.</summary>
    Glass,

    /// <summary>No glass can be had (the overlay host, a host that is no WinUI-composited window, a failed attach): the card
    /// draws ED-238's opaque panel under its tint.</summary>
    Fallback,
}

/// <summary>When a popover's material and its grow-in reached each step (Stopwatch ticks; 0 not yet), for the native timing
/// check (ED-357 (6)).</summary>
internal sealed class PopoverGlassTimeline
{
    public long Opened { get; set; }
    public long Attached { get; set; }
    public long FirstGlass { get; set; }
    public long OpacityOne { get; set; }
    public long ScaleOne { get; set; }
    public bool SawFade { get; set; }
    public bool SawGrow { get; set; }

    public double Ms(long ticks) => ticks == 0 || Opened == 0 ? double.NaN : (ticks - Opened) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    public override string ToString() => FormattableString.Invariant($"attached {Ms(Attached):F1} ms, first glass {Ms(FirstGlass):F1} ms, opacity 1 at {Ms(OpacityOne):F1} ms, scale 1 at {Ms(ScaleOne):F1} ms");
}

/// <summary>A popover's panel with its tip (see the file note).</summary>
public sealed class PopoverCard : Decorator, Avalonia.Rendering.ICustomHitTest
{
    internal const double TipHeight = 12;
    internal const double TipHalfWidth = 12;
    internal const double TipApexRadius = 3;
    internal const double TipShoulderRadius = 4;
    internal const double TipGap = 1;
    private const double Edge = 1; // ED-238's 1 pt panel edge: now the dark hairline
    private const double Rim = 1;  // the light rim inside it

    /// <summary>The card's corner radius, measured on the reference frames (see the file note).</summary>
    internal const double BodyRadius = 20;

    /// <summary>A site-specific body radius; ordinary popovers retain the reference radius.</summary>
    public double BodyCornerRadius { get; init; } = BodyRadius;
    /// <summary>DESIGN 5.3 find rectangle (ED-NEW-web-w8c-1); ordinary popovers retain their tip.</summary>
    public bool ShowTip { get; init; } = true;

    // OD-0095: "one outline, one fill, one shadow". ENGINEERING DECISION (no number is published for NSPopover's window
    // shadow; the videos show it soft and faint, more a lift than a drop): a Gaussian falloff, 0.22 black at the outline,
    // sigma 3.5 pt, gone by 10 pt, 1 pt down, following the card and its tip as one outline, in Light and Dark alike. It is
    // drawn as stacked strokes of the outline (cheap, no layer or blur per frame), and it fades out over the last 5 pt of
    // the room on the tip's side (the popup ends 1 pt past the point), so it never ends on a hard line under the tip.
    internal const double ShadowReach = 10;
    internal const double ShadowSigma = 3.5;
    internal const double ShadowPeak = 0.22;
    internal const double ShadowDrop = 1;
    private const double ShadowFade = 5;

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<PopoverCard, IBrush?>(nameof(Background));

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        AvaloniaProperty.Register<PopoverCard, IBrush?>(nameof(BorderBrush));

    /// <summary>The light rim a point inside the hairline.</summary>
    public static readonly StyledProperty<IBrush?> RimBrushProperty =
        AvaloniaProperty.Register<PopoverCard, IBrush?>(nameof(RimBrush));

    /// <summary>The opaque panel drawn under the tint where no see-through glass is under the card (ED-238's fill).</summary>
    public static readonly StyledProperty<IBrush?> FallbackBrushProperty =
        AvaloniaProperty.Register<PopoverCard, IBrush?>(nameof(FallbackBrush));

    public static readonly StyledProperty<PopoverTipEdge> TipEdgeProperty =
        AvaloniaProperty.Register<PopoverCard, PopoverTipEdge>(nameof(TipEdge), PopoverTipEdge.Bottom);

    public static readonly StyledProperty<double?> TipOffsetProperty =
        AvaloniaProperty.Register<PopoverCard, double?>(nameof(TipOffset));

    static PopoverCard()
    {
        AffectsRender<PopoverCard>(BackgroundProperty, BorderBrushProperty, RimBrushProperty, FallbackBrushProperty, TipEdgeProperty, TipOffsetProperty);
        AffectsMeasure<PopoverCard>(TipEdgeProperty);
    }

    /// <summary>
    /// Whether the card keeps room round itself (its Margin, on the sides away from the tip) for its shadow inside the
    /// popup it is the content of: the presenter's cards do; a card drawn on its own (a test, a probe) does not.
    /// </summary>
    internal bool ShadowRoom
    {
        get => shadowRoom;
        set { shadowRoom = value; Margin = value ? (ShowTip ? ShadowMargin(TipEdge) : new Thickness(ShadowReach + ShadowDrop)) : default; }
    }

    private bool shadowRoom;

    /// <summary>The room a presented card keeps round itself for its shadow: none on the tip's side (the tip's own room
    /// holds it there), the shadow's reach and its drop on the others.</summary>
    internal static Thickness ShadowMargin(PopoverTipEdge edge)
    {
        var side = ShadowReach + ShadowDrop;
        return edge switch
        {
            PopoverTipEdge.Top => new Thickness(side, 0, side, side),
            PopoverTipEdge.Bottom => new Thickness(side, side, side, 0),
            PopoverTipEdge.Left => new Thickness(0, side, side, side),
            _ => new Thickness(side, side, 0, side),
        };
    }

    public PopoverCard()
    {
        LayoutUpdated += (_, _) => FollowAnchor();
        ActualThemeVariantChanged += (_, _) => glass?.SetDark(ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark);
    }

    /// <summary>The glass's tint, drawn over the see-through glass (translucent).</summary>
    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    /// <summary>The panel's 1 pt edge: the dark hairline round the glass.</summary>
    public IBrush? BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }

    /// <summary>The 1 pt light rim inside the hairline.</summary>
    public IBrush? RimBrush { get => GetValue(RimBrushProperty); set => SetValue(RimBrushProperty, value); }

    /// <summary>The opaque panel under the tint where no glass is (see <see cref="Material"/>).</summary>
    public IBrush? FallbackBrush { get => GetValue(FallbackBrushProperty); set => SetValue(FallbackBrushProperty, value); }

    /// <summary>What is under the card: see-through glass (or glass still to come), or nothing, so it draws its opaque
    /// fallback panel.</summary>
    internal PopoverMaterial Material
    {
        get => material;
        set { if (material == value) return; material = value; InvalidateVisual(); }
    }

    private PopoverMaterial material;

    /// <summary>The edge the tip stands on.</summary>
    public PopoverTipEdge TipEdge { get => GetValue(TipEdgeProperty); set => SetValue(TipEdgeProperty, value); }

    /// <summary>Where along that edge the tip's centre is, from the card's left or top; null: the middle.</summary>
    public double? TipOffset { get => GetValue(TipOffsetProperty); set => SetValue(TipOffsetProperty, value); }

    /// <summary>The control the popover hangs from: the tip turns to face it and points at its middle. Read live from
    /// <see cref="AnchorSource"/> when there is one (a Popup shown again from another control while it still shows moves to
    /// that control, and the tip follows it), else the control set here.</summary>
    public Control? Anchor
    {
        get => AnchorSource is { } source ? source() : anchor;
        set => anchor = value;
    }

    private Control? anchor;

    /// <summary>Where the anchor is read from after every layout, for a popover whose anchor can change while its card stays
    /// (PopoverHost: the popup's PlacementTarget).</summary>
    public Func<Control?>? AnchorSource { get; set; }

    /// <summary>The rect in the anchor's own coordinates the popover was shown relative to (NSPopover.show(relativeTo:of:),
    /// a Popup's PlacementRect), read after every layout; null: the whole anchor.</summary>
    public Func<Rect?>? AnchorRect { get; set; }

    private bool Vertical => TipEdge is PopoverTipEdge.Top or PopoverTipEdge.Bottom;

    private static double Reach => TipHeight + TipGap;

    // The card's own rectangle, inside the room the tip and its gap take.
    private Rect Body(Size size) => !ShowTip ? new Rect(size) : TipEdge switch
    {
        PopoverTipEdge.Top => new Rect(0, Reach, size.Width, Math.Max(0, size.Height - Reach)),
        PopoverTipEdge.Bottom => new Rect(0, 0, size.Width, Math.Max(0, size.Height - Reach)),
        PopoverTipEdge.Left => new Rect(Reach, 0, Math.Max(0, size.Width - Reach), size.Height),
        _ => new Rect(0, 0, Math.Max(0, size.Width - Reach), size.Height),
    };

    /// <summary>What a card with its tip on <paramref name="edge"/> adds round its content: its edge, and the tip with its
    /// gap on the tip's side.</summary>
    internal static Size RoomAround(PopoverTipEdge edge) =>
        edge is PopoverTipEdge.Top or PopoverTipEdge.Bottom ? new Size(2 * Edge, 2 * Edge + Reach) : new Size(2 * Edge + Reach, 2 * Edge);

    protected override Size MeasureOverride(Size availableSize)
    {
        var room = ShowTip ? RoomAround(TipEdge) : new Size(2 * Edge, 2 * Edge);
        Child?.Measure(new Size(Math.Max(0, availableSize.Width - room.Width), Math.Max(0, availableSize.Height - room.Height)));
        var child = Child?.DesiredSize ?? default;
        return new Size(child.Width + room.Width, child.Height + room.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is { } child)
        {
            var inner = Body(finalSize).Deflate(Edge);
            child.Arrange(inner);
            // The content stays inside the round corners (a row's hover fill, a list running to the edge): clipped to the
            // body inside the hairline, concentric with it. A site's own clip on its content is left alone.
            if (child.Clip is null || ReferenceEquals(child.Clip, contentClip))
            {
                var radius = Math.Max(0, BodyCornerRadius - Edge);
                if (contentClip is not RectangleGeometry { } current || current.Rect != new Rect(inner.Size))
                    contentClip = new RectangleGeometry(new Rect(inner.Size), radius, radius);
                child.Clip = contentClip;
            }
        }
        return finalSize;
    }

    private Geometry? contentClip;

    /// <summary>How far from either end of its edge the tip's centre is held: past the round corner and the tip's own half
    /// width and shoulder, so the tip always stands on the straight part.</summary>
    internal static double TipLeast => BodyRadius + TipHalfWidth + TipShoulderRadius;

    // The tip's centre along its edge, held on the straight part of the edge.
    private double Along(Size size)
    {
        var length = Vertical ? size.Width : size.Height;
        var least = BodyCornerRadius + TipHalfWidth + TipShoulderRadius;
        var at = TipOffset ?? length / 2;
        return length < 2 * least ? length / 2 : Math.Clamp(at, least, length - least);
    }

    /// <summary>The tip's point, in the card's own coordinates.</summary>
    public Point TipPoint
    {
        get
        {
            var size = Bounds.Size;
            if (!ShowTip) return new Point(size.Width / 2, size.Height / 2);
            var along = Along(size);
            return TipEdge switch
            {
                PopoverTipEdge.Top => new Point(along, TipGap),
                PopoverTipEdge.Bottom => new Point(along, size.Height - TipGap),
                PopoverTipEdge.Left => new Point(TipGap, along),
                _ => new Point(size.Width - TipGap, along),
            };
        }
    }

    /// <summary>The tip's point as a fraction of the card, for a grow-in that starts there (PopoverAppear).</summary>
    public RelativePoint TipOrigin
    {
        get
        {
            var size = Bounds.Size;
            if (!ShowTip) return new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            if (size.Width <= 0 || size.Height <= 0) return new RelativePoint(0.5, TipEdge == PopoverTipEdge.Top ? 0 : 1, RelativeUnit.Relative);
            var tip = TipPoint;
            return new RelativePoint(tip.X / size.Width, tip.Y / size.Height, RelativeUnit.Relative);
        }
    }

    /// <summary>The panel's one outline: the card's rectangle and the tip, at the card's current size.</summary>
    internal Geometry Outline => Shape(Bounds.Size, Edge / 2);

    /// <summary>The glass's own outline, the outer edge of the hairline: what the tint fills and the window's glass is
    /// clipped to.</summary>
    internal Geometry GlassOutline => Shape(Bounds.Size, 0);

    /// <summary>
    /// The card takes a press only inside its one outline: the clear room beside the tip and round the card (its gap,
    /// its shadow) is not the popover (a press there light-dismisses it, PopoverHost).
    /// </summary>
    public bool HitTest(Point point) => Bounds.Width > 0 && Bounds.Height > 0 && Outline.FillContains(point);

    // The outline <paramref name="inset"/> inside the glass's own edge: the body's rectangle deflated by it with concentric
    // corners, and the tip's sides moved in by it along their normals (sides at 45 degrees: the point comes in by inset * sqrt 2,
    // the half width where the sides meet the deflated edge is TipHalfWidth + inset - inset * sqrt 2), the point's rounding
    // concentric, the shoulders' too. Inset 0 is the glass; Edge / 2 is the hairline's stroke line (Outline).
    private Geometry Shape(Size size, double inset)
    {
        var body = Body(size).Deflate(inset);
        var along = Along(size);
        var depth = TipHeight + inset - inset * Math.Sqrt(2); // from the deflated edge to the point
        var half = TipHalfWidth + inset - inset * Math.Sqrt(2);
        var corner = Math.Max(0, Math.Min(BodyCornerRadius - inset, Math.Min(body.Width, body.Height) / 2));
        var apex = Math.Max(0, TipApexRadius - inset);
        var shoulder = TipShoulderRadius + inset;
        // Clockwise from the top left, with the tip's three corners on the edge it stands on.
        var corners = new List<(Point Point, double Radius)>();
        void Tip(Point shoulderIn, Point point, Point shoulderOut)
        {
            corners.Add((shoulderIn, shoulder));
            corners.Add((point, apex));
            corners.Add((shoulderOut, shoulder));
        }
        corners.Add((body.TopLeft, corner));
        if (ShowTip && TipEdge == PopoverTipEdge.Top) Tip(new Point(along - half, body.Top), new Point(along, body.Top - depth), new Point(along + half, body.Top));
        corners.Add((body.TopRight, corner));
        if (ShowTip && TipEdge == PopoverTipEdge.Right) Tip(new Point(body.Right, along - half), new Point(body.Right + depth, along), new Point(body.Right, along + half));
        corners.Add((body.BottomRight, corner));
        if (ShowTip && TipEdge == PopoverTipEdge.Bottom) Tip(new Point(along + half, body.Bottom), new Point(along, body.Bottom + depth), new Point(along - half, body.Bottom));
        corners.Add((body.BottomLeft, corner));
        if (ShowTip && TipEdge == PopoverTipEdge.Left) Tip(new Point(body.Left, along + half), new Point(body.Left - depth, along), new Point(body.Left, along - half));
        return Rounded(corners);
    }

    // A closed polygon with each corner rounded by its radius (an arc tangent to both sides).
    private static StreamGeometry Rounded(List<(Point Point, double Radius)> corners)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        var count = corners.Count;
        for (var i = 0; i < count; i++)
        {
            var (point, radius) = corners[i];
            var previous = corners[(i + count - 1) % count].Point;
            var next = corners[(i + 1) % count].Point;
            if (radius <= 0)
            {
                if (i == 0) context.BeginFigure(point, true);
                else context.LineTo(point);
                continue;
            }
            var toPrevious = Unit(previous - point);
            var toNext = Unit(next - point);
            var cos = Math.Clamp(toPrevious.X * toNext.X + toPrevious.Y * toNext.Y, -1, 1);
            var half = Math.Acos(cos) / 2;
            var reach = radius / Math.Tan(half);
            var from = point + toPrevious * reach;
            var to = point + toNext * reach;
            if (i == 0) context.BeginFigure(from, true);
            else context.LineTo(from);
            var turn = toPrevious.X * toNext.Y - toPrevious.Y * toNext.X;
            context.ArcTo(to, new Size(radius, radius), 0, false, turn < 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
        }
        context.EndFigure(true);
        return geometry;
    }

    private static Vector Unit(Vector v) => v / Math.Max(1e-9, Math.Sqrt(v.X * v.X + v.Y * v.Y));

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        var shape = Shape(size, Edge / 2);
        var glassShape = Shape(size, 0);
        // The shadow lies outside the glass only: under a translucent card it would darken the glass from inside.
        using (context.PushGeometryClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(size).Inflate(ShadowReach + ShadowDrop + 2)), glassShape)))
        using (context.PushTransform(Matrix.CreateTranslation(0, ShadowDrop)))
            foreach (var pen in ShadowPens(size)) context.DrawGeometry(null, pen, shape);
        if (Material == PopoverMaterial.Fallback && FallbackBrush is { } panel) context.DrawGeometry(panel, null, glassShape);
        context.DrawGeometry(Background, null, glassShape);
        if (BorderBrush is { } edge) context.DrawGeometry(null, new Pen(edge, Edge), shape);
        if (RimBrush is { } rim) context.DrawGeometry(null, new Pen(rim, Rim), Shape(size, Edge + Rim / 2));
    }

    private (Size Size, PopoverTipEdge Edge) shadowKey;
    private ImmutablePen[] shadowPens = [];

    // One ring a point wide for each point of the shadow's reach, widest first: a stroke of width 2k on the outline covers
    // k points outside it (the fill covers the inside), so the rings stack to the falloff, each ring's own share solved
    // from the outside in. Each ring's black fades to nothing over the last points of the tip's side of the card.
    private ImmutablePen[] ShadowPens(Size size)
    {
        if (shadowKey == (size, TipEdge) && shadowPens.Length > 0) return shadowPens;
        shadowKey = (size, TipEdge);
        var count = (int)ShadowReach;
        double Falloff(double distance) => ShadowPeak * Math.Exp(-distance * distance / (2 * ShadowSigma * ShadowSigma));
        var pens = new ImmutablePen[count];
        var outside = 0.0; // what the rings further out already cover at this ring's middle
        for (var k = count; k >= 1; k--)
        {
            var wanted = Falloff(k - 0.5);
            var share = Math.Clamp(1 - (1 - wanted) / (1 - outside), 0, 1);
            outside = wanted;
            pens[count - k] = new ImmutablePen(ShowTip ? Fade(size, share) : new ImmutableSolidColorBrush(Color.FromArgb((byte)Math.Round(share * 255), 0, 0, 0)), 2 * k, lineJoin: PenLineJoin.Round);
        }
        return shadowPens = pens;
    }

    // Black at the ring's share, fading to nothing over ShadowFade points, gone in the last point before the card's bounds
    // on the tip's side (in the shadow's own space, which is one drop lower than the card's).
    private ImmutableLinearGradientBrush Fade(Size size, double share)
    {
        var solid = Color.FromArgb((byte)Math.Round(share * 255), 0, 0, 0);
        var clear = Color.FromArgb(0, 0, 0, 0);
        var (from, to) = TipEdge switch
        {
            PopoverTipEdge.Top => (new Point(0, 1 + ShadowFade - ShadowDrop), new Point(0, 1 - ShadowDrop)),
            PopoverTipEdge.Bottom => (new Point(0, size.Height - 1 - ShadowFade - ShadowDrop), new Point(0, size.Height - 1 - ShadowDrop)),
            PopoverTipEdge.Left => (new Point(1 + ShadowFade, 0), new Point(1, 0)),
            _ => (new Point(size.Width - 1 - ShadowFade, 0), new Point(size.Width - 1, 0)),
        };
        return new ImmutableLinearGradientBrush(
            [new ImmutableGradientStop(0, solid), new ImmutableGradientStop(1, clear)],
            spreadMethod: GradientSpreadMethod.Pad,
            startPoint: new RelativePoint(from, RelativeUnit.Absolute),
            endPoint: new RelativePoint(to, RelativeUnit.Absolute));
    }

    /// <summary>
    /// The edge facing the anchor and the tip's place along it, given where the card and the anchor are (in one
    /// coordinate space): the card's side of the anchor decides the edge (it may have flipped from the preferred one for
    /// want of room); the tip's centre is the anchor's middle, held on the straight part of the edge.
    /// </summary>
    internal static (PopoverTipEdge Edge, double Offset) Aim(Rect card, Rect anchor, PopoverTipEdge preferred, double bodyCornerRadius = BodyRadius)
    {
        // A popup flips along the axis it was placed on, never across it: above or below for a tip on the top or bottom,
        // left or right for one on the side. The tip turns only once the card's body (the card less the tip's room) is
        // wholly on the other side of the anchor; a card pushed over its anchor keeps the edge it has (turning by the
        // middles, each turn moved the card's rooms, the middles crossed back and the layout never settled).
        var vertical = preferred is PopoverTipEdge.Top or PopoverTipEdge.Bottom;
        const double slack = 0.5;
        var edge = preferred switch
        {
            PopoverTipEdge.Bottom => card.Top >= anchor.Bottom - slack ? PopoverTipEdge.Top : PopoverTipEdge.Bottom,
            PopoverTipEdge.Top => card.Bottom <= anchor.Top + slack ? PopoverTipEdge.Bottom : PopoverTipEdge.Top,
            PopoverTipEdge.Right => card.Left >= anchor.Right - slack ? PopoverTipEdge.Left : PopoverTipEdge.Right,
            _ => card.Right <= anchor.Left + slack ? PopoverTipEdge.Right : PopoverTipEdge.Left,
        };
        var length = vertical ? card.Width : card.Height;
        var at = vertical ? anchor.Center.X - card.Left : anchor.Center.Y - card.Top;
        var least = bodyCornerRadius + TipHalfWidth + TipShoulderRadius;
        return (edge, length < 2 * least ? length / 2 : Math.Clamp(at, least, length - least));
    }

    // After every layout: where the card has ended up beside its anchor, so the tip faces it (the popup may have flipped
    // or slid to stay on screen) and points at its middle; the grow-in's origin follows the tip.
    private void FollowAnchor()
    {
        if (!ShowTip) { SyncGlass(); return; }
        if (Anchor is not { } anchor || this.GetVisualParent() is not Visual parent || TopLevel.GetTopLevel(this) is not { } top
            || TopLevel.GetTopLevel(anchor) is not { } anchorTop) return;
        // A top-level that has closed (the anchor's row rebuilt while the popover showed) is no longer on screen: leave
        // the card as it is (PointToScreen would throw out of the layout pass).
        if (!parent.IsAttachedToVisualTree() || !anchor.IsAttachedToVisualTree()) return;
        // From the parents, so a grow-in's scale (a render transform on the card) does not move what is measured.
        var cardOrigin = parent.PointToScreen(Bounds.Position);
        var within = AnchorRect?.Invoke() ?? new Rect(anchor.Bounds.Size); // the rect it was shown relative to, or the anchor
        var anchorOrigin = anchor.PointToScreen(within.Position);
        var card = new Rect(cardOrigin.X / top.RenderScaling, cardOrigin.Y / top.RenderScaling, Bounds.Width, Bounds.Height);
        var target = new Rect(anchorOrigin.X / anchorTop.RenderScaling, anchorOrigin.Y / anchorTop.RenderScaling, within.Width, within.Height);
        var (edge, offset) = Aim(card, target, TipEdge, BodyCornerRadius);
        if (edge != TipEdge) TipEdge = edge;
        if (TipOffset is not { } current || Math.Abs(current - offset) > 0.01) TipOffset = offset;
        if (RenderTransform is ScaleTransform) RenderTransformOrigin = TipOrigin; // a grow-in under way follows the tip
        SyncGlass();
    }

    private PopoverGlass? glass;
    private TopLevel? glassWindow;
    private bool pumping;

    /// <summary>The steps of the last showing's material and grow-in (ED-357 (6)).</summary>
    internal PopoverGlassTimeline Timeline { get; private set; } = new();

    /// <summary>The window glass under the card while it shows (null otherwise).</summary>
    internal PopoverGlass? Glass => glass;

    /// <summary>The card has just been shown in <paramref name="host"/> (PopoverHost, on the popup's Opened): a native popup
    /// window gets its see-through glass, kept in step with the card from here on; any other host (the overlay's) has none, so
    /// the card draws its opaque panel.</summary>
    internal void Shown(Control? host)
    {
        DetachGlass();
        Timeline = new PopoverGlassTimeline { Opened = System.Diagnostics.Stopwatch.GetTimestamp() };
        if (host is not TopLevel window || PopoverGlassAttach.Current(window) is not { } made)
        {
            Material = PopoverMaterial.Fallback;
            return;
        }
        Material = PopoverMaterial.Glass;
        glass = made;
        glassWindow = window;
        SyncGlass();
    }

    /// <summary>The popup has closed: its glass goes with it.</summary>
    internal void Hidden() => DetachGlass();

    private void DetachGlass()
    {
        glass?.Dispose();
        glass = null;
        glassWindow = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TipEdgeProperty && shadowRoom) Margin = ShowTip ? ShadowMargin(TipEdge) : new Thickness(ShadowReach + ShadowDrop); // the card flipped
        if (glass is null) return;
        if (change.Property == RenderTransformProperty || change.Property == OpacityProperty || change.Property == TipOffsetProperty
            || change.Property == TipEdgeProperty || change.Property == BoundsProperty) SyncGlass();
    }

    // Puts the window's glass under the card's outline as it is drawn now (its scale and fade included); while the card
    // grows or fades, or the glass waits for the window's first frame, again on the next frame.
    private void SyncGlass()
    {
        if (glass is not { } shown || glassWindow is not { } window) return;
        if (glass.Failure is null && !glass.Attached) glass.SetDark(ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark);
        var shape = GlassShape(window);
        var placed = shape is { } s && shown.Update(s);
        if (shown.Failure is not null)
        {
            Material = PopoverMaterial.Fallback;
            DetachGlass();
            return;
        }
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (placed && Timeline.Attached == 0) Timeline.Attached = now;
        if (Timeline.FirstGlass == 0 && shown.FirstShownTicks != 0) Timeline.FirstGlass = shown.FirstShownTicks;
        // The grow-in starts just after the popup has opened (Presentation: PopoverAppear.Play after IsOpen): a step counts
        // once the card has been seen below it.
        if (Opacity < 1) Timeline.SawFade = true;
        if (RenderTransform is ScaleTransform { ScaleX: < 1 }) Timeline.SawGrow = true;
        if (Timeline.OpacityOne == 0 && Timeline.SawFade && Opacity >= 1) Timeline.OpacityOne = now;
        if (Timeline.ScaleOne == 0 && Timeline.SawGrow && RenderTransform is not ScaleTransform { ScaleX: < 1 }) Timeline.ScaleOne = now;
        var moving = !placed || RenderTransform is not null || Opacity < 1;
        if (moving && !pumping)
        {
            pumping = true;
            window.RequestAnimationFrame(_ =>
            {
                pumping = false;
                SyncGlass();
            });
        }
    }

    /// <summary>Where the card's glass is in <paramref name="window"/>'s physical pixels, its grow-in's scale and its fade
    /// included; null while the card is not laid out in it.</summary>
    internal PopoverGlass.Shape? GlassShape(Visual window)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0 || this.TransformToVisual(window) is not { } toWindow) return null;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var m = toWindow * Matrix.CreateScale(scaling, scaling);
        var body = Body(size);
        var topLeft = body.TopLeft.Transform(m);
        var bottomRight = body.BottomRight.Transform(m);
        var scale = m.M11; // the card is only moved and scaled, never turned
        var along = Along(size);
        var centre = TipEdge switch
        {
            PopoverTipEdge.Top => new Point(along, body.Top),
            PopoverTipEdge.Bottom => new Point(along, body.Bottom),
            PopoverTipEdge.Left => new Point(body.Left, along),
            _ => new Point(body.Right, along),
        };
        double opacity = 1;
        for (Visual? at = this; at is not null && !ReferenceEquals(at, window); at = at.GetVisualParent())
        {
            if (!at.IsVisible) opacity = 0;
            opacity *= at.Opacity;
        }
        return new PopoverGlass.Shape(new Rect(topLeft, bottomRight), (ShowTip ? BodyRadius : BodyCornerRadius) * scale, centre.Transform(m),
            ShowTip ? TipHalfWidth * Math.Sqrt(2) * scale : 0, ShowTip ? TipApexRadius * scale : 0, opacity);
    }
}

/// <summary>Where a popup window's glass comes from: the window's own composition (PopoverGlass), which a test replaces.</summary>
internal static class PopoverGlassAttach
{
    internal static Marrow.Core.Port.Ambient<Func<TopLevel, PopoverGlass?>> Make { get; } = new(Default);

    internal static PopoverGlass? Current(TopLevel window) => Make.Current(window);

    internal static PopoverGlass? Default(TopLevel window)
    {
        if (!OperatingSystem.IsWindows() || window.PlatformImpl is not { } impl || impl.GetType().Assembly.GetName().Name != "Avalonia.Win32") return null;
        try
        {
            return PopoverGlass.For(window, window.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark);
        }
        catch (Exception error) when (PopoverGlass.NonFatal(error))
        {
            return null; // no glass: the card draws its opaque panel
        }
    }
}
