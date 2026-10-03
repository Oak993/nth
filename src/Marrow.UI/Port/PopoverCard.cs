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

/// <summary>A popover's panel with its tip (see the file note).</summary>
public sealed class PopoverCard : Decorator
{
    internal const double TipHeight = 12;
    internal const double TipHalfWidth = 12;
    internal const double TipApexRadius = 3;
    internal const double TipShoulderRadius = 4;
    internal const double TipGap = 1;
    private const double Edge = 1; // ED-238's 1 pt panel edge

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

    public static readonly StyledProperty<PopoverTipEdge> TipEdgeProperty =
        AvaloniaProperty.Register<PopoverCard, PopoverTipEdge>(nameof(TipEdge), PopoverTipEdge.Bottom);

    public static readonly StyledProperty<double?> TipOffsetProperty =
        AvaloniaProperty.Register<PopoverCard, double?>(nameof(TipOffset));

    static PopoverCard()
    {
        AffectsRender<PopoverCard>(BackgroundProperty, BorderBrushProperty, TipEdgeProperty, TipOffsetProperty);
        AffectsMeasure<PopoverCard>(TipEdgeProperty);
    }

    /// <summary>
    /// Whether the card keeps room round itself (its Margin, on the sides away from the tip) for its shadow inside the
    /// popup it is the content of: the presenter's cards do; a card drawn on its own (a test, a probe) does not.
    /// </summary>
    internal bool ShadowRoom
    {
        get => shadowRoom;
        set { shadowRoom = value; Margin = value ? ShadowMargin(TipEdge) : default; }
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TipEdgeProperty && shadowRoom) Margin = ShadowMargin(TipEdge); // the card flipped
    }

    public PopoverCard()
    {
        LayoutUpdated += (_, _) => FollowAnchor();
    }

    /// <summary>The panel's fill.</summary>
    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    /// <summary>The panel's 1 pt edge.</summary>
    public IBrush? BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }

    /// <summary>The edge the tip stands on.</summary>
    public PopoverTipEdge TipEdge { get => GetValue(TipEdgeProperty); set => SetValue(TipEdgeProperty, value); }

    /// <summary>Where along that edge the tip's centre is, from the card's left or top; null: the middle.</summary>
    public double? TipOffset { get => GetValue(TipOffsetProperty); set => SetValue(TipOffsetProperty, value); }

    /// <summary>The control the popover hangs from: the tip turns to face it and points at its middle.</summary>
    public Control? Anchor { get; set; }

    private bool Vertical => TipEdge is PopoverTipEdge.Top or PopoverTipEdge.Bottom;

    private static double Reach => TipHeight + TipGap;

    // The card's own rectangle, inside the room the tip and its gap take.
    private Rect Body(Size size) => TipEdge switch
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
        var room = RoomAround(TipEdge);
        Child?.Measure(new Size(Math.Max(0, availableSize.Width - room.Width), Math.Max(0, availableSize.Height - room.Height)));
        var child = Child?.DesiredSize ?? default;
        return new Size(child.Width + room.Width, child.Height + room.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(Body(finalSize).Deflate(Edge));
        return finalSize;
    }

    // The tip's centre along its edge, held on the straight part of the edge.
    private double Along(Size size)
    {
        var length = Vertical ? size.Width : size.Height;
        var least = TipHalfWidth + TipShoulderRadius;
        var at = TipOffset ?? length / 2;
        return length < 2 * least ? length / 2 : Math.Clamp(at, least, length - least);
    }

    /// <summary>The tip's point, in the card's own coordinates.</summary>
    public Point TipPoint
    {
        get
        {
            var size = Bounds.Size;
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
            if (size.Width <= 0 || size.Height <= 0) return new RelativePoint(0.5, TipEdge == PopoverTipEdge.Top ? 0 : 1, RelativeUnit.Relative);
            var tip = TipPoint;
            return new RelativePoint(tip.X / size.Width, tip.Y / size.Height, RelativeUnit.Relative);
        }
    }

    /// <summary>The panel's one outline: the card's rectangle and the tip, at the card's current size.</summary>
    internal Geometry Outline => Shape(Bounds.Size);

    private Geometry Shape(Size size)
    {
        var body = Body(size).Deflate(Edge / 2); // the edge is stroked on the outline: half of it inside
        var along = Along(size);
        var depth = TipHeight - Edge / 2;
        // Clockwise from the top left, with the tip's three corners on the edge it stands on (radius 0: a card corner).
        var corners = new List<(Point Point, double Radius)>();
        void Tip(Point shoulderIn, Point apex, Point shoulderOut)
        {
            corners.Add((shoulderIn, TipShoulderRadius));
            corners.Add((apex, TipApexRadius));
            corners.Add((shoulderOut, TipShoulderRadius));
        }
        corners.Add((body.TopLeft, 0));
        if (TipEdge == PopoverTipEdge.Top) Tip(new Point(along - TipHalfWidth, body.Top), new Point(along, body.Top - depth), new Point(along + TipHalfWidth, body.Top));
        corners.Add((body.TopRight, 0));
        if (TipEdge == PopoverTipEdge.Right) Tip(new Point(body.Right, along - TipHalfWidth), new Point(body.Right + depth, along), new Point(body.Right, along + TipHalfWidth));
        corners.Add((body.BottomRight, 0));
        if (TipEdge == PopoverTipEdge.Bottom) Tip(new Point(along + TipHalfWidth, body.Bottom), new Point(along, body.Bottom + depth), new Point(along - TipHalfWidth, body.Bottom));
        corners.Add((body.BottomLeft, 0));
        if (TipEdge == PopoverTipEdge.Left) Tip(new Point(body.Left, along + TipHalfWidth), new Point(body.Left - depth, along), new Point(body.Left, along - TipHalfWidth));
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
        var shape = Shape(size);
        using (context.PushTransform(Matrix.CreateTranslation(0, ShadowDrop)))
            foreach (var pen in ShadowPens(size)) context.DrawGeometry(null, pen, shape);
        context.DrawGeometry(Background, BorderBrush is { } edge ? new Pen(edge, Edge) : null, shape);
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
            pens[count - k] = new ImmutablePen(Fade(size, share), 2 * k, lineJoin: PenLineJoin.Round);
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
    internal static (PopoverTipEdge Edge, double Offset) Aim(Rect card, Rect anchor, PopoverTipEdge preferred)
    {
        // A popup flips along the axis it was placed on, never across it: above or below for a tip on the top or bottom,
        // left or right for one on the side; the card's middle against the anchor's says which.
        var vertical = preferred is PopoverTipEdge.Top or PopoverTipEdge.Bottom;
        var edge = vertical
            ? (card.Center.Y < anchor.Center.Y ? PopoverTipEdge.Bottom : PopoverTipEdge.Top)
            : (card.Center.X < anchor.Center.X ? PopoverTipEdge.Right : PopoverTipEdge.Left);
        var length = vertical ? card.Width : card.Height;
        var at = vertical ? anchor.Center.X - card.Left : anchor.Center.Y - card.Top;
        var least = TipHalfWidth + TipShoulderRadius;
        return (edge, length < 2 * least ? length / 2 : Math.Clamp(at, least, length - least));
    }

    // After every layout: where the card has ended up beside its anchor, so the tip faces it (the popup may have flipped
    // or slid to stay on screen) and points at its middle; the grow-in's origin follows the tip.
    private void FollowAnchor()
    {
        if (Anchor is not { } anchor || this.GetVisualParent() is not Visual parent || TopLevel.GetTopLevel(this) is not { } top
            || TopLevel.GetTopLevel(anchor) is not { } anchorTop) return;
        // From the parents, so a grow-in's scale (a render transform on the card) does not move what is measured.
        var cardOrigin = parent.PointToScreen(Bounds.Position);
        var anchorOrigin = anchor.PointToScreen(default);
        var card = new Rect(cardOrigin.X / top.RenderScaling, cardOrigin.Y / top.RenderScaling, Bounds.Width, Bounds.Height);
        var target = new Rect(anchorOrigin.X / anchorTop.RenderScaling, anchorOrigin.Y / anchorTop.RenderScaling, anchor.Bounds.Width, anchor.Bounds.Height);
        var (edge, offset) = Aim(card, target, TipEdge);
        if (edge != TipEdge) TipEdge = edge;
        if (TipOffset is not { } current || Math.Abs(current - offset) > 0.01) TipOffset = offset;
        if (RenderTransform is ScaleTransform) RenderTransformOrigin = TipOrigin; // a grow-in under way follows the tip
    }
}
