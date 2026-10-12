using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-1 (DESIGN.md rev 5, 3.2, 12.6b; ED-NEW-dextracore-5): one set of words and slots for every task list, and
// the legal and illegal moves between task and head states, written out literally.
public sealed class MissionStateTests
{
    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-5")]
    public void Labels_are_the_designs_words()
    {
        Assert.Equal("Needs you", MissionStates.Label(MissionState.NeedsYou));
        Assert.Equal("Pausing\U00002026", MissionStates.Label(MissionState.Pausing)); // U+2026 HORIZONTAL ELLIPSIS
        Assert.Equal("Stopping\U00002026", MissionStates.Label(MissionState.Stopping)); // U+2026 HORIZONTAL ELLIPSIS
        Assert.Equal("Stopped", MissionStates.Label(MissionState.Stopped));
        Assert.Equal("Queued", MissionStates.Label(HeadState.Queued));
        Assert.Equal("Pausing\U00002026", MissionStates.Label(HeadState.Pausing)); // U+2026 HORIZONTAL ELLIPSIS
        foreach (var state in Enum.GetValues<MissionState>()) Assert.False(string.IsNullOrEmpty(MissionStates.Label(state)));
        foreach (var state in Enum.GetValues<HeadState>()) Assert.False(string.IsNullOrEmpty(MissionStates.Label(state)));
    }

    // Each line: the state, slot A and whether it is enabled, slot B and whether it is enabled (12.6b's table).
    private const string TaskSlots = """
        Planning Pause+ Stop+
        NeedsYou Pause+ Stop+
        Working Pause+ Stop+
        Reviewing Pause+ Stop+
        Pausing Resume+ Stop+
        Paused Resume+ Stop+
        Stopping Resume- Stop-
        Stopped Resume+ RevertChanges+
        Blocked Pause+ Stop+
        Done None- None-
        Failed Restart+ Stop+
        """;

    private static string Show(ControlSlots slots) =>
        $"{slots.A}{(slots.AEnabled ? '+' : '-')} {slots.B}{(slots.BEnabled ? '+' : '-')}";

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-5")]
    public void Each_task_state_has_the_designs_two_slots()
    {
        var lines = TaskSlots.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(Enum.GetValues<MissionState>().Length, lines.Length);
        foreach (var line in lines)
        {
            var cells = line.Split(' ', 2);
            var state = Enum.Parse<MissionState>(cells[0]);
            Assert.Equal(cells[1], Show(MissionStates.Slots(state, changedFiles: true, reverted: false)));
        }
        // Stopped with nothing changed: Revert is disabled, not removed. After a revert: Restart, Revert off.
        Assert.Equal("Resume+ RevertChanges-", Show(MissionStates.Slots(MissionState.Stopped, changedFiles: false, reverted: false)));
        Assert.Equal("Restart+ RevertChanges-", Show(MissionStates.Slots(MissionState.Stopped, changedFiles: false, reverted: true)));
        // Revert never shows on any other state.
        foreach (var state in Enum.GetValues<MissionState>().Where(s => s != MissionState.Stopped))
            Assert.NotEqual(SlotB.RevertChanges, MissionStates.Slots(state, changedFiles: true, reverted: false).B);
    }

    private const string HeadSlots = """
        Queued Pause+ Stop+
        Working Pause+ Stop+
        Pausing Resume+ Stop+
        Paused Resume+ Stop+
        Stopping Resume- Stop-
        Stopped Resume+ Stop-
        Blocked Pause+ Stop+
        Done None- None-
        """;

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-5")]
    public void Each_head_state_has_its_slots_and_heads_never_revert()
    {
        var lines = HeadSlots.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(Enum.GetValues<HeadState>().Length, lines.Length);
        foreach (var line in lines)
        {
            var cells = line.Split(' ', 2);
            var state = Enum.Parse<HeadState>(cells[0]);
            Assert.Equal(cells[1], Show(MissionStates.Slots(state)));
            Assert.NotEqual(SlotB.RevertChanges, MissionStates.Slots(state).B);
        }
        Assert.Equal("Resume- Stop+", Show(MissionStates.Slots(HeadState.Paused, heldByReserve: true)));
    }

