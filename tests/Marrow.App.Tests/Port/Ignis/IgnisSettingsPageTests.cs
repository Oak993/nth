using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Port.Ignis.Settings;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Testing;
using Marrow.UI.Port;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port.Ignis;

// Ignis U5 (OD-0134, ED-NEW-ignis-settings; work/ignis/PLAN.md U5; design phoenix-designs.html "Settings › Ignis"): the page's place in the
// sidebar and its search words, its rows persisting through the one "ignis" key, the savings history read from a fake ledger (tiles, month
// picker, sources, day by day) and the rebuild points table. Headless, the test runtime's loop and clock; no engine, no network.
[Collection("Headless")]
public sealed class IgnisSettingsPageTests(HeadlessSession headless)
{
    private static readonly DateOnly Today = new(2026, 10, 6); // a Tuesday

    private static IgnisDayTotals Day(int month, int day, decimal reuse = 0, decimal warm = 0, decimal tidy = 0, decimal help = 0, int reuses = 0, int pings = 0, int tidyUps = 0, int jobs = 0) =>
        new(new DateOnly(2026, month, day), reuse, warm, tidy, help, 0m, 0m, reuses, pings, tidyUps, jobs);

    private static FakeSavingsLedger Ledger() => new([
        Day(8, 30, reuse: 1.00m, reuses: 10),
        Day(9, 15, reuse: 2.00m, warm: 0.50m, tidy: 0.25m, help: 0.25m, reuses: 20, pings: 1, tidyUps: 2, jobs: 1),
        Day(10, 1, reuse: 3.00m, reuses: 30),
        Day(10, 6, reuse: 1.20m, warm: 0.30m, reuses: 12, pings: 3),
    ]);

    private static FakeIgnisPoints Points() => new("2026.10.08",
        FakeIgnisPoints.Model("claude", "claude-haiku-5-5", "Haiku 5.5", 200_000, stepAt: 100_000),
        FakeIgnisPoints.Model("claude", "claude-opus-5-5", "Opus 5.5", 1_000_000, tidyAt: 400_000, tidyReason: IgnisWords.WhyBreakEven, helper: "claude-haiku-5-5"),
        FakeIgnisPoints.Model("codex", "gpt-6.1-codex", "Codex", 400_000));

