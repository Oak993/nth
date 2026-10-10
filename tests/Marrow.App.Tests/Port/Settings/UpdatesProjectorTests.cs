using Marrow.App.Port.Settings;
using Marrow.Core.Port.Updates;

namespace Marrow.App.Tests.Port.Settings;

// Unit UE-4 (BUILD-PLAN.md 2.4 UE-4; OD-0100's pick: place A, words U1 "Update everything / Up to date / Check now / Restart Marrow"). The page's states and words are projected from the
// orchestrator's rows (UE-2, ED-308) the way the picked design page (decisions-2026-10-04/update-everything, ue-page.js texts()) derives them. Pure: no control, no clock, no network.
public sealed class UpdatesProjectorTests
{
    private static UpdateRow Row(string id, string title, UpdateItemKind kind, UpdateRowState state, string? installed = "1.0.0", string? latest = "1.0.0", double? progress = null, string? message = null, bool restart = false) =>
        new(id, title, kind, state, installed, latest, progress, message, restart);

    private static UpdateRow Marrow(UpdateRowState state, string? installed = "1.0.0", string? latest = "1.1.0", double? progress = null, bool restart = false) =>
        Row("marrow", "Nth", UpdateItemKind.Marrow, state, installed, latest, progress, null, restart);

    private static UpdateRow Claude(UpdateRowState state, string? installed = "2.1.283", string? latest = "2.1.290", string? message = null) =>
        Row("cli:claude", "Claude Code", UpdateItemKind.ProviderCli, state, installed, latest, null, message);

    private static UpdateRow Codex(UpdateRowState state, string? installed = "0.157.1", string? latest = "0.160.0", string? message = null) =>
        Row("cli:codex", "Codex", UpdateItemKind.ProviderCli, state, installed, latest, null, message);

    private static UpdatesFlags Quiet(string? checkedAgo = null, IReadOnlySet<string>? runIds = null) => new(false, false, false, false, checkedAgo, runIds);

    // The orchestrator lists CLI rows first and Marrow last (run order); the page lists Marrow first, as the design does.
    private static IReadOnlyList<UpdateRow> AllCurrent() =>
        [Claude(UpdateRowState.UpToDate, "2.1.290", "2.1.290"), Codex(UpdateRowState.UpToDate, "0.160.0", "0.160.0"), Marrow(UpdateRowState.UpToDate, "1.1.0", "1.1.0")];

    private static IReadOnlyList<UpdateRow> AllOut() =>
        [Claude(UpdateRowState.UpdateAvailable), Codex(UpdateRowState.UpdateAvailable), Marrow(UpdateRowState.UpdateAvailable)];

    [Fact]
    [Trait("Decision", "ED-308")] // U1: sumCur / detCur, "Check now" as the quiet button
    public void Everything_current_says_so_names_the_providers_kept_current_in_the_background_and_offers_Check_now()
    {
        var page = UpdatesProjector.Project(AllCurrent(), Quiet("2 hours ago"));
        Assert.Equal(UpdatesPhase.Current, page.Header.Phase);
        Assert.Equal("Everything is up to date", page.Header.Title);
        Assert.Equal("Checked 2 hours ago. Nth also keeps Claude Code and Codex current in the background.", page.Header.Detail);
        Assert.Equal(("Check now", UpdatesAction.Check, false, true), (page.Header.ButtonLabel, page.Header.Action, page.Header.ButtonBusy, page.Header.ButtonQuiet));
        Assert.False(page.Banner.Visible);
        Assert.All(page.Sections.SelectMany(s => s.Rows), r => Assert.Equal((RowStatus.Current, "Up to date"), (r.Status, r.StatusText)));
    }

    [Fact]
    [Trait("Decision", "ED-308")] // U1 detCur without a known time: the sentence is left out, not invented
    public void The_checked_time_is_left_out_when_nothing_was_checked_here_and_the_background_sentence_needs_a_provider_row()
    {
        var withoutTime = UpdatesProjector.Project(AllCurrent(), Quiet());
        Assert.Equal("Nth also keeps Claude Code and Codex current in the background.", withoutTime.Header.Detail);
        var onlyMarrow = UpdatesProjector.Project([Marrow(UpdateRowState.UpToDate, "1.1.0", "1.1.0")], Quiet("just now"));
        Assert.Equal("Checked just now.", onlyMarrow.Header.Detail);
        var empty = UpdatesProjector.Project([], Quiet());
        Assert.Equal(("Everything is up to date", ""), (empty.Header.Title, empty.Header.Detail));
        Assert.Empty(empty.Sections);
    }

