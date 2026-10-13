using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Port.Armilla;
using static Marrow.App.Tests.Fixtures.HomeSamples;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-8 (DESIGN.md rev 5, 2.5 "Motion budget", 16 "at most 8 moving marks per view"; the canvas's budget(); ED-NEW-dextra-as8-5):
// one window gives out its 8 moving marks per list, Board first; a list that does not fit goes still as a whole, a hidden list
// costs nothing, and idle marks, small working rings and paused tasks never count.
[Collection("Headless")]
[Trait("Feature", "AS-8")]
[Trait("Decision", "ED-NEW-dextra-as8-5")]
public sealed class MotionScopeTests(HeadlessSession headless)
{
    [Fact]
    public void A_list_that_does_not_fit_goes_still_as_a_whole_and_a_later_smaller_one_still_moves()
    {
        headless.OnUiThread(() =>
        {
            var scope = new MotionScope();
            var calls = new List<string>();
            var board = scope.Join(ArmillaListRank.Board, () => 6, () => true, c => calls.Add("board " + c));
            var result = scope.Join(ArmillaListRank.Result, () => 4, () => true, c => calls.Add("result " + c));
            var header = scope.Join(ArmillaListRank.Header, () => 2, () => true, c => calls.Add("header " + c));

            Assert.False(board.IsCalm);
            Assert.True(result.IsCalm); // 6 + 4 > 8: all four go still, never two of them
            Assert.False(header.IsCalm); // 6 + 2 fits
            Assert.Equal(["result True"], calls);
        });
    }

    [Fact]
    public void A_hidden_list_costs_nothing_and_leaving_gives_its_share_back()
    {
        headless.OnUiThread(() =>
        {
            var scope = new MotionScope();
            var boardShown = false;
            var board = scope.Join(ArmillaListRank.Board, () => 20, () => boardShown, _ => { });
            var result = scope.Join(ArmillaListRank.Result, () => 5, () => true, _ => { });
            Assert.False(board.IsCalm);
            Assert.False(result.IsCalm);

            boardShown = true;
            board.Changed();
            result.Changed(); // two changes, one recount
            Dispatcher.UIThread.RunJobs();
            Assert.True(board.IsCalm);
            Assert.False(result.IsCalm);

            var header = scope.Join(ArmillaListRank.Header, () => 4, () => true, _ => { });
            Assert.True(header.IsCalm); // 5 + 4 > 8
            result.Leave();
            Dispatcher.UIThread.RunJobs();
            Assert.False(header.IsCalm);
            Assert.Equal(2, scope.Lists.Count);
        });
    }

    [Fact]
    public void One_window_has_one_scope()
    {
        headless.OnUiThread(() =>
        {
            using var stage = new HomeStage(new Border(), 400, 300);
            using var other = new HomeStage(new Border(), 400, 300);
            Assert.Same(MotionScope.For(stage.Window), MotionScope.For(stage.Window));
            Assert.NotSame(MotionScope.For(stage.Window), MotionScope.For(other.Window));
        });
    }

    [Fact]
    public void Paused_done_and_small_working_marks_never_count_and_a_thinking_one_does()
    {
        headless.OnUiThread(() =>
        {
            var jobs = new FakeMissionJobs();
            jobs.Set(Job("Paused", MissionState.Paused), Job("Working", MissionState.Working), Job("Stopped", MissionState.Stopped), Job("Needs you", MissionState.NeedsYou));
            var result = new ResultList(jobs, new FakeGlobalControls());
            using var stage = new HomeStage(result, 700);
            result.Open(true);
            stage.Layout();
            Assert.Equal(4, result.Rows.Count);
            Assert.Equal(0, result.Moving); // a 20 px working ring is still; paused, stopped and waiting marks never move

            jobs.Replace(Job("Planning", MissionState.Planning));
            stage.Layout();
            Assert.Equal(1, result.Moving);
            result.Open(false);
            Assert.Equal(0, result.Moving); // folded: costs nothing
        });
    }

    [Fact]
    public void The_board_counts_a_working_cards_shimmer_as_one_and_nothing_for_paused_cards()
    {
        headless.OnUiThread(() =>
        {
            var jobs = new FakeMissionJobs();
            jobs.Set(Job("A", MissionState.Working), Job("B", MissionState.Working), Job("C", MissionState.Paused), Job("D", MissionState.Done));
            var board = new MissionBoard(jobs);
            using var stage = new HomeStage(board, 700);
            Assert.Equal(2, board.Moving);
        });
    }
}
