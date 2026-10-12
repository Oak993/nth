// PORT-ONLY: Tactus pill v2, the pill's or card's shape as a function of time (OD-0145, ED-NEW-pcctl-23; DESIGN.md 10.3
// "Grab in flight", 10.6 "Hit testing"). One immutable timeline per change: the top-left from a travel (or a fixed point),
// the width and height from their springs (or fixed values), the 24 DIP corner, in the DIP of the monitor that holds the
// shape. Rendering, the region at rest, the style-flip fallback and the grab in flight all read this one curve, so what is
// drawn, what takes input and what a press in flight grabs are the same shape at the same moment. Pure: no Avalonia, no
// Win32, nothing mutable, so the low-level hook's callback and the reader thread can read it while the UI thread
// publishes the next one.
namespace Marrow.Core.Port.PcControl.Pill;

/// <summary>The shape's top-left, width and height over time (seconds on the pill's one clock).</summary>
public sealed class PillShape
{
    /// <summary>The corner radius of the pill and the card (canvas: one clip, round 24 px).</summary>
    public const double Radius = 24;

    /// <summary>A region and a hit test take the rest shape inflated by this much (DIP).</summary>
    public const double HitInflate = 2;

    private readonly PillTravel? travel;
    private readonly PillVector fixedAt;
    private readonly PillCurve? width;
    private readonly PillCurve? height;
    private readonly double fixedWidth;
    private readonly double fixedHeight;

    private PillShape(PillTravel? travel, PillVector fixedAt, PillCurve? width, double fixedWidth, PillCurve? height, double fixedHeight)
    {
        this.travel = travel;
        this.fixedAt = fixedAt;
        this.width = width;
        this.fixedWidth = fixedWidth;
        this.height = height;
        this.fixedHeight = fixedHeight;
    }

    /// <summary>A shape at rest.</summary>
    public static PillShape Resting(PillRect rect) => new(null, new PillVector(rect.X, rect.Y), null, rect.Width, null, rect.Height);

    /// <summary>A shape whose top-left follows <paramref name="travel"/> and whose size follows the given curves (or stays).</summary>
    public static PillShape Moving(PillTravel travel, PillCurve? width, double fixedWidth, PillCurve? height, double fixedHeight) =>
        new(travel, PillVector.Zero, width, fixedWidth, height, fixedHeight);

    /// <summary>A shape held in place whose size changes (a width change, a morph).</summary>
    public static PillShape Sizing(PillVector at, PillCurve? width, double fixedWidth, PillCurve? height, double fixedHeight) =>
        new(null, at, width, fixedWidth, height, fixedHeight);

    /// <summary>True when nothing about the shape changes with time.</summary>
    public bool AtRest => travel is null && width is null && height is null;

    /// <summary>The shape's rectangle at <paramref name="now"/>.</summary>
    public PillRect At(double now)
    {
        var at = travel?.Value(now) ?? fixedAt;
        return new PillRect(at.X, at.Y, width?.Value(now) ?? fixedWidth, height?.Value(now) ?? fixedHeight);
    }

    /// <summary>
    /// True when every part of the shape is visually settled at <paramref name="now"/> (within 1 DIP and under 20 DIP/s for
    /// the travel; the size springs settled): the region may be set, policies may run.
    /// </summary>
    public bool Settled(double now) => (travel?.VisuallySettled(now) ?? true) && (width?.Settled(now) ?? true) && (height?.Settled(now) ?? true);

    /// <summary>The shape's final rectangle (where it rests once every curve ends).</summary>
    public PillRect Final => new(travel?.Target.X ?? fixedAt.X, travel?.Target.Y ?? fixedAt.Y, width?.To ?? fixedWidth, height?.To ?? fixedHeight);

    /// <summary>
    /// True when <paramref name="point"/> (DIP, the shape's monitor) is inside the rounded shape at <paramref name="now"/>,
    /// inflated by <paramref name="inflate"/>. Allocation-free, for the hook and the flip thread.
    /// </summary>
    public bool Contains(PillVector point, double now, double inflate = 0) => RoundedContains(At(now).Inflate(inflate), Radius + inflate, point);

    /// <summary>True when <paramref name="point"/> is inside <paramref name="rect"/> with corners of <paramref name="radius"/>.</summary>
    public static bool RoundedContains(PillRect rect, double radius, PillVector point)
    {
        if (point.X < rect.X || point.X >= rect.Right || point.Y < rect.Y || point.Y >= rect.Bottom) return false;
        var r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
        var cx = Math.Clamp(point.X, rect.X + r, rect.Right - r);
        var cy = Math.Clamp(point.Y, rect.Y + r, rect.Bottom - r);
        var dx = point.X - cx;
        var dy = point.Y - cy;
        return (dx * dx) + (dy * dy) <= r * r;
    }
}

/// <summary>Which band height a state needs (DESIGN.md 10.6 "One overlay per monitor").</summary>
public enum PillBandState
{
    /// <summary>The pill alone: 120 DIP (shadow and rubber-band room).</summary>
    Pill,

    /// <summary>While dragging: 208 DIP (the 120 DIP downward rubber band).</summary>
    Dragging,

    /// <summary>A card: its height plus 160 DIP.</summary>
    Card,
}

/// <summary>When a band's height change runs against a morph: before it when growing, after it when shrinking, never per frame.</summary>
public enum PillBandResize
{
    None,
    BeforeMotion,
    AfterMotion,
}

/// <summary>
/// The band across the top of a monitor's work area that holds the pill: the work area's full width (so horizontal travel
/// never moves the window) and the height its state needs, changed with one window call at a state change.
/// </summary>
public static class PillBand
{
    public const double PillHeight = 120;
    public const double DragHeight = 208;
    public const double CardRoom = 160;

    /// <summary>Room kept below the pill's bottom edge for its shadow (the canvas's 26 px blur, 10 px down).</summary>
    public const double ShadowRoom = 48;

    /// <summary>
    /// The band's height in DIP for <paramref name="state"/>, at least the pill's lowest point below the work area's top
    /// (<paramref name="pillBottom"/>, a dodge below a caption bar or a top taskbar) plus its shadow room.
    /// </summary>
    public static double Height(PillBandState state, double cardHeight = 0, double pillBottom = 0)
    {
        var height = state switch
        {
            PillBandState.Dragging => DragHeight,
            PillBandState.Card => Math.Max(PillHeight, cardHeight + CardRoom),
            _ => PillHeight,
        };
        return Math.Max(height, pillBottom + ShadowRoom);
    }

    /// <summary>When a change from <paramref name="current"/> to <paramref name="next"/> DIP runs: grow first, shrink last.</summary>
    public static PillBandResize Plan(double current, double next) =>
        next > current ? PillBandResize.BeforeMotion : next < current ? PillBandResize.AfterMotion : PillBandResize.None;

    /// <summary>The band in DIP of its monitor: the work area's left and top, its full width, the given height.</summary>
    public static PillRect Rect(PillArea work, double height) => new(work.Left, work.Top, work.Width, Math.Min(height, work.Height));
}
