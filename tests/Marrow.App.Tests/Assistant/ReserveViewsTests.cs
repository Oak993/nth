using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-13 (DESIGN.md rev 5, 11.3 to 11.5; the approved canvas "Usage reserves"; ED-NEW-dextra-as13-10): the plan card (the one-line
// plan, how the rule was read, a bar per account, Start when the plan needs the owner, then the after-report) and the Reserves view
// (standing rules, the line to say a new one in, the one question, the two switches, a bar per account window). In a headless window.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-as13-10")]
[Trait("Feature", "AS-13")]
public sealed class ReserveViewsTests(HeadlessSession headless)
{
    private static PlanAccountLine Account(string name, double used, double p50, double p90, double? floor, bool rough = false, ReserveWindowKind kind = ReserveWindowKind.FiveHour) =>
        new("key:" + name, name, kind, "", used, p50, p90, floor, rough);

    private static ReservePlan Canvas() => new([
        Account("Codex A", 22, 12, 14, 76.6, kind: ReserveWindowKind.Weekly),
        Account("Claude B", 41, 8, 10, 82.3, kind: ReserveWindowKind.Weekly),
        Account("Claude 20x", 60, 4, 6, 70),
    ])
    {
        Reading = "Keeping 30% of what's left on each account",
        Reserves = [new PlanReserve("Claude 20x", 30, ReserveLifetime.Standing)],
        Team = "Hydra team of 4",
        Duration = TimeSpan.FromMinutes(25),
    };

    // ---- the plan card ----

    [Fact]
    public void The_plan_card_says_the_plan_how_the_rule_was_read_and_shows_a_bar_per_account_with_Start_when_the_owner_must_tap()
    {
        headless.OnUiThread(() =>
        {
            var started = 0;
            var adjusted = 0;
            var card = new PlanCard(() => started++, () => adjusted++);
            card.ShowPlan(Canvas());
            using var stage = new ReserveViewStage(card);

            Assert.Equal("About 12% of Codex A, 8% of Claude B and up to 6% of Claude 20x. Your 30% reserve on Claude 20x stays.", card.Line);
            Assert.Equal(Canvas().Details, card.Reading);
            Assert.Equal(["Codex A", "Claude B", "Claude 20x"], card.Rows.Select(r => r.AccountName));
            Assert.Equal(["22.0% used · plan +12.0% · floor 76.6% · weekly", "41.0% used · plan +8.0% · floor 82.3% · weekly", "60.0% used · plan +6.0% · floor 70.0% · 5-hour window"],
                card.Rows.Select(r => r.Numbers));
            Assert.Equal([14.0, 10.0, 6.0], card.Rows.Select(r => r.Bar.Planned));          // the bar draws the p90, the one checked against the floor
            Assert.Equal<double?[]>([76.6, 82.3, 70.0], [.. card.Rows.Select(r => r.Bar.Floor)]);
            Assert.Equal("Start needs you: over 5% of a window; a floor is close.", card.Note);
            Assert.Contains(card.Note, Texts(card));

            Assert.NotNull(card.StartButton);
            Assert.NotNull(card.AdjustButton);
            Assert.Equal("Start the task as planned", AutomationProperties.GetName(card.StartButton!));
            Assert.Equal("Change what the task may use", AutomationProperties.GetName(card.AdjustButton!));
            Click(card.StartButton!);
            Click(card.AdjustButton!);
            Assert.Equal((1, 1), (started, adjusted));
            Assert.Equal("Claude 20x: 60.0% used · plan +6.0% · floor 70.0% · 5-hour window", AutomationProperties.GetName(card.Rows[2]));

            // The task starts: the buttons give way to one line that is read out.
            card.ShowRunning("Running · tracking live");
            stage.Layout();
            Assert.Null(card.StartButton);
            Assert.Null(card.AdjustButton);
            Assert.Contains("Running · tracking live", Texts(card));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Nodes<LiveStatusText>(card).Single(t => t.Text == "Running · tracking live")));
        });
    }

    [Fact]
    public void A_plan_that_needs_no_tap_shows_no_Start_and_says_it_starts_on_its_own()
    {
        headless.OnUiThread(() =>
        {
            var card = new PlanCard(() => { }, () => { });
            card.ShowPlan(new ReservePlan([Account("Codex A", 20, 2, 3, 70)]) { Reading = "Keeping 30% of what's left on each account" });
            using var stage = new ReserveViewStage(card);
            Assert.Null(card.StartButton);
            Assert.NotNull(card.AdjustButton);
            Assert.Equal("", card.Note);
            Assert.EndsWith("starts on its own", card.Reading, StringComparison.Ordinal);

            var bare = new PlanCard();                               // given nothing to do, it shows no button at all
            bare.ShowPlan(Canvas());
            using var bareStage = new ReserveViewStage(bare);
            Assert.Null(bare.StartButton);
            Assert.Null(bare.AdjustButton);
            Assert.NotEmpty(bare.Note);
        });
    }

    [Fact]
    public void After_the_task_the_card_shows_what_each_account_used_the_state_of_the_reserves_and_the_outside_use_apart()
    {
        headless.OnUiThread(() =>
        {
            var since = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
            var report = new ReserveAfterReport("Thesis outline",
                [new ReportRow("Codex A", 12, 11.6, "weekly"), new ReportRow("Claude 20x", 6, 6.4, "5-hour window")],
                [new ReportOutside("Claude 20x", "5-hour window", 8, since)],
                [new ReportReserve("Claude 20x", "Always keep 30% on Claude 20x", 0, false)]);
            var card = new PlanCard(() => { }, () => { });
            card.ShowPlan(Canvas());
            card.ShowReport(report, "Done: 40-page outline with 63 sources, reviewed.");
            using var stage = new ReserveViewStage(card);

            Assert.Equal("Done: 40-page outline with 63 sources, reviewed.", card.Line);
            Assert.Equal("Every reserve intact.", card.Reading);
            Assert.Null(card.StartButton);
            Assert.Null(card.AdjustButton);
            Assert.Empty(card.Rows);
            var texts = Texts(card);
            Assert.Equal(["Account", "Planned", "Used", "Codex A", "12%", "11.6%", "Claude 20x", "6%", "6.4%"], texts.Skip(2).Take(9).ToArray());
            Assert.Contains(report.OutsideLines[0], texts);
            Assert.Equal("Usage report", AutomationProperties.GetName(card));
        });
    }

    [Fact]
    public void Long_names_wrap_inside_a_narrow_card_and_a_right_to_left_window_lays_it_out_all_the_same()
    {
        headless.OnUiThread(() =>
        {
            var name = "Claude Max with the very long name the owner gave to his second work account";
            var plan = new ReservePlan([Account(name, 22, 12, 14, 76.6), Account("Codex A", 41, 8, 10, 82.3, rough: true)])
            {
                Reading = "Keeping 30% of what's left on each account", Reserves = [new PlanReserve(name, 30, ReserveLifetime.Task)],
            };
            var card = new PlanCard(() => { }, () => { });
            card.ShowPlan(plan);
            card.Measure(new Size(320, double.PositiveInfinity));
            Assert.True(card.DesiredSize.Width <= 320.01, $"the card asked for {card.DesiredSize.Width}");
            Assert.True(card.DesiredSize.Height > 120);

            foreach (var flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            {
                var shown = new PlanCard(() => { }, () => { });
                shown.ShowPlan(plan);
                using var stage = new ReserveViewStage(shown, 320, 600, flow);
                Assert.True(shown.Bounds.Width <= 320.01);
                Assert.True(shown.StartButton!.Bounds.Width > 0);
                var button = shown.StartButton!.TranslatePoint(new Point(0, 0), stage.Window)!.Value;
                Assert.InRange(button.X, 0, 320);                    // the button is inside the window, whichever way the text runs
            }
        });
    }

    [Fact]
    public void The_bar_keeps_what_it_is_given_and_draws_at_any_width_and_value()
    {
        headless.OnUiThread(() =>
        {
            var bar = new ReserveBar();
            bar.Set(60, 6, 70, 55);                                  // eased: the floor was 55 before
            Assert.Equal((60.0, 6.0, 70.0, 55.0), (bar.Used, bar.Planned, bar.Floor!.Value, bar.Unadjusted!.Value));
            bar.Set(60, 6, 70, 70);                                  // not eased: no marker
            Assert.Null(bar.Unadjusted);
            bar.Set(double.NaN, double.NaN, double.NaN);
            Assert.Equal((0.0, 0.0), (bar.Used, bar.Planned));
            Assert.Null(bar.Floor);
            bar.Set(140, -5, 100);                                   // over 100 is 100; a floor of 100 is no reserve
            Assert.Equal((100.0, 0.0), (bar.Used, bar.Planned));
            Assert.Null(bar.Floor);
            Assert.Equal(ReserveBar.BoxHeight, bar.Height);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(bar));

            foreach (var width in new[] { 40.0, 200.0, 600.0 })
            {
                bar.Set(60, 6, 70, 55);
                var host = new Border { Width = width, Height = ReserveBar.BoxHeight, Child = bar };
                host.Measure(new Size(width, ReserveBar.BoxHeight));
                host.Arrange(new Rect(0, 0, width, ReserveBar.BoxHeight));
                Assert.Equal(width, bar.Bounds.Width, 6);
                var target = new RenderTargetBitmap(new PixelSize((int)width, (int)ReserveBar.BoxHeight));
                target.Render(host);                                 // drawn without throwing; nothing was measured again to do it
                host.Child = null;
            }
        });
    }

    // ---- the Reserves view ----

    private static ReserveAccountBar Bar(string name, double used, double? floor, double outside = 0, bool rough = false, bool eased = false, double? before = null) =>
        new("key:" + name, name, [new ReserveWindowBar("five", ReserveWindowKind.FiveHour, "5-hour limit", used, floor, before, eased, null)], outside, rough);

    private static (ReservesModel Model, ReserveSettings Settings, List<ReserveAccountBar> Bars) Setup(params ReserveAccountBar[] bars)
    {
        var settings = new ReserveSettings();
        var list = new List<ReserveAccountBar>(bars);
        return (new ReservesModel(settings, () => list), settings, list);
    }

    [Fact]
    public void The_Reserves_view_lists_the_standing_rules_and_a_bar_per_account_in_the_canvas_s_words()
    {
        headless.OnUiThread(() =>
        {
            var (model, settings, _) = Setup(Bar("Claude 20x", 60, 70, outside: 8), Bar("Codex A", 22, null, rough: true));
            Assert.Null(settings.Add(new ReserveRule("", ReserveScope.Named("Claude 20x"), ReserveLifetime.Standing) { KeepPercent = 30 }));
            Assert.Null(settings.Add(new ReserveRule("", ReserveScope.Named("OpenRouter key"), ReserveLifetime.Standing) { KeepDollars = 5m, Windows = ReserveWindows.Day }));
            var view = new ReservesView(model);
            using var stage = new ReserveViewStage(view, 1100, 800);

            var texts = Texts(view);
            Assert.Contains(ReservesView.Title, texts);
            Assert.Contains(ReservesView.RulesTitle, texts);
            Assert.Contains(ReservesView.AccountsTitle, texts);
            Assert.Equal(["Always keep 30% on Claude 20x", "Keep $5 a day on OpenRouter key"], view.RuleTitles);
            Assert.Contains("Standing · whole 5-hour and weekly windows · all assistants", texts);
            Assert.False(view.ShowsEmptyRules);
            Assert.False(view.ShowsEmptyAccounts);
            Assert.Equal(["Claude 20x", "Codex A"], view.AccountBars.Select(r => r.AccountName));
            Assert.Equal(["60.0% used · floor 70.0% · 5-hour limit · 8% used outside nth in the last 24 hours", "22.0% used · no reserve · 5-hour limit · estimate rough"],
                view.AccountBars.Select(r => r.Numbers));
            Assert.Equal<double?[]>([70.0, null], [.. view.AccountBars.Select(r => r.Bar.Floor)]);
            Assert.Contains(ReservesView.Hint, texts);
            foreach (var word in new[] { "Used", "Planned", "Reserve", "Floor" }) Assert.Contains(word, texts);
            Assert.False(view.IsStacked);
            Assert.Equal("New reserve rule", AutomationProperties.GetName(view.Field));
            Assert.All(view.RemoveButtons, b => Assert.StartsWith("Remove the rule: ", AutomationProperties.GetName(b), StringComparison.Ordinal));
        });
    }

    [Fact]
    public void An_easing_floor_shows_where_it_began_and_the_view_follows_the_guard_as_the_numbers_move()
    {
        headless.OnUiThread(() =>
        {
            var (model, _, bars) = Setup(Bar("Claude 20x", 60, 85, eased: true, before: 70));
            var view = new ReservesView(model);
            using var stage = new ReserveViewStage(view);
            var row = Assert.Single(view.AccountBars);
            Assert.Equal("60.0% used · floor 85.0% · 5-hour limit · easing toward the reset, was 70.0%", row.Numbers);
            Assert.Equal(70.0, row.Bar.Unadjusted);

            bars[0] = Bar("Claude 20x", 62, 90, eased: true, before: 70);                   // same account and window: the row is kept, not made again
            model.Raise();
            stage.Layout();
            Assert.Same(row, Assert.Single(view.AccountBars));
            Assert.Equal("62.0% used · floor 90.0% · 5-hour limit · easing toward the reset, was 70.0%", row.Numbers);
            bars.Add(Bar("Codex A", 10, null));
            model.Raise();
            Assert.Equal(2, view.AccountBars.Count);
            bars.Clear();
            model.Raise();
            Assert.True(view.ShowsEmptyAccounts);
            Assert.Equal(ReservesView.EmptyAccounts, Texts(view).Single(t => t == ReservesView.EmptyAccounts));
        });
    }

    [Fact]
    public void Saying_a_rule_adds_it_on_Enter_and_Escape_drops_the_words()
    {
        headless.OnUiThread(() =>
        {
            var (model, settings, _) = Setup(Bar("Claude 20x", 10, null), Bar("Codex A", 10, null));
            var view = new ReservesView(model);
            using var stage = new ReserveViewStage(view);
            Assert.True(view.ShowsEmptyRules);
            view.Field.Focus();
            view.Field.Text = "always keep 25% on codex a";
            stage.Press(Key.Enter);
            Assert.Equal(["Always keep 25% on Codex A"], view.RuleTitles);
            Assert.Equal("", view.Field.Text ?? "");                                  // the words are taken
            Assert.False(view.ShowsEmptyRules);
            Assert.Single(settings.Rules);

            view.Field.Focus();
            view.Field.Text = "keep plenty";
            stage.Press(Key.Enter);
            Assert.Equal("Say how much to keep, for example: always keep 25% on Codex A.", view.ProblemText);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Nodes<LiveStatusText>(view).Single(t => t.Text == view.ProblemText)));
            Assert.Single(settings.Rules);
            stage.Press(Key.Escape);
            Assert.Equal("", view.ProblemText);
            Assert.Equal("", view.Field.Text ?? "");
        });
    }

    [Fact]
    public void A_rule_that_does_not_name_its_accounts_gets_exactly_one_question_and_either_answer_makes_the_rule()
    {
        headless.OnUiThread(() =>
        {
            var (model, settings, _) = Setup(Bar("Claude 20x", 10, null), Bar("Codex A", 10, null));
            var view = new ReservesView(model);
            using var stage = new ReserveViewStage(view);
            view.Field.Focus();
            view.Field.Text = "keep 30%";
            stage.Press(Key.Enter);
            Assert.True(view.ShowsQuestion);
            Assert.Equal("Keep 30% on every account, or only on Claude 20x?", view.QuestionText);
            Assert.Empty(settings.Rules);                                              // nothing is guessed
            Assert.Equal("Keep it on every account", AutomationProperties.GetName(view.EveryButton!));
            Assert.Equal("Keep it only on Claude 20x", AutomationProperties.GetName(view.OnlyButton!));

            Click(view.OnlyButton!);
            stage.Layout();
            Assert.False(view.ShowsQuestion);
            Assert.Equal(["Always keep 30% on Claude 20x"], view.RuleTitles);

            view.Field.Focus();
            view.Field.Text = "keep 20%";
            stage.Press(Key.Enter);
            Click(view.EveryButton!);
            stage.Layout();
            Assert.Equal(["Always keep 30% on Claude 20x", "Always keep 20%"], view.RuleTitles);

            // Escape on the question drops it and puts the owner back in the field.
            view.Field.Focus();
            view.Field.Text = "keep 10%";
            stage.Press(Key.Enter);
            Assert.True(view.ShowsQuestion);
            view.EveryButton!.Focus();
            stage.Press(Key.Escape);
            Assert.False(view.ShowsQuestion);
            Assert.True(view.Field.IsFocused);
            Assert.Equal(2, settings.Rules.Count);
        });
    }

    [Fact]
    public void Removing_a_rule_and_flipping_the_switches_change_the_settings_and_a_newer_version_s_file_is_shown_not_changed()
    {
        headless.OnUiThread(() =>
        {
            var (model, settings, _) = Setup(Bar("Claude 20x", 10, null));
            Assert.Null(settings.Add(new ReserveRule("", ReserveScope.Named("Claude 20x"), ReserveLifetime.Standing) { KeepPercent = 30 }));
            var view = new ReservesView(model);
            using var stage = new ReserveViewStage(view);

            Click(view.RemoveButtons.Single());
            stage.Layout();
            Assert.Empty(settings.Rules);
            Assert.True(view.ShowsEmptyRules);

            Assert.Equal(ReservesView.HoldTitle, AutomationProperties.GetName(FirstNamed(view.HoldSwitch)));
            Assert.Equal(ReservesView.AskTitle, AutomationProperties.GetName(FirstNamed(view.AskSwitch)));
            Assert.Contains(ReservesView.HoldDetail, Texts(view));
            model.SetHoldReserves(true);
            model.SetAlwaysAskBeforeStarting(true);
            Assert.True(settings.HoldReserves);
            Assert.True(settings.AlwaysAskBeforeStarting);
            Assert.True(model.HoldReserves && model.AlwaysAskBeforeStarting);
        });

        headless.OnUiThread(() =>
        {
            var newer = new ReserveSettings();
            newer.Load(new System.Text.Json.Nodes.JsonObject { ["version"] = ReserveSettings.FormatVersion + 1 });
            var model = new ReservesModel(newer, () => []);
            var view = new ReservesView(model);
            using var stage = new ReserveViewStage(view);
            Assert.True(view.ShowsNewerVersion);
            Assert.False(view.Field.IsEnabled);
            Assert.False(view.AddButton!.IsEnabled);
            Assert.Equal(ReservesView.NewerVersion, Texts(view).Single(t => t == ReservesView.NewerVersion));
        });
    }

    [Fact]
    public void Under_900_px_the_two_panels_stack_and_every_line_wraps_inside_the_width()
    {
        headless.OnUiThread(() =>
        {
            var (model, settings, _) = Setup(Bar("A second work account with a name that goes on and on and on", 60, 70, outside: 8));
            Assert.Null(settings.Add(new ReserveRule("", ReserveScope.Every, ReserveLifetime.Standing) { KeepPercent = 30 }));
            var view = new ReservesView(model);
            using var stage = new ReserveViewStage(view, 420, 900);
            Assert.True(view.IsStacked);
            Assert.True(view.Bounds.Width <= 420.01);
            view.Measure(new Size(420, 2_000));
            Assert.True(view.DesiredSize.Width <= 420.01, $"the view asked for {view.DesiredSize.Width}");

            using var wide = new ReserveViewStage(new ReservesView(model), 1200, 800);
            Assert.False(((ReservesView)wide.Window.Content!).IsStacked);
        });
    }

    private static Control FirstNamed(Control root)
    {
        Control found = root;
        while (found is Decorator { Child: { } next }) found = next;
        return found;
    }
}