    [Fact]
    [Trait("Decision", "ED-308")] // U1: sumOut / detOut, the primary "Update everything"
    public void Updates_available_are_counted_named_with_their_new_versions_in_page_order_and_offered_as_Update_everything()
    {
        var page = UpdatesProjector.Project(AllOut(), Quiet());
        Assert.Equal(UpdatesPhase.Available, page.Header.Phase);
        Assert.Equal("3 updates available", page.Header.Title);
        Assert.Equal("Nth 1.1.0, Claude Code 2.1.290 and Codex 0.160.0.", page.Header.Detail);
        Assert.Equal(("Update everything", UpdatesAction.UpdateAll, false, false), (page.Header.ButtonLabel, page.Header.Action, page.Header.ButtonBusy, page.Header.ButtonQuiet));
        var one = UpdatesProjector.Project([Claude(UpdateRowState.UpdateAvailable), Codex(UpdateRowState.UpToDate, "0.160.0", "0.160.0")], Quiet());
        Assert.Equal(("1 update available", "Claude Code 2.1.290."), (one.Header.Title, one.Header.Detail));
    }

    [Fact]
    [Trait("Decision", "ED-308")] // BUILD-PLAN 2.4: "Checks when updated" when a tool has no public listing
    public void Rows_with_no_listing_say_when_they_are_checked_and_alone_they_do_not_claim_an_update_or_a_current_state()
    {
        var page = UpdatesProjector.Project([Claude(UpdateRowState.ChecksWhenUpdated, "2.1.283", null), Marrow(UpdateRowState.UpToDate, "1.1.0", "1.1.0")], Quiet());
        Assert.Equal(UpdatesPhase.Unsure, page.Header.Phase);
        Assert.Equal("Versions are checked when updated", page.Header.Title);
        Assert.Equal("Claude Code.", page.Header.Detail);
        Assert.Equal(("Update everything", UpdatesAction.UpdateAll), (page.Header.ButtonLabel, page.Header.Action));
        var claude = page.Sections.SelectMany(s => s.Rows).Single(r => r.Id == "cli:claude");
        Assert.Equal((RowStatus.ChecksWhenUpdated, "Checks when updated", "2.1.283", "—"), (claude.Status, claude.StatusText, claude.Installed, claude.Latest));
        var mixed = UpdatesProjector.Project([Claude(UpdateRowState.ChecksWhenUpdated, "2.1.283", null), Codex(UpdateRowState.UpdateAvailable)], Quiet());
        Assert.Equal(("1 update available", UpdatesPhase.Available), (mixed.Header.Title, mixed.Header.Phase));
    }

    [Fact]
    [Trait("Decision", "ED-308")] // U1: sumChk / detChk / "Checking…"
    public void While_a_check_runs_the_header_and_every_row_say_so_and_the_button_is_busy()
    {
        var page = UpdatesProjector.Project(AllOut(), new UpdatesFlags(true, false, false, true, null, null));
        Assert.Equal(UpdatesPhase.Checking, page.Header.Phase);
        Assert.Equal(("Checking for updates…", "Asking each tool for its latest version."), (page.Header.Title, page.Header.Detail));
        Assert.Equal(("Checking…", UpdatesAction.None, true), (page.Header.ButtonLabel, page.Header.Action, page.Header.ButtonBusy));
        Assert.All(page.Sections.SelectMany(s => s.Rows), r => Assert.Equal((RowStatus.Checking, "Checking…", false), (r.Status, r.StatusText, r.CanUpdate)));
    }

