using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.UI.Web;

namespace Marrow.App.Tests.Views.Panes;

// nth web surfaces, unit W3b: drag a header to re-dock (work/design/web-surfaces/DESIGN.md 3.6, 2.4, 2.5, 13.3 "header drag
// re-dock (simulated pointer), keyboard moves"). Press on a header's empty area and move 4 DIP and the pane lifts (scale 1.02,
// the deeper shadow, its web content a snapshot); outlines show the drops the solver can express and the one under the
// pointer fills softly; a release springs the pane on from where it was let go, a release over nothing or Esc springs it home.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w3b-2")]
public sealed class PaneRedockWorkspaceTests(HeadlessSession headless)
{
    // Tasks | Changes over a wide Visus: the canvas's own arrangement.
    private static PaneWorkspaceRig Three(bool reduceMotion = false, Func<PaneKind, PaneContent?>? contents = null)
    {
        var rig = new PaneWorkspaceRig(reduceMotion: reduceMotion, contents: contents);
        rig.Workspace.Open(PaneKind.BackgroundTasks);
        rig.Workspace.Open(PaneKind.Changes);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        rig.Changes.Clear();
        return rig;
    }

    private static Point Lift(PaneWorkspaceRig rig, PaneKind kind)
    {
        var press = rig.HeaderPoint(kind);
        rig.Down(press);
        rig.Move(press + new Vector(2, 0));
        Assert.False(rig.Workspace.IsRedocking, "2 DIP is not a lift");
        rig.Move(press + new Vector(6, 0));
        Assert.True(rig.Workspace.IsRedocking);
        return press;
    }

    private static void Near(Rect expected, Rect actual, string context, double tolerance = 1) =>
        Assert.True(Math.Abs(expected.X - actual.X) <= tolerance && Math.Abs(expected.Y - actual.Y) <= tolerance
            && Math.Abs(expected.Width - actual.Width) <= tolerance && Math.Abs(expected.Height - actual.Height) <= tolerance,
            $"{context}: expected {expected}, got {actual}");

    [Fact]
    public void A_press_that_moves_4_DIP_lifts_the_pane_one_to_one_with_the_pointer() => headless.OnUiThread(() =>
    {
        using var rig = Three();
        var slot = rig.Card(PaneKind.Changes).Bounds;
        var press = rig.HeaderPoint(PaneKind.Changes);
        rig.Down(press);
        rig.Move(press + new Vector(3, 0));
        Assert.False(rig.Workspace.IsRedocking);
        Assert.Equal(new Vector(0, 0), rig.Card(PaneKind.Changes).Offset);

        rig.Move(press + new Vector(5, 2)); // past 4 DIP
        Assert.True(rig.Workspace.IsRedocking);
        Assert.Equal(PaneKind.Changes, rig.Workspace.RedockKind);
        var card = rig.Card(PaneKind.Changes);
        Assert.True(card.IsLiftedLook, "the deeper shadow");
        Assert.Equal(40, card.ZIndex);
        Assert.True(card.Header.IsLifted);
        Assert.Equal(new Vector(5, 2), card.Offset); // from where the button went down, one to one
        for (var i = 0; i < 40 && rig.Workspace.IsMoving; i++) rig.Frame();
        Assert.Equal(PaneRedock.LiftScale, card.Scale, 2);
        rig.Move(press + new Vector(-120, 77));
        Assert.Equal(new Vector(-120, 77), card.Offset);
        Assert.Equal(slot, card.Bounds); // a render transform: the layout does not move

        // The other panes stay where they are, and no splitter takes the pointer while a pane is held.
        Assert.False(rig.Workspace.SplitterOf(PaneGutterKeys.Chat).IsHitTestVisible);
        Assert.Equal(0, rig.Card(PaneKind.BackgroundTasks).Offset.X);
        rig.Up(press + new Vector(-120, 77));
    });

