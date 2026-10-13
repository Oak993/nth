using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-17 (DESIGN.md rev 5, 2.1, 2.5 "An Astur's own mark", 9.4; the canvas's asState; ED-NEW-dextra-as17-3): what an Astur is doing, worked out from its
// missions and its open decisions. What waits on the owner comes first, then what works, what thinks, what is blocked, paused and stopped.
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-3")]
public sealed class AsturPresenceTests
{
    [Fact]
    public void No_task_at_all_is_idle_and_a_finished_one_counts_for_nothing()
    {
        Assert.Equal(AsturPresence.Idle, AsturPresences.Of([]));
        Assert.Equal(AsturPresence.Idle, AsturPresences.Of([MissionState.Done, MissionState.Done]));
    }

    [Theory]
    [InlineData(MissionState.Working, AsturPresence.Working)]
    [InlineData(MissionState.Pausing, AsturPresence.Working)]
    [InlineData(MissionState.Planning, AsturPresence.Thinking)]
    [InlineData(MissionState.Reviewing, AsturPresence.Thinking)]
    [InlineData(MissionState.NeedsYou, AsturPresence.Waiting)]
    [InlineData(MissionState.Blocked, AsturPresence.Blocked)]
    [InlineData(MissionState.Failed, AsturPresence.Blocked)]
    [InlineData(MissionState.Paused, AsturPresence.Paused)]
    [InlineData(MissionState.Stopping, AsturPresence.Stopped)]
    [InlineData(MissionState.Stopped, AsturPresence.Stopped)]
    public void One_task_shows_the_presence_of_its_state(MissionState state, AsturPresence expected) =>
        Assert.Equal(expected, AsturPresences.Of([state]));

    [Fact]
    public void Every_mission_state_is_covered_so_a_new_one_cannot_fall_through_to_idle_unnoticed()
    {
        foreach (var state in Enum.GetValues<MissionState>())
        {
            var presence = AsturPresences.Of([state]);
            if (state == MissionState.Done) Assert.Equal(AsturPresence.Idle, presence);
            else Assert.NotEqual(AsturPresence.Idle, presence);
        }
    }

    [Fact]
    public void What_waits_on_the_owner_comes_first_then_what_works_then_what_thinks()
    {
        Assert.Equal(AsturPresence.Waiting, AsturPresences.Of([MissionState.Working, MissionState.NeedsYou]));
        Assert.Equal(AsturPresence.Waiting, AsturPresences.Of([MissionState.Working], waitingOnOwner: true));
        Assert.Equal(AsturPresence.Waiting, AsturPresences.Of([], waitingOnOwner: true));
        Assert.Equal(AsturPresence.Working, AsturPresences.Of([MissionState.Planning, MissionState.Working]));
        Assert.Equal(AsturPresence.Thinking, AsturPresences.Of([MissionState.Planning, MissionState.Paused, MissionState.Blocked]));
        Assert.Equal(AsturPresence.Blocked, AsturPresences.Of([MissionState.Paused, MissionState.Failed, MissionState.Stopped]));
        Assert.Equal(AsturPresence.Paused, AsturPresences.Of([MissionState.Stopped, MissionState.Paused, MissionState.Done]));
        Assert.Equal(AsturPresence.Stopped, AsturPresences.Of([MissionState.Done, MissionState.Stopping]));
    }

    [Fact]
    public void Only_working_and_thinking_are_busy_which_is_what_lights_a_node_on_Dextras_orbit()
    {
        Assert.Equal([AsturPresence.Thinking, AsturPresence.Working], Enum.GetValues<AsturPresence>().Where(AsturPresences.Busy));
    }

    [Fact]
    public void Each_presence_has_its_own_word_for_the_chip_and_none_is_empty()
    {
        var words = Enum.GetValues<AsturPresence>().Select(AsturPresences.Label).ToList();
        Assert.Equal(["Idle", "Thinking", "Working", "Needs you", "Blocked", "Paused", "Stopped"], words);
        Assert.Equal(words.Count, words.Distinct().Count());
    }
}
