using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Testing;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

// Dextra AS-17 (DESIGN.md rev 5, 2.1 "Mission chats ... appear in a collapsed 'Missions' group under the assistant that owns them"; the canvas's note under the sidebar;
// ED-NEW-dextra-as17-10): each Astur's lead chats hang in a collapsed Missions group under its own row, and Dextra's group lists only what Dextra owns.
[Collection("Headless")]
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-10")]
public sealed class AsturMissionsTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static TeamSnapshot Build(IReadOnlyList<AssistantProfile> team, params (string AssistantId, MissionJob Job)[] jobs) =>
        TeamSnapshot.Build(team, jobs, [], now: HomeSamples.Now);

    [Fact]
    public void Each_assistant_lists_the_lead_chats_of_the_tasks_it_owns_live_ones_first_then_todays_finished_ones()
    {
        var voice = Task("a", "Voiceover", MissionState.Working, 10);
        var cut = Task("a", "Cut the intro", MissionState.Done, 1);
        var old = Task("a", "Last week", MissionState.Done, 60 * 30);
        old.Job = old.Job with { Ended = HomeSamples.Now.AddDays(-1) };
        var quiet = Task("a", "No chat", MissionState.Working, 2);
        quiet.Job = quiet.Job with { LeadThread = null };
        var update = Task("s", "Weekly update", MissionState.Paused);
        var plan = Task(Root, "Plan the week", MissionState.Planning);

        var snap = Build(Team(ChannelA, Store), voice, cut, old, quiet, update, plan);

        Assert.Equal(["Voiceover", "Cut the intro"], snap.Find("a")!.Chats.Select(c => c.Title));
        Assert.Equal(["Weekly update"], snap.Find("s")!.Chats.Select(c => c.Title));
        Assert.Equal(["Plan the week"], snap.Dextra!.Chats.Select(c => c.Title));
        var first = snap.Find("a")!.Chats[0];
        Assert.Equal(voice.Job.LeadThread, first.Thread);
        Assert.Equal(voice.Job.Id, first.Id);
        Assert.Equal("Voiceover, Working", first.Reading);
        Assert.Equal("Cut the intro, Done", snap.Find("a")!.Chats[1].Reading);
        Assert.Equal(MissionState.Working, first.State);
    }

    [Fact]
    public void The_chats_are_part_of_what_a_member_is_so_a_new_chat_is_a_change_and_the_same_again_is_not()
    {
        var one = Task("a", "Voiceover", MissionState.Working);
        var again = Build(Team(ChannelA), one);
        Assert.Equal(again.Find("a"), Build(Team(ChannelA), one).Find("a"));

        var two = Build(Team(ChannelA), one, Task("a", "Thumbnail", MissionState.Planning));
        Assert.NotEqual(again.Find("a"), two.Find("a"));
        Assert.Empty(Build(Team(ChannelA)).Find("a")!.Chats);
    }

    [Fact]
    public void AsturOf_names_the_Astur_that_owns_a_task_and_is_null_for_Dextra_for_nobody_and_for_a_retired_Astur()
    {
        var mine = Task("a", "Voiceover", MissionState.Working);
        var dextras = Task(Root, "Plan", MissionState.Working);
        var nobodys = Task("a", "Orphan", MissionState.Paused);
        var gone = Task("g", "Of a retired one", MissionState.Paused);
        var owners = new[] { mine, dextras, gone }.ToDictionary(t => t.Job.Id, t => t.AssistantId);
        var retired = Astur("g", "Gone", AsturShape.Star) with { Retired = true };
        var source = new TeamSource(() => Team(ChannelA, retired), new FakeMissionJobs(), id => owners.GetValueOrDefault(id), new FakeDecisions(), new FakeGlobalControls());

        Assert.Equal("a", source.AsturOf(mine.Job.Id));
        Assert.Null(source.AsturOf(dextras.Job.Id));
        Assert.Null(source.AsturOf(nobodys.Job.Id));
        Assert.Null(source.AsturOf(gone.Job.Id));
        Assert.Null(source.AsturOf(Guid.NewGuid()));
    }

    [Fact]
    public void The_live_source_builds_each_Asturs_chats_with_its_own_clock_so_a_finished_chat_of_today_is_listed()
    {
        var done = Task("a", "Cut the intro", MissionState.Done, 1);
        var jobs = new FakeMissionJobs();
        jobs.Set([done.Job]);
        var source = new TeamSource(() => Team(ChannelA), jobs, _ => "a", new FakeDecisions(), new FakeGlobalControls());
        using var clock = Marrow.Core.Port.SwiftClock.Provider.Override(new ManualTimeProvider(HomeSamples.Now));

        Assert.Equal(["Cut the intro"], source.Now().Find("a")!.Chats.Select(c => c.Title));
    }

    [Fact]
    public void An_Astur_with_no_chat_has_no_group_and_one_with_a_chat_has_a_collapsed_Missions_group_under_its_face()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeTeamSource { Current = Build(Team(ChannelA, Store), Task("a", "Voiceover", MissionState.Working), Task("a", "Thumbnail", MissionState.Planning)) };
            var tree = new AsturTree(source);
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 500);

            var a = tree.RowOf("a")!;
            var s = tree.RowOf("s")!;
            Assert.Equal(0, s.Chats.Count);
            Assert.False(s.ChatsReveal.IsVisible);
            Assert.Equal(42, s.Bounds.Height, 1);                                                  // a row without chats is the row it was
            Assert.Equal(2, a.Chats.Count);
            Assert.Equal("Missions  2", a.Chats.Label.Text);
            Assert.False(a.Chats.IsOpen);
            Assert.True(a.ChatsReveal.IsVisible);
            Assert.Equal("Show Channel A's missions, 2", AutomationProperties.GetName(a.Chats.Toggle));
            Assert.True(stage.Box(a.Button).Bottom <= stage.Box(a.Chats.Toggle).Y + .5);           // under the face
            Assert.Equal(42 + 24, a.Bounds.Height, 1);                                             // the face and the disclosure line, folded
            Assert.True(stage.Box(a).Bottom <= stage.Box(s).Y + .5);                                // the next Astur follows the group
        });
    }

    [Fact]
    public void Opening_the_group_lists_the_chats_each_named_by_its_state_and_a_press_opens_that_chat()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var voice = Task("a", "Voiceover", MissionState.Working);
            var thumb = Task("a", "Thumbnail", MissionState.Planning, 20);
            var opened = new List<Guid>();
            var source = new FakeTeamSource { Current = Build(Team(ChannelA, Store), voice, thumb) };
            var tree = new AsturTree(source, new AsturTreeActions { OpenChat = opened.Add });
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 500);
            var a = tree.RowOf("a")!;
            var closed = a.Bounds.Height;

            Click(a.Chats.Toggle);
            stage.Layout();

            Assert.True(a.Chats.IsOpen);
            Assert.Equal(["Voiceover, Working", "Thumbnail, Planning"], a.Chats.Buttons.Select(b => AutomationProperties.GetName(b)));
            Assert.Equal("Hide Channel A's missions, 2", AutomationProperties.GetName(a.Chats.Toggle));
            Assert.True(a.Bounds.Height > closed);
            Assert.All(a.Chats.Buttons, b => Assert.True(stage.Box(b).Right <= stage.Box(tree).Right + .5 && stage.Box(b).X >= stage.Box(a.Button).X - .5));
            Click(a.Chats.Buttons[1]);
            Assert.Equal([thumb.Job.LeadThread!.Value], opened);
        });
    }

    [Fact]
    public void The_trunk_goes_on_past_the_group_to_the_next_Astur_and_ends_at_the_last_ones_face()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeTeamSource { Current = Build(Team(ChannelA, Store), Task("a", "Voiceover", MissionState.Working), Task("s", "Update", MissionState.Paused)) };
            var tree = new AsturTree(source);
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 500);
            var a = tree.RowOf("a")!;
            var s = tree.RowOf("s")!;
            Click(a.Chats.Toggle);
            Click(s.Chats.Toggle);
            stage.Layout();

            // the trunk's cell takes the face and the group, so the line is not broken by the group; the tick still leaves it at the middle of the face
            Assert.Equal(a.Bounds.Height, stage.Box(a.Elbow).Height, 1);
            Assert.Equal(s.Bounds.Height, stage.Box(s.Elbow).Height, 1);
            Assert.Equal(21, TreeElbow.TickY, 1);
            Assert.False(a.IsLast);
            Assert.True(s.IsLast);
        });
    }

    [Fact]
    public void A_chat_that_comes_or_goes_grows_or_folds_the_group_and_with_Reduce_motion_it_is_at_once()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeTeamSource { Current = Build(Team(ChannelA, Store)) };
            var tree = new AsturTree(source);
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 500);
            var a = tree.RowOf("a")!;
            Assert.False(a.ChatsReveal.IsVisible);

            source.Set(Build(Team(ChannelA, Store), Task("a", "Voiceover", MissionState.Working)));
            stage.Layout();
            Assert.True(a.ChatsReveal.IsVisible);
            Assert.Equal(1, a.ChatsReveal.Reveal);
            Assert.Equal(42 + 24, a.Bounds.Height, 1);

            source.Set(Build(Team(ChannelA, Store)));
            stage.Layout();
            Assert.False(a.ChatsReveal.IsVisible);
            Assert.Equal(42, a.Bounds.Height, 1);
        });
    }

    [Fact]
    public void Without_Reduce_motion_the_group_grows_from_zero_on_the_groups_own_reveal_and_never_snaps()
    {
        headless.OnUiThread(() =>
        {
            var source = new FakeTeamSource { Current = Build(Team(ChannelA, Store)) };
            var tree = new AsturTree(source);
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 500);
            var a = tree.RowOf("a")!;

            source.Set(Build(Team(ChannelA, Store), Task("a", "Voiceover", MissionState.Working)));
            stage.Layout();

            Assert.True(a.ChatsReveal.IsOpen);
            Assert.True(a.ChatsReveal.IsVisible);
            Assert.True(a.ChatsReveal.Reveal < 1);                                                  // it starts from nothing: the row did not jump to its full height
            var grow = Assert.Single(a.ChatsReveal.Transitions!.OfType<Avalonia.Animation.DoubleTransition>());
            Assert.Equal(RevealBox.RevealProperty, grow.Property);
            Assert.Equal(RevealBox.In, grow.Duration);
        });
    }

    [Fact]
    public void Up_and_Down_walk_the_disclosure_the_face_the_group_and_its_lines_while_it_is_open()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeTeamSource { Current = Build(Team(ChannelA, Store), Task("a", "Voiceover", MissionState.Working), Task("a", "Thumbnail", MissionState.Planning)) };
            var tree = new AsturTree(source);
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 500);
            var a = tree.RowOf("a")!;
            var s = tree.RowOf("s")!;

            Assert.Equal<Control>([a.Button, a.Chats.Toggle], a.Stops());                          // folded: the lines are not stops
            Click(a.Chats.Toggle);
            stage.Layout();
            Assert.Equal<Control>([a.Button, a.Chats.Toggle, .. a.Chats.Buttons], a.Stops());
            Assert.Equal<Control>([s.Button], s.Stops());

            a.Chats.Toggle.Focus(NavigationMethod.Directional);
            stage.Press(Key.Down);
            Assert.True(a.Chats.Buttons[0].IsFocused);
            stage.Press(Key.Down);
            stage.Press(Key.Down);
            Assert.True(s.Button.IsFocused);
            stage.Press(Key.Up);
            Assert.True(a.Chats.Buttons[1].IsFocused);
        });
    }

    [Fact]
    public void The_groups_marks_follow_the_calm_flag_and_cost_the_motion_budget_only_while_they_move()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeTeamSource { Current = Build(Team(ChannelA), Task("a", "Voiceover", MissionState.Working), Task("a", "Review", MissionState.NeedsYou)) };
            var tree = new AsturTree(source);
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { tree } }, 300, 500);
            var a = tree.RowOf("a")!;

            Assert.Equal(0, tree.MovingMarks);                                                      // folded: nothing of it is drawn, so nothing is counted
            Click(a.Chats.Toggle);
            stage.Layout();
            tree.Calm = true;
            Assert.Equal(0, tree.MovingMarks);                                                      // stilled: the still poses
            Assert.True(a.Chats.Calm);
            tree.Calm = false;
            Assert.False(a.Chats.Calm);
            Assert.True(tree.MovingMarks >= 0);
        });
    }

    // where a control really is on the screen (a mirrored visual's own origin is its right edge, so both corners are read)
    private static Rect Screen(HomeStage stage, Control control)
    {
        var a = control.TranslatePoint(default, stage.Window)!.Value;
        var b = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), stage.Window)!.Value;
        return new Rect(new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
    }

    [Fact]
    public void A_long_chat_title_stays_inside_its_line_at_240_px_and_in_a_right_to_left_window_the_group_still_sits_under_the_face_and_inside_the_tree()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var source = new FakeTeamSource { Current = Build(Team(ChannelA), Task("a", new string('w', 300), MissionState.Working)) };
            var tree = new AsturTree(source);
            using var stage = new HomeStage(new StackPanel { Width = 240, Children = { tree } }, 260, 400, FlowDirection.RightToLeft);
            var a = tree.RowOf("a")!;
            Click(a.Chats.Toggle);
            stage.Layout();

            var line = Screen(stage, a.Chats.Buttons[0]);
            var all = Screen(stage, tree);
            var face = Screen(stage, a.Button);
            Assert.True(line.X >= all.X - .5 && line.Right <= all.Right + .5, $"{line} in {all}");  // the long title is trimmed, not run past the sidebar
            var title = a.Chats.Buttons[0].GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.True(Screen(stage, title).Right <= line.Right + .5 && Screen(stage, title).X >= line.X - .5, $"{Screen(stage, title)} in {line}");   // and the words end in an ellipsis inside the line
            Assert.True(face.Bottom <= Screen(stage, a.Chats.Toggle).Y + .5);
            Assert.Equal(a.Bounds.Height, stage.Box(a.Elbow).Height, 1);
            Assert.True(Screen(stage, a.Elbow).Center.X > face.Center.X);                           // the trunk is on the right, where the tree starts in a right-to-left window
        });
    }

    [Fact]
    public void Dextras_group_lists_only_the_tasks_no_Astur_owns_so_a_chat_is_never_in_two_groups()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var own = HomeSamples.Job("Plan the week", MissionState.Working);
            var astur = HomeSamples.Job("Voiceover", MissionState.Working);
            var team = new FakeTeamSource { Current = Build(Team(ChannelA), (Root, own), ("a", astur)), Owners = id => id == astur.Id ? "a" : null };
            var row = new AssistantSidebarRow("Dextra", new AssistantRowActions { Team = _ => team });
            var jobs = new FakeMissionJobs();
            jobs.Set([own, astur]);
            row.Bind(new DextraLive(jobs, new FakeDecisions(), new FakeGlobalControls()));
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { row } }, 300, 500);

            Assert.Equal(["Plan the week, Working"], row.MissionButtons.Select(b => AutomationProperties.GetName(b)));
            Assert.Equal("Missions  1", row.MissionsLabel.Text);
            Assert.Equal(["Voiceover, Working"], row.Asturs.RowOf("a")!.Chats.Buttons.Select(b => AutomationProperties.GetName(b)));
        });
    }

    [Fact]
    public void A_long_chat_title_in_Dextras_own_group_ends_inside_its_line_too()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var row = new AssistantSidebarRow("Dextra", new AssistantRowActions { Collapsed = () => false });
            var jobs = new FakeMissionJobs();
            jobs.Set([HomeSamples.Job(new string('w', 300), MissionState.Working)]);
            row.Bind(new DextraLive(jobs, new FakeDecisions(), new FakeGlobalControls()));
            using var stage = new HomeStage(new StackPanel { Width = 240, Children = { row } }, 260, 400);

            var line = stage.Box(row.MissionButtons[0]);
            var title = row.MissionButtons[0].GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.True(stage.Box(title).Right <= line.Right + .5, $"{stage.Box(title)} in {line}");
        });
    }

    [Fact]
    public void A_task_of_a_retired_Astur_goes_back_to_Dextras_group_and_without_a_team_Dextras_group_lists_them_all()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var own = HomeSamples.Job("Plan the week", MissionState.Working);
            var theirs = HomeSamples.Job("Of a retired one", MissionState.Paused);
            var jobs = new FakeMissionJobs();
            jobs.Set([own, theirs]);
            var live = new DextraLive(jobs, new FakeDecisions(), new FakeGlobalControls());

            var team = new FakeTeamSource { Current = Build(Team(ChannelA)), Owners = _ => null };       // the retired Astur no longer owns anything
            var withTeam = new AssistantSidebarRow("Dextra", new AssistantRowActions { Team = _ => team });
            withTeam.Bind(live);
            var bare = new AssistantSidebarRow("Dextra");
            bare.Bind(live);
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { withTeam, bare } }, 300, 600);

            Assert.Equal(2, withTeam.MissionButtons.Count);
            Assert.Equal(2, bare.MissionButtons.Count);
        });
    }

    [Fact]
    public void A_press_on_an_Astur_chat_in_the_sidebar_opens_it_through_the_Assistant_rows_own_chat_action()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var voice = Task("a", "Voiceover", MissionState.Working);
            var opened = new List<Guid>();
            var team = new FakeTeamSource { Current = Build(Team(ChannelA), voice) };
            var row = new AssistantSidebarRow("Dextra", new AssistantRowActions { Team = _ => team, OpenChat = opened.Add });
            row.Bind(new DextraLive(new FakeMissionJobs(), new FakeDecisions(), new FakeGlobalControls()));
            using var stage = new HomeStage(new StackPanel { Width = 260, Children = { row } }, 300, 500);

            var a = row.Asturs.RowOf("a")!;
            Click(a.Chats.Toggle);
            stage.Layout();
            Click(a.Chats.Buttons[0]);
            Assert.Equal([voice.Job.LeadThread!.Value], opened);
        });
    }
}