    [Fact]
    public void The_outlines_are_the_drops_the_solver_can_express_and_the_one_under_the_pointer_lights() => headless.OnUiThread(() =>
    {
        using var rig = Three();
        var press = Lift(rig, PaneKind.Changes);
        for (var i = 0; i < 40 && rig.Workspace.IsMoving; i++) rig.Frame();
        var visuals = rig.Workspace.DropVisuals;
        Assert.Equal(4, visuals.Count); // Tasks and Visus, each a body and a header
        Assert.DoesNotContain(visuals, visual => visual.Target.Target == PaneKind.Changes);
        Assert.All(visuals, visual =>
        {
            Assert.Equal(1, visual.Opacity, 2);
            Assert.Equal(0, visual.Hot, 3);
            Assert.False(visual.IsHitTestVisible);
        });
        Assert.Null(rig.Workspace.HotDrop);

        var tasksBody = rig.BodyPoint(PaneKind.BackgroundTasks);
        rig.Move(tasksBody);
        var hot = rig.Workspace.HotDrop;
        Assert.NotNull(hot);
        Assert.Equal((PaneDropKind.Body, PaneKind.BackgroundTasks), (hot.Target.Kind, hot.Target.Target));
        for (var i = 0; i < 40 && rig.Workspace.IsMoving; i++) rig.Frame();
        Assert.Equal(1, hot.Hot, 2);
        Assert.All(rig.Workspace.DropVisuals.Where(visual => !ReferenceEquals(visual, hot)), visual => Assert.Equal(0, visual.Hot, 2));

        rig.Move(rig.HeaderPoint(PaneKind.Browser, dx: 60));
        Assert.Equal(PaneDropKind.Header, rig.Workspace.HotDrop!.Target.Kind);
        rig.Move(new Point(30, 300)); // over the chat: no target
        Assert.Null(rig.Workspace.HotDrop);
        for (var i = 0; i < 40 && rig.Workspace.IsMoving; i++) rig.Frame();
        Assert.All(rig.Workspace.DropVisuals, visual => Assert.Equal(0, visual.Hot, 2));
        rig.Up(new Point(30, 300));
    });

    [Fact]
    public void A_release_over_another_cells_body_swaps_the_two_and_springs_on_from_where_it_was_let_go() => headless.OnUiThread(() =>
    {
        using var rig = Three();
        var changesSlot = rig.Card(PaneKind.Changes).Bounds;
        var tasksSlot = rig.Card(PaneKind.BackgroundTasks).Bounds;
        var press = Lift(rig, PaneKind.Changes);
        var drop = rig.BodyPoint(PaneKind.BackgroundTasks);
        rig.Move(drop);
        var travel = new Vector(drop.X - press.X, drop.Y - press.Y);
        rig.Up(drop);

        Assert.Equal([PaneKind.Changes, PaneKind.BackgroundTasks, PaneKind.Browser], rig.Workspace.State.GridPanes);
        Assert.Single(rig.Changes);
        // The pane continues from the dragged rectangle (no snap back to its old slot, no jump to the new one).
        var card = rig.Card(PaneKind.Changes);
        Near(new Rect(changesSlot.X + travel.X, changesSlot.Y + travel.Y, changesSlot.Width, changesSlot.Height), card.Bounds, "at the release", 2);
        Assert.Equal(new Vector(0, 0), card.Offset);
        Assert.True(rig.Workspace.IsMoving);
        Assert.Equal(40, card.ZIndex); // over the others until it has landed
        var seen = new List<double>();
        for (var i = 0; i < 12 && rig.Workspace.IsMoving; i++)
        {
            rig.Frame();
            seen.Add(Math.Abs(card.Bounds.X - tasksSlot.X) + Math.Abs(card.Bounds.Y - tasksSlot.Y));
        }
        Assert.True(seen[0] > seen[^1], "it travels toward the slot Tasks had");
        rig.Settle();
        Near(tasksSlot, card.Bounds, "Changes in Tasks's slot");
        Near(changesSlot, rig.Card(PaneKind.BackgroundTasks).Bounds, "Tasks in Changes's slot");
        Assert.Equal(1, card.Scale, 3);
        Assert.Equal(0, card.ZIndex);
        Assert.False(card.IsLiftedLook);
        Assert.Empty(rig.Workspace.DropVisuals);
        Assert.True(rig.Workspace.SplitterOf(PaneGutterKeys.Chat).IsHitTestVisible);
        Assert.False(rig.Workspace.IsRedocking);
        foreach (var kind in new[] { PaneKind.BackgroundTasks, PaneKind.Changes, PaneKind.Browser })
            Near(rig.ExpectedBounds(kind), rig.Card(kind).Bounds, kind.ToString());
    });

