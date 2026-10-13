using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Assistant.Home;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-smooth-10")]
public sealed class DextraSmoothBoardVisibilityTests(HeadlessSession headless)
{
    [Theory]
    [InlineData("hidden")]
    [InlineData("covered")]
    [InlineData("minimized")]
    [InlineData("cloaked")]
    public void An_unfocused_visible_board_ticks_and_catches_up_after_each_unseen_condition(string condition) => headless.OnUiThread(() =>
    {
        using var still = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        AmbientWindowState? source = null;
        AmbientWindowState.TestFactory = w => { source = new(w, true); source.SetWindowFacts(new(true, false, false, false, false, false)); return source; };
        var jobs = new FakeMissionJobs(); jobs.Set(HomeSamples.Job("Initial", MissionState.Working));
        var board = new MissionBoard(jobs);
        try
        {
            using var stage = new HomeStage(board);
            Assert.Single(board.AllCards); // Initially shown without ever gaining focus.
            var reads = jobs.Snapshots; stage.Advance(TimeSpan.FromSeconds(1));
            Assert.True(jobs.Snapshots > reads, "visible unfocused board lost its 250 ms tick");
            jobs.Set(HomeSamples.Job("Unfocused update", MissionState.Working)); stage.Layout();
            Assert.Equal("Unfocused update", board.AllCards.Single().Surface.Job!.Title);
            stage.Advance(TimeSpan.FromSeconds(31)); reads = jobs.Snapshots;
            stage.Advance(TimeSpan.FromSeconds(1)); Assert.True(jobs.Snapshots > reads);
            source!.SetWindowFacts(new(condition != "hidden", condition == "minimized", condition == "cloaked", condition == "covered", false, false)); stage.Layout();
            reads = jobs.Snapshots;
            for (var i = 0; i < 20; i++) jobs.Set(HomeSamples.Job("Latest " + i, MissionState.Working));
            stage.Advance(TimeSpan.FromSeconds(5)); Assert.Equal(reads, jobs.Snapshots);
            Assert.Equal(0, stage.Clock.ActiveTimers);
            source.SetWindowFacts(new(true, false, false, false, false, false)); stage.Layout();
            Assert.Equal("Latest 19", board.AllCards.Single().Surface.Job!.Title);
            reads = jobs.Snapshots; stage.Advance(TimeSpan.FromSeconds(1)); Assert.True(jobs.Snapshots > reads);
        }
        finally { AmbientWindowState.TestFactory = null; }
    });
}
