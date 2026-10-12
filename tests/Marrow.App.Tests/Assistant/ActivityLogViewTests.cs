using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;

namespace Marrow.App.Tests.Assistant;

// PORT-ONLY: the Activity log view (DESIGN.md 12.5: the chain is verified on open and the view shows "Verified" or the first
// line that fails; filters; lineage; export; unit AS-21, ED-NEW-dextra-as21-6), in a headless window over ActivityDataset's six
// actions, the clock at 2026-10-13 12:00 UTC (so the 12th is "Yesterday"). The expected words and numbers are written out here.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-as21-6")]
[Trait("Feature", "AS-21")]
public sealed class ActivityLogViewTests(HeadlessSession headless)
{
    private static void Sync(Task task) => task.GetAwaiter().GetResult();

    private static IEnumerable<ActivityRowCell> Cells(ActivityViewStage stage) => stage.View.List.Cells.OrderBy(c => c.Key).Select(c => c.Value);

    private static ActivityRowCell CellOf(ActivityViewStage stage, long sequence) =>
        Assert.Single(Cells(stage), c => c.Item?.Row?.Sequence == sequence);

    private static byte Alpha(ActivityRowCell cell) => Assert.IsAssignableFrom<ISolidColorBrush>(cell.Background).Color.A;

    private static void Press(ActivityViewStage stage, Key key)
    {
        stage.Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        stage.Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        stage.Layout();
    }

    private static void PointerClick(ActivityViewStage stage, Control target, double x = 40, double y = 17)
    {
        var point = target.TranslatePoint(new Point(x, y), stage.Window)!.Value;
        stage.Window.MouseDown(point, MouseButton.Left);
        stage.Window.MouseUp(point, MouseButton.Left);
        stage.Layout();
    }

    private static void Tampered(ActivityLogRig data, string day, int index, string from, string to)
    {
        data.Close();
        var lines = data.Lines(day);
        lines[index] = lines[index].Replace(from, to, StringComparison.Ordinal);
        data.Rewrite(day, lines);
    }

    [Fact]
    public void Opening_the_view_checks_the_chain_and_says_verified()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            using var stage = new ActivityViewStage(data);

