// PORT-ONLY: ED-NEW-frame-pacing. The 60 Hz decorative cap (T44) paced on the display's refreshes, never on a timer.
using System.Runtime.CompilerServices;
using Avalonia.Rendering.Composition;

namespace Marrow.UI.Port;

/// <summary>
/// PORT-ONLY (ED-NEW-frame-pacing). Decides which display refreshes are the 60 Hz slots of decorative (ambient) motion.
/// Every ambient source of one compositor asks the same pacer at each of its refresh ticks, so all of them advance on the
/// same refresh (the effort track and its knob share one render) and exactly once per 1/60 s slot: every n-th refresh when
/// 1/60 s is a whole number n of refreshes (4 at 240 Hz, 2 at 120 Hz, 1 at 60 Hz), else spread evenly over the refreshes
/// (2 or 3 at 144 and 165 Hz, never 1 or 4). The refreshes are counted on a grid locked to the ticks themselves (relative
/// counts, not DWM's vblank timestamp as an absolute clock), so a tick's timing jitter does not move a slot.
/// </summary>
internal sealed class AmbientRefreshPacer
{
    private static readonly ConditionalWeakTable<Compositor, AmbientRefreshPacer> Shared = new();

    /// <summary>The pacer every ambient source of <paramref name="compositor"/> shares (its render thread's refreshes).</summary>
    internal static AmbientRefreshPacer For(Compositor compositor) => Shared.GetValue(compositor, static _ => new AmbientRefreshPacer());

    /// <summary>A pause longer than this starts the count again (the next tick is a slot).</summary>
    internal const double ResetSeconds = .25;

    private readonly Func<TimeSpan?>? periodSource;
    private readonly object gate = new();
    private Func<TimeSpan?>? readFrom;
    private bool started, lastGo;
    private long key = long.MinValue, count, slot;
    private double period, perSlot, anchor, last, periodRead, due, distrusted = -1;
    private int early;
    private int? whole;

    /// <param name="periodSource">The display's refresh period (tests: a fixed one); null reads <see cref="RenderFrameSource.PeriodSource"/>.</param>
    internal AmbientRefreshPacer(Func<TimeSpan?>? periodSource = null) => this.periodSource = periodSource;

    /// <summary>The refresh the last tick was counted as, from the start of the current run (tests).</summary>
    internal long Refresh { get { lock (gate) return count; } }

    /// <summary>
    /// Whether the refresh tick <paramref name="tick"/> (an identity shared by all callers in one tick: the compositor's
    /// time of the tick, or a frame's time on main) at <paramref name="now"/> seconds is a 60 Hz slot. Every caller in the
    /// same tick gets the same answer. A source that runs at the display rate (an interaction) still asks, to keep the count.
    /// </summary>
    internal bool Beat(long tick, double now)
    {
        lock (gate)
        {
            if (tick == key) return lastGo;
            key = tick;
            var source = periodSource ?? RenderFrameSource.PeriodSource;
            if (!started || now < last || now - last > ResetSeconds || !ReferenceEquals(source, readFrom) || now - periodRead >= 1 && PeriodChanged(source))
            {
                Start(source, now);
                return Go();
            }
            last = now;
            if (period <= 0)
            {
                // no refresh period known: a 1/60 s grid of due times, a millisecond early at most (ED-117 (2), ED-232)
                if (now + .001 < due) return lastGo = false;
                due = now - due >= 2 * AmbientMotionClock.FrameSeconds ? now + AmbientMotionClock.FrameSeconds : due + AmbientMotionClock.FrameSeconds;
                return Go();
            }
            var n = (long)Math.Round((now - anchor) / period);
            if (n <= count)
            {
                n = count + 1; // a new tick is always a new refresh
                // Ticks that keep coming sooner than the period says are not this display's refreshes (a window on another
                // monitor): the run goes on by the 1/60 s grid of due times instead, never faster.
                if (++early >= 4) { distrusted = period; period = 0; due = now + AmbientMotionClock.FrameSeconds; return lastGo = false; }
            }
            else if (early > 0) early--;
            // Lock the grid to the ticks: a tick comes a little after its vblank, never before, so the grid follows an
            // earlier tick at once and a later one slowly (a late tick within half a refresh keeps its refresh).
            var residual = now - (anchor + n * period);
            anchor += residual < 0 ? residual : residual / 32;
            count = n;
            var at = whole is { } w ? n / w : (long)Math.Floor(n / perSlot + 1e-9);
            if (at <= slot) return lastGo = false;
            slot = at;
            return Go();
        }
    }

    private bool PeriodChanged(Func<TimeSpan?> source)
    {
        periodRead = last;
        var read = Seconds(source);
        return read != period && read != distrusted;
    }

    private static double Seconds(Func<TimeSpan?> source) => source() is { TotalSeconds: > 0 } value ? value.TotalSeconds : 0;

    private void Start(Func<TimeSpan?> source, double now)
    {
        readFrom = source;
        period = Seconds(source);
        distrusted = -1; early = 0;
        whole = period > 0 ? AnimationTimeline.RefreshesPer(AmbientMotionClock.FrameSeconds, TimeSpan.FromSeconds(period)) : null;
        perSlot = period > 0 ? AmbientMotionClock.FrameSeconds / period : 1;
        started = true;
        anchor = last = periodRead = now;
        due = now + AmbientMotionClock.FrameSeconds;
        count = slot = 0;
        beats = 0;
    }

    // ---- Calculi WU4 (ED-NEW-calculi-wu4-1): the 30 Hz divisor. Kept apart so the 60 Hz pacing above reads as before. ----

    private long beats; // the 60 Hz slots of the current run, the first one being 1

    private bool Go()
    {
        beats++;
        return lastGo = true;
    }

    /// <summary>
    /// Whether the refresh tick is a slot of a client that runs at 60 / <paramref name="every"/> Hz (2: 30 Hz): it takes every
    /// <paramref name="every"/>-th 60 Hz slot of the run, the run's first slot included, and advances at that slot's time. Every
    /// such client of one compositor gets the same slots (one shared parity), and asking still keeps the shared count.
    /// </summary>
    internal bool Beat(long tick, double now, int every)
    {
        lock (gate)
        {
            if (!Beat(tick, now)) return false;
            return every <= 1 || (beats - 1) % every == 0;
        }
    }
}

/// <summary>
/// PORT-ONLY (ED-NEW-frame-pacing, ED-NEW-lean-9). The seam to the shared pacer for an ambient source outside Marrow.UI (the
/// composer's working dots in Marrow.App): <see cref="AmbientRefreshPacer.Beat"/> of the same per-compositor pacer every ambient
/// source of that compositor asks, so the dots advance on the same 60 Hz slots as the rest (never a private copy).
/// </summary>
public static class AmbientRefreshPacing
{
    /// <summary>The shared pacer of <paramref name="compositor"/>: Beat(tick, now) is whether that refresh tick is a 60 Hz slot.</summary>
    public static Func<long, double, bool> For(Compositor compositor) => AmbientRefreshPacer.For(compositor).Beat;
}
