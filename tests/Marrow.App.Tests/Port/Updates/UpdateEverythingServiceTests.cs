using Marrow.App.Port;
using Marrow.App.Port.Updates;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Updates;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Port.Updates;

// UE-5 (OD-0100 place D). The service is what the sidebar's pill reads and what its button drives: it owns the orchestrator (UE-2), projects its rows (UpdatePillProjector) and runs the
// pill's actions. Items are fakes (nothing runs, nothing is reached); the clock is the test runtime's.
public sealed class UpdateEverythingServiceTests
{
    private static FakeUpdateItem Cli(string id, string title, string installed = "1.0.0", string? latest = "1.1.0") => new(id, title, UpdateItemKind.ProviderCli, installed, latest);

    private static FakeUpdateItem MarrowItem() => new("marrow", "Nth", UpdateItemKind.Marrow, "1.0.0", "1.1.0") { NeedsRestart = true };

    private static void Settle(TestRuntime runtime) => runtime.RunUntilIdle();

    [Fact]
    [Trait("Decision", "ED-308")]
    public void A_service_with_no_items_shows_no_pill_and_a_press_runs_nothing()
    {
        using var runtime = new TestRuntime();
        var service = runtime.OnMain(() => new UpdateEverythingService([], () => { }));
        Assert.Equal(UpdatePillPhase.Hidden, runtime.OnMain(() => service.State.Phase));
        runtime.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything));
        Settle(runtime);
        Assert.Equal(UpdatePillPhase.Hidden, runtime.OnMain(() => service.State.Phase));
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void Configure_replaces_the_items_and_the_restart_and_the_state_follows()
    {
        using var runtime = new TestRuntime();
        var restarts = 0;
        var service = runtime.OnMain(() => new UpdateEverythingService([], () => { }));
        runtime.OnMain(() => service.Configure([Cli("c", "Claude Code")], () => restarts++));
        runtime.OnMain(() => service.CheckNow());
        Settle(runtime);
        Assert.Equal(UpdatePillPhase.Available, runtime.OnMain(() => service.State.Phase));
        runtime.OnMain(() => service.Perform(UpdatePillAction.RestartMarrow));
        Assert.Equal(1, restarts);
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void Checking_finds_the_newer_versions_and_the_pill_offers_Update_everything()
    {
        using var runtime = new TestRuntime();
        var service = runtime.OnMain(() => new UpdateEverythingService([Cli("c", "Claude Code"), Cli("x", "Codex", "1.1.0", "1.1.0"), MarrowItem()], () => { }));
        Assert.Equal(UpdatePillPhase.Hidden, runtime.OnMain(() => service.State.Phase)); // never checked
        runtime.OnMain(() => service.CheckNow());
        Settle(runtime);
        var state = runtime.OnMain(() => service.State);
        Assert.Equal(UpdatePillPhase.Available, state.Phase);
        Assert.Equal("2 updates", state.PillText);
        Assert.Equal(UpdatePillAction.UpdateEverything, state.Action);
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void The_pill_action_runs_every_item_one_at_a_time_and_ends_on_Restart_when_Marrow_needs_it()
    {
        using var runtime = new TestRuntime();
        var gate = new TaskCompletionSource();
        var claude = Cli("c", "Claude Code");
        var marrow = MarrowItem();
        claude.Gate = gate.Task;
        var service = runtime.OnMain(() => new UpdateEverythingService([claude, marrow], () => { }));
        runtime.OnMain(() => service.CheckNow());
        Settle(runtime);
        runtime.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything));
        Settle(runtime);
        var running = runtime.OnMain(() => service.State);
        Assert.Equal(UpdatePillPhase.Running, running.Phase);
        Assert.Equal("Updating 1 of 2", running.PillText);
        Assert.Equal(0, marrow.Updates); // Marrow waits for the CLI: one at a time, Marrow last
        gate.SetResult();
        Settle(runtime);
        var done = runtime.OnMain(() => service.State);
        Assert.Equal(UpdatePillPhase.Restart, done.Phase);
        Assert.Equal("Restart Nth", done.ButtonText);
        Assert.Equal(1, marrow.Updates);
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void A_second_press_while_a_run_goes_changes_nothing()
    {
        using var runtime = new TestRuntime();
        var gate = new TaskCompletionSource();
        var claude = Cli("c", "Claude Code");
        claude.Gate = gate.Task;
        var service = runtime.OnMain(() => new UpdateEverythingService([claude], () => { }));
        runtime.OnMain(() => service.CheckNow());
        Settle(runtime);
        runtime.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything));
        Settle(runtime);
        runtime.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything));
        Settle(runtime);
        gate.SetResult();
        Settle(runtime);
        Assert.Equal(1, claude.Updates);
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void A_failed_item_shows_Failed_and_Retry_runs_it_alone()
    {
        using var runtime = new TestRuntime();
        var codex = Cli("x", "Codex", "0.157.1", "0.160.0");
        var claude = Cli("c", "Claude Code");
        codex.Fails = true;
        var service = runtime.OnMain(() => new UpdateEverythingService([claude, codex], () => { }));
        runtime.OnMain(() => service.CheckNow());
        Settle(runtime);
        runtime.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything));
        Settle(runtime);
        var failed = runtime.OnMain(() => service.State);
        Assert.Equal(UpdatePillPhase.Failed, failed.Phase);
        Assert.Equal("Codex didn’t update", failed.PillText);
        Assert.Equal("the updater failed", failed.BannerText);
        codex.Fails = false;
        runtime.OnMain(() => service.Perform(UpdatePillAction.Retry));
        Settle(runtime);
        Assert.Equal(2, codex.Updates);
        Assert.Equal(1, claude.Updates); // the retry ran the failed one only
        var done = runtime.OnMain(() => service.State);
        Assert.Equal(UpdatePillPhase.Done, done.Phase); // everything is current and none needs a restart: the design's done phase
        Assert.Equal("2 updated", done.PillText);
        runtime.OnMain(service.AcknowledgeDone);
        Assert.Equal(UpdatePillPhase.Hidden, runtime.OnMain(() => service.State.Phase)); // seen
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void The_done_phases_button_checks_again_and_a_check_that_finds_everything_current_ends_it()
    {
        using var runtime = new TestRuntime();
        var service = runtime.OnMain(() => new UpdateEverythingService([Cli("c", "Claude Code")], () => { }));
        runtime.OnMain(() => service.CheckNow());
        runtime.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything));
        Settle(runtime);
        Assert.Equal(UpdatePillAction.CheckNow, runtime.OnMain(() => service.State.Action));
        runtime.OnMain(() => service.Perform(UpdatePillAction.CheckNow));
        Settle(runtime);
        Assert.Equal(UpdatePillPhase.Hidden, runtime.OnMain(() => service.State.Phase));
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void The_restart_action_calls_the_restart_seam_and_the_other_actions_do_not()
    {
        using var runtime = new TestRuntime();
        var restarts = 0;
        var service = runtime.OnMain(() => new UpdateEverythingService([Cli("c", "Claude Code")], () => restarts++));
        runtime.OnMain(() => service.Perform(UpdatePillAction.None));
        Assert.Equal(0, restarts);
        runtime.OnMain(() => service.Perform(UpdatePillAction.RestartMarrow));
        Assert.Equal(1, restarts);
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void A_change_of_state_is_seen_by_whatever_observes_the_service()
    {
        using var runtime = new TestRuntime();
        var service = runtime.OnMain(() => new UpdateEverythingService([Cli("c", "Claude Code")], () => { }));
        var seen = new List<UpdatePillPhase>();
        Tracked<UpdatePillPhase>? watch = null;
        runtime.OnMain(() => watch = new Tracked<UpdatePillPhase>(() => service.State.Phase, phase => seen.Add(phase)));
        runtime.OnMain(() => service.CheckNow());
        Settle(runtime);
        watch?.Dispose();
        Assert.Contains(UpdatePillPhase.Available, seen);
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void A_development_run_starts_no_background_checks()
    {
        using var runtime = new TestRuntime();
        using var development = AppInfo.Running.Override(ReleaseFeed.Running("1.0.0", isDevelopment: true));
        var item = Cli("c", "Claude Code");
        var service = runtime.OnMain(() => new UpdateEverythingService([item], () => { }));
        runtime.OnMain(service.StartBackgroundChecks);
        runtime.Advance(TimeSpan.FromHours(13));
        Assert.Equal(UpdatePillPhase.Hidden, runtime.OnMain(() => service.State.Phase));
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void The_background_check_runs_after_the_launch_delay_and_again_every_interval()
    {
        using var runtime = new TestRuntime();
        using var released = AppInfo.Running.Override(ReleaseFeed.Running("1.0.0", isDevelopment: false));
        var item = Cli("c", "Claude Code", latest: "1.0.0");
        var service = runtime.OnMain(() => new UpdateEverythingService([item], () => { }));
        runtime.OnMain(service.StartBackgroundChecks);
        runtime.Advance(TimeSpan.FromSeconds(UpdateEverythingService.LaunchDelaySeconds - 1));
        Assert.Equal(UpdatePillPhase.Hidden, runtime.OnMain(() => service.State.Phase)); // not yet
        runtime.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(UpdateRowState.UpToDate, runtime.OnMain(() => service.Rows.Single().State)); // the first check ran
        item.Latest = "1.1.0";
        runtime.Advance(TimeSpan.FromSeconds(UpdateEverythingService.CheckIntervalSeconds));
        Assert.Equal(UpdatePillPhase.Available, runtime.OnMain(() => service.State.Phase)); // and the next
        runtime.OnMain(service.StartBackgroundChecks); // a second start changes nothing
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void A_background_check_leaves_a_run_alone()
    {
        using var runtime = new TestRuntime();
        using var released = AppInfo.Running.Override(ReleaseFeed.Running("1.0.0", isDevelopment: false));
        var gate = new TaskCompletionSource();
        var item = Cli("c", "Claude Code");
        item.Gate = gate.Task;
        var service = runtime.OnMain(() => new UpdateEverythingService([item], () => { }));
        runtime.OnMain(() => service.CheckNow());
        runtime.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything));
        Settle(runtime);
        runtime.OnMain(service.StartBackgroundChecks);
        runtime.Advance(TimeSpan.FromSeconds(UpdateEverythingService.LaunchDelaySeconds + 1)); // would throw "already going" if it tried
        Assert.Equal(UpdatePillPhase.Running, runtime.OnMain(() => service.State.Phase));
        gate.SetResult();
        Settle(runtime);
    }
}