    private static void Show(Control page, TestRuntime runtime, Action<HostWindow> body)
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var window = new HostWindow { Width = 600, Height = 1400, Content = new ScrollViewer { Content = new Border { Width = 520, Child = page } } };
        window.Show();
        runtime.RunUntilIdle();
        window.UpdateLayout();
        try { body(window); } finally { window.Close(); }
    }

    private static void Toggle(Control page, string name)
    {
        var host = page.GetVisualDescendants().OfType<SettingsSwitch>().Single(s => AutomationProperties.GetName(s) == name);
        var toggle = host.GetVisualDescendants().OfType<SmallSwitch>().Single();
        var peer = ControlAutomationPeer.CreatePeerForElement(toggle);
        Assert.IsAssignableFrom<IToggleProvider>(peer).Toggle();
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    [Trait("Decision", "ED-NEW-ignis-settings")] // PLAN U5: the page sits directly below Hydra, with its title, symbol and IgnisWords' search words
    public void Ignis_is_listed_directly_below_Hydra_and_found_by_its_words()
    {
        var all = SwiftEnum<SettingsPage>.AllCases.ToList();
        Assert.Equal(all.IndexOf(SettingsPage.Hydra) + 1, all.IndexOf(SettingsPage.Ignis));
        var listed = SettingsSidebar.Pages("").ToList();
        Assert.Equal(listed.IndexOf(SettingsPage.Hydra) + 1, listed.IndexOf(SettingsPage.Ignis));
        Assert.True(IgnisSettingsPage.IsReady);
        Assert.True(SettingsPage.Ignis.IsReady);
        Assert.Equal(("ignis", "Ignis", "flame.fill"), (SettingsPage.Ignis.Id, SettingsPage.Ignis.Title, SettingsPage.Ignis.Symbol));
        Assert.NotNull(Marrow.UI.Port.SymbolCatalogue.Mapping(SettingsPage.Ignis.Symbol));
        Assert.Equal(IgnisWords.SettingsKeywords, SettingsPage.Ignis.Keywords);
        Assert.Equal([SettingsPage.Ignis], SettingsSidebar.Pages("ignis"));
        Assert.Equal([SettingsPage.Ignis], SettingsSidebar.Pages("quick memory"));
        Assert.Equal([SettingsPage.Ignis], SettingsSidebar.Pages("Keep warm"));
        Assert.Equal([SettingsPage.Ignis], SettingsSidebar.Pages("model points"));
        Assert.Contains(SettingsPage.Ignis, SettingsSidebar.Pages("cache"));
        Assert.Contains(SettingsPage.Ignis, SettingsSidebar.Pages("tidy up"));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-ignis-settings")] // the factory line, and the sidebar row draws Ignis's lit flame (as Hydra draws its own mark)
    public void The_factory_builds_the_page_and_the_sidebar_row_draws_the_flame() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        Assert.IsType<IgnisSettingsPage>(SettingsView.PageFor(SettingsPage.Ignis, "", ""));
        Assert.False(SettingsView.HasChromeControls(SettingsPage.Ignis, new IgnisSettingsPage()));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-ignis-settings")] // design: the rows, their words and the defaults (Warm + tidy up, every switch on but pings, Last 12 turns, Update automatically on)
    public void The_rows_read_the_designs_words_and_defaults() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var settings = new IgnisSettings(runtime.Defaults);
        var page = new IgnisSettingsPage(settings, Ledger(), Points(), () => Today, () => null);
        Show(page, runtime, _ =>
        {
            var texts = page.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
            foreach (var word in new[]
            {
                IgnisWords.SettingsLead, IgnisWords.RowDefaultMode, IgnisWords.RowDefaultModeDetail, IgnisWords.RowAutomatic, IgnisWords.RowAutomaticDetail,
                IgnisWords.RowHeads, IgnisWords.RowHeadsDetail, IgnisWords.RowSmartHelpers, IgnisWords.RowSmartHelpersDetail("Haiku 5.5"),
                IgnisWords.RowExplanations, IgnisWords.RowExplanationsDetail, IgnisWords.RowShowPings, IgnisWords.RowShowPingsDetail,
                IgnisWords.RowKeepWordForWord, IgnisWords.RowKeepWordForWordDetail, IgnisWords.SavingsHistory, IgnisWords.RebuildPoints,
                IgnisWords.ModelPoints, IgnisWords.ModelPointsDetail("2026.10.08"), IgnisWords.UpdateAutomatically, IgnisWords.UpdateAutomaticallyDetail, IgnisWords.PointMovesHint,
            })
                Assert.Contains(word, texts);
            Assert.Equal(IgnisWords.DefaultModeAccessibleName, AutomationProperties.GetName(page.ModePicker));
            Assert.Equal(IgnisWords.KeepTurns(12), page.KeepTurnsMenu.Items.Single(item => item.Id == page.KeepTurnsMenu.SelectedId).Label.Text);
            Assert.Equal(["Last 6 turns", "Last 12 turns", "Last 24 turns"], page.KeepTurnsMenu.Items.Select(item => item.Label.Text));
            Assert.Equal(IgnisWords.UpdateNow, page.UpdateNowLabel);
            Assert.Null(runtime.Defaults.Object(IgnisSettings.SettingsKey)); // reading never writes
        });
    });

    [Fact]
    [Trait("Decision", "ED-NEW-ignis-settings")] // every row writes the one "ignis" key, and a fresh reader sees it
    public void Every_row_persists_through_the_ignis_key() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var settings = new IgnisSettings(runtime.Defaults);
        var page = new IgnisSettingsPage(settings, Ledger(), Points(), () => Today, () => null);
        Show(page, runtime, _ =>
        {
            Toggle(page, IgnisWords.RowAutomatic);
            Toggle(page, IgnisWords.RowHeads);
            Toggle(page, IgnisWords.RowSmartHelpersAccessibleName);
            Toggle(page, IgnisWords.RowExplanations);
            Toggle(page, IgnisWords.RowShowPings);
            Toggle(page, IgnisWords.UpdateAutomaticallyAccessibleName);
            Click(page.ModePicker.GetVisualDescendants().OfType<Button>().Single(button => button.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Keep warm")));
            page.KeepTurnsMenu.Select("24");
            runtime.RunUntilIdle();
            var stored = Assert.IsType<string>(runtime.Defaults.Object(IgnisSettings.SettingsKey));
            var read = new IgnisSettings(runtime.Defaults).Value;
            Assert.Equal(
                (IgnisMode.KeepWarm, false, false, false, false, true, 24, false),
                (read.DefaultMode, read.Auto, read.IncludeHeads, read.SmartHelpers, read.ShowExplanations, read.ShowPings, read.KeepTurns, read.AutoUpdatePoints));
            Assert.Equal(stored, IgnisSettings.ToJson(read));
            Toggle(page, IgnisWords.RowShowPings);
            runtime.RunUntilIdle();
            Assert.False(new IgnisSettings(runtime.Defaults).Value.ShowPings);
        });
    });

    [Fact]
    [Trait("Decision", "ED-NEW-ignis-settings")] // a change made elsewhere (the composer, another window) shows on the open page
    public void A_change_made_elsewhere_shows_on_the_open_page() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var settings = new IgnisSettings(runtime.Defaults);
        var page = new IgnisSettingsPage(settings, Ledger(), Points(), () => Today, () => null);
        Show(page, runtime, _ =>
        {
            settings.Update(p => p with { KeepTurns = 6, ShowPings = true });
            runtime.RunUntilIdle();
            Assert.Equal("6", page.KeepTurnsMenu.SelectedId);
            var pings = page.GetVisualDescendants().OfType<SettingsSwitch>().Single(s => AutomationProperties.GetName(s) == IgnisWords.RowShowPings).GetVisualDescendants().OfType<SmallSwitch>().Single();
            Assert.Equal(ToggleState.On, Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(pings)).ToggleState);
        });
    });

    [Fact]
    [Trait("Decision", "ED-NEW-ignis-settings")] // design .sv-tiles / .mpick / .mchart / .sv-legend / .days from the ledger: the newest month first, then the picker steps back
    public void The_history_is_built_from_the_ledger_with_the_month_picker_totals_and_days() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var page = new IgnisSettingsPage(new IgnisSettings(runtime.Defaults), Ledger(), Points(), () => Today, () => null);
        Show(page, runtime, _ =>
        {
            var history = Assert.IsType<IgnisSavingsHistoryView>(page.History);
            Assert.Equal(("$8.50", "Since 30 August 2026, across all chats"), (history.AllTimeAmount.Text, history.SinceText.Text));
            Assert.Equal([new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)], history.Bars.Select(bar => bar.Month));
            Assert.Equal(["August $1.00", "September $3.00", "October $4.50"], history.Bars.Select(bar => bar.Name));
            Assert.Equal([false, false, true], history.Bars.Select(bar => bar.Picked));
            Assert.Equal(("October so far", "$4.50", "6 days · about $0.75 a day", "October 2026"), (history.MonthTileLabel.Text, history.MonthTileAmount.Text, history.MonthTileDetail.Text, history.MonthName.Text));
            Assert.Equal((true, false), (history.PreviousButton.IsEnabled, history.NextButton.IsEnabled));
            Assert.Equal("Where October’s savings came from", history.SourceTitle.Text);
            Assert.Equal([("Reused quick memory", "$4.20"), ("Kept warm", "$0.30"), ("Tidy-ups", "$0.00"), ("Smart helpers", "$0.00")], history.Legend);
            Assert.Equal(6, history.DayLines.Count);
            Assert.Equal(("Today", "$1.50"), (history.DayLines[0].Label, history.DayLines[0].Amount));
            Assert.Equal(["12 quick reuses", "kept warm 3 times"], history.DayLines[0].Did);
            Assert.Equal(("Mon 5 Oct", "$0.00"), (history.DayLines[1].Label, history.DayLines[1].Amount));
            Assert.Equal([IgnisWords.AQuietDay], history.DayLines[1].Did);
            Assert.Equal("Thu 1 Oct", history.DayLines[^1].Label);

            Click(history.PreviousButton);
            runtime.RunUntilIdle();
            Assert.Equal(("September", "$3.00", "30 days · about $0.10 a day", "September 2026"), (history.MonthTileLabel.Text, history.MonthTileAmount.Text, history.MonthTileDetail.Text, history.MonthName.Text));
            Assert.Equal("Where September’s savings came from", history.SourceTitle.Text);
            Assert.Equal([("Reused quick memory", "$2.00"), ("Kept warm", "$0.50"), ("Tidy-ups", "$0.25"), ("Smart helpers", "$0.25")], history.Legend);
            Assert.Equal(30, history.DayLines.Count);
            Assert.Equal("Wed 30 Sep", history.DayLines[0].Label);
            var fifteenth = history.DayLines.Single(line => line.Label == "Tue 15 Sep");
            Assert.Equal(["20 quick reuses", "kept warm 1 time", "2 tidy-ups", "1 helper job"], fifteenth.Did);
            Assert.Equal("$3.00", fifteenth.Amount);
            Assert.Equal((true, true), (history.PreviousButton.IsEnabled, history.NextButton.IsEnabled));

            history.PickBar(new DateOnly(2026, 8, 1));
            runtime.RunUntilIdle();
            Assert.Equal(("August", "$1.00", "2 days · about $0.50 a day"), (history.MonthTileLabel.Text, history.MonthTileAmount.Text, history.MonthTileDetail.Text));
            Assert.Equal([true, false, false], history.Bars.Select(bar => bar.Picked));
            Assert.Equal(["Mon 31 Aug", "Sun 30 Aug"], history.DayLines.Select(line => line.Label));
            Assert.False(history.PreviousButton.IsEnabled);
            Click(history.NextButton);
            runtime.RunUntilIdle();
            Assert.Equal("September 2026", history.MonthName.Text);
        });
    });

    [Fact]
    [Trait("Decision", "ED-NEW-ignis-settings")] // no ledger yet (Ignis not running, or nothing saved): one month, today, $0.00, a quiet day
    public void An_empty_ledger_reads_as_today_with_nothing_saved()
    {
        var history = IgnisSavingsHistory.From(null, Today);
        Assert.Equal(("$0.00", "Since 6 October 2026, across all chats"), (history.AllTimeAmount, history.SinceText));
        var month = Assert.Single(history.Months);
        Assert.Equal(("October so far", "1 day · about $0.00 a day"), (month.TileLabel, month.TileDetail));
        var day = Assert.Single(month.DayRows(Today));
        Assert.Equal(("Today", 0), (day.Label, day.Did.Count));
        var empty = IgnisSavingsHistory.From(new FakeSavingsLedger([]), Today);
        Assert.Equal(Today, empty.Since);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-ignis-settings")] // the chart shows at most twelve months and moves back so the picked one is always in it
    public void A_long_history_charts_twelve_months_around_the_picked_one()
    {
        var history = IgnisSavingsHistory.From(new FakeSavingsLedger([new(new DateOnly(2025, 1, 10), 1m, 0, 0, 0, 0, 0, 1, 0, 0, 0)]), Today);
        Assert.Equal(22, history.Months.Count);
        Assert.Equal((10, 12), history.ChartWindow(21));
        Assert.Equal((3, 12), history.ChartWindow(3));
        Assert.Equal((0, 12), history.ChartWindow(0));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-ignis-settings")] // the ledger's Changed rebuilds the history and keeps the picked month
    public void A_ledger_change_rebuilds_the_history_and_keeps_the_picked_month() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var ledger = Ledger();
        var page = new IgnisSettingsPage(new IgnisSettings(runtime.Defaults), ledger, Points(), () => Today, () => null);
        Show(page, runtime, _ =>
        {
            var before = page.History!;
            Click(before.PreviousButton);
            ledger.Raise();
            runtime.RunUntilIdle();
            Assert.NotSame(before, page.History);
            Assert.Equal("September 2026", page.History!.MonthName.Text);
        });
    });

    [Fact]
    [Trait("Decision", "ED-NEW-ignis-settings")] // design table: model / cheapest point / why, "in use" on the chat's model; Update now checks the feed only
    public void The_rebuild_points_table_and_Update_now() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var points = Points();
        points.Feed = "2026.10.10";
        var page = new IgnisSettingsPage(new IgnisSettings(runtime.Defaults), Ledger(), points, () => Today, () => (ProviderKind.Codex, "gpt-6.1-codex"));
        Show(page, runtime, _ =>
        {
            Assert.Equal(
                [("Haiku 5.5", "95K", "Price step at 100K", false), ("Opus 5.5", "400K", IgnisWords.WhyBreakEven, false), ("Codex", "300K", "Rebuild is cheapest before the last quarter of its 400K window", true)],
                page.PointsTable);
            Assert.Contains(IgnisSettingsWords.InUse, page.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
            Assert.Equal(IgnisWords.ModelPointsDetail("2026.10.08"), page.ModelPointsDetail);

            page.UpdateNowButton.Activate();
            runtime.RunUntilIdle();
            Assert.Equal([true], points.Checks);
            Assert.Equal(IgnisWords.UpToDate, page.UpdateNowLabel);
            Assert.True(page.UpdateNowButton.Dimmed);
            Assert.Contains(IgnisWords.ModelPointsDetail("2026.10.10"), page.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
        });
    });

    [Fact]
    [Trait("Decision", "ED-NEW-ignis-settings")] // a feed that cannot be read gives "Update now" back
    public void A_failed_check_gives_Update_now_back() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var points = Points();
        points.FeedError = "offline";
        var page = new IgnisSettingsPage(new IgnisSettings(runtime.Defaults), Ledger(), points, () => Today, () => null);
        Show(page, runtime, _ =>
        {
            page.UpdateNowButton.Activate();
            runtime.RunUntilIdle();
            Assert.Equal(IgnisWords.UpdateNow, page.UpdateNowLabel);
            Assert.False(page.UpdateNowButton.Dimmed);
            Assert.Equal(IgnisWords.ModelPointsDetail("2026.10.08"), page.ModelPointsDetail);
        });
    });
}
