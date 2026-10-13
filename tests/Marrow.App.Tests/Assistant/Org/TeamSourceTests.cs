using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

// Dextra AS-17 (DESIGN.md rev 5, 2.1, 9.4, 12.6b; ED-NEW-dextra-as17-4, -5): the source the org chart and the sidebar's Astur rows read. It joins the tasks to the assistant they belong
// to, raises one Changed for whatever the snapshot reads, and pauses and resumes one assistant's tasks through the board's own calls.
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-4")]
public sealed class TeamSourceTests
{
    private static (TeamSource Source, FakeMissionJobs Jobs, FakeDecisions Cards, FakeGlobalControls Controls, Dictionary<Guid, string> Owners) Make(params (string Owner, MissionJob Job)[] tasks)
    {
        var jobs = new FakeMissionJobs();
        jobs.Set([.. tasks.Select(t => t.Job)]);
        var owners = tasks.ToDictionary(t => t.Job.Id, t => t.Owner);
        var cards = new FakeDecisions();
        var controls = new FakeGlobalControls();
        var source = new TeamSource(() => Team(ChannelA, Store), jobs, id => owners.GetValueOrDefault(id), cards, controls);
        return (source, jobs, cards, controls, owners);
    }

    [Fact]
    public void The_snapshot_joins_each_task_to_its_assistant_and_a_task_nobody_claims_is_Dextras()
    {
        var mine = Task("a", "Voiceover", MissionState.Working);
        var stray = Task("a", "Orphan", MissionState.Paused);
        var (source, jobs, _, _, owners) = Make(mine, Task("s", "Invoices", MissionState.Planning), stray);
        owners.Remove(stray.Job.Id);

        var snap = source.Now();

        Assert.Equal(["Voiceover"], snap.Find("a")!.Missions.Select(m => m.Title));
        Assert.Equal(["Invoices"], snap.Find("s")!.Missions.Select(m => m.Title));
        Assert.Equal(["Orphan"], snap.Dextra!.Missions.Select(m => m.Title));
        Assert.True(jobs.Snapshots > 0);
    }

    [Fact]
    public void The_cards_that_wait_and_a_stop_in_effect_reach_the_snapshot()
    {
        var (source, _, cards, controls, _) = Make(Task("a", "Voiceover", MissionState.Working));
        cards.Add(Card("s", "Send the update"));
        Assert.Equal(1, source.Now().Find("s")!.Waiting);

        controls.StopAll(Marrow.App.Safety.StopSource.Program);
        Assert.Equal(AsturPresence.Stopped, source.Now().Dextra!.Presence);
    }

    [Fact]
    public void Changed_is_raised_for_the_tasks_the_cards_the_controls_and_the_team_and_not_after_Dispose()
    {
        var jobs = new FakeMissionJobs();
        var cards = new FakeDecisions();
        var controls = new FakeGlobalControls();
        Action? teamChanged = null;
        var watch = new TeamWatch(a => teamChanged = a);
        var source = new TeamSource(() => Team(), jobs, _ => null, cards, controls, watchTeam: watch.Subscribe);
        var raised = 0;
        source.Changed += () => raised++;

        jobs.Raise();
        cards.Add(Card("a", "x"));
        controls.Raise();
        teamChanged!();
        Assert.Equal(4, raised);

        source.Dispose();
        jobs.Raise();
        controls.Raise();
        teamChanged();
        Assert.Equal(4, raised);
        Assert.True(watch.Disposed);
    }

    [Fact]
    public void Pause_pauses_the_tasks_of_that_assistant_that_can_pause_and_says_how_many_through_the_boards_own_call()
    {
        var one = Task("a", "Voiceover", MissionState.Working);
        var two = Task("a", "Stats", MissionState.NeedsYou);
        var done = Task("a", "Tidy", MissionState.Done);
        var other = Task("s", "Invoices", MissionState.Working);
        var (source, jobs, _, _, _) = Make(one, two, done, other);

        var line = source.PauseTasks("a");

        Assert.Equal("Pausing 2 tasks of Channel A: each stops at its next turn boundary and nothing is cut.", line);
        Assert.Equal(["Pause " + one.Job.Id.ToString("N")[..4], "Pause " + two.Job.Id.ToString("N")[..4]], jobs.Calls);
        Assert.DoesNotContain(jobs.Calls, c => c.Contains(other.Job.Id.ToString("N")[..4], StringComparison.Ordinal));
    }

    [Fact]
    public void Resume_resumes_the_paused_tasks_of_that_assistant_only()
    {
        var paused = Task("s", "Invoices", MissionState.Paused);
        var pausing = Task("s", "Prices", MissionState.Pausing);
        var working = Task("s", "Reviews", MissionState.Working);
        var (source, jobs, _, _, _) = Make(paused, pausing, working, Task("a", "Voiceover", MissionState.Paused));

        var line = source.ResumeTasks("s");

        Assert.Equal("Resumed 2 tasks of Store.", line);
        Assert.Equal(2, jobs.Calls.Count);
        Assert.All(jobs.Calls, c => Assert.StartsWith("Resume ", c, StringComparison.Ordinal));
    }

    [Fact]
    public void With_nothing_to_act_on_the_line_says_so_and_a_refusal_is_told_as_the_board_tells_it()
    {
        var (source, jobs, _, _, _) = Make(Task("a", "Tidy", MissionState.Done));
        Assert.Equal("Channel A has nothing to pause.", source.PauseTasks("a"));
        Assert.Equal("Store has nothing to resume.", source.ResumeTasks("s"));
        Assert.Empty(jobs.Calls);

        var (refusing, fake, _, _, _) = Make(Task("a", "Voiceover", MissionState.Working));
        fake.Refusal = "That task is not here any more.";
        Assert.Equal("That task is not here any more.", refusing.PauseTasks("a"));
    }

    [Fact]
    public void Dextras_own_tasks_are_paused_as_an_Asturs_are()
    {
        var own = Task("root", "Fix the login", MissionState.Working);
        var (source, jobs, _, _, _) = Make(own, Task("a", "Voiceover", MissionState.Working));

        Assert.Equal("Pausing 1 task of Dextra: each stops at its next turn boundary and nothing is cut.", source.PauseTasks("root"));
        Assert.Equal(["Pause " + own.Job.Id.ToString("N")[..4]], jobs.Calls);
    }
}
