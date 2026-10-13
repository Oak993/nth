using Avalonia.Controls;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Assistant.Home;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-smooth-4")]
public sealed class DextraSmoothBoardIdleTests(HeadlessSession headless)
{
    [Fact]
    public void An_empty_board_has_no_periodic_snapshots_or_timer() => headless.OnUiThread(() =>
    {
        using var still = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        var jobs = new FakeMissionJobs();
        var board = new MissionBoard(jobs);
        using var stage = new HomeStage(board);
        var reads = jobs.Snapshots;
        stage.Advance(TimeSpan.FromMinutes(5));
        Assert.True(reads == jobs.Snapshots, $"idle snapshots={jobs.Snapshots - reads}");
        Assert.Equal(0, stage.Clock.ActiveTimers);
        jobs.Set(HomeSamples.Job("Working", MissionState.Working)); stage.Layout();
        reads = jobs.Snapshots; stage.Advance(TimeSpan.FromSeconds(1));
        Assert.True(jobs.Snapshots > reads);
        jobs.Set(); stage.Layout();
        reads = jobs.Snapshots; stage.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(reads, jobs.Snapshots);
    });

    [Fact]
    public void Hiding_an_ancestor_suspends_live_snapshots_and_reshowing_updates_immediately() => headless.OnUiThread(() =>
    {
        using var still = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        var jobs = new FakeMissionJobs(); jobs.Set(HomeSamples.Job("Working", MissionState.Working));
        var board = new MissionBoard(jobs); var parent = new Border { Child = board };
        using var stage = new HomeStage(parent);
        parent.IsVisible = false; stage.Layout();
        var reads = jobs.Snapshots; stage.Advance(TimeSpan.FromSeconds(5));
        Assert.True(reads == jobs.Snapshots, $"hidden snapshots={jobs.Snapshots - reads}");
        Assert.Equal(0, stage.Clock.ActiveTimers);
        for (var i = 0; i < 20; i++) jobs.Set(HomeSamples.Job("Update " + i, MissionState.Working));
        stage.Layout();
        Assert.True(reads == jobs.Snapshots, $"hidden event snapshots={jobs.Snapshots - reads}");
        parent.IsVisible = true; stage.Layout();
        Assert.True(jobs.Snapshots > reads);
        Assert.Equal("Update 19", board.AllCards.Single().Surface.Job!.Title);
    });

    [Fact]
    public void Finished_cards_wait_for_midnight_instead_of_polling_live_values() => headless.OnUiThread(() =>
    {
        using var still = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        var jobs = new FakeMissionJobs(); jobs.Set(HomeSamples.Job("Done", MissionState.Done));
        var board = new MissionBoard(jobs);
        using var stage = new HomeStage(board);
        var reads = jobs.Snapshots;
        stage.Advance(TimeSpan.FromMinutes(5));
        Assert.True(reads == jobs.Snapshots, $"finished-board snapshots={jobs.Snapshots - reads}");
        var local = TaskWords.Local(stage.Clock.GetUtcNow());
        stage.Advance(new DateTimeOffset(local.Date.AddDays(1), stage.Clock.LocalTimeZone.GetUtcOffset(local.Date.AddDays(1))) - stage.Clock.GetUtcNow() + TimeSpan.FromSeconds(1));
        Assert.Empty(board.AllCards);
        Assert.Equal(0, stage.Clock.ActiveTimers);
    });
}
