using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port.Armilla;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

// Dextra AS-17 (DESIGN.md rev 5, 2.1 "Sidebar": "The Asturs are nested under it, one row each. A row shows the Astur's mark (its node shape and tint), the name, the current work line and a badge when that
// Astur needs the owner. The disclosure is remembered."; ED-NEW-dextra-as17-7): the sidebar's tree of Asturs.
[Collection("Headless")]
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-7")]
public sealed class AsturTreeTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static FakeTeamSource Source(IReadOnlyList<AssistantProfile> team, IReadOnlyList<(string AssistantId, MissionJob Job)>? jobs = null, IReadOnlyList<DecisionItem>? cards = null) =>
        new() { Current = TeamSnapshot.Build(team, jobs ?? [], cards ?? []) };

    [Fact]
    public void With_no_Astur_the_group_is_not_shown_and_takes_no_room()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var tree = new AsturTree(Source(Team()));
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 400);

            Assert.False(tree.IsVisible);
            Assert.Empty(tree.Rows);
            Assert.Equal(0, tree.Bounds.Height);
        });
    }

    [Fact]
    public void Each_Astur_has_a_row_with_its_own_mark_at_20_px_its_name_the_work_line_and_a_badge_for_the_cards_that_wait()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var jobs = new[] { Task("a", "Voiceover", MissionState.Working), Task("s", "Weekly update", MissionState.NeedsYou) };
            var tree = new AsturTree(Source(Team(ChannelA, ChannelB, Store), jobs, [Card("s", "Send the update"), Card("s", "Sign in")]));
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 400);

            Assert.True(tree.IsVisible);
            Assert.Equal("Asturs  3", tree.LabelText);
            Assert.Equal(["Channel A", "Channel B", "Store"], tree.Rows.Select(r => r.NameText));
            Assert.Equal(["running the tests (12 of 40)", "Nothing at work", "Waiting on you: Send the update"], tree.Rows.Select(r => r.LineText));
            Assert.All(tree.Rows, r =>
            {
                Assert.Equal(new Size(20, 20), r.Mark.Bounds.Size);
                Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(r.Mark));
                Assert.False(r.Mark.Moves);
                Assert.Equal(42, r.Button.Bounds.Height, 1);                                       // the face and its 1 px above and below (the row is taller by its Missions group)
            });
            Assert.Equal([ArmillaShape.Squircle, ArmillaShape.Hexagon, ArmillaShape.Diamond], tree.Rows.Select(r => r.Mark.Shape));
            Assert.Equal([AsturMarkState.Working, AsturMarkState.Idle, AsturMarkState.Waiting], tree.Rows.Select(r => r.Mark.State));
            Assert.Equal([false, false, true], tree.Rows.Select(r => r.Badge.IsVisible));
            Assert.Equal("2", tree.Rows[2].BadgeCount);
            Assert.Equal("Store, Needs you, 2 decisions open. Waiting on you: Send the update", AutomationProperties.GetName(tree.Rows[2].Button));
            Assert.Equal("2 decisions open", AutomationProperties.GetHelpText(tree.Rows[2].Button));
            Assert.Equal("No decisions open", AutomationProperties.GetHelpText(tree.Rows[0].Button));
        });
    }

    [Fact]
    public void A_state_change_never_changes_a_rows_size_or_place()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = Source(Team(ChannelA, ChannelB));
            var tree = new AsturTree(source);
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 400);
            var faces = tree.Rows.Select(r => stage.Box(r.Button)).ToList();

            // the first task gives Channel A its Missions group (ED-NEW-dextra-as17-10), which the rows below it follow; its face stays where and as big as it was
            source.Set(TeamSnapshot.Build(Team(ChannelA, ChannelB), [Task("a", new string('x', 200), MissionState.Working)], []));
            stage.Layout();
            Assert.Equal(faces[0], stage.Box(tree.Rows[0].Button));
            Assert.Equal(faces[1].Size, stage.Box(tree.Rows[1].Button).Size);
            var boxes = tree.Rows.Select(r => stage.Box(r)).ToList();

            // after that no change of state moves or resizes a row
            foreach (var state in new[] { MissionState.NeedsYou, MissionState.Paused, MissionState.Stopped, MissionState.Blocked, MissionState.Done, MissionState.Working })
            {
                source.Set(TeamSnapshot.Build(Team(ChannelA, ChannelB), [Task("a", new string('x', 200), state)], state == MissionState.NeedsYou ? [Card("a", "Send")] : []));
                stage.Layout();
                Assert.Equal(boxes, tree.Rows.Select(r => stage.Box(r)).ToList());
            }
        });
    }

    [Fact]
    public void The_rows_hang_on_one_tree_line_the_last_one_ends_it_and_a_row_added_or_taken_away_moves_it()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = Source(Team(ChannelA, ChannelB));
            var tree = new AsturTree(source);
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 400);

            Assert.Equal([false, true], tree.Rows.Select(r => r.IsLast));
            source.Set(TeamSnapshot.Build(Team(ChannelA, ChannelB, Store), [], []));
            stage.Layout();
            Assert.Equal([false, false, true], tree.Rows.Select(r => r.IsLast));
            source.Set(TeamSnapshot.Build(Team(ChannelA, Store), [], []));
            stage.Layout();
            Assert.Equal(["a", "s"], tree.Rows.Select(r => r.Id));
            Assert.Equal([false, true], tree.Rows.Select(r => r.IsLast));
        });
    }

    [Fact]
    public void The_disclosure_folds_the_group_and_the_choice_is_remembered_and_read_back()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var collapsed = false;
            var tree = new AsturTree(Source(Team(ChannelA, ChannelB)), new AsturTreeActions { Collapsed = () => collapsed, SetCollapsed = c => collapsed = c });
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 400);

            Assert.True(tree.IsOpen);
            Assert.True(tree.Toggle.IsExpanded);
            Assert.Equal("Hide Asturs, 2", AutomationProperties.GetName(tree.Toggle));
            Assert.True(tree.Rows[0].Bounds.Height > 0);
            Click(tree.Toggle);
            stage.Layout();
            Assert.False(tree.IsOpen);
            Assert.True(collapsed);                                                                 // remembered
            Assert.False(tree.Rows[0].IsEffectivelyVisible);                                            // the rows are folded away
            Assert.Equal("Show Asturs, 2", AutomationProperties.GetName(tree.Toggle));
            Click(tree.Toggle);
            Assert.False(collapsed);
            Assert.True(tree.IsOpen);

            var folded = new AsturTree(Source(Team(ChannelA)), new AsturTreeActions { Collapsed = () => true });
            using var stage2 = new HomeStage(new StackPanel { Width = 260, Children = { folded } }, 300, 400);
            Assert.False(folded.IsOpen);
        });
    }

    [Fact]
    public void A_press_opens_the_Astur_and_the_selected_row_takes_the_selected_look_and_weight()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var opened = new List<string>();
            var tree = new AsturTree(Source(Team(ChannelA, Store)), new AsturTreeActions { Open = opened.Add });
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 400);

            Click(tree.Rows[1].Button);
            Assert.Equal(["s"], opened);
            tree.SelectedId = "s";
            Assert.True(tree.Rows[1].IsSelected);
            Assert.False(tree.Rows[0].IsSelected);
            tree.SelectedId = null;
            Assert.False(tree.Rows[1].IsSelected);
        });
    }

    [Fact]
    public void The_menu_holds_Open_Pause_or_Resume_its_tasks_and_Show_in_Team_each_disabled_when_it_has_nothing_to_do()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var opened = new List<string>();
            var shown = new List<string>();
            var said = new List<string>();
            var source = Source(Team(ChannelA, ChannelB), [Task("a", "Voiceover", MissionState.Working), Task("b", "Stats", MissionState.Paused)]);
            var tree = new AsturTree(source, new AsturTreeActions { Open = opened.Add, ShowInTeam = shown.Add, Said = said.Add });
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 400);

            var working = tree.Rows[0].MenuItems();
            Assert.Equal(["Open", "Pause its tasks", "Show in Team"], working.Select(i => (string)i.Header!));
            Assert.All(working, i => Assert.True(i.IsEnabled));
            var paused = tree.Rows[1].MenuItems();
            Assert.Equal(["Open", "Resume its tasks", "Show in Team"], paused.Select(i => (string)i.Header!));

            working[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            working[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            working[2].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            paused[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(["a"], opened);
            Assert.Equal(["a"], shown);
            Assert.Equal(["Pause a", "Resume b"], source.Calls);
            Assert.Equal(["Pausing a.", "Resumed b."], said);

            var idle = new AsturTree(Source(Team(ChannelA)), new AsturTreeActions());
            using var stage2 = new HomeStage(new StackPanel { Width = 260, Children = { idle } }, 300, 400);
            Assert.All(idle.Rows[0].MenuItems(), i => Assert.False(i.IsEnabled));
        });
    }

    [Fact]
    public void The_source_changing_redraws_the_tree_once_and_a_tree_taken_off_the_window_stops_reading()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = Source(Team(ChannelA));
            var tree = new AsturTree(source);
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 400);
            var reads = source.Reads;

            source.Raise();
            source.Raise();
            stage.Layout();
            Assert.Equal(reads + 1, source.Reads);

            stage.Window.Content = null;
            stage.Layout();
            var after = source.Reads;
            source.Raise();
            stage.Layout();
            Assert.Equal(after, source.Reads);
        });
    }

    [Fact]
    public void Binding_another_source_lets_an_owned_one_go_and_null_hides_the_group()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var tree = new AsturTree();
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 400);
            Assert.False(tree.IsVisible);

            var disposable = new DisposableTeamSource(Source(Team(ChannelA, ChannelB)));
            tree.Bind(disposable, owned: true);
            stage.Layout();
            Assert.Equal(2, tree.Rows.Count);

            tree.Bind(Source(Team(Store)));
            Assert.True(disposable.Disposed);
            Assert.Equal(["s"], tree.Rows.Select(r => r.Id));
            tree.Bind(null);
            stage.Layout();
            Assert.False(tree.IsVisible);
            Assert.Empty(tree.Rows);
        });
    }

    [Fact]
    public void A_new_Astur_grows_in_and_a_retired_one_folds_away_with_motion_and_both_are_at_once_without_it()
    {
        headless.OnUiThread(() =>
        {
            var source = Source(Team(ChannelA));
            var tree = new AsturTree(source);
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 400);

            source.Set(TeamSnapshot.Build(Team(ChannelA, Store), [], []));
            stage.Layout();
            Assert.NotNull(tree.Rows[1].Reveal.Transitions);                                        // it grows from zero height on the group's own ease
            Assert.True(tree.Rows[1].Reveal.IsOpen);

            source.Set(TeamSnapshot.Build(Team(ChannelA), [], []));
            stage.Layout();
            Assert.Equal(["a"], tree.Rows.Select(r => r.Id));                                       // it is out of the list at once; the fold finishes in place
            stage.Advance(TimeSpan.FromMilliseconds(400));
        });
    }

    [Fact]
    public void Up_and_Down_walk_the_disclosure_line_and_the_rows()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var tree = new AsturTree(Source(Team(ChannelA, ChannelB)));
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 400);

            tree.Toggle.Focus();
            stage.Layout();
            stage.Press(Key.Down);
            Assert.Same(tree.Rows[0].Button, stage.Focused);
            stage.Press(Key.Down);
            Assert.Same(tree.Rows[1].Button, stage.Focused);
            stage.Press(Key.Down);
            Assert.Same(tree.Rows[1].Button, stage.Focused);
            stage.Press(Key.Up);
            stage.Press(Key.Up);
            Assert.Same(tree.Toggle, stage.Focused);
        });
    }

    [Fact]
    public void The_marks_follow_the_calm_flag_and_a_long_name_ends_in_an_ellipsis_inside_the_row_at_240_px()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var longName = Astur("a", new string('M', 80), AsturShape.Star);
            var tree = new AsturTree(Source(Team(longName, ChannelB), [Task("a", new string('w', 300), MissionState.Working)]));
            using var stage = new HomeStage(new StackPanel { Width = 240, Children = { tree } }, 240, 400);

            tree.Calm = true;
            Assert.All(tree.Rows, r => Assert.True(r.Mark.SentLook is null || r.Mark.SentLook.Reduce));
            var past = Nodes<Control>(tree).Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.IsAttachedToVisualTree() && stage.Box(c).Right > 240.5).Select(c => c.GetType().Name + " " + stage.Box(c)).ToList();
            Assert.True(past.Count == 0, string.Join(" | ", past));
            Assert.Equal(42, tree.Rows[0].Button.Bounds.Height, 1);
        });
    }
}
