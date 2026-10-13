// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.1 "Sidebar", 2.5 "An Astur's own mark", 9.4 "Org chart (Team tab)"; the canvas's asState, work/design/assistant/
// index.html lines 2262-2271; unit AS-17; ED-NEW-dextra-as17-3). No Swift twin.
// What an Astur is doing, as the sidebar row, the org chart's node and its mark say it: one of seven presences, worked out from the states of its
// missions and whether a decision of its own waits on the owner, and the words that go with each. The org chart's status chip and the Astur's
// corner badge both read it, so the two never disagree. Pure: no clock, no I/O.
namespace Marrow.Core.Port.Assistant;

/// <summary>What an Astur is doing now (the canvas's asState, with Thinking told apart from Working).</summary>
public enum AsturPresence { Idle, Thinking, Working, Waiting, Blocked, Paused, Stopped }

public static class AsturPresences
{
    /// <summary>
    /// The presence of one Astur from the states of its missions. What waits on the owner comes first (a decision of its own, or a task
    /// that needs him), as it does for Dextra's own mark; then what works, and what only plans or reviews thinks; then what is blocked or
    /// failed; then what is paused; then what is stopped (or stopping). Finished tasks count for nothing. No task at all is Idle.
    /// </summary>
    public static AsturPresence Of(IEnumerable<MissionState> missions, bool waitingOnOwner = false)
    {
        ArgumentNullException.ThrowIfNull(missions);
        var any = new HashSet<MissionState>(missions.Where(s => s != MissionState.Done));
        if (waitingOnOwner || any.Contains(MissionState.NeedsYou)) return AsturPresence.Waiting;
        if (any.Contains(MissionState.Working) || any.Contains(MissionState.Pausing)) return AsturPresence.Working;
        if (any.Contains(MissionState.Planning) || any.Contains(MissionState.Reviewing)) return AsturPresence.Thinking;
        if (any.Contains(MissionState.Blocked) || any.Contains(MissionState.Failed)) return AsturPresence.Blocked;
        if (any.Contains(MissionState.Paused)) return AsturPresence.Paused;
        if (any.Contains(MissionState.Stopping) || any.Contains(MissionState.Stopped)) return AsturPresence.Stopped;
        return AsturPresence.Idle;
    }

    /// <summary>Working or thinking: the Astur's node on Dextra's orbit brightens and points a stem back to the centre (2.5 "Busy").</summary>
    public static bool Busy(AsturPresence presence) => presence is AsturPresence.Working or AsturPresence.Thinking;

    /// <summary>The words of the status chip and of a screen reader ("Needs you", never colour alone).</summary>
    public static string Label(AsturPresence presence) => presence switch
    {
        AsturPresence.Idle => "Idle",
        AsturPresence.Thinking => "Thinking",
        AsturPresence.Working => "Working",
        AsturPresence.Waiting => "Needs you",
        AsturPresence.Blocked => "Blocked",
        AsturPresence.Paused => "Paused",
        AsturPresence.Stopped => "Stopped",
        _ => throw new ArgumentOutOfRangeException(nameof(presence)),
    };
}
