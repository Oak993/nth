// PORT-ONLY: Dextra (DESIGN.md rev 5, 3.2, 12.6b; unit AS-1; ED-NEW-dextracore-5). No Swift twin.
// The task and head states, their labels, the two control slots of each state and the legal moves between states.
// One model and one set of words for every task list (the Board, the home's Result list, Background tasks, the pill).
namespace Marrow.Core.Port.Assistant;

/// <summary>A mission's (a task's) state (3.2, 12.6b).</summary>
public enum MissionState { Planning, NeedsYou, Working, Reviewing, Pausing, Paused, Stopping, Stopped, Blocked, Done, Failed }

/// <summary>A Hydra head's state inside a mission (12.6b). Queued is a head's only.</summary>
public enum HeadState { Queued, Working, Pausing, Paused, Stopping, Stopped, Blocked, Done }

/// <summary>What slot A holds: Pause, Resume or Restart.</summary>
public enum SlotA { None, Pause, Resume, Restart }

/// <summary>What slot B holds: Stop or Revert changes.</summary>
public enum SlotB { None, Stop, RevertChanges }

/// <summary>
/// The two fixed control slots of a row (12.6b). A control with nothing to do is disabled, never removed, so nothing
/// shifts; a Done card drops both slots (None).
/// </summary>
public sealed record ControlSlots(SlotA A, bool AEnabled, SlotB B, bool BEnabled);

/// <summary>The Board's groups (3.2), in the order they show: whatever waits on the owner first.</summary>
public enum BoardGroup { NeedsYou, Working, DoneToday }

/// <summary>Labels, slots and legal moves (12.6b).</summary>
public static class MissionStates
{
    // U+2026 HORIZONTAL ELLIPSIS, as in "Pausing..." and "Stopping...".
    private const string Ellipsis = "\U00002026";

