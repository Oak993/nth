using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Updates;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Updates;

[Trait("Feature", "UE-3")]
[Trait("Rule", "R-TEST-4")]
public sealed class WingetUpdaterTests
{
    [Fact]
    public void Git_installed_and_available_versions_come_from_the_recorded_list()
    {
        var p = Assert.Single(WingetUpdater.ParseList(TrialHFixtures.Result("04-git-list")));
        Assert.Equal(("Git.Git", "Git", "2.54.0", "2.55.0.5", "winget"), (p.Id, p.Title, p.Version, p.Available, p.Source));
    }

    [Fact]
    public void Missing_GitHub_CLI_is_not_an_installed_row() => Assert.Empty(WingetUpdater.ParseList(TrialHFixtures.Result("12-gh-list")));

    [Fact]
    public void Node_keeps_the_actual_winget_match_without_selecting_another_major()
    {
        var p = Assert.Single(WingetUpdater.ParseList(TrialHFixtures.Result("16-node-list")));
        Assert.Equal(("OpenJS.NodeJS.22", "24.14.1", null), (p.Id, p.Version, p.Available));
    }

    [Fact]
    public void Python_rows_are_the_two_CPython_installs_without_Conda_Store_manager_or_launcher()
    {
        var p = WingetUpdater.ParseList(TrialHFixtures.Result("24-python-list"));
        Assert.Equal(["Python.Python.3.13", "Python.Python.3.14"], p.Select(v => v.Id));
        Assert.Equal(["3.13.14", "3.14.4"], p.Select(v => v.Version));
        Assert.Equal(["3.13.15", "3.14.7"], p.Select(v => v.Available));
    }

    [Theory]
    [InlineData("07-git-upgrade", "Git.Git")]
    [InlineData("19-node-upgrade", "OpenJS.NodeJS.22")]
    [InlineData("28-python314-upgrade", "Python.Python.3.14")]
    [InlineData("32-python313-upgrade", "Python.Python.3.13")]
    public void Upgrade_argv_is_byte_equal_to_the_single_tool_trial(string fixture, string id)
    {
        var actual = WingetUpdater.UpgradeCommand(id);
        var expected = TrialHFixtures.Command(fixture);
        Assert.Equal(expected.Executable, actual.Executable);
        Assert.Equal(expected.Arguments, actual.Arguments);
    }

    [Theory]
    [InlineData("Git.Git --all")]
    [InlineData("Python.Launcher")]
    [InlineData("Anaconda.Miniconda3")]
    [InlineData("9NQ7512CXL7T")]
    public void An_untrialled_or_injected_package_id_is_refused(string id) => Assert.Throws<ArgumentException>(() => WingetUpdater.UpgradeCommand(id));

    [Fact]
    public async Task Git_installer_failure_stays_failed_even_though_its_version_can_be_read()
    {
        var replay = new TrialHReplay();
        replay.Add("04-git-list"); replay.Add("07-git-upgrade"); replay.Add("04-git-list");
        var progress = new DevToolProgress();
        var result = await new WingetUpdater("Git.Git", "Git", replay.Run).Update(progress, default);
        Assert.False(result.Succeeded); Assert.False(result.Changed); Assert.False(result.RestartRequired);
        Assert.Equal("2.54.0", result.NewVersion);
        Assert.Contains("Installer failed with exit code: 1", result.Message, StringComparison.Ordinal);
        Assert.Equal(new double?[] { null }, progress.Values);
        replay.Complete();
    }

    [Fact]
    public async Task No_applicable_Node_update_is_successful_and_unchanged()
    {
        var replay = new TrialHReplay();
        replay.Add("16-node-list"); replay.Add("19-node-upgrade"); replay.Add("16-node-list");
        var result = await new WingetUpdater("OpenJS.NodeJS.22", "Node.js", replay.Run, ["--name", "Node.js"]).Update(new DevToolProgress(), default);
        Assert.True(result.Succeeded); Assert.False(result.Changed); Assert.Equal("24.14.1", result.NewVersion);
        replay.Complete();
    }

    [Fact]
    public async Task Python_update_verifies_the_new_version_and_drives_UE2_to_Updated()
    {
        var replay = new TrialHReplay();
        replay.Add("24-python-list"); replay.Add("24-python-list"); replay.Add("24-python-list");
        replay.Add("28-python314-upgrade"); replay.Add("41-python-list-after"); replay.Add("41-python-list-after");
        var item = new WingetUpdater("Python.Python.3.14", "Python 3.14", replay.Run, ["--name", "Python"]);
        var orchestrator = new UpdateOrchestrator([item]);
        await orchestrator.UpdateEverything();
        var row = Assert.Single(orchestrator.Rows);
        Assert.Equal(UpdateItemKind.DevTool, row.Kind); Assert.Equal(UpdateRowState.Updated, row.State);
        Assert.Equal("3.14.7", row.Installed); Assert.False(row.RestartRequired);
        replay.Complete();
    }