    [Fact]
    public void A_release_over_another_cells_header_joins_it_as_a_tab() => headless.OnUiThread(() =>
    {
        using var rig = Three();
        var visusSlot = rig.Card(PaneKind.Browser).Bounds;
        Lift(rig, PaneKind.Changes);
        var drop = rig.HeaderPoint(PaneKind.Browser, dx: 80);
        rig.Move(drop);
        Assert.Equal(PaneDropKind.Header, rig.Workspace.HotDrop!.Target.Kind);
        rig.Up(drop);
        rig.Settle();

        Assert.Equal(2, rig.Workspace.State.Order.Count);
        Assert.Equal([PaneKind.Browser, PaneKind.Changes], rig.Workspace.State.Order[1].Panes);
        Assert.Equal(PaneKind.Changes, rig.Workspace.State.RecentTabs[0]);
        var changes = rig.Card(PaneKind.Changes);
        Assert.True(changes.IsVisible);
        Assert.False(rig.Card(PaneKind.Browser).IsVisible, "the tab that was shown gives way");
        Assert.Equal(2, changes.Header.TabStrip.Children.Count);
        Near(rig.ExpectedBounds(PaneKind.Changes), changes.Bounds, "the joined pane fills the group's cell");
        Assert.Equal(rig.ExpectedBounds(PaneKind.Browser), rig.ExpectedBounds(PaneKind.Changes)); // one cell for both tabs
        // Two cells now take the grid in place of three: the layout is the solver's.
        Near(rig.ExpectedBounds(PaneKind.BackgroundTasks), rig.Card(PaneKind.BackgroundTasks).Bounds, "Tasks");
        Assert.NotEqual(visusSlot, changes.Bounds);
    });

    [Fact]
    public void A_release_over_nothing_and_Esc_spring_the_pane_home_and_change_nothing() => headless.OnUiThread(() =>
    {
        foreach (var byEscape in new[] { false, true })
        {
            using var rig = Three();
            var state = rig.Workspace.State;
            var slot = rig.Card(PaneKind.Changes).Bounds;
            var press = Lift(rig, PaneKind.Changes);
            var away = new Point(40, 400); // over the chat
            rig.Move(away);
            Assert.Equal(new Vector(away.X - press.X, away.Y - press.Y), rig.Card(PaneKind.Changes).Offset);
            if (byEscape)
            {
                rig.Move(rig.BodyPoint(PaneKind.BackgroundTasks)); // over a target, then Esc
                rig.Key(Key.Escape);
            }
            else rig.Up(away);
            Assert.False(rig.Workspace.IsRedocking);
            Assert.True(rig.Workspace.IsMoving, "it springs home, it does not snap");
            var firstFrame = rig.Card(PaneKind.Changes).Offset;
            rig.Frame();
            rig.Frame();
            Assert.True(Math.Abs(rig.Card(PaneKind.Changes).Offset.X) < Math.Abs(firstFrame.X) || Math.Abs(rig.Card(PaneKind.Changes).Offset.Y) < Math.Abs(firstFrame.Y));
            rig.Settle();
            Assert.Equal(new Vector(0, 0), rig.Card(PaneKind.Changes).Offset);
            Assert.Equal(1, rig.Card(PaneKind.Changes).Scale, 3);
            Assert.Equal(slot, rig.Card(PaneKind.Changes).Bounds);
            Assert.Equal(state, rig.Workspace.State);
            Assert.Empty(rig.Changes);
            Assert.Empty(rig.Workspace.DropVisuals);
        }
    });