    public static string Label(MissionState state) => state switch
    {
        MissionState.Planning => "Planning",
        MissionState.NeedsYou => "Needs you",
        MissionState.Working => "Working",
        MissionState.Reviewing => "Reviewing",
        MissionState.Pausing => "Pausing" + Ellipsis,
        MissionState.Paused => "Paused",
        MissionState.Stopping => "Stopping" + Ellipsis,
        MissionState.Stopped => "Stopped",
        MissionState.Blocked => "Blocked",
        MissionState.Done => "Done",
        MissionState.Failed => "Failed",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static string Label(HeadState state) => state switch
    {
        HeadState.Queued => "Queued",
        HeadState.Working => "Working",
        HeadState.Pausing => "Pausing" + Ellipsis,
        HeadState.Paused => "Paused",
        HeadState.Stopping => "Stopping" + Ellipsis,
        HeadState.Stopped => "Stopped",
        HeadState.Blocked => "Blocked",
        HeadState.Done => "Done",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <summary>A turn may be running: the task counts as running (Planning, Working, Reviewing).</summary>
    public static bool IsRunning(MissionState state) => state is MissionState.Planning or MissionState.Working or MissionState.Reviewing;

    /// <summary>Still live: not Stopping, Stopped or Done.</summary>
    public static bool IsLive(MissionState state) => state is not (MissionState.Stopping or MissionState.Stopped or MissionState.Done);

    /// <summary>Whether Pause can act on a task in this state ("Pause all" shows while any task can still pause).</summary>
    public static bool CanPause(MissionState state) =>
        state is MissionState.Planning or MissionState.Working or MissionState.Reviewing or MissionState.NeedsYou or MissionState.Blocked;

    /// <summary>The slots of a task's row or card (12.6b's table).</summary>
    public static ControlSlots Slots(MissionState state, bool changedFiles, bool reverted) => state switch
    {
        MissionState.Planning or MissionState.Working or MissionState.Reviewing or MissionState.NeedsYou or MissionState.Blocked =>
            new ControlSlots(SlotA.Pause, true, SlotB.Stop, true),
        MissionState.Pausing or MissionState.Paused => new ControlSlots(SlotA.Resume, true, SlotB.Stop, true),
        MissionState.Failed => new ControlSlots(SlotA.Restart, true, SlotB.Stop, true),
        MissionState.Stopping => new ControlSlots(SlotA.Resume, false, SlotB.Stop, false),
        MissionState.Stopped => reverted
            ? new ControlSlots(SlotA.Restart, true, SlotB.RevertChanges, false)
            : new ControlSlots(SlotA.Resume, true, SlotB.RevertChanges, changedFiles),
        MissionState.Done => new ControlSlots(SlotA.None, false, SlotB.None, false),
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <summary>The slots of a head's line. Heads have no Revert; a head a reserve holds cannot be resumed by hand.</summary>
    public static ControlSlots Slots(HeadState state, bool heldByReserve = false) => state switch
    {
        HeadState.Queued or HeadState.Working or HeadState.Blocked => new ControlSlots(SlotA.Pause, true, SlotB.Stop, true),
        HeadState.Pausing => new ControlSlots(SlotA.Resume, true, SlotB.Stop, true),
        HeadState.Paused => new ControlSlots(SlotA.Resume, !heldByReserve, SlotB.Stop, true),
        HeadState.Stopping => new ControlSlots(SlotA.Resume, false, SlotB.Stop, false),
        HeadState.Stopped => new ControlSlots(SlotA.Resume, !heldByReserve, SlotB.Stop, false),
        HeadState.Done => new ControlSlots(SlotA.None, false, SlotB.None, false),
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <summary>
    /// The Board group of a task (3.2). Pausing, Paused, Stopping and Stopped stay in the group of the state they came
    /// from; a reserve's pause goes to Needs you.
    /// </summary>
    public static BoardGroup Group(MissionState state, MissionState? cameFrom = null, bool pausedByReserve = false)
    {
        if (state == MissionState.Paused && pausedByReserve) return BoardGroup.NeedsYou;
        if (state is MissionState.Pausing or MissionState.Paused or MissionState.Stopping or MissionState.Stopped)
            return cameFrom is { } from && from is not (MissionState.Pausing or MissionState.Paused or MissionState.Stopping or MissionState.Stopped)
                ? Group(from)
                : BoardGroup.Working;
        return state switch
        {
            MissionState.NeedsYou or MissionState.Blocked or MissionState.Failed => BoardGroup.NeedsYou,
            MissionState.Done => BoardGroup.DoneToday,
            _ => BoardGroup.Working,
        };
    }

    /// <summary>Paused by the owner, Stopping and Stopped show dimmed in their group (3.2).</summary>
    public static bool Dimmed(MissionState state, bool pausedByReserve = false) =>
        (state == MissionState.Paused && !pausedByReserve) || state is MissionState.Stopping or MissionState.Stopped;

    private static readonly Dictionary<MissionState, MissionState[]> MissionMoves = new()
    {
        [MissionState.Planning] = [MissionState.NeedsYou, MissionState.Working, MissionState.Pausing, MissionState.Paused, MissionState.Stopping, MissionState.Stopped, MissionState.Blocked, MissionState.Failed],
        [MissionState.NeedsYou] = [MissionState.Planning, MissionState.Working, MissionState.Reviewing, MissionState.Pausing, MissionState.Paused, MissionState.Stopping, MissionState.Stopped, MissionState.Blocked],
        [MissionState.Working] = [MissionState.NeedsYou, MissionState.Reviewing, MissionState.Pausing, MissionState.Paused, MissionState.Stopping, MissionState.Stopped, MissionState.Blocked, MissionState.Failed],
        [MissionState.Reviewing] = [MissionState.Working, MissionState.NeedsYou, MissionState.Pausing, MissionState.Paused, MissionState.Stopping, MissionState.Stopped, MissionState.Done, MissionState.Failed],
        [MissionState.Pausing] = [MissionState.Paused, MissionState.Planning, MissionState.Working, MissionState.Reviewing, MissionState.NeedsYou, MissionState.Stopping, MissionState.Stopped],
        [MissionState.Paused] = [MissionState.Planning, MissionState.Working, MissionState.Reviewing, MissionState.NeedsYou, MissionState.Blocked, MissionState.Stopping, MissionState.Stopped],
        [MissionState.Stopping] = [MissionState.Stopped],
        [MissionState.Stopped] = [MissionState.Planning, MissionState.Working, MissionState.Reviewing, MissionState.NeedsYou, MissionState.Blocked, MissionState.Failed],
        [MissionState.Blocked] = [MissionState.Planning, MissionState.Working, MissionState.NeedsYou, MissionState.Paused, MissionState.Stopping, MissionState.Stopped, MissionState.Failed],
        [MissionState.Failed] = [MissionState.Planning, MissionState.Stopping, MissionState.Stopped],
        [MissionState.Done] = [],
    };

    private static readonly Dictionary<HeadState, HeadState[]> HeadMoves = new()
    {
        [HeadState.Queued] = [HeadState.Working, HeadState.Paused, HeadState.Stopping, HeadState.Stopped, HeadState.Blocked],
        [HeadState.Working] = [HeadState.Pausing, HeadState.Paused, HeadState.Stopping, HeadState.Stopped, HeadState.Blocked, HeadState.Done],
        [HeadState.Pausing] = [HeadState.Paused, HeadState.Working, HeadState.Stopping, HeadState.Stopped],
        [HeadState.Paused] = [HeadState.Queued, HeadState.Working, HeadState.Blocked, HeadState.Stopping, HeadState.Stopped],
        [HeadState.Stopping] = [HeadState.Stopped],
        [HeadState.Stopped] = [HeadState.Queued, HeadState.Working, HeadState.Blocked],
        [HeadState.Blocked] = [HeadState.Queued, HeadState.Working, HeadState.Paused, HeadState.Stopping, HeadState.Stopped],
        [HeadState.Done] = [],
    };

    /// <summary>Whether a task may move from one state to another (12.6b). Staying in a state is not a move.</summary>
    public static bool IsLegal(MissionState from, MissionState to) => MissionMoves[from].Contains(to);

    /// <summary>Whether a head may move from one state to another (12.6b).</summary>
    public static bool IsLegal(HeadState from, HeadState to) => HeadMoves[from].Contains(to);
}
