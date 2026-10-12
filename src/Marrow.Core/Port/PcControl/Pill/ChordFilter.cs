// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-12, ED-NEW-pcctl-15; DESIGN.md 10.1). Which presses of the pause
// chord count: never an auto-repeat (RegisterHotKey runs with MOD_NOREPEAT, and the hook drops repeats too), never a chord
// whose keys were injected (no program can pause or resume Tactus by sending keys), and presses under 400 ms apart count
// once. What a counted press does: it resumes only when every live app is paused (or, with nothing live, when Pause all is
// on); otherwise it pauses all, so nothing paused by hand resumes unseen.
namespace Marrow.Core.Port.PcControl.Pill;

public sealed class ChordFilter
{
    /// <summary>Presses closer than this to the one before count once.</summary>
    public const int DebounceMs = 400;

    private long? last;

    /// <summary>Whether a press at <paramref name="nowMs"/> counts.</summary>
    public bool Accept(long nowMs, bool repeat, bool injected)
    {
        if (repeat || injected) return false;
        var counts = last is not { } before || nowMs - before >= DebounceMs;
        last = nowMs;
        return counts;
    }

    /// <summary>The pause chord's choice: true resumes all, false pauses all.</summary>
    public static bool Resumes(int live, int paused, bool latched) => live > 0 ? paused == live : latched;
}
