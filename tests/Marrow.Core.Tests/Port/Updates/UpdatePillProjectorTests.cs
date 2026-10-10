using Marrow.Core.Port.Updates;

namespace Marrow.Core.Tests.Port.Updates;

// Unit UE-5 (BUILD-PLAN.md 2.4 UE-5; OD-0100 with its pick: place D, "a glass pill above Settings in the main window, shown only when something is out of date, with the run's
// progress ring and a popover list", words U1 "Update everything / Up to date / Check now / Restart Marrow"). The projector is pure: it reads the orchestrator's rows, so no CLI runs and
// no network is reached. The words are the design page's U1 set (update-everything/src/ue-page.js, WORDS.U1), the ENGINEERING DECISIONS are in ED-344.
public sealed class UpdatePillProjectorTests
{
    private static UpdateRow Row(string id, string title, UpdateRowState state, string? installed = "1.0.0", string? latest = "1.1.0", double? progress = null, string? message = null, bool restart = false, UpdateItemKind kind = UpdateItemKind.ProviderCli) =>
        new(id, title, kind, state, installed, latest, progress, message, restart);

    private static UpdatePillState Project(params UpdateRow[] rows) => new UpdatePillProjector().Project(rows, busy: false);

    [Fact]
    [Trait("Feature", "UE-5")]
    [Trait("Rule", "R-TEST-4")]
    public void Nothing_out_of_date_hides_the_pill()
    {
        var state = Project(
            Row("a", "Claude Code", UpdateRowState.Unchecked, null, null),
            Row("b", "Codex", UpdateRowState.UpToDate, "0.160.0", "0.160.0"),
            Row("c", "OpenCode", UpdateRowState.NotInstalled, null, null),
            Row("d", "Cursor CLI", UpdateRowState.ChecksWhenUpdated, "2026.09.25", null));
        Assert.Equal(UpdatePillPhase.Hidden, state.Phase);
        Assert.Equal(UpdatePillAction.None, state.Action);
    }

    [Fact]
    [Trait("Feature", "UE-5")]
    [Trait("Rule", "R-TEST-4")]
    public void Rows_with_a_newer_version_show_the_count_and_offer_Update_everything()
    {
        var state = Project(
            Row("m", "nth", UpdateRowState.UpdateAvailable, "1.0.0", "1.1.0", kind: UpdateItemKind.Marrow),
            Row("c", "Claude Code", UpdateRowState.UpdateAvailable, "2.1.283", "2.1.290"),
            Row("x", "Codex", UpdateRowState.UpdateAvailable, "0.157.1", "0.160.0"),
            Row("o", "OpenCode", UpdateRowState.UpToDate, "1.4.2", "1.4.2"));
        Assert.Equal(UpdatePillPhase.Available, state.Phase);
        Assert.Equal("3 updates", state.PillText);
        Assert.Equal("3 updates available", state.Title);
        Assert.Equal("Update everything", state.ButtonText);
        Assert.Equal(UpdatePillAction.UpdateEverything, state.Action);
        Assert.True(state.ButtonEnabled);
        Assert.Equal("nth 1.1.0, Claude Code 2.1.290 and Codex 0.160.0.", state.Detail);
    }

    [Fact]
    [Trait("Feature", "UE-5")]
    [Trait("Rule", "R-TEST-4")]
    public void One_update_reads_in_the_singular()
    {
        var state = Project(Row("c", "Claude Code", UpdateRowState.UpdateAvailable, "2.1.283", "2.1.290"));
        Assert.Equal("1 update", state.PillText);
        Assert.Equal("1 update available", state.Title);
        Assert.Equal("Claude Code 2.1.290.", state.Detail);
    }

