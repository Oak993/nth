using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Updates;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Updates;

[Trait("Feature", "UE-3")]
[Trait("Rule", "R-TEST-4")]
public sealed class OfficeC2RUpdaterTests
{
    [Fact]
    public void Office_argv_matches_the_once_only_trial_without_forcing_app_shutdown()
    {
        var expected = TrialHFixtures.Command("37-office-upgrade");
        var actual = OfficeC2RUpdater.UpgradeCommand(TrialHFixtures.Office("34-office-before").ClientPath);
        Assert.Equal(expected.Executable, actual.Executable); Assert.Equal(expected.Arguments, actual.Arguments);
    }

    [Fact]
    public async Task Installed_Office_and_channel_are_read_without_a_process_or_network_call()
    {
        var install = TrialHFixtures.Office("34-office-before");
        Assert.EndsWith("492350f6-3a01-4f97-b9c0-c7c6ddf67d60", install.Channel, StringComparison.Ordinal);
        var item = new OfficeC2RUpdater(_ => Task.FromResult<OfficeInstallation?>(install));
        Assert.Equal("16.0.20430.20092", await item.InstalledVersion(default)); Assert.Null(await item.LatestVersion(default));
        Assert.Equal(UpdateItemKind.DevTool, item.Kind);
    }

    [Fact]
    public async Task Recorded_zero_exit_with_unchanged_Office_is_unconfirmed_in_UE2_not_UpToDate()
    {
        var replay = new TrialHReplay(); replay.Add("37-office-upgrade");
        var snapshots = new Queue<OfficeInstallation>([TrialHFixtures.Office("34-office-before"), TrialHFixtures.Office("38-office-after-immediate")]);
        var after = TrialHFixtures.Office("43-office-after-delayed");
        var item = new OfficeC2RUpdater(_ => Task.FromResult<OfficeInstallation?>(snapshots.Count > 0 ? snapshots.Dequeue() : after), replay.Run, (_, _) => Task.CompletedTask);
        var orchestrator = new UpdateOrchestrator([item]);
        await orchestrator.UpdateEverything();
        var row = Assert.Single(orchestrator.Rows);
        Assert.Equal(UpdateRowState.Failed, row.State); Assert.Equal("16.0.20430.20092", row.Installed);
        Assert.Contains("not confirmed", row.Message, StringComparison.Ordinal);
        Assert.False(row.RestartRequired); replay.Complete();
    }

    [Fact]
    public async Task A_changed_Office_version_after_a_success_is_verified_without_relaunching_the_updater()
    {
        // Synthetic follow-up state tests the branch not witnessed by this unchanged live trial.
        var replay = new TrialHReplay(); replay.Add("37-office-upgrade");
        var before = TrialHFixtures.Office("34-office-before"); var reads = 0;
        var progress = new DevToolProgress();
        var item = new OfficeC2RUpdater(_ => Task.FromResult<OfficeInstallation?>(reads++ == 0 ? before : before with { Version = "16.0.20430.20100" }), replay.Run);
        var outcome = await item.Update(progress, default);
        Assert.True(outcome.Succeeded); Assert.True(outcome.Changed); Assert.False(outcome.RestartRequired);
        Assert.Equal("16.0.20430.20100", outcome.NewVersion); Assert.Equal(new double?[] { null }, progress.Values);
        replay.Complete();
    }

    [Fact]
    public async Task Missing_Office_never_launches_an_updater()
    {
        var replay = new TrialHReplay();
        var item = new OfficeC2RUpdater(_ => Task.FromResult<OfficeInstallation?>(null), replay.Run);
        Assert.Null(await item.InstalledVersion(default)); Assert.False((await item.Update(new DevToolProgress(), default)).Succeeded);
        Assert.Empty(replay.Calls);
    }

    [Fact]
    public async Task Missing_Click_to_Run_client_is_a_failure_without_process_execution()
    {
        var replay = new TrialHReplay(); var before = TrialHFixtures.Office("34-office-before") with { ClientExists = false };
        var item = new OfficeC2RUpdater(_ => Task.FromResult<OfficeInstallation?>(before), replay.Run);
        Assert.False((await item.Update(new DevToolProgress(), default)).Succeeded); Assert.Empty(replay.Calls);
    }

