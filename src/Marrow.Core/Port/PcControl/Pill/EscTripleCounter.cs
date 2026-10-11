// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-12 item 2, ED-NEW-pcctl-15; DESIGN.md 10.2 path 5). Esc three
// times within 1 s stops all, the first press while the foreground is a Tactus window (one with a Tactus step this turn, or
// one a step opened) or nth's overlay. Only physical Esc key-downs count: auto-repeats and injected keys (Tactus's own
// included) neither count nor reset; an Esc an IME consumes and any other physical key reset the count. The counter only
// observes: every Esc still reaches the app.
namespace Marrow.Core.Port.PcControl.Pill;

/// <summary>One key-down as the low-level hook reports it: whether it is Esc, an auto-repeat, injected, eaten by an IME, and whether the foreground is a Tactus window.</summary>
public readonly record struct PcKeyDown(bool Escape, bool Repeat, bool Injected, bool ImeConsumed, bool InTactusWindow);

public sealed class EscTripleCounter
{
    public const int WindowMs = 1000;
    public const int Presses = 3;

    private long first;
    private int count;

    /// <summary>The Escs counted so far in the current second.</summary>
    public int Count => count;

    /// <summary>Counts one key-down at <paramref name="nowMs"/>; true when it is the third Esc (and the count starts over).</summary>
    public bool Key(long nowMs, PcKeyDown key)
    {
        if (key.Repeat || key.Injected) return false;
        if (!key.Escape || key.ImeConsumed)
        {
            count = 0;
            return false;
        }
        if (count > 0 && nowMs - first > WindowMs) count = 0;
        if (count == 0)
        {
            if (!key.InTactusWindow) return false;
            first = nowMs;
        }
        count++;
        if (count < Presses) return false;
        count = 0;
        return true;
    }
}
