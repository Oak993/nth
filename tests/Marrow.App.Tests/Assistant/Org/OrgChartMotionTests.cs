using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port.Armilla;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

// Dextra AS-17 (DESIGN.md rev 5, 9.4, 2.5 "Moves", the owner's bar: no fades between moves, nothing snaps, Reduce motion honoured; ED-NEW-dextra-as17-1): the chart when the team changes. A new
// Astur's node rises in; a retired one fades first and then the others move into its place by a transform alone; the connectors wait for the nodes to land and come back; a change of state moves
// nothing; and with Reduce motion the chart is simply the new chart.
[Collection("Headless")]
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-1")]
public sealed class OrgChartMotionTests(HeadlessSession headless)
{
    private static TeamSnapshot Snap(params AssistantProfile[] asturs) => TeamSnapshot.Build(Team(asturs), [], []);

    private static TeamSnapshot Working(params AssistantProfile[] asturs) => TeamSnapshot.Build(Team(asturs), [Task(asturs[0].Id, "Voiceover", MissionState.Working)], []);

    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static bool Glides(OrgNode node) => node.RenderTransform is TranslateTransform { Transitions.Count: 2 };

    [Fact]
    public void A_new_Astur_rises_into_place_and_the_ones_that_stay_glide_to_where_the_row_centres_them_by_a_transform_alone()
    {
        headless.OnUiThread(() =>
        {
            var chart = new OrgChart();
            chart.Show(Snap(ChannelA, ChannelB));
            using var stage = new HomeStage(chart, 900, 700);
            var before = chart.Kids.Select(k => k.Bounds.X).ToList();

            chart.Show(Snap(ChannelA, ChannelB, Store));
            stage.Layout();

            Assert.Equal(3, chart.Kids.Count);
            Assert.NotEqual(before[0], chart.Kids[0].Bounds.X);                                   // the row is centred again
            Assert.True(Glides(chart.Kids[0]) && Glides(chart.Kids[1]));                          // and the nodes get there on a transform, they were not re-laid-out in motion
            var fresh = chart.Kids[2];
            Assert.NotNull(fresh.Transitions);                                                    // the new node comes in on a fade and a rise
            Assert.True(fresh.RenderTransform is TranslateTransform { Transitions.Count: 1 });
            Assert.All(chart.Kids, k => Assert.Equal(chart.Kids[0].Bounds.Y, k.Bounds.Y));
        });
    }

    [Fact]
    public void The_connectors_leave_while_the_nodes_move_and_come_back_once_they_have_landed()
    {
        headless.OnUiThread(() =>
        {
            var chart = new OrgChart();
            chart.Show(Snap(ChannelA));
            using var stage = new HomeStage(chart, 900, 700);
            Assert.Equal(1, chart.Panel.Connectors.Opacity);

            chart.Show(Snap(ChannelA, ChannelB));
            stage.Layout();
            // OD-0076 and ED-NEW-dextra-fix-12: leave on a fade, rather than a hard cut.
            Assert.Equal(0, chart.Panel.Connectors.GetBaseValue(Visual.OpacityProperty).Value);
            var fade = Assert.IsType<Avalonia.Animation.DoubleTransition>(Assert.Single(chart.Panel.Connectors.Transitions!));
            Assert.Equal(Visual.OpacityProperty, fade.Property);
            Assert.Equal(TimeSpan.FromMilliseconds(260), fade.Duration);

            stage.Advance(OrgChart.Move + TimeSpan.FromMilliseconds(20));
            Assert.Same(fade, Assert.Single(chart.Panel.Connectors.Transitions!));                  // the same fade returns after landing
            Assert.Equal(1, chart.Panel.Connectors.GetBaseValue(Visual.OpacityProperty).Value);
            Assert.Equal(2, chart.Panel.Connectors.Plan!.Kids.Count);
        });
    }

    [Fact]
    public void With_reduce_motion_a_new_Astur_is_simply_there_and_so_are_the_lines()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var chart = new OrgChart();
            chart.Show(Snap(ChannelA, ChannelB));
            using var stage = new HomeStage(chart, 900, 700);

            chart.Show(Snap(ChannelA, ChannelB, Store));
            stage.Layout();