    [Fact]
    [Trait("Feature", "UE-5")]
    [Trait("Rule", "R-TEST-4")]
    public void While_a_run_goes_the_pill_counts_the_item_in_hand_and_the_ring_counts_the_finished_ones()
    {
        var projector = new UpdatePillProjector();
        var queued = new[]
        {
            Row("c", "Claude Code", UpdateRowState.Queued), Row("x", "Codex", UpdateRowState.Queued), Row("m", "nth", UpdateRowState.Queued, kind: UpdateItemKind.Marrow),
        };
        var first = projector.Project(queued, busy: true);
        Assert.Equal(UpdatePillPhase.Running, first.Phase);
        Assert.Equal("Updating 1 of 3", first.PillText);
        Assert.Equal(0, first.Fraction);
        Assert.Equal("Updating…", first.ButtonText);
        Assert.False(first.ButtonEnabled);
        Assert.Equal(UpdatePillAction.None, first.Action);

        var second = projector.Project(
        [
            Row("c", "Claude Code", UpdateRowState.Updated, "2.1.290", "2.1.290"),
            Row("x", "Codex", UpdateRowState.Updating, "0.157.1", "0.160.0"),
            Row("m", "nth", UpdateRowState.Queued, kind: UpdateItemKind.Marrow),
        ], busy: true);
        Assert.Equal("Updating 2 of 3", second.PillText);
        Assert.Equal("Updating Codex… 2 of 3", second.Title);
        Assert.Equal("Keep working; nth restarts only when you say so.", second.Detail);
        Assert.Equal(1d / 3, second.Fraction, 6);

        var third = projector.Project(
        [
            Row("c", "Claude Code", UpdateRowState.Updated, "2.1.290", "2.1.290"),
            Row("x", "Codex", UpdateRowState.UpToDate, "0.160.0", "0.160.0"),
            Row("m", "nth", UpdateRowState.Updating, "1.0.0", "1.1.0", progress: 0.5, kind: UpdateItemKind.Marrow),
        ], busy: true);
        Assert.Equal("Updating 3 of 3", third.PillText);
        Assert.Equal((2 + 0.5) / 3, third.Fraction, 6);
    }

    [Fact]
    [Trait("Feature", "UE-5")]
    [Trait("Rule", "R-TEST-4")]
    public void A_retry_run_counts_only_the_items_it_runs()
    {
        var projector = new UpdatePillProjector();
        projector.Project(
        [
            Row("c", "Claude Code", UpdateRowState.Queued), Row("x", "Codex", UpdateRowState.Queued),
        ], busy: true);
        projector.Project(
        [
            Row("c", "Claude Code", UpdateRowState.Updated, "2.1.290", "2.1.290"), Row("x", "Codex", UpdateRowState.Failed, "0.157.1", "0.160.0", message: "the updater failed"),
        ], busy: false);
        var retry = projector.Project(
        [
            Row("c", "Claude Code", UpdateRowState.Updated, "2.1.290", "2.1.290"), Row("x", "Codex", UpdateRowState.Updating, "0.157.1", "0.160.0"),
        ], busy: true);
        Assert.Equal("Updating 1 of 1", retry.PillText);
        Assert.Equal("Updating Codex… 1 of 1", retry.Title);
    }

    [Fact]
    [Trait("Feature", "UE-5")]
    [Trait("Rule", "R-TEST-4")]
    public void A_failed_item_names_itself_offers_Retry_and_carries_its_reason_in_a_banner()
    {
        var state = Project(
            Row("c", "Claude Code", UpdateRowState.Updated, "2.1.290", "2.1.290"),
            Row("x", "Codex", UpdateRowState.Failed, "0.157.1", "0.160.0", message: "the updater failed or the version could not be read afterwards"),
            Row("m", "nth", UpdateRowState.Updated, "1.1.0", "1.1.0", restart: true, kind: UpdateItemKind.Marrow));
        Assert.Equal(UpdatePillPhase.Failed, state.Phase);
        Assert.Equal("Codex didn’t update", state.PillText);
        Assert.Equal("Codex didn’t update", state.Title);
        Assert.Equal("Updated Claude Code and nth. The fix is below.", state.Detail);
        Assert.Equal("Retry", state.ButtonText);
        Assert.Equal(UpdatePillAction.Retry, state.Action);
        Assert.Equal("Codex didn’t update", state.BannerTitle);
        Assert.Equal("the updater failed or the version could not be read afterwards", state.BannerText);
    }

