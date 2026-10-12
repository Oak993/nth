// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.5 "Render budget" and "The Avalonia port"; the canvas's hub; ED-NEW-dextra-as8-2).
// No Swift twin.
// The one shared clock of every Armilla mark on a compositor: each mark's handler asks it for the time at its animation frame,
// and every mark in one compositor tick gets the same time, so every thinking mark scans in step. A step is capped at 50 ms
// (the canvas's hub), so a mark that wakes after a long sleep does not jump. Marks with nothing moving stop asking (their
// handler leaves the clock until their state changes).
using System.Runtime.CompilerServices;
using Avalonia.Rendering.Composition;

namespace Marrow.UI.Port.Armilla;

internal sealed class ArmillaHub
{
    private static readonly ConditionalWeakTable<Compositor, ArmillaHub> shared = new();
    private readonly Lock gate = new();
    private long lastTick = long.MinValue;
    private double lastNow = double.NaN;
    private double time;

    /// <summary>The longest step of the shared clock (the canvas's hub caps a frame at 50 ms).</summary>
    public const double MaxStep = .05;

    /// <summary>The clock every mark of <paramref name="compositor"/> shares.</summary>
    public static ArmillaHub For(Compositor compositor) => shared.GetValue(compositor, static _ => new ArmillaHub());

    /// <summary>
    /// The shared time (s) at the compositor tick <paramref name="tick"/> (an identity shared by every caller in one tick), when the
    /// clock reads <paramref name="now"/> seconds. The first caller of a tick advances it; the others get the same value.
    /// </summary>
    public double Time(long tick, double now)
    {
        lock (gate)
        {
            if (tick == lastTick) return time;
            if (!double.IsNaN(lastNow)) time += Math.Clamp(now - lastNow, 0, MaxStep);
            lastNow = now;
            lastTick = tick;
            return time;
        }
    }
}
