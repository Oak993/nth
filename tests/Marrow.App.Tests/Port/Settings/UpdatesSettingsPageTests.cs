using Avalonia;
using HostWindow = Avalonia.Controls.Window;
using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Port.Updates;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// Unit UE-4 (BUILD-PLAN.md 2.4 UE-4; OD-0100 place A, words U1; OD-0076 transitions; ED-320 glass). The Updates page over UE-2's real orchestrator and scripted items, in a headless window with
// the test runtime's main loop and clock and, where a transition is measured, a frame source the test drives by hand. No CLI, no network, no real time.
[Collection("Headless")]
public sealed class UpdatesSettingsPageTests(HeadlessSession headless)
{
    private static ScriptedUpdateItem Claude(string installed = "2.1.283", string? latest = "2.1.290") => new("cli:claude", "Claude Code", UpdateItemKind.ProviderCli, installed, latest);

    private static ScriptedUpdateItem Codex(string installed = "0.157.1", string? latest = "0.160.0") => new("cli:codex", "Codex", UpdateItemKind.ProviderCli, installed, latest);

    private static ScriptedUpdateItem MarrowItem(Func<IProgress<double?>, Task<UpdateOutcome>>? update = null) =>
        new("marrow", "nth", UpdateItemKind.Marrow, "1.0.0", "1.1.0") { OnUpdate = update ?? (_ => Task.FromResult(new UpdateOutcome(true, "1.1.0", true, true, null))) };

    private static UpdatesSettingsPage Page(List<string> restarts, params IUpdateItem[] items) =>
        new(new OrchestratorUpdatesRunner(new UpdateOrchestrator(items), () => restarts.Add("restart")));

    // Hosts the page the width the Settings window gives a page (780 less the 212 sidebar less 24 either side), shows the window and lets the opening check finish.
    private static void Show(UpdatesSettingsPage page, TestRuntime runtime, Action<HostWindow> body, double width = 520)
    {
        var window = new HostWindow { Width = width + 80, Height = 900, Content = new Border { Width = width, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Child = page } };
        window.Show();
        runtime.RunUntilIdle();
        window.UpdateLayout();
        try { body(window); } finally { window.Close(); }
    }

    private static void Step(TestRuntime runtime, ManualFrameSource frames, double milliseconds)
    {
        runtime.AdvanceMilliseconds(milliseconds);
        frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        runtime.RunUntilIdle();
    }

    private static Rect RectIn(Control control, Visual ancestor) => new(control.TranslatePoint(new Point(0, 0), ancestor) ?? default, control.Bounds.Size);

    [Fact]
    [Trait("Decision", "ED-251")] // BUILD-PLAN 2.4 UE-4 "Sets IsReady": the page is listed and searchable now
    public void The_page_is_ready_so_the_sidebar_lists_and_finds_it()
    {
        Assert.True(UpdatesSettingsPage.IsReady);
        Assert.True(SettingsPage.Updates.IsReady);
        Assert.Contains(SettingsSidebar.Pages(""), page => page == SettingsPage.Updates);
        Assert.Contains(SettingsSidebar.Pages("check now"), page => page == SettingsPage.Updates);
    }

