// PORT-ONLY: Dextra AS-17 (DESIGN.md rev 5, 2.5 "An Astur's own mark", "Moves", "Reduce motion", "Never clipped"; the canvas's .av.ast, work/design/assistant/
// index.html lines 117-152 and 555-559; ED-NEW-dextra-as17-2). No Swift twin.
// An Astur's own mark: its node shape alone, in its tint, drawn with Armilla's geometry (the same six shapes, one radius for all). At rest it shows at
// 80 % scale and 72 % strength; busy, waiting or done it shows at full size and strength; paused, stopping and stopped dim it. It keeps a small corner
// badge for its own state, and the shape itself never moves. Every change of state is one spring (damping 0.8 for the shape, a little looser for the
// badge's pop) from what is on screen to the next pose: no fade and no cut. With Reduce motion, or when the motion budget stills its list, each state
// shows its own still pose at once, and working and thinking, which the springs alone cannot tell from rest, take a dot and three dots in the badge.
// Pure: no Avalonia, no Skia, no clock of its own; the host ticks it and hands it a canvas.
using Marrow.Core.Port.Assistant;

namespace Marrow.UI.Port.Armilla;

/// <summary>What an Astur's mark says (the canvas's states): Still is a chat message's mark, drawn once at full strength.</summary>
public enum AsturMarkState { Idle, Still, Thinking, Working, Waiting, Done, Blocked, Paused, Stopping, Stopped }

/// <summary>The small corner badge of a state: a check, a bar and a dot, a minus, two bars, a dot, three dots, a hollow square, a filled one.</summary>
public enum AsturBadge { None, Check, Bang, Minus, Bars, Dot, Dots, Hollow, Solid }

public static class AsturMarkStates
{
    /// <summary>The mark of an Astur's presence (the org chart's chip and the corner badge read the same state).</summary>
    public static AsturMarkState For(AsturPresence presence) => presence switch
    {
        AsturPresence.Idle => AsturMarkState.Idle,
        AsturPresence.Thinking => AsturMarkState.Thinking,
        AsturPresence.Working => AsturMarkState.Working,
        AsturPresence.Waiting => AsturMarkState.Waiting,
        AsturPresence.Blocked => AsturMarkState.Blocked,
        AsturPresence.Paused => AsturMarkState.Paused,
        AsturPresence.Stopped => AsturMarkState.Stopped,
        _ => throw new ArgumentOutOfRangeException(nameof(presence)),
    };

    /// <summary>The node shape Armilla draws for an Astur's look (a missing look takes the first, as the picker does).</summary>
    public static ArmillaShape ShapeOf(AsturShape? shape) => shape switch
    {
        AsturShape.Hexagon => ArmillaShape.Hexagon,
        AsturShape.Diamond => ArmillaShape.Diamond,
        AsturShape.RoundedTriangle => ArmillaShape.Triangle,
        AsturShape.Clover => ArmillaShape.Clover,
        AsturShape.Star => ArmillaShape.Star,
        _ => ArmillaShape.Squircle,
    };

    /// <summary>The badge a state shows; <paramref name="still"/> (Reduce motion, or a calmed list) adds a dot for Working and three for Thinking.</summary>
    public static AsturBadge BadgeOf(AsturMarkState state, bool still) => state switch
    {
        AsturMarkState.Done => AsturBadge.Check,
        AsturMarkState.Waiting => AsturBadge.Bang,
        AsturMarkState.Blocked => AsturBadge.Minus,
        AsturMarkState.Paused => AsturBadge.Bars,
        AsturMarkState.Stopping => AsturBadge.Hollow,
        AsturMarkState.Stopped => AsturBadge.Solid,
        AsturMarkState.Working => still ? AsturBadge.Dot : AsturBadge.None,
        AsturMarkState.Thinking => still ? AsturBadge.Dots : AsturBadge.None,
        _ => AsturBadge.None,
    };

    /// <summary>Whether the shape shows at full size and strength (busy, waiting, done, or a chat message's) rather than at rest.</summary>
    public static bool Full(AsturMarkState state) => state is AsturMarkState.Working or AsturMarkState.Thinking or AsturMarkState.Waiting or AsturMarkState.Done or AsturMarkState.Still;