            var strip = stage.View.Strip;
            Assert.Equal(ActivityLoadPhase.Ready, stage.Model.Phase);
            Assert.Equal("Verified", strip.Headline);
            Assert.Equal("6 lines checked, each sealed by the one before it", strip.Detail);
            Assert.Equal("checkmark.shield.fill", strip.Symbol);
            Assert.Null(strip.ActionTitle);
            Assert.Equal("6 lines", stage.View.SubtitleText);
            var headline = Assert.Single(Nodes<LiveStatusText>(strip));
            Assert.Equal("Verified. 6 lines checked, each sealed by the one before it", AutomationProperties.GetName(headline));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(headline));
        });
    }

    [Fact]
    public void The_view_shows_a_spinner_while_the_chain_is_checked_and_the_result_when_it_ends()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            var work = new GatedActivityWork { Gated = true };
            using var stage = new ActivityViewStage(data, work: work);

            Assert.Equal(ActivityLoadPhase.Checking, stage.Model.Phase);
            Assert.Equal("Checking the log's chain…", stage.View.Strip.Headline);
            Assert.Null(stage.View.Strip.Symbol); // the spinner
            Assert.Single(Nodes<ProgressBar>(stage.View.Strip));
            Assert.Empty(Cells(stage));
            Assert.Equal("", stage.View.SubtitleText);

            work.Release(0);
            work.Release(1);
            stage.Layout();

            Assert.Equal("Verified", stage.View.Strip.Headline);
            Assert.Empty(Nodes<ProgressBar>(stage.View.Strip));
            Assert.Equal(8, Cells(stage).Count());
        });
    }

    [Fact]
    public void The_log_lists_newest_first_under_day_headers_with_one_cell_of_34_a_line()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            using var stage = new ActivityViewStage(data);

            var cells = Cells(stage).ToList();
            Assert.Equal(8, cells.Count);
            for (var i = 0; i < cells.Count; i++)
            {
                Assert.Equal(i * 34.0, cells[i].Bounds.Y, 3);
                Assert.Equal(34.0, cells[i].Bounds.Height, 3);
            }
            Assert.True(cells[0].IsHeader);
            Assert.Equal("Yesterday", cells[0].TitleText);
            Assert.Equal("4 lines", cells[0].SubtitleText);
            var last = cells[1];
            Assert.Equal(6, last.Item!.Row!.Sequence);
            Assert.Equal("08:03:30", last.TimeText);
            Assert.Equal("dextra", last.AssistantText);
            Assert.Equal("shell · E:/shop", last.WhatText);
            Assert.Equal("Run", last.ClassText);
            Assert.True(last.ClassVisible);
            Assert.Equal("grant:g-1", last.GrantText);
            Assert.True(last.GrantButton.IsVisible);
            Assert.False(last.ShowsTrustMark);
            Assert.Equal("08:03:30, dextra, shell · E:/shop, Run, mission m-3, allowed by grant:g-1, tests ok, Verified", AutomationProperties.GetName(last));
            Assert.Equal("astur-shop", cells[3].AssistantText);
            Assert.Equal("mail · Lee", cells[3].WhatText);
            Assert.Equal("decision:d-7", cells[3].GrantText);
            Assert.Equal("mail · Pat", cells[4].WhatText);
            Assert.Equal("Sun 11 Oct 2026", cells[5].TitleText);
            Assert.Equal("2 lines", cells[5].SubtitleText);
            Assert.Equal("", cells[6].GrantText); // line 2 names no grant
            Assert.False(cells[6].GrantButton.IsVisible);
            Assert.Equal("web · https://example.test/docs", cells[6].WhatText);
            Assert.Equal("20:00:00", cells[7].TimeText);
            Assert.Equal(AutomationControlType.List, AutomationProperties.GetControlTypeOverride(stage.View.List));
            Assert.Equal(AutomationControlType.ListItem, AutomationProperties.GetControlTypeOverride(cells[1]));
        });
    }

    [Fact]
    public void Picking_a_line_with_the_pointer_selects_it_and_fills_the_detail()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            using var stage = new ActivityViewStage(data);
            Assert.Contains("Select a line", Assert.Single(stage.View.Detail.Texts), StringComparison.Ordinal);
            Assert.All(Cells(stage), c => Assert.Equal(0, Alpha(c)));

            PointerClick(stage, CellOf(stage, 6));

            Assert.Equal(6, stage.Model.Selected!.Sequence);
            Assert.True(Alpha(CellOf(stage, 6)) > 0, "the picked line is filled");
            Assert.Equal(0, Alpha(CellOf(stage, 5)));
            var texts = stage.View.Detail.Texts;
            foreach (var expected in new[]
            {
                "shell", "When", "2026-10-12 08:03:30 +00:00", "Assistant", "dextra", "Mission", "m-3", "Agent", "t-1", "Tool", "Class", "Run",
                "Target", "E:/shop", "Result", "tests ok", "Allowed by", "grant:g-1", "Cost", "$0.05", "Tokens", "500",
                "In the chain", "Check", "Verified", "Line", "4 of 2026-10-12.jsonl · number 6", "HMAC", "Previous",
            })
                Assert.Contains(expected, texts);
            Assert.Equal(
                ["Filter to this assistant", "Show this mission's lineage", "Filter to this agent", "Filter to this class", "Filter to this grant"],
                stage.View.Detail.Actions.Select(AutomationProperties.GetName));
        });
    }

    [Fact]
    public void A_line_with_inputs_lists_them_and_a_secret_stays_masked()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            using var stage = new ActivityViewStage(data);

            PointerClick(stage, CellOf(stage, 3));

            var texts = stage.View.Detail.Texts;
            Assert.Contains("Inputs", texts);
            Assert.Contains("body", texts);
            Assert.Contains("typed 148 characters", texts);
            Assert.Contains("token", texts);
            Assert.Contains("[secret]", texts);
            Assert.Contains("decision:d-7", texts);
        });
    }

    [Fact]
    public void The_detail_buttons_walk_the_lineage_by_filtering_to_a_grant_a_mission_and_the_rest()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            using var stage = new ActivityViewStage(data);
            PointerClick(stage, CellOf(stage, 6));

            ActivityViewStage.Click(stage.View.Detail.Actions[4]); // Filter to this grant
            stage.Layout();

            Assert.Equal("grant:g-1", stage.Model.Filter.Grant);
            Assert.Equal(4, Cells(stage).Count()); // two day headers, lines 6 and 1
            Assert.Equal("2 lines match of 6 lines · grant grant:g-1", stage.View.SubtitleText);
            Assert.Equal(6, stage.Model.Selected!.Sequence); // the picked line still passes, so it stays picked

            Sync(stage.Model.ClearFilterAsync());
            ActivityViewStage.Click(CellOf(stage, 1).GrantButton); // the grant on a line filters to it, too
            stage.Layout();
            Assert.Equal("grant:g-1", stage.Model.Filter.Grant);

            Sync(stage.Model.ClearFilterAsync());
            stage.Layout();
            ActivityViewStage.Click(stage.View.Detail.Actions[1]); // Show this mission's lineage
            stage.Layout();

            Assert.Equal(ActivityViewMode.Lineage, stage.Model.Mode);
            Assert.Equal("m-3", stage.Model.Filter.Mission);
            Assert.Equal(3, Cells(stage).Count()); // the mission, its agent, its one line
            Assert.Equal(ActivityViewMode.Lineage, stage.View.Filters.State.Mode);
            Assert.Equal("m-3", stage.View.Filters.State.Mission);
        });
    }

    [Fact]
    public void The_pickers_offer_the_values_the_log_holds_and_choosing_one_filters()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            using var stage = new ActivityViewStage(data);
            var filters = stage.View.Filters;

            PopoverItem[] Open(GlassPickerButton<string?> picker)
            {
                var button = Assert.Single(Nodes<Button>(picker));
                ActivityViewStage.Click(button);
                return stage.OpenedItems(button);
            }

            static string[] Titles(PopoverItem[] items) => items.Select(i => Texts(i).First()).ToArray();

            var assistants = Open(filters.AssistantPicker!);
            Assert.Equal(["All assistants", "astur-shop", "dextra"], Titles(assistants));
            Assert.Equal(["All missions", "m-3", "m-2", "m-1", "No mission"], Titles(Open(filters.MissionPicker!)));
            Assert.Equal(["All classes", "Agents", "Outward", "Read web", "Run"], Titles(Open(filters.ClassPicker!)));
            Assert.Equal(["Any day", "Today", "Last 7 days", "Last 30 days", "Yesterday · 4 lines", "Sun 11 Oct 2026 · 2 lines"], Titles(Open(filters.DayPicker!)));

            Assert.Equal([1.0, 0.0, 0.0], assistants.Select(i => Nodes<SwiftImage>(i).Single(image => image.SymbolName == "checkmark").Opacity)); // the check is on "All assistants"
            ActivityViewStage.Click(Assert.Single(Nodes<Button>(assistants[2])));
            stage.Layout();

            Assert.Equal("dextra", stage.Model.Filter.Assistant);
            Assert.Contains("dextra", Texts(filters.AssistantPicker!));
            Assert.Equal(6, Cells(stage).Count()); // lines 6, 5, 2, 1 under two day headers
            Assert.Equal("4 lines match of 6 lines · assistant dextra", stage.View.SubtitleText);
        });
    }

    [Fact]
    public void The_day_picker_sets_the_two_days_and_a_filter_set_elsewhere_shows_in_the_pickers()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            using var stage = new ActivityViewStage(data);
            var filters = stage.View.Filters;
            var button = Assert.Single(Nodes<Button>(filters.DayPicker!));

            ActivityViewStage.Click(button);
            ActivityViewStage.Click(Assert.Single(Nodes<Button>(stage.OpenedItems(button)[4]))); // Yesterday · 4 lines
            stage.Layout();

            Assert.Equal(new DateOnly(2026, 10, 12), stage.Model.Filter.From);
            Assert.Equal(new DateOnly(2026, 10, 12), stage.Model.Filter.To);
            Assert.Equal("2026-10-12", filters.State.Day);
            Assert.Equal(5, Cells(stage).Count());

            ActivityViewStage.Click(button);
            ActivityViewStage.Click(Assert.Single(Nodes<Button>(stage.OpenedItems(button)[2]))); // Last 7 days
            stage.Layout();

            Assert.Equal(new DateOnly(2026, 10, 7), stage.Model.Filter.From);
            Assert.Equal(new DateOnly(2026, 10, 13), stage.Model.Filter.To);
            Assert.Equal("7", filters.State.Day);
            Assert.Equal(8, Cells(stage).Count());

            // A filter set by the model (a mission card's Lineage button) shows in the bar without writing back.
            Sync(stage.Model.ShowMissionAsync("m-2"));
            stage.Layout();
            Assert.Equal("m-2", filters.State.Mission);
            Assert.Null(filters.State.Day);
            Assert.Contains("m-2", Texts(filters.MissionPicker!));
            Assert.Equal(ActivityViewMode.Lineage, filters.State.Mode);
        });
    }

    [Fact]
    public void Typing_filters_after_the_delay_and_Enter_does_not_wait_and_Clear_filters_is_always_laid_out()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            using var stage = new ActivityViewStage(data);
            var filters = stage.View.Filters;
            var box = Assert.Single(Nodes<TextBox>(filters));
            var clear = filters.ClearButton;
            Assert.False(clear.IsEnabled);
            Assert.Equal(0, clear.Opacity);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(clear));
            var size = clear.Bounds.Size;
            Assert.Equal(32, size.Height);

            box.Text = "shop";
            stage.Layout();

            Assert.Equal("shop", filters.State.Text);
            Assert.Equal("", stage.Model.Filter.Text); // the delay has not passed

            filters.FlushSearch();
            stage.Layout();

            Assert.Equal("shop", stage.Model.Filter.Text);
            Assert.Equal(6, Cells(stage).Count()); // lines 6, 4, 3 and 1, under two headers
            Assert.True(clear.IsEnabled);
            Assert.Equal(1, clear.Opacity);
            Assert.Equal(size, clear.Bounds.Size); // it only fades
            Assert.NotEqual(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(clear));

            box.Text = "mail";
            Assert.Equal("shop", stage.Model.Filter.Text);
            var field = Assert.Single(Nodes<ChromeSearchField>(filters));
            field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            stage.Layout();
            Assert.Equal("mail", stage.Model.Filter.Text);
            Assert.Equal(2, stage.Model.MatchCount);

            ActivityViewStage.Click(clear);
            stage.Layout();

            Assert.True(stage.Model.Filter.IsEmpty);
            Assert.Equal("", filters.State.Text);
            Assert.Equal("", box.Text ?? "");
            Assert.False(clear.IsEnabled);
            Assert.Equal(0, clear.Opacity);
            Assert.Equal(size, clear.Bounds.Size);
            Assert.Equal(8, Cells(stage).Count());
        });
    }

    [Fact]
    public void A_line_that_fails_shows_where_marks_it_and_what_follows_and_Show_line_brings_it_into_sight()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            Tampered(data, "2026-10-12", 2, "duty armed", "duty x"); // line 5 of the log
            using var stage = new ActivityViewStage(data);

            var strip = stage.View.Strip;
            Assert.Equal("Line 3 of 2026-10-12.jsonl fails: its HMAC does not match its contents (it was edited, or sealed without the key).", strip.Headline);
            Assert.Equal("4 lines verified before the failure; the lines from it on are marked", strip.Detail);
            Assert.Equal("xmark.octagon.fill", strip.Symbol);
            Assert.Equal("Show line", strip.ActionTitle);
            foreach (var sequence in new long[] { 1, 2, 3, 4 })
            {
                Assert.False(CellOf(stage, sequence).ShowsTrustMark, "line " + sequence);
                Assert.Equal(0, Alpha(CellOf(stage, sequence)));
            }
            foreach (var sequence in new long[] { 5, 6 })
            {
                Assert.Equal("xmark.octagon.fill", CellOf(stage, sequence).TrustSymbol);
                Assert.True(Alpha(CellOf(stage, sequence)) > 0, "line " + sequence + " is tinted");
            }

            Sync(stage.Model.SetFilterAsync(new ActivityFilter { Assistant = "astur-shop" })); // hides the failing line
            stage.Layout();
            Assert.DoesNotContain(Cells(stage), c => c.Item?.Row?.Sequence == 5);

            ActivityViewStage.Click(strip.ActionButton!);
            stage.Layout();

            Assert.True(stage.Model.Filter.IsEmpty);
            Assert.Equal(5, stage.Model.Selected!.Sequence);
            Assert.Contains("Not verified", stage.View.Detail.Texts);
            Assert.True(Alpha(CellOf(stage, 5)) > 0);
        });
    }

    [Fact]
    public void A_line_that_is_not_a_sealed_audit_line_is_a_row_you_can_see_and_pick()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            data.Close();
            var lines = data.Lines("2026-10-11");
            lines[1] = "garbage password=hunter2";
            data.Rewrite("2026-10-11", lines);
            using var stage = new ActivityViewStage(data);

            Assert.Equal("Line 2 of 2026-10-11.jsonl fails: it is not a sealed audit line.", stage.View.Strip.Headline);
            var garbage = Assert.Single(Cells(stage), c => c.Item?.Row?.Kind == ActivityRowKind.Unreadable);
            var garbageRow = garbage.Item!.Row;
            Assert.Equal("Unreadable line", garbage.WhatText);
            Assert.Equal("line 2", garbage.TimeText);
            Assert.Equal("", garbage.AssistantText);
            Assert.True(garbage.ShowsTrustMark);
            Assert.Equal("Line 2 of 2026-10-11.jsonl, unreadable. Not verified.", AutomationProperties.GetName(garbage));

            ActivityViewStage.Click(stage.View.Strip.ActionButton!);
            stage.Layout();

            Assert.Same(garbageRow, stage.Model.Selected);
            var texts = stage.View.Detail.Texts;
            Assert.Contains("Unreadable line", texts);
            Assert.Contains("Why", texts);
            Assert.DoesNotContain(texts, t => t.Contains("hunter2", StringComparison.Ordinal)); // masked even here
            Assert.Contains(texts, t => t.StartsWith("It is not a sealed audit line. garbage password=[secret]", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void A_log_that_cannot_be_read_says_why_and_an_empty_log_says_so_and_a_filter_with_no_match_says_so()
    {
        headless.OnUiThread(() =>
        {
            using (var data = ActivityDataset.Make())
            using (var stage = new ActivityViewStage(data, source: data.Source with { PrintedDayFinals = () => throw new IOException("The disk went away.") }))
            {
                Assert.Equal("The log could not be read", stage.View.Strip.Headline);
                Assert.Equal("The disk went away.", stage.View.Strip.Detail);
                Assert.Equal("exclamationmark.triangle.fill", stage.View.Strip.Symbol);
                Assert.Empty(Cells(stage));
                Assert.Equal("", stage.View.SubtitleText);
            }
            using (var empty = new ActivityLogRig())
            using (var stage = new ActivityViewStage(empty))
            {
                Assert.Equal("No activity yet", stage.View.Strip.Headline);
                Assert.Equal(ActivityWords.EmptyLog, stage.View.Strip.Detail);
                Assert.Equal(ActivityWords.EmptyLog, stage.View.List.Message);
                Assert.Equal("0 lines", stage.View.SubtitleText);
            }
            using (var data = ActivityDataset.Make())
            using (var stage = new ActivityViewStage(data))
            {
                Sync(stage.Model.SetFilterAsync(ActivityFilter.ForMission("m-9")));
                stage.Layout();
                Assert.Equal(ActivityWords.NothingMatches, stage.View.List.Message);
                Assert.Empty(Cells(stage));
                Assert.Equal("Verified", stage.View.Strip.Headline);
                Assert.Equal("0 lines match of 6 lines · mission m-9", stage.View.SubtitleText);
            }
        });
    }

    [Fact]
    public void The_lineage_lists_mission_agent_and_action_and_a_header_folds_with_the_pointer_and_the_keyboard()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            using var stage = new ActivityViewStage(data);

            Sync(stage.Model.SetModeAsync(ActivityViewMode.Lineage));
            stage.Layout();

            var cells = Cells(stage).ToList();
            Assert.Equal(15, cells.Count);
            Assert.Equal("m-3", cells[0].TitleText);
            Assert.Equal("dextra", cells[0].SubtitleText);
            Assert.Equal("1 line · $0.05 · 500 tokens · 1 grant or decision", cells[0].StatsText);
            Assert.Equal(90, cells[0].ChevronAngle);
            Assert.Equal("t-1", cells[1].TitleText);
            Assert.Equal("1 action", cells[1].StatsText);
            Assert.Equal(6, cells[2].Item!.Row!.Sequence);
            Assert.Equal("m-2", cells[3].TitleText);
            Assert.Equal("No mission", cells[12].TitleText);
            Assert.Equal("No agent", cells[13].TitleText);
            Assert.Equal("m-3, dextra, 1 line · $0.05 · 500 tokens · 1 grant or decision, expanded", AutomationProperties.GetName(cells[0]));

            PointerClick(stage, cells[3]); // the header of m-2 folds

            Assert.Equal(12, Cells(stage).Count());
            Assert.Equal(0, Cells(stage).First(c => c.TitleText == "m-2").ChevronAngle);
            Assert.Contains(ActivityListBuilder.MissionKey("m-2"), stage.Model.Folded);

            stage.View.List.Focus();
            stage.Layout();
            Press(stage, Key.Right); // Right on a folded header opens it
            Assert.Equal(15, Cells(stage).Count());
        });
    }

    [Fact]
    public void The_keyboard_moves_through_the_lines_and_skips_the_day_headers()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            using var stage = new ActivityViewStage(data);
            var list = stage.View.List;

            list.Focus();
            stage.Layout();
            Assert.Equal(1, list.KeyboardRow); // the first line, not the day header over it

            Press(stage, Key.Down);
            Assert.Equal(5, stage.Model.Selected!.Sequence);
            Press(stage, Key.Down);
            Press(stage, Key.Down);
            Press(stage, Key.Down);
            Assert.Equal(2, stage.Model.Selected!.Sequence); // lines 6, 5, 4, 3 and then over the 11th's header to line 2
            Press(stage, Key.End);
            Assert.Equal(1, stage.Model.Selected!.Sequence);
            Press(stage, Key.Home);
            Assert.Equal(6, stage.Model.Selected!.Sequence);
            Press(stage, Key.Up);
            Assert.Equal(6, stage.Model.Selected!.Sequence);
            Press(stage, Key.PageDown);
            Assert.Equal(1, stage.Model.Selected!.Sequence);
            Press(stage, Key.PageUp);
            Assert.Equal(6, stage.Model.Selected!.Sequence);
            Assert.Contains("grant:g-1", stage.View.Detail.Texts);
        });
    }

    [Fact]
    public void Export_asks_where_to_save_then_writes_and_says_so_and_a_cancel_writes_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            var asked = new List<(ActivityExportFormat Format, string Name)>();
            string? answer = null;
            using var stage = new ActivityViewStage(data, pickPath: (format, name) =>
            {
                asked.Add((format, name));
                return Task.FromResult(answer);
            });
            Assert.Equal(["JSON, every field", "CSV, for a spreadsheet"], stage.View.ExportChoices);

            Sync(stage.View.ExportAsync(ActivityExportFormat.Json)); // the owner cancels
            Assert.Equal(ActivityExportPhase.Idle, stage.Model.Export.Phase);
            Assert.Equal("", stage.View.ExportNote);

            var path = Path.Combine(data.Temp.Folder, "exported.json");
            answer = path;
            Sync(stage.View.ExportAsync(ActivityExportFormat.Json));
            Assert.Equal([(ActivityExportFormat.Json, "nth-activity-2026-10-13.json"), (ActivityExportFormat.Json, "nth-activity-2026-10-13.json")], asked);
            Assert.Equal(ActivityExportPhase.Done, stage.Model.Export.Phase);
            Assert.Equal($"Exported 6 lines to {path}", stage.View.ExportNote);
            Assert.True(File.Exists(path));

            answer = Path.Combine(data.Temp.Folder, "no-such-folder", "exported.csv");
            Sync(stage.View.ExportAsync(ActivityExportFormat.Csv));
            Assert.Equal(ActivityExportPhase.Failed, stage.Model.Export.Phase);
            Assert.StartsWith("The export failed: ", stage.View.ExportNote, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_export_menu_offers_two_formats_and_choosing_one_asks_where_to_save()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            var path = Path.Combine(data.Temp.Folder, "menu.csv");
            var asked = new List<ActivityExportFormat>();
            using var stage = new ActivityViewStage(data, pickPath: (format, _) =>
            {
                asked.Add(format);
                return Task.FromResult<string?>(path);
            });

            ActivityViewStage.Click(stage.View.ExportControl);
            var items = stage.OpenedItems(stage.View.ExportControl);

            Assert.Equal(["JSON, every field", "CSV, for a spreadsheet"], items.Select(i => Texts(i).First()));
            Assert.Equal(["curlybraces", "list.bullet"], items.Select(i => Nodes<SwiftImage>(i).First().SymbolName));
            ActivityViewStage.Click(Assert.Single(Nodes<Button>(items[1])));
            stage.Layout();

            Assert.Equal([ActivityExportFormat.Csv], asked);
            Assert.True(File.Exists(path));
            Assert.Equal(7, File.ReadAllText(path).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.Equal("Export the lines shown, as JSON or CSV", AutomationProperties.GetName(stage.View.ExportControl));
        });
    }

    [Fact]
    public void Refresh_reads_and_checks_the_log_again()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            using var stage = new ActivityViewStage(data);
            Assert.Equal("6 lines", stage.View.SubtitleText);
            data.Add(result: "one more");

            ActivityViewStage.Click(stage.View.RefreshControl);
            stage.Layout();

            Assert.Equal("7 lines", stage.View.SubtitleText);
            Assert.Equal("7 lines checked, each sealed by the one before it", stage.View.Strip.Detail);
            Assert.Equal("Refresh", AutomationProperties.GetName(stage.View.RefreshControl));
        });
    }

    [Fact]
    public void A_view_that_leaves_the_window_lets_go_of_the_log_and_reads_it_again_when_it_returns()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            using var stage = new ActivityViewStage(data);
            Sync(stage.Model.SetFilterAsync(new ActivityFilter { Assistant = "dextra" }));
            Assert.Equal(ActivityLoadPhase.Ready, stage.Model.Phase);

            stage.Window.Content = null;
            stage.Layout();

            Assert.Equal(ActivityLoadPhase.Idle, stage.Model.Phase);
            Assert.Null(stage.Model.Snapshot);
            Assert.Equal("dextra", stage.Model.Filter.Assistant);

            stage.Window.Content = stage.View;
            stage.Layout();

            Assert.Equal(ActivityLoadPhase.Ready, stage.Model.Phase);
            Assert.Equal(4, stage.Model.MatchCount);
            Assert.Equal("dextra", stage.View.Filters.State.Assistant);
        });
    }

    [Fact]
    public void Narrow_windows_stack_the_detail_under_the_list_and_very_narrow_ones_leave_out_the_class_and_the_grant()
    {
        headless.OnUiThread(() =>
        {
            using var data = ActivityDataset.Make();
            using var stage = new ActivityViewStage(data);
            var listCard = (Control)stage.View.List.Parent!;
            var detailCard = (Control)stage.View.Detail.Parent!;
            Point At(Control control) => control.TranslatePoint(new Point(0, 0), stage.View)!.Value;

            Assert.False(stage.View.IsStacked);
            Assert.False(stage.View.List.IsCompact);
            Assert.True(At(detailCard).X >= At(listCard).X + listCard.Bounds.Width, "the detail sits beside the list");
            Assert.Equal(340, detailCard.Bounds.Width, 3);
            Assert.True(CellOf(stage, 6).ClassVisible);

            stage.Window.Width = 700;
            stage.Layout();

            Assert.True(stage.View.IsStacked);
            Assert.Equal(At(listCard).X, At(detailCard).X, 3);
            Assert.True(At(detailCard).Y >= At(listCard).Y + listCard.Bounds.Height, "the detail sits under the list");
            Assert.Equal(260, detailCard.Bounds.Height, 3);

            stage.Window.Width = 560;
            stage.Layout();

            Assert.True(stage.View.List.IsCompact);
            Assert.False(CellOf(stage, 6).ClassVisible);
            Assert.False(CellOf(stage, 6).GrantButton.IsVisible);
            Assert.Equal("shell · E:/shop", CellOf(stage, 6).WhatText);
        });
    }

    [Fact]
    public void A_long_log_keeps_only_the_lines_on_screen_as_cells_and_scrolling_moves_them()
    {
        headless.OnUiThread(() =>
        {
            using var data = new ActivityLogRig();
            data.AddMany(400, i => new AuditEntry { Assistant = "dextra", Mission = "m-1", Agent = "t-1", Tool = "shell", ActionClass = "Run", Result = "ok " + i });
            using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
            using var stage = new ActivityViewStage(data);
            var list = stage.View.List;
            var viewport = list.Scroll.Viewport.Height;

            Assert.Equal(401 * 34.0, list.Scroll.Extent.Height, 3); // 400 lines and one day header
            Assert.InRange(list.Cells.Count, 1, viewport / 34 + 2 * ActivityRowList.Buffer + 2);
            Assert.True(list.Cells.ContainsKey(0));
            Assert.False(list.Cells.ContainsKey(200));

            list.Scroll.Offset = new Vector(0, 34 * 200);
            stage.Layout();

            Assert.True(list.Cells.ContainsKey(200));
            Assert.False(list.Cells.ContainsKey(0));
            Assert.Equal(34 * 200.0, list.Cells[200].Bounds.Y, 3);
            Assert.InRange(list.Cells.Count, 1, viewport / 34 + 2 * ActivityRowList.Buffer + 2);

            list.ScrollTo(380); // with Reduce motion it is there at once
            stage.Layout();

            Assert.True(list.Cells.ContainsKey(380));
            var cell = list.Cells[380];
            Assert.True(cell.Bounds.Y >= list.Scroll.Offset.Y - 0.5 && cell.Bounds.Bottom <= list.Scroll.Offset.Y + viewport + 0.5, "line 380 is in view");
        });
    }
}
