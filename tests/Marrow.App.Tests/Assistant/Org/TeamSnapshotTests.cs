using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.UI.Port.Armilla;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

// Dextra AS-17 (DESIGN.md rev 5, 2.1, 9.4; the canvas's org chart; ED-NEW-dextra-as17-4): what the org chart and the sidebar's Astur rows show, worked out once from the
// profiles, the tasks, the cards that wait and today's use. Dextra first, then the Asturs; each one's state and work line from its own tasks; what waits on the owner first.
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-4")]
public sealed class TeamSnapshotTests
{
    private static TeamSnapshot Build(IReadOnlyList<AssistantProfile> team, IReadOnlyList<(string AssistantId, MissionJob Job)>? jobs = null, IReadOnlyList<DecisionItem>? cards = null,
        TeamUsage? usage = null, bool stopped = false, Func<string, TeamFacts>? facts = null) =>
        TeamSnapshot.Build(team, jobs ?? [], cards ?? [], usage, stopped, facts);

    [Fact]
    public void Dextra_comes_first_then_the_Asturs_in_the_order_they_were_made_and_a_retired_one_is_left_out()
    {
        var retired = Astur("r", "Old", AsturShape.Star) with { Retired = true };
        var snap = Build(Team(ChannelA, retired, Store));

        Assert.Equal(["Dextra", "Channel A", "Store"], snap.Members.Select(m => m.Name));
        Assert.True(snap.Dextra!.IsDextra);
        Assert.Equal(["a", "s"], snap.Asturs.Select(m => m.Id));
        Assert.Equal(AsturShape.Diamond, snap.Find("s")!.Shape);
        Assert.Null(snap.Find("r"));
        Assert.Null(TeamSnapshot.Empty.Dextra);
    }

    [Fact]
    public void An_Astur_with_no_task_is_idle_and_says_there_is_nothing_at_work()
    {
        var a = Build(Team(ChannelA)).Find("a")!;

        Assert.Equal(AsturPresence.Idle, a.Presence);
        Assert.Equal("Nothing at work", a.Work);
        Assert.Equal(AsturMarkState.Idle, a.MarkState);
        Assert.Empty(a.Missions);
        Assert.Null(a.Activity);
        Assert.Equal("Channel A, Idle. Nothing at work", a.Reading);
    }

    [Fact]
    public void A_working_Astur_shows_what_its_head_at_work_does_and_its_tasks_as_chips_newest_first()
    {
        var older = Task("a", "Next video: Why lenses fog up", MissionState.Working, 30);
        var newer = Task("a", "Weekly stats", MissionState.Working, 3, HomeSamples.Head("Reader", HeadState.Working));
        var snap = Build(Team(ChannelA, ChannelB), [older, newer, Task("b", "Shorts", MissionState.Planning)]);
        var a = snap.Find("a")!;

        Assert.Equal(AsturPresence.Working, a.Presence);
        Assert.Equal("running the tests (12 of 40) (+1 more)", a.Work);                   // the newest first, the other counted
        Assert.Equal("running the tests (12 of 40)", a.Activity);
        Assert.Equal(["Weekly stats", "Next video: Why lenses fog up"], a.Missions.Select(m => m.Title));
        Assert.Equal([true, false], a.Missions.Select(m => m.Hydra));
        Assert.All(a.Missions, m => Assert.Equal(TeamChipKind.Working, m.Kind));
        Assert.Equal(AsturPresence.Thinking, snap.Find("b")!.Presence);
        Assert.Equal("Shorts", snap.Find("b")!.Work);                                    // a planning task has no activity: its title
    }

