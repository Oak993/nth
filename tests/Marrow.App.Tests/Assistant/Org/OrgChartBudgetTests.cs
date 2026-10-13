using Avalonia.Controls;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port.Armilla;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

// Dextra AS-17 (DESIGN.md rev 5, 2.5 "Motion budget": at most 8 moving marks per view, an Astur's mark never counts, a list that does not fit goes still as a whole; ED-NEW-dextra-as17-8): the chart in the
// window's budget. Dextra's live mark is the one mark that moves; the Asturs' marks cost nothing; a chart the budget stills shows every mark in its still pose; a hidden chart costs nothing.
[Collection("Headless")]
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-8")]
public sealed class OrgChartBudgetTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static OrgChart Make(params AssistantProfile[] asturs)
    {
        var chart = new OrgChart();
        chart.Show(TeamSnapshot.Build(Team(asturs), [Task(asturs[0].Id, "Voiceover", MissionState.Working)], []));
        return chart;
    }

    [Fact]
    public void The_chart_costs_the_budget_one_moving_mark_Dextras_and_never_the_Asturs()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var chart = Make(ChannelA, ChannelB, Store);
            using var stage = new HomeStage(chart, 900, 700);
            var scope = MotionScope.For(stage.Window);

            var entry = Assert.Single(scope.Lists, l => l.Rank == ArmillaListRank.Board);
            Assert.True(chart.Root!.DextraMark!.Moves);                                              // Dextra at 64 px, working: its mark moves
            Assert.True(chart.Moves);
            Assert.All(chart.Kids, k => Assert.False(k.AsturMark!.Moves));
            Assert.False(entry.IsCalm);
        });
    }

    [Fact]
    public void A_chart_the_budget_stills_shows_every_mark_in_its_still_pose()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var chart = Make(ChannelA, ChannelB);
            using var stage = new HomeStage(new Border(), 900, 700);
            var scope = MotionScope.For(stage.Window);
            scope.Join(ArmillaListRank.Board, () => ArmillaMotionBudget.PerView, () => true, _ => { });   // the Board's first claim takes the whole budget

            stage.Window.Content = chart;
            stage.Layout();
            scope.Recount();

            Assert.True(chart.Calm);
            Assert.True(chart.Root!.DextraMark!.Calm);
            Assert.All(chart.Kids, k => Assert.True(k.AsturMark!.Calm));
            Assert.True(chart.Root.DextraMark.SentLook!.Reduce);
        });
    }

    [Fact]
    public void A_chart_that_is_not_shown_costs_nothing_and_a_new_Astur_gets_the_charts_calm()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var chart = Make(ChannelA);
            using var stage = new HomeStage(new Border(), 900, 700);
            var scope = MotionScope.For(stage.Window);
            scope.Join(ArmillaListRank.Board, () => ArmillaMotionBudget.PerView, () => true, _ => { });
            stage.Window.Content = chart;
            stage.Layout();
            scope.Recount();
            Assert.True(chart.Calm);

            chart.Show(TeamSnapshot.Build(Team(ChannelA, Store), [], []));
            stage.Layout();
            Assert.All(chart.Kids, k => Assert.True(k.AsturMark!.Calm));                             // the Astur that came in is calm too

            chart.IsVisible = false;
            scope.Recount();
            Assert.False(scope.Lists[^1].Shown());
        });
    }
}