    [Fact]
    [Trait("Decision", "ED-308")] // U1: sumRun(i, n, name), detRun, st.que / st.run / st.dl; the bar is moving for a CLI and exact for Marrow's download
    public void A_run_names_the_item_and_its_place_and_each_row_shows_waiting_updating_downloading_or_done()
    {
        IReadOnlyList<UpdateRow> rows =
        [
            Claude(UpdateRowState.Updated, "2.1.290", "2.1.290"),
            Row("cli:codex", "Codex", UpdateItemKind.ProviderCli, UpdateRowState.Updating, "0.157.1", "0.160.0"),
            Marrow(UpdateRowState.Queued),
        ];
        HashSet<string> run = ["cli:claude", "cli:codex", "marrow"];
        var page = UpdatesProjector.Project(rows, new UpdatesFlags(false, false, false, true, null, run));
        Assert.Equal(UpdatesPhase.Running, page.Header.Phase);
        Assert.Equal(("Updating Codex… 2 of 3", "Keep working; Nth restarts only when you say so."), (page.Header.Title, page.Header.Detail));
        Assert.Equal(("Updating…", UpdatesAction.None, true), (page.Header.ButtonLabel, page.Header.Action, page.Header.ButtonBusy));
        var by = page.Sections.SelectMany(s => s.Rows).ToDictionary(r => r.Id);
        Assert.Equal((RowStatus.Updated, "Updated", false), (by["cli:claude"].Status, by["cli:claude"].StatusText, by["cli:claude"].ShowProgress));
        Assert.Equal((RowStatus.Updating, "Updating…", true, (double?)null), (by["cli:codex"].Status, by["cli:codex"].StatusText, by["cli:codex"].ShowProgress, by["cli:codex"].Progress));
        Assert.Equal((RowStatus.Waiting, "Waiting", false), (by["marrow"].Status, by["marrow"].StatusText, by["marrow"].ShowProgress));
        var download = UpdatesProjector.Project([Marrow(UpdateRowState.Updating, progress: .42)], new UpdatesFlags(false, false, false, true, null, new HashSet<string> { "marrow" }));
        var marrow = download.Sections.Single().Rows.Single();
        Assert.Equal((RowStatus.Downloading, "Downloading 42%", true, (double?).42), (marrow.Status, marrow.StatusText, marrow.ShowProgress, marrow.Progress));
        Assert.Equal("Updating Nth… 1 of 1", download.Header.Title);
    }

    [Fact]
    [Trait("Decision", "ED-308")] // with no remembered run the count is taken from the rows that are part of one
    public void Without_a_remembered_run_the_count_comes_from_the_rows_that_are_queued_updating_or_finished()
    {
        IReadOnlyList<UpdateRow> rows = [Claude(UpdateRowState.Queued), Codex(UpdateRowState.Queued), Marrow(UpdateRowState.Queued)];
        var page = UpdatesProjector.Project(rows, new UpdatesFlags(false, false, false, true, null, null));
        Assert.Equal("Updating Claude Code… 1 of 3", page.Header.Title);
    }

    [Fact]
    [Trait("Decision", "ED-308")] // U1: sumFail / detFail / the failure line with the command to run by hand / "Retry"
    public void A_failure_names_the_item_keeps_the_others_result_shows_the_fix_and_makes_Retry_the_button()
    {
        IReadOnlyList<UpdateRow> rows =
        [
            Claude(UpdateRowState.Updated, "2.1.290", "2.1.290"),
            Codex(UpdateRowState.Failed, "codex-cli 0.157.1", "0.160.0", "the updater failed or the version could not be read afterwards"),
            Marrow(UpdateRowState.Updated, "1.0.0", "1.1.0", restart: true),
        ];
        var page = UpdatesProjector.Project(rows, Quiet(runIds: new HashSet<string> { "cli:claude", "cli:codex", "marrow" }));
        Assert.Equal(UpdatesPhase.Failed, page.Header.Phase);
        Assert.Equal(("Codex didn’t update", "Updated Nth and Claude Code. The fix is below."), (page.Header.Title, page.Header.Detail));
        Assert.Equal(("Retry", UpdatesAction.Retry, false, false), (page.Header.ButtonLabel, page.Header.Action, page.Header.ButtonBusy, page.Header.ButtonQuiet));
        var codex = page.Sections.SelectMany(s => s.Rows).Single(r => r.Id == "cli:codex");
        Assert.Equal((RowStatus.DidNotUpdate, "Didn’t update", "0.157.1"), (codex.Status, codex.StatusText, codex.Installed));
        Assert.Equal(("Codex is still 0.157.1 after its updater ran. Retry, or run ", "codex update", " in a terminal."), (codex.FailureLead, codex.FailureCommand, codex.FailureTail));
        Assert.True(codex.CanRetry);
        Assert.True(page.Banner.Visible);
    }

