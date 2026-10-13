using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-15 (DESIGN.md rev 5, 13.2; the approved canvas "Standing rules" and "A big task, by voice"; ED-NEW-dextra-as15-1, -2): the plan card
// shows the swarm gate's four facts (agents, time, estimated cost, who pays) and waits for Start when there are more than 8 agents or a cloud or
// API fan-out; the Reserves view lists the owner's caps with his reserve rules, in the canvas's own words, and takes a cap said in words. In a
// headless window.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-as15-1")]
[Trait("Feature", "AS-15")]
public sealed class CostViewsTests(HeadlessSession headless)
{
    private static PlanAccountLine Account(string name, double used, double p50, double p90, double? floor) =>
        new("key:" + name, name, ReserveWindowKind.FiveHour, "", used, p50, p90, floor, false);

    private static ReservePlan Swarm(string payer = "OpenRouter") => new([Account("Claude 20x", 40, 2, 3, 70)])
    {
        Agents = 12,
        Lanes = [new SwarmLane("claude:20x", "Claude 20x", SwarmPlace.Local, 8), new SwarmLane("openrouter:key", payer, SwarmPlace.Api, 4)],
        Duration = TimeSpan.FromMinutes(25),
        TokensP50 = 1_200_000,
        TokensP90 = 2_400_000,
        ApiDollars = 3.5m,
    };

    // ---- the plan card ----

    [Fact]
    public void A_plan_over_eight_agents_shows_the_agents_the_time_the_cost_and_who_pays_and_waits_for_Start()
    {
        headless.OnUiThread(() =>
        {
            var started = 0;
            var card = new PlanCard(() => started++, () => { });
            card.ShowPlan(Swarm());
            using var stage = new ReserveViewStage(card);

            Assert.Equal([("Agents", "12 agents: 8 on this PC, 4 on cloud or API accounts"), ("Time", "about 25 min"),
                ("Estimated cost", "about 1.2M tokens (up to 2.4M) · about $3.5 of API spend"), ("Paid by", "Claude 20x and OpenRouter")], card.SwarmRows);
            var texts = Texts(card);
            foreach (var (label, value) in card.SwarmRows)
            {
                Assert.Contains(label, texts);
                Assert.Contains(value, texts);
            }
            Assert.Equal("Start needs you: 12 agents planned; 4 cloud or API agents at once; about $3.5 of API spend.", card.Note);
            Assert.NotNull(card.StartButton);
            Click(card.StartButton!);
            Assert.Equal(1, started);
            Assert.Contains(Nodes<Grid>(card), grid => AutomationProperties.GetName(grid) == PlanCard.SwarmName);
        });
    }

    [Fact]
    public void A_small_plan_shows_none_of_it_and_a_started_or_finished_plan_takes_it_away()
    {
        headless.OnUiThread(() =>
        {
            var card = new PlanCard(() => { }, () => { });
            card.ShowPlan(new ReservePlan([Account("Claude 20x", 40, 4, 6, 70)]) { Agents = 8, Lanes = [new SwarmLane("claude:20x", "Claude 20x", SwarmPlace.Local, 8)] });
            using var stage = new ReserveViewStage(card);
            Assert.Empty(card.SwarmRows);
            Assert.DoesNotContain(Nodes<Grid>(card), grid => AutomationProperties.GetName(grid) == PlanCard.SwarmName);

            card.ShowPlan(Swarm());
            stage.Layout();
            Assert.Equal(4, card.SwarmRows.Count);
            card.ShowRunning("Running · tracking live");
            stage.Layout();
            Assert.Empty(card.SwarmRows);
            Assert.DoesNotContain("Paid by", Texts(card));
        });
    }