    // Each line: a state, then every state it may move to. Anything not listed is illegal.
    private const string TaskMoves = """
        Planning: NeedsYou Working Pausing Paused Stopping Stopped Blocked Failed
        NeedsYou: Planning Working Reviewing Pausing Paused Stopping Stopped Blocked
        Working: NeedsYou Reviewing Pausing Paused Stopping Stopped Blocked Failed
        Reviewing: Working NeedsYou Pausing Paused Stopping Stopped Done Failed
        Pausing: Paused Planning Working Reviewing NeedsYou Stopping Stopped
        Paused: Planning Working Reviewing NeedsYou Blocked Stopping Stopped
        Stopping: Stopped
        Stopped: Planning Working Reviewing NeedsYou Blocked Failed
        Blocked: Planning Working NeedsYou Paused Stopping Stopped Failed
        Failed: Planning Stopping Stopped
        Done:
        """;

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-5")]
    public void Every_task_move_is_legal_or_illegal_as_written()
    {
        var lines = TaskMoves.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var all = Enum.GetValues<MissionState>();
        Assert.Equal(all.Length, lines.Length);
        foreach (var line in lines)
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var from = Enum.Parse<MissionState>(line[..colon]);
            var legal = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => Enum.Parse<MissionState>(s)).ToHashSet();
            foreach (var to in all)
                Assert.True(legal.Contains(to) == MissionStates.IsLegal(from, to), $"{from} -> {to}");
        }
        // The rules the table carries: Stopping only ever ends Stopped; Done is final; Failed never pauses.
        Assert.False(MissionStates.IsLegal(MissionState.Stopping, MissionState.Working));
        Assert.False(MissionStates.IsLegal(MissionState.Failed, MissionState.Paused));
        Assert.False(MissionStates.IsLegal(MissionState.Failed, MissionState.Pausing));
        Assert.False(MissionStates.IsLegal(MissionState.Stopped, MissionState.Pausing));
        Assert.False(MissionStates.IsLegal(MissionState.Blocked, MissionState.Pausing));
    }

    private const string HeadMoves = """
        Queued: Working Paused Stopping Stopped Blocked
        Working: Pausing Paused Stopping Stopped Blocked Done
        Pausing: Paused Working Stopping Stopped
        Paused: Queued Working Blocked Stopping Stopped
        Stopping: Stopped
        Stopped: Queued Working Blocked
        Blocked: Queued Working Paused Stopping Stopped
        Done:
        """;

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-5")]
    public void Every_head_move_is_legal_or_illegal_as_written()
    {
        var lines = HeadMoves.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var all = Enum.GetValues<HeadState>();
        Assert.Equal(all.Length, lines.Length);
        foreach (var line in lines)
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var from = Enum.Parse<HeadState>(line[..colon]);
            var legal = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => Enum.Parse<HeadState>(s)).ToHashSet();
            foreach (var to in all)
                Assert.True(legal.Contains(to) == MissionStates.IsLegal(from, to), $"{from} -> {to}");
        }
        Assert.False(MissionStates.IsLegal(HeadState.Queued, HeadState.Pausing));
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-5")]
    public void Board_groups_put_what_waits_on_the_owner_first_and_held_states_stay_in_their_group()
    {
        Assert.Equal(BoardGroup.NeedsYou, MissionStates.Group(MissionState.NeedsYou));
        Assert.Equal(BoardGroup.NeedsYou, MissionStates.Group(MissionState.Blocked));
        Assert.Equal(BoardGroup.NeedsYou, MissionStates.Group(MissionState.Failed));
        Assert.Equal(BoardGroup.Working, MissionStates.Group(MissionState.Reviewing));
        Assert.Equal(BoardGroup.DoneToday, MissionStates.Group(MissionState.Done));
        Assert.Equal(BoardGroup.Working, MissionStates.Group(MissionState.Paused, MissionState.Working));
        Assert.Equal(BoardGroup.NeedsYou, MissionStates.Group(MissionState.Stopped, MissionState.Blocked));
        Assert.Equal(BoardGroup.NeedsYou, MissionStates.Group(MissionState.Paused, MissionState.Working, pausedByReserve: true));
        Assert.True(MissionStates.Dimmed(MissionState.Paused));
        Assert.False(MissionStates.Dimmed(MissionState.Paused, pausedByReserve: true));
        Assert.False(MissionStates.Dimmed(MissionState.Pausing));
        Assert.True(MissionStates.Dimmed(MissionState.Stopped));
        Assert.Equal(
            new[] { MissionState.Planning, MissionState.NeedsYou, MissionState.Working, MissionState.Reviewing, MissionState.Blocked },
            Enum.GetValues<MissionState>().Where(MissionStates.CanPause));
    }
}
