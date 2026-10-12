// PORT-ONLY: Tactus, the hands-off ring's fill (ED-NEW-pcctl-38; DESIGN.md 4.5 "Hands-off behaviour": "A ring around the
// Cuspis mark fills while the user stays still ... Any physical input empties the ring smoothly. It never snaps."; canvas
// pc-control-ui `ringTick`). The fill follows the gate's stillness up at once (its speed is the threshold's), drains
// toward a lower stillness at most one full ring per 260 ms, and fills the rest in 90 ms once the borrow runs. A frame
// longer than 64 ms counts as 64 ms (a stalled frame drains no more than one ordinary frame would). Pure.
namespace Marrow.Core.Port.PcControl.Pill;

public sealed class HandsRing
{
    /// <summary>A full ring drains in this long (canvas `dt/260`).</summary>
    public const double DrainSeconds = 0.26;

    /// <summary>Once the borrow runs, a ring fills the rest in this long (canvas `dt/90`).</summary>
    public const double RunSeconds = 0.09;

    /// <summary>The longest step one frame may take (canvas `Math.min(64, ...)`).</summary>
    public const double MaxStepSeconds = 0.064;

    public double Fill { get; private set; }

    /// <summary>
    /// One frame <paramref name="seconds"/> after the last: <paramref name="stillness"/> is the gate's (0 to 1), and
    /// <paramref name="running"/> whether the borrow holds the user's input now. Returns the fill.
    /// </summary>
    public double Next(double stillness, double seconds, bool running)
    {
        var step = Math.Clamp(double.IsFinite(seconds) ? seconds : 0, 0, MaxStepSeconds);
        if (running)
        {
            Fill = Math.Min(1, Fill + (step / RunSeconds));
            return Fill;
        }
        var target = double.IsFinite(stillness) ? Math.Clamp(stillness, 0, 1) : 0;
        Fill = target >= Fill ? target : Math.Max(target, Fill - (step / DrainSeconds));
        return Fill;
    }

    /// <summary>A new request starts empty.</summary>
    public void Reset() => Fill = 0;
}