    [Fact]
    public void What_waits_on_the_owner_comes_first_and_the_line_names_the_card()
    {
        var snap = Build(Team(ChannelA, Store), [Task("s", "Weekly update", MissionState.NeedsYou), Task("s", "Invoices", MissionState.Working)],
            [Card("s", "Send the weekly update to the team channel"), Card("s", "Sign in to the shop")]);
        var store = snap.Find("s")!;

        Assert.Equal(AsturPresence.Waiting, store.Presence);
        Assert.Equal(2, store.Waiting);
        Assert.Equal("Waiting on you: Send the weekly update to the team channel", store.Work);
        Assert.Equal(AsturMarkState.Waiting, store.MarkState);
        Assert.Equal("Store, Needs you, 2 decisions open. Waiting on you: Send the weekly update to the team channel", store.Reading);
        Assert.Equal(0, snap.Find("a")!.Waiting);
    }

    [Fact]
    public void Blocked_paused_and_stopped_tasks_name_their_state_and_their_title()
    {
        var snap = Build(Team(ChannelA, ChannelB, Store),
        [
            Task("a", "Sign in to Studio", MissionState.Blocked),
            Task("b", "Espresso machines", MissionState.Paused),
            Task("s", "Price watch", MissionState.Stopped),
        ]);

        Assert.Equal("Blocked: Sign in to Studio", snap.Find("a")!.Work);
        Assert.Equal("Paused: Espresso machines", snap.Find("b")!.Work);
        Assert.Equal("Stopped: Price watch", snap.Find("s")!.Work);
        Assert.Equal([TeamChipKind.Attention], snap.Find("a")!.Missions.Select(m => m.Kind));
        Assert.Equal([TeamChipKind.Paused], snap.Find("b")!.Missions.Select(m => m.Kind));
        Assert.Empty(snap.Find("s")!.Missions);                                          // a stopped task has no chip, but the node still says it
    }

    [Fact]
    public void A_finished_task_has_no_chip_and_leaves_the_Astur_idle()
    {
        var a = Build(Team(ChannelA), [Task("a", "Tidy the README", MissionState.Done)]).Find("a")!;

        Assert.Equal(AsturPresence.Idle, a.Presence);
        Assert.Empty(a.Missions);
    }

    [Fact]
    public void Pause_and_Resume_reach_what_the_slots_of_its_tasks_allow()
    {
        var snap = Build(Team(ChannelA, ChannelB, Store),
        [
            Task("a", "Working", MissionState.Working),
            Task("b", "Paused", MissionState.Paused),
            Task("s", "Working", MissionState.Working),
            Task("s", "Paused", MissionState.Pausing),
        ]);
        var idle = Build(Team(ChannelA)).Find("a")!;

        Assert.Equal((true, false), (snap.Find("a")!.CanPause, snap.Find("a")!.CanResume));
        Assert.Equal((false, true), (snap.Find("b")!.CanPause, snap.Find("b")!.CanResume));
        Assert.Equal((true, true), (snap.Find("s")!.CanPause, snap.Find("s")!.CanResume));
        Assert.Equal((false, false), (idle.CanPause, idle.CanResume));
    }

    [Fact]
    public void The_meter_is_the_part_of_the_slice_used_today_and_its_words_say_it()
    {
        var usage = Usage(Line(ChannelA, 5.7, .38), Line(ChannelB, 12, 1.4, 5.5m), Line(Store, 0, 0));
        var snap = Build(Team(ChannelA, ChannelB, Store), usage: usage);

        Assert.Equal(.38, snap.Find("a")!.Meter);
        Assert.Equal("38% of its daily slice", snap.Find("a")!.MeterWords);
        Assert.Equal(1, snap.Find("b")!.Meter);                                          // the bar is full at the slice and no further
        Assert.Equal("At its daily slice", snap.Find("b")!.MeterWords);
        Assert.Equal(0, snap.Find("s")!.Meter);
        Assert.Equal(.12, snap.Dextra!.Meter);
        Assert.Equal("12% of the busiest window today, all together", snap.Dextra.MeterWords);
    }

    [Fact]
    public void Without_cost_controls_there_is_no_meter_to_show()
    {
        var snap = Build(Team(ChannelA));

        Assert.Null(snap.Find("a")!.Meter);
        Assert.Equal("", snap.Find("a")!.MeterWords);
        Assert.Null(snap.Dextra!.Meter);
    }

