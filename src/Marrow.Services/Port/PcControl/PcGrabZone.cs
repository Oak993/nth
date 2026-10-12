// PORT-ONLY: Tactus pill v2, the grab in flight (OD-0145, ED-NEW-pcctl-23; DESIGN.md 10.3 "Grab in flight", 10.6 "Hit
// testing" 1). With the region hit test the moving pill takes no input (its region is empty while it travels, morphs or
// dodges, because a region that lags the drawn shape would take clicks meant for the app beneath). A mouse press on the
// moving shape is still the user's grab: the low-level mouse hook asks this zone, at the record's own time, whether the
// press lands inside the shape the pill presents at that moment (the same closed-form curve the overlay draws). A press
// inside is swallowed, pair-aware, so the app beneath never sees it, and the drag then runs from the hook's records on the
// reader thread until that button's up. Pen and touch never grab a moving pill (they pass through); once it rests the
// region takes them natively. The zone is a published immutable snapshot, so the hook reads it without a lock or an
// allocation while the UI thread publishes the next.
using System.Diagnostics;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Services.Port.PcControl;

/// <summary>What the mouse hook asks about a physical press: is it on the moving pill right now?</summary>
public interface IPcGrabZone
{
    /// <summary>
    /// True when the physical press at (<paramref name="x"/>, <paramref name="y"/>) screen pixels, at the hook's
    /// <paramref name="tick"/> (GetTickCount ms), lands on the moving pill or card. Allocation-free.
    /// </summary>
    bool Contains(int x, int y, uint tick);
}

/// <summary>The moving shape the hook tests, on one monitor (its bounds in physical pixels and its scale).</summary>
public sealed class PcGrabZone : IPcGrabZone
{
    /// <summary>The pen and touch signature Windows puts in a promoted mouse event's extra info (high 24 bits).</summary>
    public const ulong PenTouchSignature = 0xFF515700;

    public const ulong PenTouchMask = 0xFFFFFF00;

    private Snapshot? current;

    /// <summary>True when <paramref name="extra"/> marks a mouse event Windows promoted from pen or touch.</summary>
    public static bool FromPenOrTouch(ulong extra) => (extra & PenTouchMask) == PenTouchSignature;

    /// <summary>
    /// Publishes the moving <paramref name="shape"/> in DIP of the monitor at <paramref name="monitor"/> (physical pixels,
    /// <paramref name="scale"/> pixels per DIP). <paramref name="secondsNow"/> is the pill clock at
    /// <paramref name="tickNow"/> (GetTickCount ms), so a record's tick maps onto the curve's time.
    /// </summary>
    public void Publish(PillShape shape, PcRect monitor, double scale, double secondsNow, uint tickNow) =>
        Volatile.Write(ref current, shape.AtRest ? null : new Snapshot(shape, monitor, scale, secondsNow, tickNow));

    /// <summary>The pill rests, hides or has no shape: nothing grabs (at rest the region takes input natively).</summary>
    public void Clear() => Volatile.Write(ref current, null);

    /// <summary>The shape published now, for tests and the reader.</summary>
    public bool Active => Volatile.Read(ref current) is not null;

    public bool Contains(int x, int y, uint tick)
    {
        var snapshot = Volatile.Read(ref current);
        if (snapshot is null) return false;
        var seconds = snapshot.Seconds + (unchecked((int)(tick - snapshot.Tick)) / 1000.0);
        var point = new PillVector((x - snapshot.Monitor.X) / snapshot.Scale, (y - snapshot.Monitor.Y) / snapshot.Scale);
        return snapshot.Shape.Contains(point, seconds);
    }

    /// <summary>The pill's clock: Stopwatch seconds, the clock every pill curve starts on.</summary>
    public static double ClockSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private sealed record Snapshot(PillShape Shape, PcRect Monitor, double Scale, double Seconds, uint Tick);
}