    [Fact]
    public void A_fan_out_of_two_api_agents_is_shown_the_same_way_though_there_are_only_two()
    {
        headless.OnUiThread(() =>
        {
            var card = new PlanCard(() => { }, () => { });
            card.ShowPlan(new ReservePlan([]) { Agents = 2, Lanes = [new SwarmLane("deepseek:key", "DeepSeek", SwarmPlace.Api, 2)] });
            using var stage = new ReserveViewStage(card);
            Assert.Equal("Start needs you: 2 cloud or API agents at once.", card.Note);
            Assert.Equal("2 agents: 2 on cloud or API accounts", card.SwarmRows[0].Value);
            Assert.Equal("not estimated", card.SwarmRows[1].Value);
            Assert.Equal("not estimated", card.SwarmRows[2].Value);
            Assert.Equal("DeepSeek", card.SwarmRows[3].Value);
        });
    }

    [Fact]
    public void Long_names_wrap_inside_a_narrow_card_and_a_right_to_left_window_lays_the_facts_out_all_the_same()
    {
        headless.OnUiThread(() =>
        {
            var payer = "OpenRouter with the very long name the owner gave to the key he keeps for the big jobs";
            var plan = Swarm(payer);
            var card = new PlanCard(() => { }, () => { });
            card.ShowPlan(plan);
            card.Measure(new Size(320, double.PositiveInfinity));
            Assert.True(card.DesiredSize.Width <= 320.01, $"the card asked for {card.DesiredSize.Width}");

            foreach (var flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            {
                var shown = new PlanCard(() => { }, () => { });
                shown.ShowPlan(plan);
                using var stage = new ReserveViewStage(shown, 320, 700, flow);
                Assert.True(shown.Bounds.Width <= 320.01);
                var grid = Nodes<Grid>(shown).Single(g => AutomationProperties.GetName(g) == PlanCard.SwarmName);
                Assert.True(grid.Bounds.Width <= 320.01, $"the facts are {grid.Bounds.Width} wide");
                var value = Nodes<TextBlock>(grid).Single(t => (t.Text ?? "").StartsWith("Claude 20x and OpenRouter with", StringComparison.Ordinal));
                Assert.True(value.Bounds.Height > value.FontSize * 1.5, "the long name wrapped onto a second line");
                var button = shown.StartButton!.TranslatePoint(new Point(0, 0), stage.Window)!.Value;
                Assert.InRange(button.X, 0, 320);
            }
        });
    }

    // ---- the Reserves view ----

    private static ReserveAccountBar Bar(string key, string name, double used) =>
        new(key, name, [new ReserveWindowBar("five", ReserveWindowKind.FiveHour, "5-hour limit", used, null, null, false, null)], 0, false);

    private static readonly CapAssistant[] Assistants = [new("a", "Channel A"), new("b", "Channel B")];

    private static (ReservesModel Model, ReserveSettings Settings) Setup(params ReserveAccountBar[] bars)
    {
        var settings = new ReserveSettings();
        return (new ReservesModel(settings, () => bars, null, () => Assistants), settings);
    }

    [Fact]
    public void The_Reserves_view_lists_the_caps_with_the_reserve_rules_in_the_canvas_s_words()
    {
        headless.OnUiThread(() =>
        {
            var (model, settings) = Setup(Bar("claude:20x", "Claude 20x", 10));
            Assert.Null(settings.Add(new ReserveRule("", ReserveScope.Named("Claude 20x"), ReserveLifetime.Standing) { KeepPercent = 30 }));
            Assert.Null(settings.AddCap(new CostCap("", CostCapScope.AssistantDay) { Percent = 15, AssistantId = "a" }));
            Assert.Null(settings.AddCap(new CostCap("", CostCapScope.ApiDay) { Dollars = 20m }));
            var view = new ReservesView(model);
            using var stage = new ReserveViewStage(view, 1100, 800);

            Assert.Equal(["Always keep 30% on Claude 20x", "Channel A: at most 15% of any account a day", "At most $20 a day on API keys"], view.RuleTitles);
            var texts = Texts(view);
            Assert.Contains("Standing · Channel A only", texts);
            Assert.Contains("Standing · API keys, dollars", texts);
            Assert.Contains(ReservesView.CapsHint, texts);
            Assert.False(view.ShowsEmptyRules);
            Assert.Equal(["Remove the rule: Always keep 30% on Claude 20x", "Remove the cap: Channel A: at most 15% of any account a day", "Remove the cap: At most $20 a day on API keys"],
                view.RemoveButtons.Select(b => AutomationProperties.GetName(b)));

            Click(view.RemoveButtons[1]);
            stage.Layout();
            Assert.Equal(["Always keep 30% on Claude 20x", "At most $20 a day on API keys"], view.RuleTitles);
            Assert.Single(settings.Caps);
        });
    }

    [Fact]
    public void Saying_a_cap_adds_it_on_Enter_and_a_cap_that_cannot_be_read_says_why_without_touching_the_reserve_rules()
    {
        headless.OnUiThread(() =>
        {
            var (model, settings) = Setup(Bar("claude:20x", "Claude 20x", 10), Bar("openrouter:key", "OpenRouter", 0));
            var view = new ReservesView(model);
            using var stage = new ReserveViewStage(view);
            Assert.True(view.ShowsEmptyRules);

            view.Field.Focus();
            view.Field.Text = "Channel A: at most 15% of any account a day";
            stage.Press(Key.Enter);
            Assert.Equal(["Channel A: at most 15% of any account a day"], view.RuleTitles);
            Assert.Equal("", view.Field.Text ?? "");
            Assert.False(view.ShowsEmptyRules);
            Assert.Equal("a", settings.Caps.Single().AssistantId);
            Assert.Empty(settings.Rules);

            view.Field.Focus();
            view.Field.Text = "no more than $5 a day on the OpenRouter key";
            stage.Press(Key.Enter);
            Assert.Equal(["OpenRouter"], settings.Caps[1].AccountNames);
            Assert.Equal("At most $5 a day on the OpenRouter key", view.RuleTitles[1]);

            view.Field.Focus();
            view.Field.Text = "at most 15% of any account";
            stage.Press(Key.Enter);
            Assert.Equal("Say when the cap applies, for example: at most 15% of any account a day.", view.ProblemText);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Nodes<LiveStatusText>(view).Single(t => t.Text == view.ProblemText)));
            Assert.Equal(2, settings.Caps.Count);
            stage.Press(Key.Escape);
            Assert.Equal("", view.ProblemText);

            view.Field.Focus();
            view.Field.Text = "always keep 25% on claude 20x";                          // a reserve is still a reserve
            stage.Press(Key.Enter);
            Assert.Single(settings.Rules);
            Assert.Equal(2, settings.Caps.Count);
        });
    }

    [Fact]
    public void A_newer_version_s_file_shows_its_caps_as_nothing_and_changes_none()
    {
        headless.OnUiThread(() =>
        {
            var newer = new ReserveSettings();
            newer.Load(new System.Text.Json.Nodes.JsonObject { ["version"] = ReserveSettings.FormatVersion + 1 });
            var model = new ReservesModel(newer, () => [], null, () => Assistants);
            var view = new ReservesView(model);
            using var stage = new ReserveViewStage(view);
            view.Field.Text = "at most 15% of any account a day";
            Assert.False(view.Field.IsEnabled);
            model.Draft = "at most 15% of any account a day";
            model.Submit();
            Assert.Equal("These settings were saved by a newer version; this one leaves them as they are.", model.Problem);
            Assert.Empty(newer.Caps);
        });
    }

    [Fact]
    public void Long_cap_names_wrap_inside_a_narrow_view()
    {
        headless.OnUiThread(() =>
        {
            var settings = new ReserveSettings();
            var assistants = new CapAssistant[] { new("a", "Channel with a name that goes on and on and on and on and on and on") };
            var model = new ReservesModel(settings, () => [], null, () => assistants);
            Assert.Null(settings.AddCap(new CostCap("", CostCapScope.AssistantDay) { Percent = 15, AssistantId = "a" }));
            var view = new ReservesView(model);
            using var stage = new ReserveViewStage(view, 420, 900);
            view.Measure(new Size(420, 2_000));
            Assert.True(view.DesiredSize.Width <= 420.01, $"the view asked for {view.DesiredSize.Width}");
            Assert.StartsWith("Channel with a name", view.RuleTitles.Single(), StringComparison.Ordinal);
        });
    }
}
