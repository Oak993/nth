// PORT-ONLY: Tactus pill v2, auto-dodge (OD-0145, ED-NEW-pcctl-13; DESIGN.md 10.3). A dodge is temporary: the chosen anchor
// never changes, and the pill returns when the reason clears. Caption buttons under the pill (plus 8 DIP) move it sideways
// toward the centre by the least amount that clears them by 12 DIP, or, if that is more than 240 DIP, down to the caption
// bar's bottom plus 6 DIP. A showing auto-hide taskbar at the top moves it below the taskbar plus 8 DIP. Never while the
// pill is dragged or hovered, never within 500 ms of the previous dodge, and a return waits until the reason has been clear
// for 300 ms. Inputs come from window events, never per frame.
namespace Marrow.Core.Port.PcControl.Pill;

/// <summary>A rectangle in DIP.</summary>
public readonly record struct PillRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public PillRect Inflate(double by) => new(X - by, Y - by, Width + (2 * by), Height + (2 * by));

    public bool Intersects(PillRect other) => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}

/// <summary>Where the pill sits while dodging, relative to its anchor (zero: at its anchor).</summary>
public readonly record struct PillDodge(double Dx, double Dy, string Reason)
{
    public static readonly PillDodge None = new(0, 0, "");

    public bool Active => Dx != 0 || Dy != 0;
}

public sealed class PillDodgePlanner
{
    /// <summary>Caption buttons this close to the pill count as under it.</summary>
    public const double Margin = 8;

    /// <summary>A sideways dodge clears the buttons by this much.</summary>
    public const double Clearance = 12;

    /// <summary>A sideways dodge longer than this goes down instead.</summary>
    public const double MaxSideways = 240;

    /// <summary>Down below the caption bar by this much.</summary>
    public const double BelowCaption = 6;

    /// <summary>Down below a showing top taskbar by this much.</summary>
    public const double BelowTaskbar = 8;

    /// <summary>No dodge within this long of the previous one (no ping-pong).</summary>
    public const double GuardMs = 500;

    /// <summary>A return waits until the reason has been clear this long.</summary>
    public const double ReturnDelayMs = 300;

    private double lastChange = double.NegativeInfinity;
    private double clearSince = double.NaN;

    /// <summary>The dodge in force.</summary>
    public PillDodge Current { get; private set; } = PillDodge.None;

    /// <summary>
    /// The dodge for a pill resting at <paramref name="pill"/> (its anchor rect) on <paramref name="work"/>: the caption
    /// buttons of the topmost window over the pill's area, or a showing top taskbar's bottom edge. Pure; no guards.
    /// </summary>
    public static PillDodge Plan(PillRect pill, PillAnchor anchor, PillArea work, IReadOnlyList<PillRect> captionButtons, double? topTaskbarBottom)
    {
        if (topTaskbarBottom is { } taskbar && taskbar + BelowTaskbar > pill.Y)
            return new PillDodge(0, taskbar + BelowTaskbar - pill.Y, "taskbar");
        var near = pill.Inflate(Margin);
        var hits = captionButtons.Where(b => b.Intersects(near)).ToList();
        if (hits.Count == 0) return PillDodge.None;
        var left = hits.Min(b => b.X);
        var right = hits.Max(b => b.Right);
        var bottom = hits.Max(b => b.Bottom);
        // Move left so the pill's right edge is 12 DIP left of the buttons, or right so its left edge is 12 DIP past them.
        var toLeft = pill.Right - (left - Clearance);
        var toRight = (right + Clearance) - pill.X;
        var fitsLeft = pill.X - toLeft >= work.Left;
        var fitsRight = pill.Right + toRight <= work.Right;
        double? move = anchor switch
        {
            PillAnchor.Right => fitsLeft ? -toLeft : null,
            PillAnchor.Left => fitsRight ? toRight : null,
            _ => (fitsLeft, fitsRight) switch
            {
                (true, true) => toLeft <= toRight ? -toLeft : toRight,
                (true, false) => -toLeft,
                (false, true) => toRight,
                _ => null,
            },
        };
        if (move is { } dx && Math.Abs(dx) <= MaxSideways) return new PillDodge(dx, 0, "caption buttons");
        return new PillDodge(0, bottom + BelowCaption - pill.Y, "caption buttons");
    }

    /// <summary>
    /// Feeds the latest plan at <paramref name="nowMs"/> and returns the dodge to show: unchanged while dragged or hovered
    /// or within 500 ms of the last change, and a return only once the reason has been clear for 300 ms.
    /// </summary>
    public PillDodge Update(double nowMs, PillDodge planned, bool dragging, bool hovered)
    {
        if (dragging || hovered) return Current;
        if (!planned.Active)
        {
            if (!Current.Active) return Current;
            if (double.IsNaN(clearSince)) clearSince = nowMs;
            if (nowMs - clearSince < ReturnDelayMs || nowMs - lastChange < GuardMs) return Current;
            return Change(nowMs, PillDodge.None);
        }
        clearSince = double.NaN;
        if (planned == Current || nowMs - lastChange < GuardMs) return Current;
        return Change(nowMs, planned);
    }

    /// <summary>The time a return becomes due, so the caller can schedule one check (no per-frame polling); null when none waits.</summary>
    public double? ReturnDueAt => Current.Active && !double.IsNaN(clearSince) ? Math.Max(clearSince + ReturnDelayMs, lastChange + GuardMs) : null;

    private PillDodge Change(double nowMs, PillDodge to)
    {
        lastChange = nowMs;
        clearSince = double.NaN;
        Current = to;
        return to;
    }
}

/// <summary>
/// Auto-expand, one flag (DESIGN.md 10.5): armed at start; a new ask expands the card once if armed; the user's Later disarms
/// it; it re-arms when every ask is answered or withdrawn. An auto-expand waits while the pill is dragged or travelling,
/// hovered, or a mouse button is held anywhere, then expands once, and never takes focus. A click always expands.
/// </summary>
public sealed class PillAutoExpand
{
    private bool pending;

    public bool Armed { get; private set; } = true;

    /// <summary>A new ask arrived: an expand is owed if armed.</summary>
    public void AskArrived()
    {
        if (Armed) pending = true;
    }

    /// <summary>The user chose Later: no ask re-expands over their work until the queue empties.</summary>
    public void Later()
    {
        Armed = false;
        pending = false;
    }

    /// <summary>The queue changed; an empty queue re-arms.</summary>
    public void QueueChanged(int waiting)
    {
        if (waiting != 0) return;
        Armed = true;
        pending = false;
    }

    /// <summary>The card opened (by a click or an auto-expand): nothing more is owed.</summary>
    public void Expanded() => pending = false;

    /// <summary>True when an owed auto-expand may run now; it then runs once (call <see cref="Expanded"/>).</summary>
    public bool ShouldExpand(bool dragging, bool travelling, bool hovered, bool buttonHeld) =>
        pending && Armed && !dragging && !travelling && !hovered && !buttonHeld;
}