    [Fact]
    public void A_tab_of_a_group_leaves_it_by_its_own_body_or_takes_another_cells_place() => headless.OnUiThread(() =>
    {
        using var rig = Three();
        rig.Workspace.SetState(rig.Workspace.State.JoinAsTab(PaneKind.Changes, PaneKind.BackgroundTasks));
        rig.Settle();
        rig.Changes.Clear();
        Assert.Equal(2, rig.Workspace.State.Order.Count);
        var group = rig.Card(PaneKind.Changes);
        Assert.Equal(2, group.Header.TabStrip.Children.Count);

        Lift(rig, PaneKind.Changes);
        for (var i = 0; i < 40 && rig.Workspace.IsMoving; i++) rig.Frame();
        // Its own cell's body is a target now: leaving the group.
        var own = rig.Workspace.DropVisuals.Single(visual => visual.Target.Target == PaneKind.BackgroundTasks && visual.Target.Kind == PaneDropKind.Body);
        Assert.Equal(own.Target.Result.Order[0].Panes, new[] { PaneKind.Changes });
        var drop = rig.BodyPoint(PaneKind.Changes);
        rig.Move(drop);
        Assert.Same(own, rig.Workspace.HotDrop);
        rig.Up(drop);
        rig.Settle();
        Assert.Equal(3, rig.Workspace.State.Order.Count);
        Assert.All(rig.Workspace.State.Order, slot => Assert.False(slot.IsGroup));
        Assert.Equal([PaneKind.Changes, PaneKind.BackgroundTasks, PaneKind.Browser], rig.Workspace.State.GridPanes);
        foreach (var kind in new[] { PaneKind.BackgroundTasks, PaneKind.Changes, PaneKind.Browser })
            Near(rig.ExpectedBounds(kind), rig.Card(kind).Bounds, kind.ToString());
    });

    [Fact]
    public void The_chosen_tab_lifts_its_pane_and_another_tab_is_only_a_click() => headless.OnUiThread(() =>
    {
        using var rig = Three();
        rig.Workspace.SetState(rig.Workspace.State.JoinAsTab(PaneKind.Changes, PaneKind.BackgroundTasks));
        rig.Settle();
        var card = rig.Card(PaneKind.Changes);
        var tabs = card.Header.TabStrip.Children.OfType<Button>().ToList();
        var chosen = tabs.Single(tab => tab.Classes.Contains("chosen"));
        var other = tabs.Single(tab => !tab.Classes.Contains("chosen"));

        var otherAt = other.TranslatePoint(new Point(other.Bounds.Width / 2, other.Bounds.Height / 2), rig.Window)!.Value;
        rig.Down(otherAt);
        rig.Move(otherAt + new Vector(30, 10));
        Assert.False(rig.Workspace.IsRedocking, "a tab that is not chosen does not lift");
        rig.Up(otherAt + new Vector(30, 10));

        var chosenAt = chosen.TranslatePoint(new Point(chosen.Bounds.Width / 2, chosen.Bounds.Height / 2), rig.Window)!.Value;
        rig.Down(chosenAt);
        rig.Move(chosenAt + new Vector(12, 0));
        Assert.True(rig.Workspace.IsRedocking);
        Assert.Equal(PaneKind.Changes, rig.Workspace.RedockKind);
        rig.Up(new Point(30, 400));
        rig.Settle();
    });

    [Fact]
    public void A_click_on_a_header_button_still_clicks_and_a_press_on_it_that_moves_does_not_lift() => headless.OnUiThread(() =>
    {
        using var rig = Three();
        var close = rig.Card(PaneKind.Changes).Header.CloseButton;
        var at = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), rig.Window)!.Value;
        rig.Down(at);
        rig.Move(at + new Vector(40, 30));
        Assert.False(rig.Workspace.IsRedocking);
        rig.Up(at + new Vector(40, 30));
        Assert.True(rig.Workspace.State.IsOpen(PaneKind.Changes), "released elsewhere: no click");