    /// <summary>Paused, stopping and stopped dim the shape.</summary>
    public static bool Dimmed(AsturMarkState state) => state is AsturMarkState.Paused or AsturMarkState.Stopping or AsturMarkState.Stopped;

    public static string Label(AsturMarkState state) => state switch
    {
        AsturMarkState.Idle => "Idle",
        AsturMarkState.Still => "",
        AsturMarkState.Thinking => "Thinking",
        AsturMarkState.Working => "Working",
        AsturMarkState.Waiting => "Needs you",
        AsturMarkState.Done => "Done",
        AsturMarkState.Blocked => "Blocked",
        AsturMarkState.Paused => "Paused",
        AsturMarkState.Stopping => "Stopping",
        AsturMarkState.Stopped => "Stopped",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}

/// <summary>Everything an Astur's mark shows. Immutable; a change is a new look.</summary>
public sealed record AsturMarkLook
{
    public double Size { get; init; } = 30;

    public ArmillaShape Shape { get; init; }

    public AsturMarkState State { get; init; } = AsturMarkState.Idle;

    public bool Dark { get; init; } = true;

    /// <summary>Reduce motion, or the motion budget stilled this mark's list: every state shows its still pose at once.</summary>
    public bool Reduce { get; init; }
}

/// <summary>
/// The drawing calls an Astur's mark adds to Armilla's: a layer the shape is drawn into and composited with one opacity (so a shape with a stroked
/// outline has no seam at partial strength), and a cut that takes the badge's clear ring out of the shape inside that layer only.
/// </summary>
internal interface IAsturCanvas : IArmillaCanvas
{
    /// <summary>Starts a layer over the slot (<paramref name="half"/> either side of the origin); what is drawn until <see cref="EndLayer"/> is composited at <paramref name="opacity"/>.</summary>
    void BeginLayer(double opacity, double half);

    /// <summary>Takes a disc out of the layer (the ring round the badge); the surface behind shows through.</summary>
    void Cut(double x, double y, double r);

    void EndLayer();
}

/// <summary>Draws nothing: for measuring an extent and for ticking a mark that is not on screen.</summary>
internal sealed class NullAsturCanvas : IAsturCanvas
{
    public static readonly NullAsturCanvas Instance = new();

    public void Save() { }

    public void Restore() { }

    public void Translate(double dx, double dy) { }

    public void Rotate(double degrees) { }

    public void StrokeArc(double r, double fromDeg, double toDeg, double width, ArmillaRgb color, double alpha, bool roundCap) { }

    public void StrokeLine(double x0, double y0, double x1, double y1, double width, ArmillaRgb color, double alpha, bool roundCap) { }

    public void FillCircle(double x, double y, double r, ArmillaRgb color, double alpha) { }

    public void FillHaze(double r, ArmillaRgb color, double centreAlpha) { }

    public void FillShape(ArmillaShape shape, double x, double y, double r, ArmillaRgb color, double alpha) { }

    public void Glyph(ArmillaGlyph glyph, double unit, double lineWidth, ArmillaRgb color, double alpha) { }

    public void BeginLayer(double opacity, double half) { }

    public void Cut(double x, double y, double r) { }

    public void EndLayer() { }
}

internal sealed class AsturMarkEngine
{
    /// <summary>The shape's radius as a fraction of the half-box (the canvas draws it at r = 8.9 in a 24-unit box).</summary>
    public const double ShapeRadius = 8.9 / 12;

    /// <summary>The mark at rest shows at this scale and strength (2.5: "80 % scale and 72 % strength").</summary>
    public const double RestScale = .8, RestStrength = .72;

    /// <summary>How far the dimmed marks (paused, stopping, stopped) fall below their rest strength (the canvas's core opacity .55).</summary>
    public const double Dim = .55;

    // spring rates (rad/s) and dampings: the shape and its strength at damping 0.8; the badge's pop a little looser
    private const double ShapeRate = 11, ShapeDamping = .8, PopRate = 16, PopDamping = .7;

    private double scale, vScale, alpha, vAlpha, pop, vPop;
    private AsturBadge badge;
    private bool settled = true;

