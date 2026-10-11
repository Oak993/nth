using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Updates;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Updates;

[Trait("Feature", "UE-3")]
[Trait("Rule", "R-TEST-4")]
public sealed class LeadDevToolRulingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Proven_Git_in_use_survives_an_unreadable_or_missing_post_update_inventory(bool missing)
    {
        var replay = new TrialHReplay(); replay.Add("04-git-list");
        replay.Add(TrialHFixtures.Command("07-git-upgrade"), TrialHFixtures.Result("07-git-upgrade") with { InstallerLog = TrialHFixtures.Result("09-git-installer-log").Stdout });
        replay.Add(TrialHFixtures.Command("04-git-list"), missing ? new(WingetUpdater.NoInstalledPackage, "", "") : new(-1, "", "inventory unavailable"));
        var outcome = await new WingetUpdater("Git.Git", "Git", replay.Run).Update(new DevToolProgress(), default);
        Assert.False(outcome.Succeeded); Assert.Null(outcome.NewVersion);
        Assert.Equal("Git Bash is running. Close it and try again.", outcome.Message); replay.Complete(); // ED-NEW-providers-git-1: the installer's own list (bash.exe, tail.exe, grep.exe...) names what holds Git
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Cancellation_after_Office_acceptance_preserves_pending_until_a_later_verified_version(int cancelAt)
    {
        var replay = new TrialHReplay(); replay.Add("37-office-upgrade");
        var before = TrialHFixtures.Office("34-office-before"); var reads = 0; var complete = false;
        using var cancel = new CancellationTokenSource();
        var runner = new UpdateOrchestrator([new OfficeC2RUpdater(c =>
        {
            if (++reads == cancelAt) { cancel.Cancel(); c.ThrowIfCancellationRequested(); }
            return Task.FromResult<OfficeInstallation?>(complete ? before with { Version = "16.0.20430.20100" } : before);
        }, replay.Run)]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.UpdateEverything(cancel.Token));
        var row = Assert.Single(runner.Rows);
        Assert.Equal(UpdateRowState.UpdatingInBackground, row.State); Assert.Equal(before.Version, row.Installed);
        Assert.Equal("Updating in the background", row.Message); Assert.False(runner.IsBusy); Assert.False(runner.IsEverythingCurrent);
        await runner.UpdateEverything(); Assert.Single(replay.Calls);
        complete = true; await runner.RecheckBackground();
        Assert.Equal(UpdateRowState.Updated, Assert.Single(runner.Rows).State); Assert.Single(replay.Calls); replay.Complete();
    }

    [Fact]
    public async Task Recorded_Git_in_use_log_yields_the_lead_message_and_Retry_state()
    {
        var replay = new TrialHReplay();
        var upgrade = TrialHFixtures.Result("07-git-upgrade") with { InstallerLog = TrialHFixtures.Result("09-git-installer-log").Stdout };
        replay.Add("04-git-list"); replay.Add("04-git-list"); replay.Add("04-git-list");
        replay.Add(TrialHFixtures.Command("07-git-upgrade"), upgrade); replay.Add("04-git-list"); replay.Add("04-git-list");
        var runner = new UpdateOrchestrator([new WingetUpdater("Git.Git", "Git", replay.Run)]);
        await runner.UpdateEverything();
        var row = Assert.Single(runner.Rows);
        Assert.Equal(UpdateRowState.Failed, row.State);
        Assert.Equal("Git Bash is running. Close it and try again.", row.Message); // ED-NEW-providers-git-1
        // Retry launches exactly one fresh witnessed command; it does not stop any holding process.
        replay.Add("04-git-list"); replay.Add(TrialHFixtures.Command("07-git-upgrade"), upgrade); replay.Add("04-git-list"); replay.Add("04-git-list");
        await runner.Retry(row.Id);
        Assert.Equal(2, replay.Calls.Count(c => c.Arguments.FirstOrDefault() == "upgrade")); replay.Complete();
    }

    [Fact]
    public async Task A_generic_Git_failure_is_not_misclassified_as_an_in_use_error()
    {
        var replay = new TrialHReplay(); replay.Add("04-git-list"); replay.Add("07-git-upgrade"); replay.Add("04-git-list");
        var result = await new WingetUpdater("Git.Git", "Git", replay.Run).Update(new DevToolProgress(), default);
        Assert.Contains("Installer failed", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("in use by another app", result.Message, StringComparison.Ordinal); Assert.DoesNotContain(" running", result.Message, StringComparison.Ordinal); replay.Complete();
    }

    [Fact]
    public async Task Git_failure_enrichment_reads_only_the_reported_log_and_retains_both_original_streams()
    {
        var result = TrialHFixtures.Result("07-git-upgrade"); var reads = 0;
        var enriched = await GitInstallerLog.Attach(TrialHFixtures.Command("07-git-upgrade"), result, (path, _) =>
        {
            reads++; Assert.EndsWith("Git.Git.2.55.0.5-26-10-05-18-25-26.log", path, StringComparison.Ordinal);
            return Task.FromResult<string?>(TrialHFixtures.Result("09-git-installer-log").Stdout);
        }, default);
        Assert.Equal(1, reads); Assert.Equal(result.Stdout, enriched.Stdout); Assert.Equal(result.Stderr, enriched.Stderr);
        Assert.Contains("The following process(es) use Git for Windows", enriched.InstallerLog, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("C:\\logs\\Git.Git.2.55.0.5.log", true)]
    [InlineData("C:\\logs\\..\\private\\Git.Git.2.55.log", false)]
    [InlineData("C:\\logs\\credentials.log", false)]
    [InlineData("C:\\other\\Git.Git.2.55.log", false)]
    public void Git_log_reader_is_limited_to_its_diagnostic_directory_and_package_name(string path, bool accepted)
    {
        var resolved = GitInstallerLog.ValidatePath(path, "C:\\logs");
        Assert.Equal(accepted, resolved is not null);
    }

    [Fact]
    public void Developer_tool_bindings_have_no_process_termination_API_or_command()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CLAUDE.md"))) root = root.Parent;
        var source = string.Join('\n', Directory.EnumerateFiles(Path.Combine(root!.FullName, "src", "Marrow.Services", "Port", "Updates"), "*.cs").Select(File.ReadAllText));
        Assert.DoesNotMatch(@"(?i)\.Kill\s*\(|taskkill|Stop-Process|TerminateProcess", source);
    }

    [Fact]
    public async Task Office_pending_rechecks_versions_later_without_launching_another_updater()
    {
        var replay = new TrialHReplay(); replay.Add("37-office-upgrade");
        var current = TrialHFixtures.Office("34-office-before"); var reads = 0;
        var office = new OfficeC2RUpdater(_ => { reads++; return Task.FromResult<OfficeInstallation?>(current); }, replay.Run);
        var runner = new UpdateOrchestrator([office]);
        await runner.UpdateEverything();
        Assert.Equal(UpdateRowState.UpdatingInBackground, Assert.Single(runner.Rows).State);
        Assert.False(runner.IsEverythingCurrent); Assert.False(runner.IsBusy);
        var previous = reads; await runner.RecheckBackground(); Assert.True(reads > previous);
        Assert.Equal(UpdateRowState.UpdatingInBackground, Assert.Single(runner.Rows).State);
        await runner.UpdateEverything(); Assert.Single(replay.Calls);
        current = current with { Version = "16.0.20430.20100" }; // Synthetic later completion, not a live-trial claim.
        await runner.RecheckBackground();
        var row = Assert.Single(runner.Rows); Assert.Equal(UpdateRowState.Updated, row.State); Assert.Equal(current.Version, row.Installed);
        Assert.Single(replay.Calls); replay.Complete();
    }

    [Fact]
    public async Task Unreadable_later_Office_version_keeps_background_pending_without_false_done()
    {
        var replay = new TrialHReplay(); replay.Add("37-office-upgrade");
        OfficeInstallation? current = TrialHFixtures.Office("34-office-before");
        var runner = new UpdateOrchestrator([new OfficeC2RUpdater(_ => Task.FromResult<OfficeInstallation?>(current), replay.Run)]);
        await runner.UpdateEverything(); current = null;
        await runner.CheckNow(); Assert.Equal(UpdateRowState.UpdatingInBackground, Assert.Single(runner.Rows).State);
        Assert.False(runner.IsEverythingCurrent); Assert.Single(replay.Calls); replay.Complete();
    }

    [Fact]
    public async Task Office_completing_at_the_orchestrators_post_outcome_read_does_not_lose_its_old_baseline()
    {
        var replay = new TrialHReplay(); replay.Add("37-office-upgrade");
        var before = TrialHFixtures.Office("34-office-before"); var reads = 0;
        var runner = new UpdateOrchestrator([new OfficeC2RUpdater(_ => Task.FromResult<OfficeInstallation?>(++reads >= 4 ? before with { Version = "16.0.20430.20100" } : before), replay.Run)]);
        await runner.UpdateEverything();
        Assert.Equal(UpdateRowState.Updated, Assert.Single(runner.Rows).State);
        Assert.Equal("16.0.20430.20100", Assert.Single(runner.Rows).Installed);
        Assert.Single(replay.Calls); replay.Complete();
    }

    [Fact]
    public async Task A_failed_later_registry_read_preserves_pending_and_can_recover_on_a_subsequent_check()
    {
        var replay = new TrialHReplay(); replay.Add("37-office-upgrade");
        var before = TrialHFixtures.Office("34-office-before"); var unavailable = false; var complete = false;
        var runner = new UpdateOrchestrator([new OfficeC2RUpdater(_ => unavailable ? Task.FromException<OfficeInstallation?>(new IOException("registry unavailable"))
            : Task.FromResult<OfficeInstallation?>(complete ? before with { Version = "16.0.20430.20100" } : before), replay.Run)]);
        await runner.UpdateEverything(); unavailable = true; await runner.RecheckBackground();
        var row = Assert.Single(runner.Rows); Assert.Equal(UpdateRowState.UpdatingInBackground, row.State); Assert.Equal(before.Version, row.Installed);
        unavailable = false; complete = true; await runner.RecheckBackground();
        Assert.Equal(UpdateRowState.Updated, Assert.Single(runner.Rows).State); Assert.Single(replay.Calls); replay.Complete();
    }

    [Theory]
    [InlineData("success")]
    [InlineData("other-tool")]
    [InlineData("outside-directory")]
    public async Task Unrelated_successful_or_outside_directory_diagnostics_are_not_read(string kind)
    {
        var command = TrialHFixtures.Command("07-git-upgrade"); var result = TrialHFixtures.Result("07-git-upgrade");
        if (kind == "success") result = result with { ExitCode = 0 };
        if (kind == "other-tool") command = TrialHFixtures.Command("19-node-upgrade");
        if (kind == "outside-directory") result = result with { Stdout = "Installer log is available at: C:\\private\\Git.Git.2.55.log\r\n" };
        var reads = 0;
        var actual = await GitInstallerLog.Attach(command, result, (_, _) => { reads++; return Task.FromResult<string?>("private"); }, default);
        Assert.Equal(0, reads); Assert.Equal(result, actual);
    }

    [Fact]
    public async Task An_unreadable_reported_Git_log_preserves_the_original_generic_failure()
    {
        var result = TrialHFixtures.Result("07-git-upgrade");
        var actual = await GitInstallerLog.Attach(TrialHFixtures.Command("07-git-upgrade"), result, (_, _) => Task.FromException<string?>(new IOException("log unavailable")), default);
        Assert.Equal(result, actual);
    }
}
