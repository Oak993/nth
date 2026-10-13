using System.Diagnostics;
using Avalonia.Controls;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.HomeSamples;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-8 (DESIGN.md rev 5, 3.2, 3.3, 16 "groups and order", "ticks never re-lay out", "board load gate"; ED-NEW-dextra-as8-5): the
// Missions tab groups tasks into Needs you, Working and Done today, newest first, and its 4 Hz tick changes only values in fixed slots.
[Collection("Headless")]
[Trait("Feature", "AS-8")]
[Trait("Decision", "ED-NEW-dextra-as8-5")]
public sealed class MissionBoardTests(HeadlessSession headless)
{
    [Fact]
    public void Tasks_fall_into_Needs_you_Working_and_Done_today_newest_first_and_an_empty_group_says_so()
    {
        headless.OnUiThread(() =>
        {
            var jobs = new FakeMissionJobs();
            var older = Job("Write the release notes", MissionState.Working, minutesAgo: 30);
            var newer = Job("Fix the login timeout", MissionState.Working, minutesAgo: 3);
            var waiting = Job("Espresso machines under $800", MissionState.NeedsYou, minutesAgo: 10);
            var paused = Job("Next video: Why lenses fog up", MissionState.Paused, minutesAgo: 20);
            var done = Job("Tidy the README", MissionState.Done, minutesAgo: 50);
            var yesterday = Job("Old task", MissionState.Done, minutesAgo: 60 * 30) with { Ended = Now.AddDays(-1) };
            jobs.Set(older, newer, waiting, paused, done, yesterday);
            var board = new MissionBoard(jobs);
            using var stage = new HomeStage(board, 700);

            Assert.Equal(["Espresso machines under $800"], board.Cards(BoardGroup.NeedsYou).Select(c => c.TitleText));
            Assert.Equal(["Fix the login timeout", "Next video: Why lenses fog up", "Write the release notes"], board.Cards(BoardGroup.Working).Select(c => c.TitleText));
            Assert.Equal(["Tidy the README"], board.Cards(BoardGroup.DoneToday).Select(c => c.TitleText)); // only today's
            Assert.Equal(["Needs you", "Working", "Done today"], new[] { BoardGroup.NeedsYou, BoardGroup.Working, BoardGroup.DoneToday }.Select(g => board.Heading(g).Text));
            Assert.All(new[] { BoardGroup.NeedsYou, BoardGroup.Working, BoardGroup.DoneToday }, g => Assert.Equal(3, Avalonia.Automation.AutomationProperties.GetHeadingLevel(board.Heading(g))));
            Assert.All(new[] { BoardGroup.NeedsYou, BoardGroup.Working, BoardGroup.DoneToday }, g => Assert.False(board.Empty(g).IsVisible));

            jobs.Set(older);
            stage.Layout();
            Assert.True(board.Empty(BoardGroup.NeedsYou).IsVisible);
            Assert.Equal("Nothing waiting on you.", board.Empty(BoardGroup.NeedsYou).Text);
            Assert.True(board.Empty(BoardGroup.DoneToday).IsVisible);
        });
    }

    [Fact]
    public void A_task_that_changes_group_moves_its_one_card_and_announces_the_change()
    {
        headless.OnUiThread(() =>
        {
            var jobs = new FakeMissionJobs();
            var task = Job("Fix the login timeout", MissionState.Working);
            jobs.Set(task);
            var board = new MissionBoard(jobs);
            var changes = new List<(string, MissionState?)>();
            board.StateChanged += (job, was) => changes.Add((job.Title, was));
            using var stage = new HomeStage(board, 700);
            var card = board.Card(task.Id);

            jobs.Replace(Moved(task, MissionState.NeedsYou));
            stage.Layout();
            Assert.Same(card, Assert.Single(board.Cards(BoardGroup.NeedsYou)));
            Assert.Empty(board.Cards(BoardGroup.Working));
            Assert.Equal(("Fix the login timeout", MissionState.Working), changes[^1]);
        });
    }

    [Fact]
    public void The_tick_redraws_values_in_place_and_never_invalidates_a_measure()
    {
        headless.OnUiThread(() =>
        {
            var jobs = new FakeMissionJobs();
            var task = Job("Fix the login timeout", MissionState.Working) with { Spend = Spent(new MissionSpend("Claude 20x", 2.5)) };
            jobs.Set(task);
            var board = new MissionBoard(jobs);
            using var stage = new HomeStage(board, 700);
            var card = board.Card(task.Id)!;
            var size = card.Bounds.Size;
            var elapsed = card.Elapsed.Text;

            jobs.Jobs[0] = task with { Detail = "Lead at work · running the tests (13 of 40)", Agents = 3, Spend = Spent(new MissionSpend("Claude 20x", 2.6)) };
            stage.Clock.Advance(TimeSpan.FromSeconds(1)); // four ticks of the board's own 4 Hz timer
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(board.IsMeasureValid);
            Assert.True(card.IsMeasureValid);
            Assert.All(new Control[] { card.Elapsed, card.Agents, card.Spend, card.Activity }, c => Assert.True(c.IsMeasureValid));
            Assert.NotEqual(elapsed, card.Elapsed.Text);
            Assert.Equal("3 agents", card.Agents.Text);
            Assert.Equal("Claude 20x 2.6%", card.Spend.Text);
            Assert.Equal("Lead at work · running the tests (13 of 40)", card.Activity.Text);
            stage.Layout();
            Assert.Equal(size, card.Bounds.Size);
        });
    }

    [Fact]
    public void SameShape_ignores_only_the_live_values()
    {
        var a = Job("Fix the login timeout", MissionState.Working);
        Assert.True(MissionBoard.SameShape(a, a with { Detail = "other", Activity = "other", Agents = 7, Spend = Spent(new MissionSpend("A", 1)) }));
        Assert.False(MissionBoard.SameShape(a, Moved(a, MissionState.Paused)));
        Assert.False(MissionBoard.SameShape(a, a with { Title = "Renamed" }));
        Assert.False(MissionBoard.SameShape(a, a with { Progress = .9 }));
    }

    [Fact]
    public void Load_gate_a_hundred_missions_with_sixteen_working_tick_at_4_Hz_under_2_ms_each_without_a_measure()
    {
        headless.OnUiThread(() =>
        {
            var jobs = new FakeMissionJobs();
            var list = Enumerable.Range(0, 100).Select(i => Job("Task " + i, i < 16 ? MissionState.Working : i < 30 ? MissionState.Paused : MissionState.Done, minutesAgo: i + 1)).ToArray();
            jobs.Set(list);
            var board = new MissionBoard(jobs);
            using var stage = new HomeStage(new ScrollViewer { Content = board }, 700, 900);
            Assert.Equal(100, board.AllCards.Count);

            // Warm the tick once (first-call JIT), then time 40 ticks: 10 seconds at 4 Hz.
            board.Tick();
            var times = new List<double>();
            for (var t = 0; t < 40; t++)
            {
                for (var i = 0; i < 16; i++) jobs.Jobs[i] = jobs.Jobs[i] with { Detail = "running step " + t, Agents = 1 + (t % 3) };
                stage.Clock.Advance(MissionBoard.TickEvery);
                var watch = Stopwatch.StartNew();
                board.Tick();
                watch.Stop();
                times.Add(watch.Elapsed.TotalMilliseconds);
                Assert.True(board.IsMeasureValid);
                Assert.All(board.AllCards, c => Assert.True(c.IsMeasureValid));
            }
            times.Sort();
            Assert.True(times[times.Count / 2] < 2.0, $"median tick {times[times.Count / 2]:0.00} ms");
        });
    }
}