    [Fact]
    public async Task A_success_exit_without_a_readable_after_version_cannot_claim_an_update()
    {
        var replay = new TrialHReplay(); replay.Add("04-git-list");
        replay.Add(WingetUpdater.UpgradeCommand("Git.Git"), new(0, "Successfully installed\r\n", ""));
        replay.Add("12-gh-list", TrialHFixtures.Command("04-git-list"));
        var result = await new WingetUpdater("Git.Git", "Git", replay.Run).Update(new DevToolProgress(), default);
        Assert.False(result.Succeeded); Assert.False(result.Changed); Assert.Null(result.NewVersion);
        replay.Complete();
    }

    [Fact]
    public void An_unrecognised_failure_is_not_the_no_update_exit() => Assert.Throws<InvalidOperationException>(() => WingetUpdater.ParseList(new(-1, "offline", "source error")));

    [Fact]
    public void Unparseable_success_output_is_not_an_empty_installed_inventory() => Assert.Throws<InvalidDataException>(() => WingetUpdater.ParseList(new(0, "truncated", "")));

    [Fact]
    public async Task A_zero_exit_with_a_still_outdated_installed_version_does_not_claim_UpToDate()
    {
        var replay = new TrialHReplay(); replay.Add("04-git-list");
        replay.Add(WingetUpdater.UpgradeCommand("Git.Git"), new(0, "Successfully installed\r\n", ""));
        replay.Add("04-git-list");
        var result = await new WingetUpdater("Git.Git", "Git", replay.Run).Update(new DevToolProgress(), default);
        Assert.False(result.Succeeded); Assert.False(result.Changed); Assert.Equal("2.54.0", result.NewVersion);
        replay.Complete();
    }

    [Fact]
    public async Task Missing_tool_is_never_installed_by_an_update_call()
    {
        var replay = new TrialHReplay(); replay.Add("12-gh-list");
        var result = await new WingetUpdater("GitHub.cli", "GitHub CLI", replay.Run).Update(new DevToolProgress(), default);
        Assert.False(result.Succeeded); Assert.Null(result.NewVersion);
        Assert.Single(replay.Calls); replay.Complete();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public async Task Null_or_unknown_upgrade_exit_is_failed_with_readable_unchanged_version(int? exit)
    {
        var replay = new TrialHReplay(); replay.Add("04-git-list");
        replay.Add(WingetUpdater.UpgradeCommand("Git.Git"), new(exit, "", "spawn or source failure")); replay.Add("04-git-list");
        Assert.False((await new WingetUpdater("Git.Git", "Git", replay.Run).Update(new DevToolProgress(), default)).Succeeded); replay.Complete();
    }

    [Fact]
    public async Task No_update_exit_with_a_contradictory_available_version_is_not_current()
    {
        var replay = new TrialHReplay(); replay.Add("04-git-list");
        replay.Add("19-node-upgrade", WingetUpdater.UpgradeCommand("Git.Git")); replay.Add("04-git-list");
        Assert.False((await new WingetUpdater("Git.Git", "Git", replay.Run).Update(new DevToolProgress(), default)).Succeeded); replay.Complete();
    }

    [Theory]
    [InlineData(0, "truncated")]
    [InlineData(-1, "source unavailable")]
    public async Task Unreadable_post_update_inventory_is_a_failed_result(int exit, string output)
    {
        var replay = new TrialHReplay(); replay.Add("04-git-list");
        replay.Add(WingetUpdater.UpgradeCommand("Git.Git"), new(0, "Successfully installed\r\n", ""));
        replay.Add(TrialHFixtures.Command("04-git-list"), new(exit, output, ""));
        var result = await new WingetUpdater("Git.Git", "Git", replay.Run).Update(new DevToolProgress(), default);
        Assert.False(result.Succeeded); Assert.Null(result.NewVersion); replay.Complete();
    }

    [Fact]
    public async Task Python_313_success_replays_its_recorded_updater_and_installed_version()
    {
        var replay = new TrialHReplay(); replay.Add("24-python-list"); replay.Add("32-python313-upgrade"); replay.Add("41-python-list-after");
        var result = await new WingetUpdater("Python.Python.3.13", "Python 3.13", replay.Run, ["--name", "Python"]).Update(new DevToolProgress(), default);
        Assert.True(result.Succeeded); Assert.True(result.Changed); Assert.Equal("3.13.15", result.NewVersion); replay.Complete();
    }

    [Fact]
    public void Duplicate_package_matches_are_ambiguous_and_refused()
    {
        var original = TrialHFixtures.Result("04-git-list");
        Assert.Throws<InvalidDataException>(() => WingetUpdater.ParseList(original with { Stdout = original.Stdout + "Git  Git.Git 2.54.0  2.55.0.5  winget\r\n" }));
    }

    [Fact]
    public async Task A_package_from_a_different_source_never_runs_the_winget_source_upgrade()
    {
        var replay = new TrialHReplay(); var r = TrialHFixtures.Result("04-git-list");
        replay.Add(TrialHFixtures.Command("04-git-list"), r with { Stdout = r.Stdout.Replace("winget", "msstore", StringComparison.Ordinal) });
        var result = await new WingetUpdater("Git.Git", "Git", replay.Run).Update(new DevToolProgress(), default);
        Assert.False(result.Succeeded); Assert.Single(replay.Calls); replay.Complete();
    }

    [Fact]
    public async Task Installed_GitHub_CLI_has_a_row_but_its_untrialled_upgrade_is_refused()
    {
        var replay = new TrialHReplay();
        replay.Add(TrialHFixtures.Command("12-gh-list"), new(0, "Name       Id         Version Available Source\r\n---------------------------------------------\r\nGitHub CLI GitHub.cli 2.100.0 2.102.0   winget\r\n", ""));
        var item = new WingetUpdater("GitHub.cli", "GitHub CLI", replay.Run);
        Assert.False((await item.Update(new DevToolProgress(), default)).Succeeded);
        Assert.Equal(UpdateItemKind.DevTool, item.Kind); Assert.Single(replay.Calls); replay.Complete();
    }

    [Fact]
    public async Task Default_process_seam_throws_before_any_real_winget_can_run() => await Assert.ThrowsAsync<InvalidOperationException>(() => new WingetUpdater("Git.Git", "Git").InstalledVersion(default));

    [Fact]
    public async Task Cancellation_before_update_runs_no_command()
    {
        var replay = new TrialHReplay();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WingetUpdater("Git.Git", "Git", replay.Run).Update(new DevToolProgress(), new(true)));
        Assert.Empty(replay.Calls);
    }

