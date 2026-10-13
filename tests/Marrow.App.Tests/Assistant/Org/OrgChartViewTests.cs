using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port.Armilla;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

// Dextra AS-17 (DESIGN.md rev 5, 9.4 "Layout", "Each node shows...", "Narrow widths", 2.5; the canvas's org chart; ED-NEW-dextra-as17-1, -4): the org chart in a headless window. Dextra's node at
// the top with Armilla at 64 px, the Asturs beneath it with their own marks at 30 px in their tint, the words each node shows, the chart at wide and narrow widths, and the selection.
[Collection("Headless")]
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-1")]
public sealed class OrgChartViewTests(HeadlessSession headless)
{
    private static TeamSnapshot Snap(IReadOnlyList<AssistantProfile> team, IReadOnlyList<(string AssistantId, MissionJob Job)>? jobs = null, IReadOnlyList<DecisionItem>? cards = null, TeamUsage? usage = null) =>
        TeamSnapshot.Build(team, jobs ?? [], cards ?? [], usage);

    private static OrgChart Chart(TeamSnapshot snap, Action<Guid>? open = null)
    {
        var chart = new OrgChart(open);
        chart.Show(snap);
        return chart;
    }

    [Fact]
    public void Dextras_node_is_at_the_top_with_Armilla_at_64_px_and_a_node_for_each_Astur()
    {
        headless.OnUiThread(() =>
        {
            var chart = Chart(Snap(Team(ChannelA, ChannelB, Store), [Task("b", "Shorts", MissionState.Working)]));
            using var stage = new HomeStage(chart, 900, 700);

            var mark = chart.Root!.DextraMark!;
            Assert.Equal(64, mark.MarkSize);
            Assert.Equal(ArmillaKind.Live, mark.Kind);
            Assert.Equal([ArmillaShape.Squircle, ArmillaShape.Hexagon, ArmillaShape.Diamond], mark.Nodes.Select(n => n.Shape));
            Assert.Equal([false, true, false], mark.Nodes.Select(n => n.Busy));                    // a busy Astur's node brightens and shows its stem
            Assert.Equal(ArmillaState.Working, mark.State);
            Assert.Equal(new Size(64, 64), mark.Bounds.Size);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(mark));
            Assert.Equal("Dextra", chart.Root.NameText.Text);
            Assert.True(chart.Root.Bounds.Y < chart.Kids[0].Bounds.Y);
            Assert.Equal(450, chart.Root.Bounds.Center.X, 1);                                      // centred over the chart
        });
    }

    [Fact]
    public void Each_Astur_has_its_own_node_mark_at_30_px_in_its_shape_and_state()
    {
        headless.OnUiThread(() =>
        {
            var chart = Chart(Snap(Team(ChannelA, ChannelB, Store), [Task("a", "Voiceover", MissionState.Working), Task("s", "Send", MissionState.NeedsYou)], [Card("s", "Send the update")]));
            using var stage = new HomeStage(chart, 900, 700);

            var marks = chart.Kids.Select(k => k.AsturMark!).ToList();
            Assert.All(marks, m =>
            {
                Assert.Equal(30, m.MarkSize);
                Assert.Equal(new Size(30, 30), m.Bounds.Size);
                Assert.False(m.Moves);
                Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(m));
            });
            Assert.Equal([ArmillaShape.Squircle, ArmillaShape.Hexagon, ArmillaShape.Diamond], marks.Select(m => m.Shape));
            Assert.Equal([AsturMarkState.Working, AsturMarkState.Idle, AsturMarkState.Waiting], marks.Select(m => m.State));
            Assert.Equal("Channel A: running the tests (12 of 40)", ToolTip.GetTip(marks[0]));    // the hover shows the current action; the node says it in words too
            Assert.Equal("Channel B: Nothing at work", ToolTip.GetTip(marks[1]));
        });
    }

    [Fact]
    public void A_wide_chart_puts_the_nodes_where_the_layout_says_with_the_canvas_widths_and_gaps()
    {
        headless.OnUiThread(() =>
        {
            var chart = Chart(Snap(Team(ChannelA, ChannelB, Store)));
            using var stage = new HomeStage(chart, 900, 700);

            var plan = chart.Panel.Plan!;
            Assert.Equal(OrgLayoutMode.Chart, plan.Mode);
            Assert.False(chart.Panel.IsList);
            Assert.Equal(new Rect(plan.Root.X, plan.Root.Y, 220, plan.Root.Height), chart.Root!.Bounds);
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(new Rect(plan.Kids[i].X, plan.Kids[i].Y, 176, plan.Kids[i].Height), chart.Kids[i].Bounds);
                Assert.Equal(plan.Kids[0].Y, chart.Kids[i].Bounds.Y);                              // one row
            }
            Assert.Equal(12, chart.Kids[1].Bounds.X - chart.Kids[0].Bounds.Right);                  // 12 px between nodes
            Assert.Equal(34, chart.Kids[0].Bounds.Y - chart.Root.Bounds.Bottom);                    // the 34 px lane of the connectors
            Assert.Equal(3, chart.Panel.Connectors.Figures);                                         // the stem to the middle node and the two arms
            Assert.Equal(plan.Height, chart.DesiredSize.Height, 1);                                  // it asks for exactly what the plan needs
        });
    }

    [Fact]
    public void Under_640_px_the_chart_is_an_indented_list_with_the_mark_at_the_left_of_each_node()
    {
        headless.OnUiThread(() =>
        {
            var chart = Chart(Snap(Team(ChannelA, ChannelB)));
            using var stage = new HomeStage(chart, 375, 700);

            Assert.True(chart.Panel.IsList);
            Assert.Equal(375, chart.Root!.Bounds.Width, 1);
            Assert.All(chart.Kids, k => Assert.Equal(345, k.Bounds.Width, 1));
            Assert.All(chart.Kids, k => Assert.Equal(30, k.Bounds.X, 1));                           // indented by the trunk's 18 and 12
            Assert.True(chart.Kids[0].Bounds.Bottom < chart.Kids[1].Bounds.Y);                      // stacked
            Assert.Equal(8, chart.Kids[1].Bounds.Y - chart.Kids[0].Bounds.Bottom, 1);
            Assert.True(chart.Root.Compact && chart.Kids.All(k => k.Compact));
            // the mark stands at the left, the words beside it
            Assert.True(chart.Kids[0].AsturMark!.TranslatePoint(default, chart.Kids[0])!.Value.X < chart.Kids[0].NameText.TranslatePoint(default, chart.Kids[0])!.Value.X);
            Assert.Equal(2, chart.Panel.Connectors.Figures);                                         // a tick for the first, the trunk ending in the last elbow

            stage.Resize(900, 700);
            Assert.False(chart.Panel.IsList);
            Assert.True(chart.Kids.All(k => !k.Compact));
        });
    }

    [Fact]
    public void Six_Asturs_wrap_into_two_rows_at_640_px_and_nothing_overlaps()
    {
        headless.OnUiThread(() =>
        {
            var team = Team(ChannelA, ChannelB, Store, Astur("p", "Desk", AsturShape.Star), Astur("q", "Ledger", AsturShape.Clover), Astur("r", "Inbox", AsturShape.RoundedTriangle));
            var chart = Chart(Snap(team, [Task("a", "Voiceover", MissionState.Working)]));
            using var stage = new HomeStage(chart, 640, 900);

            Assert.False(chart.Panel.IsList);
            Assert.Equal(2, chart.Kids.Select(k => k.Bounds.Y).Distinct().Count());
            Assert.Equal([3, 3], chart.Kids.GroupBy(k => k.Bounds.Y).Select(g => g.Count()));
            var boxes = chart.All.Select(n => n.Bounds).ToList();
            for (var i = 0; i < boxes.Count; i++)
            for (var j = i + 1; j < boxes.Count; j++)
                Assert.False(boxes[i].Intersects(boxes[j]) && boxes[i].Intersect(boxes[j]).Width > .5 && boxes[i].Intersect(boxes[j]).Height > .5, $"nodes {i} and {j} overlap");
            Assert.All(boxes, b => Assert.InRange(b.Right, 0, 640.1));
        });
    }

    [Fact]
    public void A_node_shows_its_name_the_work_line_a_status_chip_a_meter_and_chips_for_its_live_tasks()
    {
        headless.OnUiThread(() =>
        {
            var older = Task("a", "Next video: Why lenses fog up", MissionState.Working, 30);
            var newer = Task("a", "Weekly stats", MissionState.Paused, 3, HomeSamples.Head("Reader", HeadState.Paused));
            var usage = Usage(Line(ChannelA, 5.7, .38));
            var opened = new List<Guid>();
            var chart = Chart(Snap(Team(ChannelA), [older, newer], null, usage), opened.Add);
            using var stage = new HomeStage(chart, 375, 700);                                         // the list has room for both chips

            var node = chart.Kids[0];
            Assert.Equal("Channel A", node.NameText.Text);
            Assert.Equal("running the tests (12 of 40)", node.WorkText.Text);
            Assert.Equal("Working", node.StatusText.Text);
            Assert.True(node.Meter.IsVisible);
            Assert.Equal(.38, node.Meter.Value, 3);
            Assert.Equal(3, node.Meter.Bounds.Height);
            Assert.Equal(2, node.ChipButtons.Count);
            Assert.Null(node.MoreText);
            Assert.Equal(TeamChipRow.Line, node.ChipRow.Bounds.Height);
            Assert.Equal(["Hydra team: Weekly stats, Paused", "Next video: Why lenses fog up, Working"], node.ChipButtons.Select(b => AutomationProperties.GetName(b)));
            Click(node.ChipButtons[1]);
            Assert.Equal([older.Job.LeadThread!.Value], opened);                                     // a chip opens that task's chat
        });
    }

    [Fact]
    public void In_a_wide_node_the_chips_that_fit_are_drawn_and_the_rest_are_counted_in_one_chip()
    {
        headless.OnUiThread(() =>
        {
            var jobs = Enumerable.Range(0, 5).Select(i => Task("a", $"Task number {i} of the day", MissionState.Working, 10 + i)).ToList();
            var chart = Chart(Snap(Team(ChannelA), jobs));
            using var stage = new HomeStage(chart, 900, 700);

            var node = chart.Kids[0];
            var shown = node.ChipButtons.Count;
            Assert.True(shown is >= 1 and <= 2, $"{shown} chips shown, {node.ChipRow.Hidden} counted, the line is {node.ChipRow.Bounds.Width} px");   // 152 px: the newest task and a count
            Assert.Equal(5 - shown, node.ChipRow.Hidden);
            Assert.Equal("+" + (5 - shown), node.MoreText);
            Assert.Equal(TeamChipRow.Line, node.ChipRow.Bounds.Height);
            Assert.True(node.ChipRow.Bounds.Width <= 152.5);
            Assert.Contains("+" + (5 - shown), Texts(node));
        });
    }

    [Fact]
    public void A_task_that_starts_or_ends_never_changes_a_nodes_size()
    {
        headless.OnUiThread(() =>
        {
            var chart = Chart(Snap(Team(ChannelA, ChannelB)));
            using var stage = new HomeStage(chart, 900, 700);
            var height = chart.Kids[0].Bounds.Height;

            foreach (var count in new[] { 1, 3, 6, 0 })
            {
                var jobs = Enumerable.Range(0, count).Select(i => Task("a", $"Task {i}", MissionState.Working, 5 + i)).ToList();
                chart.Show(Snap(Team(ChannelA, ChannelB), jobs));
                stage.Layout();
                Assert.Equal(height, chart.Kids[0].Bounds.Height, 1);
                Assert.Equal(height, chart.Kids[1].Bounds.Height, 1);
            }
        });
    }

    [Fact]
    public void A_node_has_the_same_size_in_every_state_so_a_change_of_state_never_moves_the_chart()
    {
        headless.OnUiThread(() =>
        {
            var heights = new List<double>();
            foreach (var state in new[] { MissionState.Working, MissionState.NeedsYou, MissionState.Paused, MissionState.Stopped, MissionState.Blocked, MissionState.Done })
            {
                var jobs = Enumerable.Range(0, state == MissionState.Working ? 4 : 1).Select(i => Task("a", "A task with a rather long title that runs on and on and on and on and on", state, 5 + i)).ToList();
                var chart = Chart(Snap(Team(ChannelA, ChannelB), jobs, state == MissionState.NeedsYou ? [Card("a", "Send it")] : null, Usage(Line(ChannelA, 4, .5))));
                using var stage = new HomeStage(chart, 900, 700);
                heights.Add(chart.Kids[0].Bounds.Height);
                heights.Add(chart.Root!.Bounds.Height);
                Assert.Equal(chart.Kids[0].Bounds.Height, chart.Kids[1].Bounds.Height);
            }
            Assert.Equal(2, heights.Distinct().Count());                                           // one height for Dextra's node, one for an Astur's
        });
    }

    [Fact]
    public void A_very_long_name_and_work_line_stay_inside_the_node_in_two_lines_at_most()
    {
        headless.OnUiThread(() =>
        {
            var long1 = Astur("a", new string('W', 90), AsturShape.Clover);
            var jobs = new[] { Task("a", new string('x', 500) + " " + new string('y', 500), MissionState.Working) };
            var chart = Chart(Snap(Team(long1, ChannelB), jobs));
            using var stage = new HomeStage(chart, 900, 700);

            var node = chart.Kids[0];
            Assert.True(node.NameText.Bounds.Width <= node.Bounds.Width - 24 + .5);
            Assert.True(node.WorkText.Bounds.Height <= 2 * 15.5 + 1);
            Assert.Equal(chart.Kids[1].Bounds.Height, node.Bounds.Height);
            Assert.True(node.Bounds.Right <= chart.Bounds.Width + .5);
        });
    }

    [Fact]
    public void Dextra_is_selected_first_a_press_selects_another_and_the_ring_follows()
    {
        headless.OnUiThread(() =>
        {
            using var still = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
            var chart = Chart(Snap(Team(ChannelA, Store)));
            using var stage = new HomeStage(chart, 900, 700);
            var seen = new List<string>();
            chart.SelectionChanged += m => seen.Add(m.Name);

            Assert.Equal("root", chart.SelectedId);
            Assert.True(chart.Root!.IsSelected);
            Assert.Equal(1, chart.Root.RingNow);
            Click(chart.Kids[1].Face);
            Assert.Equal("s", chart.SelectedId);
            Assert.Equal(["Store"], seen);
            Assert.True(chart.Kids[1].IsSelected);
            Assert.False(chart.Root.IsSelected);
            Assert.Equal(0, chart.Root.RingNow);                                                   // with Reduce motion the ring is simply there or not
            Assert.Equal(1, chart.Kids[1].RingNow);
            Assert.False(chart.Kids[1].RingEases);
            chart.Select("nobody");
            Assert.Equal("s", chart.SelectedId);
            chart.Select("s");
            Assert.Equal(["Store"], seen);                                                         // choosing what is chosen is not a change
        });
    }

    [Fact]
    public void Without_reduce_motion_the_ring_comes_in_and_goes_out_on_a_short_ease_and_the_card_lifts_on_one_under_the_pointer()
    {
        headless.OnUiThread(() =>
        {
            var chart = Chart(Snap(Team(ChannelA, Store)));
            using var stage = new HomeStage(chart, 900, 700);

            Click(chart.Kids[0].Face);
            Assert.True(chart.Kids[0].RingEases);
            Assert.True(chart.Root!.RingEases);
            Assert.True(chart.Kids[0].IsSelected);
            Assert.False(chart.Root.IsSelected);

            Assert.False(chart.Kids[1].LiftEases);
            stage.Window.MouseMove(stage.Box(chart.Kids[1].Face).Center);
            stage.Layout();
            Assert.True(chart.Kids[1].LiftEases);
        });
    }

    [Fact]
    public void With_reduce_motion_the_card_lifts_2_px_under_the_pointer_at_once_and_drops_when_it_leaves()
    {
        headless.OnUiThread(() =>
        {
            using var still = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
            var chart = Chart(Snap(Team(ChannelA, Store)));
            using var stage = new HomeStage(chart, 900, 700);

            stage.Window.MouseMove(stage.Box(chart.Kids[1].Face).Center);
            stage.Layout();
            Assert.Equal(-2, chart.Kids[1].LiftNow);
            Assert.False(chart.Kids[1].LiftEases);
            Assert.Equal(0, chart.Kids[0].LiftNow);
            stage.Window.MouseMove(new Point(2, 690));
            stage.Layout();
            Assert.Equal(0, chart.Kids[1].LiftNow);
        });
    }

    [Fact]
    public void Each_node_is_a_selectable_list_item_named_by_its_state_and_work_with_the_mark_out_of_the_tree()
    {
        headless.OnUiThread(() =>
        {
            var chart = Chart(Snap(Team(ChannelA, Store), [Task("s", "Weekly update", MissionState.NeedsYou)], [Card("s", "Send the update")]));
            using var stage = new HomeStage(chart, 900, 700);

            var store = chart.Kids[1];
            Assert.Equal("Store, Needs you, 1 decision open. Waiting on you: Send the update", AutomationProperties.GetName(store.Face));
            Assert.Equal(AutomationControlType.List, ControlAutomationPeer.CreatePeerForElement(chart).GetAutomationControlType());
            Assert.Equal("Team", AutomationProperties.GetName(chart));
            var peer = ControlAutomationPeer.CreatePeerForElement(store.Face);
            Assert.Equal(AutomationControlType.ListItem, peer.GetAutomationControlType());
            Assert.False(((Avalonia.Automation.Provider.ISelectionItemProvider)peer).IsSelected);
            ((Avalonia.Automation.Provider.ISelectionItemProvider)peer).Select();
            Assert.True(((Avalonia.Automation.Provider.ISelectionItemProvider)peer).IsSelected);
            Assert.Equal("s", chart.SelectedId);
            Assert.True(store.Face.Focusable);
            Assert.False(store.Mark.Focusable);
        });
    }

    [Fact]
    public void The_arrow_keys_home_and_end_move_the_focus_between_nodes_and_enter_selects()
    {
        headless.OnUiThread(() =>
        {
            var chart = Chart(Snap(Team(ChannelA, ChannelB, Store)));
            using var stage = new HomeStage(chart, 900, 700);

            chart.Root!.Focus();
            stage.Layout();
            Assert.Same(chart.Root.Face, stage.Focused);
            stage.Press(Key.Right);
            Assert.Same(chart.Kids[0].Face, stage.Focused);
            stage.Press(Key.Down);
            Assert.Same(chart.Kids[1].Face, stage.Focused);
            stage.Press(Key.End);
            Assert.Same(chart.Kids[2].Face, stage.Focused);
            stage.Press(Key.Right);
            Assert.Same(chart.Kids[2].Face, stage.Focused);                                       // no wrap past the end
            stage.Press(Key.Left);
            Assert.Same(chart.Kids[1].Face, stage.Focused);
            stage.Press(Key.Home);
            Assert.Same(chart.Root.Face, stage.Focused);
            stage.Press(Key.Down);
            stage.Press(Key.Enter);
            Assert.Equal("a", chart.SelectedId);
            stage.Press(Key.Right);
            stage.Press(Key.Space);
            Assert.Equal("b", chart.SelectedId);
        });
    }

    [Fact]
    public void Showing_the_same_team_again_rebuilds_nothing_and_a_change_in_one_node_touches_only_that_node()
    {
        headless.OnUiThread(() =>
        {
            var jobs = new[] { Task("a", "Voiceover", MissionState.Working) };
            var chart = Chart(Snap(Team(ChannelA, Store), jobs));
            using var stage = new HomeStage(chart, 900, 700);
            var chips = chart.Kids[0].ChipButtons[0];
            var storeChips = chart.Kids[1].ChipButtons;

            chart.Show(Snap(Team(ChannelA, Store), jobs));
            Assert.Same(chips, chart.Kids[0].ChipButtons[0]);                                       // nothing was rebuilt

            chart.Show(Snap(Team(ChannelA, Store), [.. jobs, Task("s", "Invoices", MissionState.Working)]));
            Assert.Same(chips, chart.Kids[0].ChipButtons[0]);
            Assert.NotSame(storeChips, chart.Kids[1].ChipButtons);
            Assert.Equal("Working", chart.Kids[1].StatusText.Text);
        });
    }

    [Fact]
    public void The_status_chip_says_needs_you_with_an_ink_fill_and_the_rest_in_a_tint_so_it_never_rests_on_colour_alone()
    {
        headless.OnUiThread(() =>
        {
            var chart = Chart(Snap(Team(ChannelA, Store), [Task("s", "Weekly update", MissionState.NeedsYou)], [Card("s", "Send")]));
            using var stage = new HomeStage(chart, 900, 700);

            Assert.Equal("Needs you", chart.Kids[1].StatusText.Text);
            Assert.Equal("Idle", chart.Kids[0].StatusText.Text);
            Assert.NotEqual(((SolidColorBrush)chart.Kids[0].StatusChip.Background!).Color, ((SolidColorBrush)chart.Kids[1].StatusChip.Background!).Color);
        });
    }
}