    public AsturMarkEngine(AsturMarkLook look)
    {
        Look = look;
        Snap();
    }

    public AsturMarkLook Look { get; private set; }

    /// <summary>Whether the mark is on screen and its window awake; one that is not takes its new pose at once.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>The shape's scale now (0.8 at rest, 1 when full).</summary>
    public double Scale => scale;

    /// <summary>The shape's strength now, dimming included.</summary>
    public double Strength => alpha;

    /// <summary>How far the badge has popped in: 0 hidden, 1 shown.</summary>
    public double Pop => pop;

    /// <summary>The badge drawn now (the one leaving stays until it has shrunk away).</summary>
    public AsturBadge Badge => badge;

    /// <summary>How far the last drawing reached, as a fraction of the half-box (under 1.0: 2.5 "Never clipped").</summary>
    public double MaxExtent { get; private set; }

    // ---- the pose of a state ----

    public static (double Scale, double Strength, AsturBadge Badge) PoseOf(AsturMarkState state, bool still) =>
        (AsturMarkStates.Full(state) ? 1 : RestScale,
         (AsturMarkStates.Full(state) ? 1 : RestStrength) * (AsturMarkStates.Dimmed(state) ? Dim : 1),
         AsturMarkStates.BadgeOf(state, still));

    // ---- public ----

    public void Set(AsturMarkLook next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var was = Look;
        Look = next;
        var (_, _, target) = PoseOf(next.State, next.Reduce);
        if (next.Reduce || !Visible)
        {
            Snap();
            return;
        }
        if (target != AsturBadge.None && target != badge)
        {
            // a badge that comes in grows from nothing; one that replaces another pops again from 45 % (the canvas's re-pop)
            pop = badge != AsturBadge.None && pop > .3 && was.State != next.State ? .45 : pop;
            vPop = 0;
            badge = target;
        }
        settled = false;
    }

    /// <summary>Takes the pose of the current state at once.</summary>
    public void Snap()
    {
        var (s, a, b) = PoseOf(Look.State, Look.Reduce);
        scale = s; alpha = a; badge = b;
        pop = b == AsturBadge.None ? 0 : 1;
        vScale = vAlpha = vPop = 0;
        settled = true;
    }

    /// <summary>Advances the springs by <paramref name="dt"/> seconds. Returns whether they still move (asks for another frame).</summary>
    public bool Tick(double dt)
    {
        if (settled) return false;
        if (Look.Reduce || !Visible)
        {
            Snap();
            return false;
        }
        var (s, a, b) = PoseOf(Look.State, false);
        var popTarget = b == AsturBadge.None ? 0 : 1;
        var steps = Math.Max(1, (int)Math.Ceiling(dt * 240));
        var h = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            Step(ref scale, ref vScale, s, ShapeRate, ShapeDamping, h);
            Step(ref alpha, ref vAlpha, a, ShapeRate, ShapeDamping, h);
            Step(ref pop, ref vPop, popTarget, PopRate, PopDamping, h);
        }
        if (popTarget == 0 && pop < .02)
        {
            pop = 0; vPop = 0; badge = AsturBadge.None;
        }
        if (Math.Abs(scale - s) < 1e-3 && Math.Abs(vScale) < 1e-3 && Math.Abs(alpha - a) < 1e-3 && Math.Abs(vAlpha) < 1e-3
            && Math.Abs(pop - popTarget) < 1e-3 && Math.Abs(vPop) < 1e-3)
        {
            Snap();
            return false;
        }
        return true;
    }

    private static void Step(ref double x, ref double v, double target, double w, double z, double h)
    {
        v += (-w * w * (x - target) - 2 * z * w * v) * h;
        x += v * h;
    }

    /// <summary>Nothing moves and nothing will until the look changes: the mark leaves the clock.</summary>
    public bool Settled => settled;

    // ---- drawing ----