    [Fact]
    public async Task Node_without_an_available_version_is_checked_when_updated_and_replays_the_no_update_result()
    {
        var replay = new TrialHReplay(); replay.Add("16-node-list"); replay.Add("16-node-list");
        var orchestrator = new UpdateOrchestrator([new WingetUpdater("OpenJS.NodeJS.22", "Node.js", replay.Run, ["--name", "Node.js"])]);
        await orchestrator.CheckNow();
        Assert.Equal(UpdateRowState.ChecksWhenUpdated, Assert.Single(orchestrator.Rows).State);
        Assert.False(orchestrator.IsEverythingCurrent);
        replay.Add("16-node-list"); replay.Add("19-node-upgrade"); replay.Add("16-node-list"); replay.Add("16-node-list");
        await orchestrator.UpdateEverything();
        Assert.Equal(UpdateRowState.UpToDate, Assert.Single(orchestrator.Rows).State); replay.Complete();
    }

    [Theory]
    [InlineData("Git Git.Git Unknown winget")]
    [InlineData("Git Git.Git 2.54.0")]
    public void A_table_with_a_malformed_known_package_row_is_not_an_empty_inventory(string row)
    {
        var output = "Name Id Version Available Source\r\n---------------------------------\r\n" + row + "\r\n";
        Assert.Throws<InvalidDataException>(() => WingetUpdater.ParseList(new(0, output, "")));
    }

    [Fact]
    public async Task Losing_the_available_column_after_a_zero_exit_does_not_erase_the_known_target_version()
    {
        var replay = new TrialHReplay(); replay.Add("04-git-list");
        replay.Add(WingetUpdater.UpgradeCommand("Git.Git"), new(0, "Successfully installed\r\n", ""));
        replay.Add(TrialHFixtures.Command("04-git-list"), new(0, "Name Id Version Source\r\n------------------------\r\nGit Git.Git 2.54.0 winget\r\n", ""));
        var result = await new WingetUpdater("Git.Git", "Git", replay.Run).Update(new DevToolProgress(), default);
        Assert.False(result.Succeeded); Assert.False(result.Changed); Assert.Equal("2.54.0", result.NewVersion); replay.Complete();
    }
}