    [Fact]
    [Trait("Feature", "UE-5")]
    [Trait("Rule", "R-TEST-4")]
    public void A_failure_without_a_reason_says_the_version_it_is_still_at()
    {
        var state = Project(Row("x", "Codex", UpdateRowState.Failed, "0.157.1", "0.160.0"));
        Assert.Equal("Codex is still 0.157.1 after its updater ran. Retry.", state.BannerText);
        Assert.Equal("The fix is below.", state.Detail);
    }

    [Fact]
    [Trait("Feature", "UE-5")]
    [Trait("Rule", "R-TEST-4")]
    public void When_Marrow_needs_a_restart_the_pill_says_so_and_offers_Restart_Marrow()
    {
        var state = Project(
            Row("c", "Claude Code", UpdateRowState.Updated, "2.1.290", "2.1.290"),
            Row("m", "nth", UpdateRowState.Updated, "1.1.0", "1.1.0", restart: true, kind: UpdateItemKind.Marrow));
        Assert.Equal(UpdatePillPhase.Restart, state.Phase);
        Assert.Equal("Restart to finish", state.PillText);
        Assert.Equal("2 updated", state.Title);
        Assert.Equal("Updated Claude Code and nth. Restart nth to finish its update.", state.Detail);
        Assert.Equal("Restart nth", state.ButtonText);
        Assert.Equal(UpdatePillAction.RestartMarrow, state.Action);
        Assert.Equal("Restart to finish", state.Rows.Single(r => r.Id == "m").Status);
        Assert.Equal("Updated", state.Rows.Single(r => r.Id == "c").Status);
    }

    [Fact]
    [Trait("Feature", "UE-5")]
    [Trait("Rule", "R-TEST-4")]
    public void A_run_that_needs_no_restart_stays_as_the_done_phase_with_the_designs_done_words()
    {
        var state = Project(
            Row("c", "Claude Code", UpdateRowState.Updated, "2.1.290", "2.1.290"),
            Row("x", "Codex", UpdateRowState.UpToDate, "0.160.0", "0.160.0"));
        Assert.Equal(UpdatePillPhase.Done, state.Phase);
        Assert.Equal("1 updated", state.PillText);
        Assert.Equal("1 updated", state.Title);
        Assert.Equal("Updated Claude Code.", state.Detail);
        Assert.Equal("Check now", state.ButtonText);
        Assert.Equal(UpdatePillAction.CheckNow, state.Action);
        Assert.True(state.ButtonEnabled);
    }

    [Fact]
    [Trait("Feature", "UE-5")]
    [Trait("Rule", "R-TEST-4")]
    public void The_done_phase_leaves_once_it_is_acknowledged_and_comes_back_with_the_next_run()
    {
        var projector = new UpdatePillProjector();
        var done = new[] { Row("c", "Claude Code", UpdateRowState.Updated, "2.1.290", "2.1.290"), Row("x", "Codex", UpdateRowState.Updated, "0.160.0", "0.160.0") };
        Assert.Equal("2 updated", projector.Project(done, busy: false).Title);
        Assert.Equal("Updated Claude Code and Codex.", projector.Project(done, busy: false).Detail);
        projector.Acknowledge();
        Assert.Equal(UpdatePillPhase.Hidden, projector.Project(done, busy: false).Phase);
        projector.Project([Row("c", "Claude Code", UpdateRowState.Queued)], busy: true);
        Assert.Equal(UpdatePillPhase.Done, projector.Project(done, busy: false).Phase);
    }

    [Fact]
    [Trait("Feature", "UE-5")]
    [Trait("Rule", "R-TEST-4")]
    public void A_check_that_finds_the_updated_items_current_ends_the_done_phase()
    {
        var state = Project(Row("c", "Claude Code", UpdateRowState.UpToDate, "2.1.290", "2.1.290"));
        Assert.Equal(UpdatePillPhase.Hidden, state.Phase);
    }