    [Fact]
    [Trait("Decision", "ED-308")] // OD-0100 place A: the picked design's table, with Installed and Latest columns, the header and the one button
    public void The_page_draws_the_projected_state_with_sections_columns_rows_and_the_one_button() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var page = Page([], Claude("2.1.290", "2.1.290"), Codex("0.160.0", "0.160.0"), MarrowItem());
        Show(page, runtime, _ =>
        {
            Assert.Equal(["nth", "Provider tools"], page.SectionTitles);
            Assert.Equal(["marrow", "cli:claude", "cli:codex"], page.RowControls.Select(r => r.RowId));
            Assert.Equal("1 update available", page.TitleText.Text);
            Assert.Equal("nth 1.1.0.", page.DetailText.Text);
            Assert.Equal(("Update everything", UpdatesButtonMode.Primary), (page.MainButton.Label, page.MainButton.Mode));
            var claude = page.RowControl("cli:claude");
            Assert.Equal(("Claude Code", "claude update", "2.1.290", "2.1.290"), (claude.NameText.Text, claude.SubText.Text, claude.InstalledText.Text, claude.LatestText.Text));
            Assert.Equal((RowStatus.Current, "Up to date"), (claude.StatusCell.Status, claude.StatusCell.Text));
            var marrow = page.RowControl("marrow");
            Assert.Equal(("nth", "The app", "1.0.0", "1.1.0"), (marrow.NameText.Text, marrow.SubText.Text, marrow.InstalledText.Text, marrow.LatestText.Text));
            Assert.Equal((RowStatus.Available, "Update available"), (marrow.StatusCell.Status, marrow.StatusCell.Text));
            Assert.Equal(["Item", "Installed", "Latest", "Status"], page.ColumnHeaders);
        });
    });

    [Fact]
    [Trait("Decision", "ED-308")] // U1: Check now, Update everything, then the restart offer, all from the one button
    public void The_one_button_updates_everything_and_then_offers_Restart_Marrow_with_the_banner() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        List<string> restarts = [];
        var page = Page(restarts, Claude(), MarrowItem());
        Show(page, runtime, _ =>
        {
            Assert.Equal("2 updates available", page.TitleText.Text);
            page.MainButton.Activate();
            runtime.RunUntilIdle();
            Assert.Equal(("2 updated", "Restart nth", UpdatesButtonMode.Primary), (page.TitleText.Text, page.MainButton.Label, page.MainButton.Mode));
            Assert.True(page.BannerFold.Open);
            Assert.Equal(("nth 1.1.0 is ready.", "Restart nth to finish its update."), (page.BannerTitleText.Text, page.BannerDetailText.Text));
            Assert.Equal(RowStatus.RestartToFinish, page.RowControl("marrow").StatusCell.Status);
            Assert.Equal(RowStatus.Updated, page.RowControl("cli:claude").StatusCell.Status);
            Assert.Empty(restarts);
            page.MainButton.Activate();
            runtime.RunUntilIdle();
            Assert.Equal(["restart"], restarts);
            Assert.Equal(("Restarting…", UpdatesButtonMode.Busy), (page.MainButton.Label, page.MainButton.Mode));
        });
    });

    [Fact]
    [Trait("Decision", "ED-308")] // U1 "Later": the banner folds away and the sentence moves to the detail line
    public void Later_folds_the_banner_away() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var page = Page([], MarrowItem());
        Show(page, runtime, _ =>
        {
            page.MainButton.Activate();
            runtime.RunUntilIdle();
            Assert.True(page.BannerFold.Open);
            Assert.Equal("Later", AutomationProperties.GetName(page.LaterButton));
            page.LaterButton.Activate();
            Assert.False(page.BannerFold.Open);
            Assert.Equal("Updated nth. Restart nth to finish its update.", page.DetailText.Text);
        });
    });

    [Fact]
    [Trait("Decision", "ED-308")] // BUILD-PLAN UE-4: per-row Update, offered only for a row with an update due, named for its row
    public void A_row_with_an_update_due_offers_its_own_Update_which_runs_that_row_alone() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var claude = Claude();
        var codex = Codex();
        var page = Page([], claude, codex, MarrowItem());
        Show(page, runtime, _ =>
        {
            var row = page.RowControl("cli:claude");
            Assert.True(row.UpdateReveal.Shown);
            Assert.Equal("Update Claude Code", AutomationProperties.GetName(row.UpdateButton));
            row.UpdateButton.Activate();
            runtime.RunUntilIdle();
            Assert.Equal((1, 0), (claude.Updates, codex.Updates));
            Assert.Equal(RowStatus.Updated, row.StatusCell.Status);
            Assert.False(row.UpdateReveal.Shown);
            Assert.True(page.RowControl("cli:codex").UpdateReveal.Shown);
        });
    });

    [Fact]
    [Trait("Decision", "ED-308")] // U1: the failure line with the command to run by hand, and Retry on the row
    public void A_failed_row_opens_its_failure_line_with_the_command_and_a_Retry_that_runs_it_again() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var attempts = 0;
        var codex = Codex();
        codex.OnUpdate = _ => Task.FromResult(++attempts == 1 ? new UpdateOutcome(false, null, false, false, "failed") : new UpdateOutcome(true, "0.160.0", true, false, null));
        var page = Page([], codex);
        Show(page, runtime, _ =>
        {
            var row = page.RowControl("cli:codex");
            Assert.False(row.FailureFold.Open);
            page.MainButton.Activate();
            runtime.RunUntilIdle();
            Assert.Equal(("Codex didn’t update", "Retry"), (page.TitleText.Text, page.MainButton.Label));
            Assert.True(row.FailureFold.Open);
            Assert.Equal("Codex is still 0.157.1 after its updater ran. Retry, or run codex update in a terminal.", row.FailureText);
            Assert.Equal((RowStatus.DidNotUpdate, "Didn’t update"), (row.StatusCell.Status, row.StatusCell.Text));
            Assert.Equal("Retry Codex", AutomationProperties.GetName(row.RetryButton));
            row.RetryButton.Activate();
            runtime.RunUntilIdle();
            Assert.False(row.FailureFold.Open);
            Assert.Equal(("1 updated", 2), (page.TitleText.Text, codex.Updates));
        });
    });

    [Fact]
    [Trait("Decision", "ED-308")] // BUILD-PLAN UE-4: progress is moving for a CLI (it reports none) and exact for Marrow's download, in a bar that folds open under its row
    public void The_bar_folds_open_under_a_running_row_moving_for_a_CLI_and_exact_for_Marrows_download() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var cliGate = new TaskCompletionSource();
        var marrowGate = new TaskCompletionSource();
        IProgress<double?>? report = null;
        var claude = Claude();
        claude.OnUpdate = async _ => { await cliGate.Task; return new UpdateOutcome(true, "2.1.290", true, false, null); };
        var page = Page([], claude, MarrowItem(async progress => { report = progress; await marrowGate.Task; return new UpdateOutcome(true, "1.1.0", true, true, null); }));
        Show(page, runtime, _ =>
        {
            var cli = page.RowControl("cli:claude");
            var app = page.RowControl("marrow");
            Assert.False(cli.ProgressFold.Open);
            page.MainButton.Activate();
            runtime.RunUntilIdle();
            Assert.True(cli.ProgressFold.Open);
            Assert.True(cli.ProgressBar.Indeterminate);
            Assert.False(app.ProgressFold.Open);
            cliGate.SetResult();
            runtime.RunUntilIdle();
            Assert.False(cli.ProgressFold.Open);
            Assert.True(app.ProgressFold.Open);
            report!.Report(.42);
            runtime.RunUntilIdle();
            Assert.Equal((false, .42, "Downloading 42%"), (app.ProgressBar.Indeterminate, app.ProgressBar.TargetFraction, app.StatusCell.Text));
            marrowGate.SetResult();
            runtime.RunUntilIdle();
            Assert.False(app.ProgressFold.Open);
        });
    });

    [Fact]
    [Trait("Decision", "ED-237")] // OD-0076: a status changes by a crossfade on the frame clock, never a cut
    public void A_status_crossfades_on_the_frame_clock_and_the_bar_folds_open_over_a_third_of_a_second() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var gate = new TaskCompletionSource();
        var claude = Claude();
        claude.OnUpdate = async _ => { await gate.Task; return new UpdateOutcome(true, "2.1.290", true, false, null); };
        var page = Page([], claude);
        Show(page, runtime, window =>
        {
            Step(runtime, frames, 600);
            var row = page.RowControl("cli:claude");
            Assert.Equal(RowStatus.Available, row.StatusCell.Status);
            page.MainButton.Activate();
            runtime.RunUntilIdle();
            Assert.Equal((RowStatus.Updating, 0.0, 1.0), (row.StatusCell.Status, row.StatusCell.IncomingOpacity, row.StatusCell.OutgoingOpacity));
            Assert.Equal(0, row.ProgressFold.Fraction);
            Step(runtime, frames, 120);
            Assert.InRange(row.StatusCell.IncomingOpacity, 0.001, 0.999);
            Assert.InRange(row.StatusCell.OutgoingOpacity, 0.001, 0.999);
            Assert.InRange(row.ProgressFold.Fraction, 0.01, 0.99);
            Step(runtime, frames, 500);
            Assert.Equal((1.0, 0.0, 1.0), (row.StatusCell.IncomingOpacity, row.StatusCell.OutgoingOpacity, row.ProgressFold.Fraction));
            Assert.True(row.ProgressBar.Indeterminate);
            gate.SetResult();
            runtime.RunUntilIdle(); // the run ends now; its transitions start now
            Step(runtime, frames, 600);
            Assert.Equal(0, row.ProgressFold.Fraction);
        });
    });

    [Fact]
    [Trait("Decision", "ED-237")] // OD-0076: the system's reduce-motion setting stills every change at once
    public void Reduce_motion_lands_every_change_at_once() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var gate = new TaskCompletionSource();
        var claude = Claude();
        claude.OnUpdate = async _ => { await gate.Task; return new UpdateOutcome(true, "2.1.290", true, false, null); };
        var page = Page([], claude);
        Show(page, runtime, window =>
        {
            EnvironmentValues.SetAccessibilityReduceMotion(window, true);
            var row = page.RowControl("cli:claude");
            page.MainButton.Activate();
            runtime.RunUntilIdle();
            Assert.Equal((RowStatus.Updating, 1.0, 0.0), (row.StatusCell.Status, row.StatusCell.IncomingOpacity, row.StatusCell.OutgoingOpacity));
            Assert.Equal(1, row.ProgressFold.Fraction);
            Assert.Equal(1, page.TitleText.IncomingOpacity);
            Assert.False(row.ProgressBar.Moves);
            gate.SetResult();
            runtime.RunUntilIdle();
            Assert.Equal(0, row.ProgressFold.Fraction);
        });
    });

    [Fact]
    [Trait("Decision", "ED-237")] // OD-0076: the button's label crossfades while its width eases to the new label, and the spinner slot opens
    public void The_buttons_width_eases_to_its_new_label_and_the_spinner_slot_opens_while_it_works() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var gate = new TaskCompletionSource();
        var claude = Claude();
        claude.OnUpdate = async _ => { await gate.Task; return new UpdateOutcome(true, "2.1.290", true, false, null); };
        var page = Page([], claude);
        Show(page, runtime, window =>
        {
            Step(runtime, frames, 600);
            window.UpdateLayout();
            var before = page.MainButton.Bounds.Width;
            Assert.Equal("Update everything", page.MainButton.Label);
            Assert.Equal(0, page.MainButton.SpinnerSlotWidth);
            page.MainButton.Activate();
            runtime.RunUntilIdle();
            Step(runtime, frames, 120);
            window.UpdateLayout();
            var midway = page.MainButton.Bounds.Width;
            Assert.NotEqual(before, midway);
            Assert.InRange(page.MainButton.SpinnerSlotWidth, 0.5, 20.5);
            Step(runtime, frames, 600);
            window.UpdateLayout();
            var after = page.MainButton.Bounds.Width;
            Assert.Equal(("Updating…", UpdatesButtonMode.Busy, 21.0), (page.MainButton.Label, page.MainButton.Mode, page.MainButton.SpinnerSlotWidth));
            Assert.NotEqual(midway, after);
            Assert.True(page.MainButton.IsSpinning);
            gate.SetResult();
            runtime.RunUntilIdle(); // the run ends now; its transitions start now
            Step(runtime, frames, 800);
            Assert.False(page.MainButton.IsSpinning);
        });
    });

    [Fact]
    [Trait("Decision", "ED-320")] // ED-320 / the brief: the button is drawn with GlassMaterial, accent tinted when it is the action, plain when quiet, washed when busy
    public void The_main_button_is_glass_tinted_with_the_accent_when_it_is_the_action_plain_when_quiet_and_washed_when_busy() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var gate = new TaskCompletionSource();
        var claude = Claude("2.1.290", "2.1.290");
        claude.OnUpdate = async _ => { await gate.Task; return new UpdateOutcome(true, "2.1.291", true, false, null); };
        var page = Page([], claude);
        Show(page, runtime, _ =>
        {
            var button = page.MainButton;
            Assert.Equal(("Check now", UpdatesButtonMode.Quiet), (button.Label, button.Mode));
            Assert.Equal((0.0, 1.0, 0.0), (button.PrimaryFill.Opacity, button.QuietFill.Opacity, button.BusyFill.Opacity));
            var plain = SwiftGlass.GetEffect(button.QuietFill);
            Assert.NotNull(plain);
            Assert.Null(plain.TintColor);
            var theme = Avalonia.Styling.ThemeVariant.Dark;
            Assert.True(SwiftGlass.GetEffect(button.PrimaryFill) is { TintColor: { } tint, TintOpacity: 1, IsInteractive: true } && tint.Resolve(theme) == Chrome.Accent.Resolve(theme));
            Assert.True(SwiftGlass.GetEffect(button.BusyFill) is { TintColor: { } wash, IsInteractive: false } && wash.Resolve(theme) == Chrome.PrimaryText.Resolve(theme));
            claude.Latest = "2.1.291";
            button.Activate();
            runtime.RunUntilIdle();
            Assert.Equal(("Update everything", UpdatesButtonMode.Primary), (button.Label, button.Mode));
            Assert.Equal((1.0, 0.0, 0.0), (button.PrimaryFill.Opacity, button.QuietFill.Opacity, button.BusyFill.Opacity));
            button.Activate();
            runtime.RunUntilIdle();
            Assert.Equal((UpdatesButtonMode.Busy, 1.0), (button.Mode, button.BusyFill.Opacity));
            gate.SetResult();
            runtime.RunUntilIdle();
        });
    });

    [Fact]
    [Trait("Decision", "ED-308")] // the owner's bar: no overlaps. Every cell has its own column at the Settings window's page width, and no status is wider than its column
    public void No_cell_overlaps_another_at_the_settings_page_width_and_no_status_is_wider_than_its_column() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var claude = Claude("2.1.3", "2026.09.25");
        var page = Page([], claude, Codex(), MarrowItem());
        Show(page, runtime, _ =>
        {
            foreach (var row in page.RowControls)
            {
                var cells = new[] { row.IconCell, row.NameCell, row.InstalledText, row.LatestText, row.StatusCell, row.ActionCell }.Select(c => RectIn(c, page)).ToArray();
                for (var i = 0; i < cells.Length - 1; i++)
                    Assert.True(cells[i].Right <= cells[i + 1].Left + 0.01, $"{row.RowId}: cell {i} ends at {cells[i].Right}, cell {i + 1} starts at {cells[i + 1].Left}");
                Assert.True(cells[^1].Right <= page.Bounds.Width + 0.01, row.RowId);
                Assert.True(cells[1].Width >= 90, $"{row.RowId}: the name column is {cells[1].Width}");
                Assert.True(row.InstalledText.DesiredSize.Width <= UpdatesColumns.Installed + 0.01, $"{row.RowId}: Installed needs {row.InstalledText.DesiredSize.Width}");
                Assert.True(row.LatestText.DesiredSize.Width <= UpdatesColumns.Latest + 0.01, $"{row.RowId}: Latest needs {row.LatestText.DesiredSize.Width}");
                Assert.True(row.UpdateButton.DesiredSize.Width <= UpdatesColumns.Action + 0.01, $"{row.RowId}: the Update button needs {row.UpdateButton.DesiredSize.Width}");
            }
            var header = RectIn(page.TitleText, page);
            Assert.True(header.Right <= RectIn(page.MainButton, page).Left, "the title runs under the button");
            var row0 = page.RowControl("cli:claude");
            foreach (var (status, text) in new (RowStatus, string)[] {
                (RowStatus.Available, "Update available"), (RowStatus.ChecksWhenUpdated, "Checks when updated"), (RowStatus.RestartToFinish, "Restart to finish"),
                (RowStatus.Downloading, "Downloading 100%"), (RowStatus.DidNotUpdate, "Didn’t update"), (RowStatus.Updating, "Updating…"), (RowStatus.Checking, "Checking…"), (RowStatus.Waiting, "Waiting") })
            {
                row0.StatusCell.SetStatus(status, text);
                Assert.True(row0.StatusCell.FaceDesiredWidth <= row0.StatusCell.Bounds.Width + 0.01, $"{text}: {row0.StatusCell.FaceDesiredWidth} against a column of {row0.StatusCell.Bounds.Width}");
            }
        });
    });

    [Fact]
    [Trait("Decision", "ED-308")] // a page that leaves the window stops listening: a later change elsewhere does not touch it
    public void A_page_taken_out_of_the_window_stops_following_the_orchestrator() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var items = new IUpdateItem[] { Claude("2.1.290", "2.1.290") };
        var orchestrator = new UpdateOrchestrator(items);
        var page = new UpdatesSettingsPage(new OrchestratorUpdatesRunner(orchestrator, () => { }));
        Show(page, runtime, _ => Assert.Equal("Everything is up to date", page.TitleText.Text));
        ((ScriptedUpdateItem)items[0]).Latest = "9.9.9";
        runtime.OnMain(() => orchestrator.CheckNow().GetAwaiter().GetResult());
        runtime.RunUntilIdle();
        Assert.Equal("Everything is up to date", page.TitleText.Text);
    });

    [Fact]
    [Trait("Decision", "ED-237")] // OD-0076: an item or a group that appears folds open, one that goes folds shut; nothing cuts in or out
    public void A_row_or_section_that_appears_folds_open_and_one_that_goes_folds_shut() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        UpdateRow Row(string id, string title, UpdateItemKind kind) => new(id, title, kind, UpdateRowState.UpToDate, "1.1.0", "1.1.0", null, null, false);
        var runner = new ScriptedUpdatesRunner { Rows = [Row("marrow", "nth", UpdateItemKind.Marrow)] };
        var page = new UpdatesSettingsPage(runner);
        Show(page, runtime, _ =>
        {
            Step(runtime, frames, 600);
            Assert.Equal(["nth"], page.SectionTitles);
            runner.Rows = [Row("marrow", "nth", UpdateItemKind.Marrow), Row("cli:claude", "Claude Code", UpdateItemKind.ProviderCli)];
            runner.Raise();
            runtime.RunUntilIdle();
            Assert.Equal(["nth", "Provider tools"], page.SectionTitles);
            Assert.Equal(0, page.SectionFold("Provider tools").Fraction);
            Step(runtime, frames, 120);
            Assert.InRange(page.SectionFold("Provider tools").Fraction, 0.01, 0.99);
            Step(runtime, frames, 600);
            Assert.Equal(1, page.SectionFold("Provider tools").Fraction);
            runner.Rows = [.. runner.Rows, Row("cli:codex", "Codex", UpdateItemKind.ProviderCli)];
            runner.Raise();
            runtime.RunUntilIdle();
            Assert.Equal(["marrow", "cli:claude", "cli:codex"], page.RowControls.Select(r => r.RowId));
            Assert.Equal(0, page.RowFold("cli:codex").Fraction);
            Step(runtime, frames, 600);
            Assert.Equal(1, page.RowFold("cli:codex").Fraction);
            runner.Rows = [Row("marrow", "nth", UpdateItemKind.Marrow), Row("cli:claude", "Claude Code", UpdateItemKind.ProviderCli)];
            runner.Raise();
            runtime.RunUntilIdle();
            Assert.False(page.RowFold("cli:codex").Open);
            Step(runtime, frames, 600);
            Assert.Equal(0, page.RowFold("cli:codex").Fraction);
        });
    });

    [Fact]
    [Trait("Decision", "ED-308")] // the "Checked ... ago" sentence keeps up with the clock while the page stays open
    public void The_checked_sentence_keeps_up_with_the_clock_while_the_page_is_open() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var page = Page([], Claude("2.1.290", "2.1.290"));
        Show(page, runtime, _ =>
        {
            Assert.Equal("Checked just now. nth also keeps Claude Code current in the background.", page.DetailText.Text);
            runtime.AdvanceMilliseconds(2.4 * 3_600_000);
            Assert.Equal("Checked 2 hours ago. nth also keeps Claude Code current in the background.", page.DetailText.Text);
        });
    });

    [Fact]
    [Trait("Decision", "ED-308")] // the icon of each row exists in the symbol catalogue or is the app's own: nothing is drawn blank
    public void The_icons_of_the_rows_are_catalogued_symbols_the_provider_marks_or_the_app_icon() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var dev = new ScriptedUpdateItem("dev:git", "Git", UpdateItemKind.DevTool, "2.50.0", "2.51.0");
        var skills = new ScriptedUpdateItem("skills", "Skills", UpdateItemKind.SkillsAndTools, "1", "1");
        var page = Page([], Claude(), dev, skills, MarrowItem());
        Show(page, runtime, _ =>
        {
            Assert.All(page.RowControls, row => Assert.True(row.IconCell.Bounds.Width >= 24 && row.IconCell.Bounds.Height >= 24, row.RowId));
            Assert.NotNull(SymbolCatalogue.Mapping(UpdatesRowControl.DevToolSymbol));
            Assert.NotNull(SymbolCatalogue.Mapping(UpdatesRowControl.SkillsSymbol));
        });
    });
}