    [Fact]
    public async Task Failed_updater_does_not_report_success_even_if_the_version_moved()
    {
        var replay = new TrialHReplay(); replay.Add(TrialHFixtures.Command("37-office-upgrade"), new(1, "", "denied"));
        var before = TrialHFixtures.Office("34-office-before"); var reads = 0;
        var item = new OfficeC2RUpdater(_ => Task.FromResult<OfficeInstallation?>(reads++ == 0 ? before : before with { Version = "16.0.20430.20100" }), replay.Run);
        var outcome = await item.Update(new DevToolProgress(), default);
        Assert.False(outcome.Succeeded); Assert.Contains("denied", outcome.Message, StringComparison.Ordinal); replay.Complete();
    }

    [Fact]
    public async Task Default_registry_seam_is_closed() => await Assert.ThrowsAsync<InvalidOperationException>(() => new OfficeC2RUpdater().InstalledVersion(default));

    [Fact]
    public async Task Unchanged_Office_is_polled_for_at_most_two_minutes_with_one_updater_launch()
    {
        // ENGINEERING DECISION: 12 ten-second verification waits, injected time; never sleep in a test.
        var replay = new TrialHReplay(); replay.Add("37-office-upgrade");
        var before = TrialHFixtures.Office("34-office-before"); var reads = 0; var waits = new List<TimeSpan>();
        var item = new OfficeC2RUpdater(_ => { reads++; return Task.FromResult<OfficeInstallation?>(before); }, replay.Run,
            (span, _) => { waits.Add(span); return Task.CompletedTask; });
        var outcome = await item.Update(new DevToolProgress(), default);
        Assert.False(outcome.Succeeded); Assert.Equal(14, reads);
        Assert.Equal(12, waits.Count); Assert.All(waits, span => Assert.Equal(TimeSpan.FromSeconds(10), span));
        replay.Complete();
    }

    [Fact]
    public async Task Delayed_Office_version_change_is_verified_and_stops_polling()
    {
        var replay = new TrialHReplay(); replay.Add("37-office-upgrade");
        var before = TrialHFixtures.Office("34-office-before"); var reads = 0; var waits = 0;
        var item = new OfficeC2RUpdater(_ => Task.FromResult<OfficeInstallation?>(++reads < 4 ? before : before with { Version = "16.0.20430.20100" }), replay.Run,
            (_, _) => { waits++; return Task.CompletedTask; });
        Assert.True((await item.Update(new DevToolProgress(), default)).Succeeded);
        Assert.Equal(4, reads); Assert.Equal(2, waits); replay.Complete();
    }

    [Fact]
    public async Task Cancellation_during_Office_verification_stops_polling_without_relaunch_or_success()
    {
        var replay = new TrialHReplay(); replay.Add("37-office-upgrade");
        var before = TrialHFixtures.Office("34-office-before"); var reads = 0;
        using var cancel = new CancellationTokenSource(); var token = cancel.Token;
        var item = new OfficeC2RUpdater(c => { Assert.Equal(token, c); reads++; return Task.FromResult<OfficeInstallation?>(before); },
            (command, c) => { Assert.Equal(token, c); return replay.Run(command, c); },
            (_, c) => { Assert.Equal(token, c); cancel.Cancel(); c.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => item.Update(new DevToolProgress(), token));
        Assert.Equal(2, reads); replay.Complete();
    }

    [Fact]
    public async Task A_valid_Office_installation_still_cannot_reach_the_default_process_runner()
    {
        var before = TrialHFixtures.Office("34-office-before");
        var item = new OfficeC2RUpdater(_ => Task.FromResult<OfficeInstallation?>(before));
        await Assert.ThrowsAsync<InvalidOperationException>(() => item.Update(new DevToolProgress(), default));
    }

    [Fact]
    public async Task Cancellation_before_Office_update_reads_nothing_and_launches_nothing()
    {
        var reads = 0; var replay = new TrialHReplay();
        var item = new OfficeC2RUpdater(_ => { reads++; return Task.FromResult<OfficeInstallation?>(TrialHFixtures.Office("34-office-before")); }, replay.Run);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => item.Update(new DevToolProgress(), new(true)));
        Assert.Equal(0, reads); Assert.Empty(replay.Calls);
    }
}