    [Fact]
    [Trait("Feature", "UE-5")]
    [Trait("Rule", "R-TEST-4")]
    public void A_failure_outranks_an_available_update_and_a_restart()
    {
        var state = Project(
            Row("c", "Claude Code", UpdateRowState.UpdateAvailable, "2.1.283", "2.1.290"),
            Row("x", "Codex", UpdateRowState.Failed, "0.157.1", "0.160.0", message: "no"),
            Row("m", "nth", UpdateRowState.Updated, "1.1.0", "1.1.0", restart: true, kind: UpdateItemKind.Marrow));
        Assert.Equal(UpdatePillPhase.Failed, state.Phase);
        Assert.Equal(UpdatePillAction.Retry, state.Action);
    }

    [Fact]
    [Trait("Feature", "UE-5")]
    [Trait("Rule", "R-TEST-4")]
    public void The_list_puts_Marrow_first_and_each_row_carries_the_designs_sub_line()
    {
        var state = Project(
            Row("cli:claude", "Claude Code", UpdateRowState.UpdateAvailable, "2.1.283", "2.1.290"),
            Row("marrow", "nth", UpdateRowState.UpdateAvailable, "1.0.0", "1.1.0", kind: UpdateItemKind.Marrow));
        Assert.Equal(["marrow", "cli:claude"], state.Rows.Select(r => r.Id));
        Assert.Equal(["The app", "claude update"], state.Rows.Select(r => r.Subtitle));
    }

    [Fact]
    [Trait("Feature", "UE-5")]
    [Trait("Rule", "R-TEST-4")]
    public void Marrows_sub_line_reads_the_download_percent_while_it_downloads()
    {
        var projector = new UpdatePillProjector();
        var state = projector.Project([Row("marrow", "nth", UpdateRowState.Updating, "1.0.0", "1.1.0", progress: 0.4, kind: UpdateItemKind.Marrow)], busy: true);
        Assert.Equal("Downloading 40%", state.Rows.Single().Subtitle);
    }

    [Fact]
    [Trait("Feature", "UE-5")]
    [Trait("Rule", "R-TEST-4")]
    public void The_popover_lists_installed_rows_with_the_design_words_for_each_state()
    {
        var state = Project(
            Row("a", "Unchecked one", UpdateRowState.Unchecked, null, null),
            Row("b", "Gone", UpdateRowState.NotInstalled, null, null),
            Row("c", "Newer", UpdateRowState.UpdateAvailable, "1.0.0", "1.1.0"),
            Row("d", "Same", UpdateRowState.UpToDate, "1.4.2", "1.4.2"),
            Row("e", "No listing", UpdateRowState.ChecksWhenUpdated, "2026.09.25", null),
            Row("f", "Waiting one", UpdateRowState.Queued, "1.0.0", "1.1.0"),
            Row("g", "Busy one", UpdateRowState.Updating, "1.0.0", "1.1.0", progress: 0.4),
            Row("h", "Done one", UpdateRowState.Updated, "1.1.0", "1.1.0"),
            Row("i", "Broken", UpdateRowState.Failed, "1.0.0", "1.1.0", message: "no"));
        Assert.Equal(["c", "d", "e", "f", "g", "h", "i"], state.Rows.Select(r => r.Id));
        Assert.Equal(["Update available", "Up to date", "Checks when updated", "Waiting", "Updating…", "Updated", "Didn’t update"], state.Rows.Select(r => r.Status));
        var newer = state.Rows[0];
        Assert.Equal(("1.0.0", "1.1.0"), (newer.Installed, newer.Latest));
        Assert.True(newer.ShowsLatest);
        var same = state.Rows[1];
        Assert.Equal("1.4.2", same.Installed);
        Assert.False(same.ShowsLatest);
        Assert.False(state.Rows[2].ShowsLatest);
        Assert.Equal(0.4, state.Rows[4].Progress);
        Assert.Equal("no", state.Rows[6].Message);
    }

    [Fact]
    [Trait("Feature", "UE-5")]
    [Trait("Rule", "R-TEST-4")]
    public void Checking_without_a_run_keeps_the_state_the_rows_are_in()
    {
        var rows = new[] { Row("c", "Claude Code", UpdateRowState.UpdateAvailable, "2.1.283", "2.1.290") };
        Assert.Equal(UpdatePillPhase.Available, new UpdatePillProjector().Project(rows, busy: true).Phase);
    }
}