            Assert.Equal(3, chart.Kids.Count);
            Assert.All(chart.Kids, k =>
            {
                Assert.Equal(1, k.Opacity);
                Assert.Null(k.Transitions);
                Assert.False(Glides(k));
            });
            Assert.Equal(1, chart.Panel.Connectors.Opacity);
            Assert.Null(chart.Panel.Connectors.Transitions);
        });
    }

    [Fact]
    public void A_retired_Astur_fades_first_and_then_the_others_move_into_its_place()
    {
        headless.OnUiThread(() =>
        {
            var chart = new OrgChart();
            chart.Show(Snap(ChannelA, ChannelB, Store));
            using var stage = new HomeStage(chart, 900, 700);
            var leaving = chart.NodeOf("b")!;

            chart.Show(Snap(ChannelA, Store));
            stage.Layout();

            Assert.Equal(3, chart.Kids.Count);                                                     // it is still there, fading
            Assert.NotNull(leaving.Transitions);
            Assert.Equal(0, leaving.GetBaseValue(Visual.OpacityProperty).Value);                   // it is on its way to nothing
            Assert.False(leaving.IsHitTestVisible);
            Assert.False(Glides(chart.Kids[0]));                                                   // nothing moves yet

            stage.Advance(OrgChart.Leave + TimeSpan.FromMilliseconds(20));

            Assert.Equal(["a", "s"], chart.Kids.Select(k => k.Id));
            Assert.Null(chart.NodeOf("b"));
            Assert.True(Glides(chart.Kids[0]) && Glides(chart.Kids[1]));
        });
    }

    [Fact]
    public void A_change_that_comes_while_a_node_is_fading_finishes_the_removal_first_so_nothing_is_stuck()
    {
        headless.OnUiThread(() =>
        {
            var chart = new OrgChart();
            chart.Show(Snap(ChannelA, ChannelB, Store));
            using var stage = new HomeStage(chart, 900, 700);

            chart.Show(Snap(ChannelA, Store));
            chart.Show(Snap(ChannelA, Store, Astur("p", "Desk", AsturShape.Star)));
            stage.Layout();

            Assert.Equal(["a", "s", "p"], chart.Kids.Select(k => k.Id));
            Assert.Null(chart.NodeOf("b"));
            stage.Advance(OrgChart.Leave + OrgChart.Move);
            Assert.Equal(["a", "s", "p"], chart.Kids.Select(k => k.Id));
        });
    }

    [Fact]
    public void With_reduce_motion_a_retired_Astur_is_gone_at_once()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var chart = new OrgChart();
            chart.Show(Snap(ChannelA, ChannelB));
            using var stage = new HomeStage(chart, 900, 700);

            chart.Show(Snap(ChannelA));
            stage.Layout();

            Assert.Equal(["a"], chart.Kids.Select(k => k.Id));
        });
    }

    [Fact]
    public void When_the_selected_Astur_is_retired_Dextra_takes_the_selection_back_once_the_set_has_changed()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var chart = new OrgChart();
            chart.Show(Snap(ChannelA, ChannelB));
            using var stage = new HomeStage(chart, 900, 700);
            chart.Select("b");
            var seen = new List<string>();
            chart.SelectionChanged += m => seen.Add(m.Name);

            chart.Show(Snap(ChannelA));
            stage.Layout();

            Assert.Equal("root", chart.SelectedId);
            Assert.True(chart.Root!.IsSelected);
            Assert.Equal(["Dextra"], seen);
        });
    }

    [Fact]
    public void A_change_of_state_alone_moves_nothing_and_keeps_the_lines()
    {
        headless.OnUiThread(() =>
        {
            var chart = new OrgChart();
            chart.Show(Snap(ChannelA, ChannelB));
            using var stage = new HomeStage(chart, 900, 700);

            chart.Show(Working(ChannelA, ChannelB));
            stage.Layout();

            Assert.All(chart.Kids, k => Assert.False(Glides(k)));
            Assert.Equal(1, chart.Panel.Connectors.Opacity);
            Assert.Equal(AsturMarkState.Working, chart.Kids[0].AsturMark!.State);
        });
    }

    [Fact]
    public void Reordering_the_Asturs_moves_the_nodes_by_a_transform()
    {
        headless.OnUiThread(() =>
        {
            var chart = new OrgChart();
            chart.Show(Snap(ChannelA, ChannelB, Store));
            using var stage = new HomeStage(chart, 900, 700);

            chart.Show(Snap(Store, ChannelA, ChannelB));
            stage.Layout();

            Assert.Equal(["s", "a", "b"], chart.Kids.Select(k => k.Id));
            Assert.True(chart.Kids.All(Glides));
        });
    }
}