    [Fact]
    public void An_Astur_with_a_slice_and_no_line_yet_has_used_none_of_it()
    {
        var snap = Build(Team(ChannelA), usage: Usage());

        Assert.Equal(0, snap.Find("a")!.Meter);
        Assert.Equal("0% of its daily slice", snap.Find("a")!.MeterWords);
        Assert.Equal("Nothing used today", snap.Dextra!.MeterWords);
    }

    [Fact]
    public void Dextra_shows_the_whole_teams_state_and_its_own_tasks_as_its_work_else_the_status_line()
    {
        var own = Task(Root, "Fix the login timeout", MissionState.Working);
        var snap = Build(Team(ChannelA), [own, Task("a", "Voiceover", MissionState.NeedsYou)], [Card("a", "Sign in")]);

        Assert.Equal(ArmillaState.NeedsYou, snap.DextraMark);                            // the team's: what waits on the owner first
        Assert.Equal(AsturPresence.Waiting, snap.Dextra!.Presence);
        Assert.Equal("running the tests (12 of 40)", snap.Dextra.Work);                  // its own task
        Assert.Equal(1, snap.Dextra.Waiting);                                            // every card that waits is in its queue

        var onlyAstur = Build(Team(ChannelA), [Task("a", "Voiceover", MissionState.Working)]);
        Assert.Equal(ArmillaState.Working, onlyAstur.DextraMark);
        Assert.Equal("1 mission", onlyAstur.Dextra!.Work);                               // it has none of its own: the line the header shows
        Assert.Equal("Ready", Build(Team(ChannelA)).Dextra!.Work);
        Assert.Equal(AsturPresence.Idle, Build(Team()).Dextra!.Presence);
    }

    [Fact]
    public void After_Stop_all_Dextra_says_stopped()
    {
        var snap = Build(Team(ChannelA), [Task("a", "Voiceover", MissionState.Working)], stopped: true);

        Assert.Equal(ArmillaState.Paused, snap.DextraMark);
        Assert.Equal(AsturPresence.Stopped, snap.Dextra!.Presence);
        Assert.StartsWith("Stopped", snap.Dextra.Work, StringComparison.Ordinal);
    }

    [Fact]
    public void The_facts_of_the_detail_come_from_the_host_for_each_assistant()
    {
        var snap = Build(Team(ChannelA), facts: id => id == "a" ? new TeamFacts(2, 64, 3, "Keeps 30% on Claude 20x (your standing rule).") : TeamFacts.None);

        Assert.Equal(new TeamFacts(2, 64, 3, "Keeps 30% on Claude 20x (your standing rule)."), snap.Find("a")!.Facts);
        Assert.Equal(TeamFacts.None, snap.Dextra!.Facts);
    }

    [Fact]
    public void The_same_inputs_make_an_equal_member_so_a_tick_that_changes_nothing_redraws_nothing()
    {
        var jobs = new[] { Task("a", "Voiceover", MissionState.Working) };
        var first = Build(Team(ChannelA, Store), jobs, [Card("s", "Send")], Usage(Line(ChannelA, 4, .2)));
        var second = Build(Team(ChannelA, Store), jobs, [Card("s", "Send")], Usage(Line(ChannelA, 4, .2)));

        Assert.Equal(first.Find("a"), second.Find("a"));
        Assert.Equal(first.Find("s"), second.Find("s"));
        Assert.NotEqual(first.Find("a"), Build(Team(ChannelA, Store), jobs, [], Usage(Line(ChannelA, 4, .3))).Find("a"));
    }

    [Fact]
    public void A_chip_is_labelled_short_and_read_in_full_with_its_state_and_its_Hydra_team()
    {
        var chip = new TeamMission(Guid.NewGuid(), "Next video: Why lenses fog up", MissionState.Working, true, null);

        Assert.Equal("Next video…", chip.Label);
        Assert.Equal("Hydra team: Next video: Why lenses fog up, Working", chip.Reading);
        Assert.Equal("Espresso, Needs you", new TeamMission(Guid.NewGuid(), "Espresso", MissionState.NeedsYou, false, null).Reading);
    }
}