    [Fact]
    [Trait("Decision", "ED-308")] // a failure with no command to name says the item's own reason, as a sentence
    public void A_failed_item_with_no_updater_command_shows_its_own_reason_and_two_failures_are_named_together()
    {
        var dev = Row("dev:git", "Git", UpdateItemKind.DevTool, UpdateRowState.Failed, "2.50.0", "2.51.0", message: "winget exited with code 1");
        var page = UpdatesProjector.Project([Codex(UpdateRowState.Failed, "0.157.1", "0.160.0", "the updater failed or the version could not be read afterwards"), dev], Quiet());
        Assert.Equal("Codex and Git didn’t update", page.Header.Title);
        Assert.Equal("The fix is below.", page.Header.Detail);
        var git = page.Sections.SelectMany(s => s.Rows).Single(r => r.Id == "dev:git");
        Assert.Equal(("Winget exited with code 1.", null, null), (git.FailureLead, git.FailureCommand, git.FailureTail));
        var silent = UpdatesProjector.Project([Row("dev:node", "Node.js", UpdateItemKind.DevTool, UpdateRowState.Failed, "22.0.0", "24.0.0")], Quiet());
        Assert.Equal("Node.js didn’t update.", silent.Sections.Single().Rows.Single().FailureLead);
    }

    [Fact]
    [Trait("Decision", "ED-308")] // U1: sumDone / detDone, "Restart Marrow", the banner's two lines and its Later
    public void After_an_update_that_needs_a_restart_the_banner_and_the_Restart_Marrow_button_carry_it_and_Later_moves_it_to_the_detail()
    {
        IReadOnlyList<UpdateRow> rows =
        [
            Claude(UpdateRowState.Updated, "2.1.290", "2.1.290"), Codex(UpdateRowState.Updated, "0.160.0", "0.160.0"),
            Marrow(UpdateRowState.Updated, "1.0.0", "1.1.0", restart: true),
        ];
        var page = UpdatesProjector.Project(rows, Quiet());
        Assert.Equal(UpdatesPhase.Done, page.Header.Phase);
        Assert.Equal(("3 updated", "Updated Nth, Claude Code and Codex."), (page.Header.Title, page.Header.Detail));
        Assert.Equal(("Restart Nth", UpdatesAction.Restart, false, false), (page.Header.ButtonLabel, page.Header.Action, page.Header.ButtonBusy, page.Header.ButtonQuiet));
        Assert.Equal((true, "Nth 1.1.0 is ready.", "Restart Nth to finish its update."), (page.Banner.Visible, page.Banner.Title, page.Banner.Detail));
        var marrow = page.Sections.SelectMany(s => s.Rows).Single(r => r.Id == "marrow");
        Assert.Equal((RowStatus.RestartToFinish, "Restart to finish"), (marrow.Status, marrow.StatusText));
        var later = UpdatesProjector.Project(rows, new UpdatesFlags(false, false, true, false, null, null));
        Assert.False(later.Banner.Visible);
        Assert.Equal("Updated Nth, Claude Code and Codex. Restart Nth to finish its update.", later.Header.Detail);
        Assert.Equal("Restart Nth", later.Header.ButtonLabel);
    }

    [Fact]
    [Trait("Decision", "ED-308")] // U1: after updates that need no restart the button goes back to the quiet "Check now"
    public void An_update_that_needs_no_restart_leaves_Check_now_as_the_quiet_button()
    {
        var page = UpdatesProjector.Project([Claude(UpdateRowState.Updated, "2.1.290", "2.1.290"), Codex(UpdateRowState.UpToDate, "0.160.0", "0.160.0")], Quiet());
        Assert.Equal(("1 updated", "Updated Claude Code."), (page.Header.Title, page.Header.Detail));
        Assert.Equal(("Check now", UpdatesAction.Check, true), (page.Header.ButtonLabel, page.Header.Action, page.Header.ButtonQuiet));
        Assert.False(page.Banner.Visible);
    }

    [Fact]
    [Trait("Decision", "ED-308")] // U1: sumRs / "Restarting…"
    public void Restarting_says_so_hides_the_banner_and_stops_the_button()
    {
        var rows = new[] { Claude(UpdateRowState.Updated, "2.1.290", "2.1.290"), Marrow(UpdateRowState.Updated, "1.0.0", "1.1.0", restart: true) };
        var page = UpdatesProjector.Project(rows, new UpdatesFlags(false, true, false, true, null, null));
        Assert.Equal(UpdatesPhase.Restarting, page.Header.Phase);
        Assert.Equal("Restarting Nth…", page.Header.Title);
        Assert.Equal(("Restarting…", UpdatesAction.None, true), (page.Header.ButtonLabel, page.Header.Action, page.Header.ButtonBusy));
        Assert.False(page.Banner.Visible);
    }