        rig.Down(at);
        rig.Up(at);
        rig.Settle();
        Assert.False(rig.Workspace.State.IsOpen(PaneKind.Changes), "a click closes it");
    });

    [Fact]
    public void The_chat_Side_chat_an_expanded_pane_and_a_narrow_window_cannot_be_lifted() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Workspace.Open(PaneKind.SideChat);
        rig.Settle();
        Assert.False(rig.Card(PaneKind.SideChat).Header.CanDrag, "Side chat has its column and is not in the order");
        var press = rig.HeaderPoint(PaneKind.SideChat);
        rig.Down(press);
        rig.Move(press + new Vector(20, 20));
        Assert.False(rig.Workspace.IsRedocking);
        rig.Up(press);

        PaneWorkspaceRig.Click(rig.Card(PaneKind.Browser).Header.ExpandButton);
        rig.Settle();
        Assert.False(rig.Card(PaneKind.Browser).Header.CanDrag, "an expanded pane is not lifted");
        PaneWorkspaceRig.Click(rig.Card(PaneKind.Browser).Header.ExpandButton);
        rig.Settle();
        Assert.True(rig.Card(PaneKind.Browser).Header.CanDrag);
    });

    [Fact]
    public void Closing_the_lifted_pane_ends_the_drag() => headless.OnUiThread(() =>
    {
        using var rig = Three();
        Lift(rig, PaneKind.Changes);
        rig.Workspace.Close(PaneKind.Changes);
        rig.Settle();
        Assert.False(rig.Workspace.IsRedocking);
        Assert.Empty(rig.Workspace.DropVisuals);
        Assert.False(rig.Workspace.State.IsOpen(PaneKind.Changes));
        rig.Up(new Point(10, 10));
    });

    [Fact]
    public void Under_Reduce_Motion_a_lifted_pane_does_not_scale_and_still_follows_the_pointer() => headless.OnUiThread(() =>
    {
        using var rig = Three(reduceMotion: true);
        var press = Lift(rig, PaneKind.Changes);
        for (var i = 0; i < 30 && rig.Workspace.IsMoving; i++) rig.Frame();
        Assert.Equal(1, rig.Card(PaneKind.Changes).Scale, 3);
        Assert.True(rig.Card(PaneKind.Changes).IsLiftedLook);
        rig.Move(press + new Vector(50, 50));
        Assert.Equal(new Vector(50, 50), rig.Card(PaneKind.Changes).Offset);
        rig.Up(new Point(30, 400));
        rig.Settle();
    });

    [Fact]
    public void Ctrl_Alt_Arrow_moves_the_focused_pane_one_slot_and_springs_like_a_drop() => headless.OnUiThread(() =>
    {
        using var rig = Three();
        var tasks = rig.Card(PaneKind.BackgroundTasks);
        var changes = rig.Card(PaneKind.Changes);
        var tasksSlot = tasks.Bounds;
        var changesSlot = changes.Bounds;
        rig.Bodies[PaneKind.BackgroundTasks].Focus();

        rig.Chord(Key.Left, PhysicalKey.ArrowLeft, RawInputModifiers.Control | RawInputModifiers.Alt); // already first: nothing
        Assert.Empty(rig.Changes);
        rig.Chord(Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.Equal([PaneKind.Changes, PaneKind.BackgroundTasks, PaneKind.Browser], rig.Workspace.State.GridPanes);
        Assert.True(rig.Workspace.IsMoving);
        rig.Settle();
        Near(changesSlot, tasks.Bounds, "Tasks moved into the second slot");
        Near(tasksSlot, changes.Bounds, "Changes into the first");
        Assert.True(tasks.IsKeyboardFocusWithin, "focus stays with the pane");

        rig.Chord(Key.Up, PhysicalKey.ArrowUp, RawInputModifiers.Control | RawInputModifiers.Alt); // Up is Left's twin
        rig.Settle();
        Assert.Equal([PaneKind.BackgroundTasks, PaneKind.Changes, PaneKind.Browser], rig.Workspace.State.GridPanes);
        rig.Chord(Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.Control | RawInputModifiers.Alt); // Down is Right's
        rig.Settle();
        Assert.Equal([PaneKind.Changes, PaneKind.BackgroundTasks, PaneKind.Browser], rig.Workspace.State.GridPanes);
        Assert.Equal(3, rig.Changes.Count);
        foreach (var kind in new[] { PaneKind.BackgroundTasks, PaneKind.Changes, PaneKind.Browser })
            Near(rig.ExpectedBounds(kind), rig.Card(kind).Bounds, kind.ToString());
    });

    [Fact]
    public void Ctrl_Alt_T_joins_the_focused_pane_to_the_one_before_it_and_splits_it_out_again() => headless.OnUiThread(() =>
    {
        using var rig = Three();
        rig.Bodies[PaneKind.Changes].Focus();
        rig.Chord(Key.T, PhysicalKey.T, RawInputModifiers.Control | RawInputModifiers.Alt);
        rig.Settle();
        Assert.Equal(2, rig.Workspace.State.Order.Count);
        Assert.Equal([PaneKind.BackgroundTasks, PaneKind.Changes], rig.Workspace.State.Order[0].Panes);
        Assert.Equal(2, rig.Card(PaneKind.Changes).Header.TabStrip.Children.Count);
        Assert.True(rig.Card(PaneKind.Changes).IsVisible);
        Assert.False(rig.Card(PaneKind.BackgroundTasks).IsVisible);

        rig.Bodies[PaneKind.Changes].Focus();
        rig.Chord(Key.T, PhysicalKey.T, RawInputModifiers.Control | RawInputModifiers.Alt);
        rig.Settle();
        Assert.Equal(3, rig.Workspace.State.Order.Count);
        Assert.True(rig.Card(PaneKind.BackgroundTasks).IsVisible && rig.Card(PaneKind.Changes).IsVisible);
        foreach (var kind in new[] { PaneKind.BackgroundTasks, PaneKind.Changes, PaneKind.Browser })
            Near(rig.ExpectedBounds(kind), rig.Card(kind).Bounds, kind.ToString());
    });

    // ------------------------------------------------------------------ the page under a lifted pane

    private static void PumpUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            if (DateTime.UtcNow > until) throw new TimeoutException("the condition did not come true");
            Thread.Sleep(1);
        }
    }

    [Fact]
    public void Every_web_view_shows_its_snapshot_before_a_pane_lifts_and_goes_live_when_it_has_landed() => headless.OnUiThread(() =>
    {
        var surfaces = new Dictionary<PaneKind, PaneWebSurface>();
        var hosts = new Dictionary<PaneKind, WebViewHost>();
        PaneContent? Contents(PaneKind kind)
        {
            Control body;
            if (kind == PaneKind.Browser)
            {
                var surface = surfaces[kind] = new PaneWebSurface();
                hosts[kind] = new WebViewHost(surface, _ => new Border { Focusable = true, Background = Brushes.Red });
                body = hosts[kind];
            }
            else body = new Border { Child = new TextBox { Text = kind.ToString() } };
            return new PaneContent(body, PaneKinds.Title(kind));
        }

        using var rig = Three(contents: Contents);
        PumpUntil(() => hosts[PaneKind.Browser].State == WebViewHostState.Live);
        var surface = surfaces[PaneKind.Browser];
        surface.HeldGesture = new TaskCompletionSource<Marrow.Core.Web.WebGestureFrame>();
        var host = hosts[PaneKind.Browser];

        var press = rig.HeaderPoint(PaneKind.Changes);
        rig.Down(press);
        rig.Move(press + new Vector(12, 0)); // a lift while the page is still being captured
        PumpUntil(() => surface.Captures == 1);
        Assert.True(rig.Workspace.IsRedocking);
        Assert.False(rig.Card(PaneKind.Changes).IsLiftedLook, "nothing lifts over a live page");
        Assert.False(host.IsShowingSnapshot);
        rig.Move(press + new Vector(40, 10)); // the pointer goes on; the pane waits for the capture
        Assert.Equal(new Vector(0, 0), rig.Card(PaneKind.Changes).Offset);

        surface.HeldGesture.SetResult(new Marrow.Core.Web.WebGestureFrame(surface.LastSnapshot, null));
        PumpUntil(() => rig.Card(PaneKind.Changes).IsLiftedLook);
        Assert.True(host.IsShowingSnapshot, "the page is its snapshot when the pane lifts");
        Assert.Equal(new Vector(40 - 0, 10), rig.Card(PaneKind.Changes).Offset);

        rig.Up(press + new Vector(40, 10)); // over nothing: home
        rig.Settle();
        PumpUntil(() => !host.IsShowingSnapshot);
        Assert.Equal(WebViewHostState.Live, host.State);
        Assert.Equal(1, surface.Captures);
    });
}
