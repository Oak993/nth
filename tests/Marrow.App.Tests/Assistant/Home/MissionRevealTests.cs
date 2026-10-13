using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using static Marrow.App.Tests.Fixtures.HomeSamples;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-9 (DESIGN.md rev 5, 13.1 "Duty opt-in"; ED-NEW-dextra-as9-9): a duty toast's Open shows the run. The home opens on its Missions tab and the run's
// card is brought into view. A request made before the home is built is taken up when it is built; a task that is not on the board today is not found; a
// card waiting at the same moment takes the Decisions tab first; with Dextra off there is no board and the request is let go.
[Collection("Headless")]
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-9")]
public sealed class MissionRevealTests(HeadlessSession headless)
{
    [Fact]
    public void Reveal_scrolls_a_card_far_down_the_board_into_view_and_reports_whether_it_found_the_task() => headless.OnUiThread(() =>
    {
        var jobs = new FakeMissionJobs();
        var many = Enumerable.Range(0, 12).Select(i => Job("Working task " + i, MissionState.Working, minutesAgo: i + 1)).ToArray();
        var last = Job("Espresso price run", MissionState.Done, minutesAgo: 50);
        var yesterday = Job("Old run", MissionState.Done, minutesAgo: 60 * 30) with { Ended = Now.AddDays(-1) };
        jobs.Set([.. many, last, yesterday]);
        var board = new MissionBoard(jobs);
        var scroll = new ScrollViewer { Content = board };
        using var stage = new HomeStage(scroll, 700, 360);
        Assert.Equal(0, scroll.Offset.Y);
        Assert.True(board.Reveal(last.Id));
        stage.Layout();
        Assert.True(scroll.Offset.Y > 0, "the card was not brought into view");
        Assert.False(board.Reveal(yesterday.Id));                           // only today's Done tasks are on the board
        Assert.False(board.Reveal(Guid.NewGuid()));
    });

    [Fact]
    public void A_mission_asked_of_a_built_home_picks_the_missions_tab_over_any_other_and_the_request_is_taken() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        using var stage = new HomeStage(new AssistantHomeHost(model), 1200);
        var host = (AssistantHomeHost)stage.Window.Content!;
        host.Home!.Board.Pick(BoardTab.Feed);
        stage.Layout();
        Assert.Equal(BoardTab.Feed, host.Home.Board.Tabs.Selected);
        h.Main.OnMain(() => model.ReviewAssistantMission(Guid.NewGuid()));
        stage.Layout();
        Assert.Equal(BoardTab.Missions, host.Home.Board.Tabs.Selected);
        Assert.Null(model.PendingAssistantMission);
        Assert.True(model.AssistantHomeShown);
    });

    [Fact]
    public void A_mission_asked_before_the_home_exists_is_taken_up_when_it_is_built() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        var ask = Guid.NewGuid();
        h.Main.OnMain(() => model.ReviewAssistantMission(ask));
        Assert.Equal(ask, model.PendingAssistantMission);
        using var stage = new HomeStage(new AssistantHomeHost(model), 1200);
        var host = (AssistantHomeHost)stage.Window.Content!;
        stage.Layout();
        Assert.Equal(BoardTab.Missions, host.Home!.Board.Tabs.Selected);
        Assert.Null(model.PendingAssistantMission);
    });

    [Fact]
    public void A_card_asked_at_the_same_moment_takes_the_decisions_tab_and_gives_the_mission_way() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            model.ReviewAssistantMission(Guid.NewGuid());
            model.ReviewAssistantDecision(Guid.NewGuid());
        });
        using var stage = new HomeStage(new AssistantHomeHost(model), 1200);
        var host = (AssistantHomeHost)stage.Window.Content!;
        stage.Layout();
        Assert.Equal(BoardTab.Decisions, host.Home!.Board.Tabs.Selected);
        Assert.Null(model.PendingAssistantReview);
        Assert.Null(model.PendingAssistantMission);
    });

    [Fact]
    public void With_dextra_off_there_is_no_board_and_the_request_is_let_go() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        h.Main.OnMain(() => model.AssistantHost.Settings.Update(p => p with { On = false }));
        using var stage = new HomeStage(new AssistantHomeHost(model), 1200);
        var host = (AssistantHomeHost)stage.Window.Content!;
        Assert.Null(host.Home);
        h.Main.OnMain(() => model.ReviewAssistantMission(Guid.NewGuid()));
        stage.Layout();
        Assert.Null(model.PendingAssistantMission);
        Assert.Null(host.Home);
    });

    [Fact]
    public void A_host_that_left_the_window_no_longer_hears_the_request() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        var host = new AssistantHomeHost(model);
        using (new HomeStage(host, 1200)) { }
        h.Main.OnMain(() => model.ReviewAssistantMission(Guid.NewGuid()));
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(model.PendingAssistantMission);                      // nobody took it up: it waits for the next home
    });
}