    [Fact]
    [Trait("Decision", "ED-308")] // OD-0100: the page lists items; sections follow the design (Marrow, Provider tools) with the port's other kinds after
    public void Rows_are_grouped_in_sections_with_sub_lines_clean_version_numbers_and_only_installed_items()
    {
        IReadOnlyList<UpdateRow> rows =
        [
            Claude(UpdateRowState.UpdateAvailable, "2.1.3 (Claude Code)", "2.1.290"),
            Row("cli:codex", "Codex", UpdateItemKind.ProviderCli, UpdateRowState.NotInstalled, null, null),
            Row("dev:git", "Git", UpdateItemKind.DevTool, UpdateRowState.UpToDate, "git version 2.51.0.windows.1", "2.51.0"),
            Row("skills", "Skills", UpdateItemKind.SkillsAndTools, UpdateRowState.UpToDate, "2026.10.1", "2026.10.1"),
            Marrow(UpdateRowState.UpToDate, "1.1.0", "1.1.0"),
        ];
        var page = UpdatesProjector.Project(rows, Quiet());
        Assert.Equal(["Nth", "Provider tools", "Developer tools", "Skills and tools"], page.Sections.Select(s => s.Title));
        Assert.Equal([["marrow"], ["cli:claude"], ["dev:git"], ["skills"]], page.Sections.Select(s => s.Rows.Select(r => r.Id).ToArray()));
        var all = page.Sections.SelectMany(s => s.Rows).ToDictionary(r => r.Id);
        Assert.Equal("The app", all["marrow"].Sub);
        Assert.Equal("claude update", all["cli:claude"].Sub);
        Assert.Equal("", all["dev:git"].Sub);
        Assert.Equal(("2.1.3", "2.1.290"), (all["cli:claude"].Installed, all["cli:claude"].Latest));
        Assert.Equal(("2.51.0", "2.51.0"), (all["dev:git"].Installed, all["dev:git"].Latest));
    }

    [Fact]
    [Trait("Decision", "ED-308")] // per-row Update and Retry are offered only when nothing else is running
    public void A_rows_own_Update_and_Retry_are_offered_only_while_the_page_is_idle()
    {
        IReadOnlyList<UpdateRow> rows = [Claude(UpdateRowState.UpdateAvailable), Codex(UpdateRowState.Failed, "0.157.1", "0.160.0", "x"), Marrow(UpdateRowState.UpToDate, "1.1.0", "1.1.0")];
        var idle = UpdatesProjector.Project(rows, Quiet()).Sections.SelectMany(s => s.Rows).ToDictionary(r => r.Id);
        Assert.Equal((true, false), (idle["cli:claude"].CanUpdate, idle["cli:claude"].CanRetry));
        Assert.Equal((false, true), (idle["cli:codex"].CanUpdate, idle["cli:codex"].CanRetry));
        Assert.Equal((false, false), (idle["marrow"].CanUpdate, idle["marrow"].CanRetry));
        var busy = UpdatesProjector.Project(rows, new UpdatesFlags(false, false, false, true, null, null)).Sections.SelectMany(s => s.Rows);
        Assert.All(busy, r => Assert.Equal((false, false), (r.CanUpdate, r.CanRetry)));
    }

    [Fact]
    [Trait("Decision", "ED-308")] // the "Checked ..." sentence: a plain age, singular for one
    public void The_checked_age_reads_just_now_minutes_hours_or_days()
    {
        Assert.Equal("just now", UpdatesProjector.Age(TimeSpan.FromSeconds(30)));
        Assert.Equal("1 minute ago", UpdatesProjector.Age(TimeSpan.FromSeconds(61)));
        Assert.Equal("59 minutes ago", UpdatesProjector.Age(TimeSpan.FromMinutes(59.5)));
        Assert.Equal("1 hour ago", UpdatesProjector.Age(TimeSpan.FromMinutes(61)));
        Assert.Equal("2 hours ago", UpdatesProjector.Age(TimeSpan.FromHours(2.4)));
        Assert.Equal("1 day ago", UpdatesProjector.Age(TimeSpan.FromHours(25)));
        Assert.Equal("3 days ago", UpdatesProjector.Age(TimeSpan.FromDays(3.2)));
        Assert.Equal("just now", UpdatesProjector.Age(TimeSpan.FromMinutes(-5)));
    }
}