    public void Draw(IAsturCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var s = Look.Size;
        var hb = s / 2;
        var tint = ArmillaPoses.Tint((int)Look.Shape, Look.Dark);
        var r = hb * ShapeRadius * Math.Max(0, scale);
        var extent = 0.0;

        // the badge: a disc of the ink with the state's glyph in the surface colour, tucked into the bottom right corner
        var d = Math.Max(9, .4 * s);
        var radius = d / 2;
        var shown = badge != AsturBadge.None && pop > .02;
        var k = Math.Max(0, pop);
        var cx = hb - 1.5 - radius;
        canvas.BeginLayer(Math.Clamp(alpha, 0, 1), hb);
        if (r > .05)
        {
            canvas.FillShape(Look.Shape, 0, 0, r, tint, 1);
            extent = Math.Max(extent, r * ShapeExtent(Look.Shape));
        }
        if (shown) canvas.Cut(cx, cx, (radius + 1.25) * k);
        canvas.EndLayer();
        if (shown)
        {
            var ink = Look.Dark ? ArmillaPoses.InkDark : ArmillaPoses.InkLight;
            var inverse = Look.Dark ? Inverse.Dark : Inverse.Light;
            canvas.FillCircle(cx, cx, radius * k, ink, Math.Min(1, k * 1.4));
            extent = Math.Max(extent, cx + radius * k);
            canvas.Save();
            canvas.Translate(cx, cx);
            BadgeGlyph(canvas, badge, d * k, inverse, Math.Min(1, k * 1.4));
            canvas.Restore();
        }
        MaxExtent = extent / hb;
    }

    private static class Inverse
    {
        public static readonly ArmillaRgb Dark = new(18, 18, 20), Light = new(246, 246, 248);
    }

    /// <summary>
    /// How far a shape reaches from its centre, as a multiple of its radius, along the widest axis (the slot is square, so it is the box that
    /// counts): the squircle is .81 (half its side), the hexagon 1.02, the diamond and the star 1.18, the clover 1.0, and the rounded triangle
    /// 1.26 (its apex at 1.05 and the half of the round stroke that grows its outline).
    /// </summary>
    public static double ShapeExtent(ArmillaShape shape) => shape switch
    {
        ArmillaShape.Squircle => .81,
        ArmillaShape.Hexagon => 1.02,
        ArmillaShape.Diamond => 1.18,
        ArmillaShape.Triangle => 1.26,
        ArmillaShape.Clover => 1.0,
        ArmillaShape.Star => 1.18,
        _ => 1.0,
    };

    // The glyph of a badge, in a badge of side d, centred on the origin; every glyph is made of the calls Armilla's canvas already has.
    private static void BadgeGlyph(IAsturCanvas canvas, AsturBadge kind, double d, ArmillaRgb color, double alpha)
    {
        if (d < .5) return;
        var lw = Math.Max(1.1, .125 * d);
        switch (kind)
        {
            case AsturBadge.Check:
                canvas.Glyph(ArmillaGlyph.Done, .54 * d, lw, color, alpha);
                break;
            case AsturBadge.Bang:
                canvas.StrokeLine(0, -.27 * d, 0, .03 * d, lw, color, alpha, roundCap: false);
                canvas.FillCircle(0, .2 * d, lw * .62, color, alpha);
                break;
            case AsturBadge.Minus:
                canvas.StrokeLine(-.24 * d, 0, .24 * d, 0, lw, color, alpha, roundCap: false);
                break;
            case AsturBadge.Bars:
                canvas.StrokeLine(-.145 * d, -.23 * d, -.145 * d, .23 * d, .15 * d, color, alpha, roundCap: false);
                canvas.StrokeLine(.145 * d, -.23 * d, .145 * d, .23 * d, .15 * d, color, alpha, roundCap: false);
                break;
            case AsturBadge.Dot:
                canvas.FillCircle(0, 0, .17 * d, color, alpha);
                break;
            case AsturBadge.Dots:
                foreach (var x in new[] { -.235, 0, .235 }) canvas.FillCircle(x * d, 0, .07 * d, color, alpha);
                break;
            case AsturBadge.Hollow:
                canvas.Glyph(ArmillaGlyph.Stopping, .38 * d, Math.Max(1, .1 * d), color, alpha);
                break;
            case AsturBadge.Solid:
                canvas.Glyph(ArmillaGlyph.Stopped, .38 * d, lw, color, alpha);
                break;
        }
    }
}
