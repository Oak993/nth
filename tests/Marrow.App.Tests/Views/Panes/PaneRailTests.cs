using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Views.Panes;

// nth web surfaces, unit W3b: collapse to rails and restore (work/design/web-surfaces/DESIGN.md 3.5 "dragging a gutter past a
// pane's minimum by 56 DIP collapses that side to a 22 DIP rail with the pane icons. Dragging back out past the minimum,
// clicking a rail icon, double-clicking the rail, or Space on the focused splitter restores it, to its size before the
// collapse", 4.2 "past a minimum it resists (0.3 x rubber band for 24 DIP) ... the range includes the collapsed positions",
// 3.2 rule 11, 13.3). The fold is one motion: the panes fade where they stand, the chat takes their room, the rail fades in;
// the chat never draws under 400 DIP on any frame.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w3b-3")]
public sealed class PaneRailTests(HeadlessSession headless)
{
    private static double Past(PaneGutter gutter, double overshoot) => gutter.NormalMaximum - gutter.Value + overshoot;

    private static (Point From, PaneGutter Gutter) Press(PaneWorkspaceRig rig, string key = PaneGutterKeys.Chat)
    {
        var gutter = rig.Expected().Gutter(key)!;
        var from = rig.SplitterCentre(key);
        rig.Down(from);
        rig.Move(from + new Vector(3, 0)); // past the slop
        return (from, gutter);
    }

    // Space on the focused splitter folds a collapsible side.
    private static void Fold(PaneWorkspaceRig rig, string key = PaneGutterKeys.Chat)
    {
        rig.Workspace.SplitterOf(key).Focus(NavigationMethod.Tab);
        rig.Key(Key.Space);
        rig.Settle();
    }

    [Fact]
    public void Dragging_the_chat_gutter_past_the_panes_minimum_by_56_folds_them_into_a_rail() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        rig.Workspace.SplitterOf(PaneGutterKeys.Chat).Focus(NavigationMethod.Tab);
        rig.Key(Key.Right, RawInputModifiers.Shift); // a stored size, so the fold has one to remember
        rig.Settle();
        var width = rig.Workspace.ChatBounds.Width;
        var (from, gutter) = Press(rig);

        rig.Move(from + new Vector(Past(gutter, 55), 0)); // 55 DIP past: not yet
        Assert.DoesNotContain(PaneGutterKeys.Chat, rig.Workspace.State.Collapsed);
        rig.Move(from + new Vector(Past(gutter, 57), 0)); // 57 DIP past: folds, springing
        Assert.Contains(PaneGutterKeys.Chat, rig.Workspace.State.Collapsed);
        Assert.True(rig.Workspace.IsMoving);
        rig.Up(from + new Vector(Past(gutter, 57), 0));
        rig.Settle();

        var expected = rig.Expected();
        var rail = rig.Workspace.RailOf(PaneGutterKeys.Chat);
        Assert.NotNull(rail);
        Assert.Equal([PaneKind.Browser], rail.Panes);
        Assert.Equal(22, rig.Workspace.RailBounds(PaneGutterKeys.Chat)!.Value.Width, 3);
        Assert.Equal(expected.Chat.Width, rig.Workspace.ChatBounds.Width, 1);
        Assert.False(rig.Card(PaneKind.Browser).IsVisible, "the folded pane is hidden once it has faded");
        Assert.Equal(1, rail.Opacity, 3);
        Assert.True(rig.Workspace.State.PreCollapse.ContainsKey(PaneGutterKeys.Chat), "restore remembers the size before the fold");
        Assert.Equal(width, rig.Workspace.State.PreCollapse[PaneGutterKeys.Chat] * PaneLayout.Solve(rig.Workspace.State, rig.SolverSize.Width, rig.SolverSize.Height).Gutter(PaneGutterKeys.Chat)!.Base, 1);
        Assert.Equal(3, rig.Changes.Count); // the open, the key's step, then the drag's one commit
        Assert.True(rig.Workspace.SplitterOf(PaneGutterKeys.Chat).IsVisible, "the splitter stays on the rail's edge");
    });

    [Fact]
    public void A_collapsible_limit_gives_a_third_of_the_overshoot_then_the_divider_springs_back_on_release() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var (from, gutter) = Press(rig);
        var limit = gutter.NormalMaximum;
        var tile = rig.Card(PaneKind.Browser).Bounds;
        var minimum = PaneKinds.Size(PaneKind.Browser).MinWidth;

        rig.Move(from + new Vector(Past(gutter, 0), 0));
        Assert.Equal(limit, rig.Workspace.ChatBounds.Width, 1);
        foreach (var overshoot in new[] { 10.0, 30.0, 50.0 })
        {
            rig.Move(from + new Vector(Past(gutter, overshoot), 0));
            var give = overshoot * SplitterMath.Resistance;
            Assert.Equal(limit + give, rig.Workspace.ChatBounds.Width, 1);
            var card = rig.Card(PaneKind.Browser).Bounds;
            Assert.Equal(limit + give + PaneKinds.Gap, card.X, 1); // the pane starts where the divider gave
            Assert.Equal(minimum - give, card.Width, 1); // and is that much narrower than its minimum, for now
            Assert.Equal(card.X - PaneKinds.Gap / 2, rig.Workspace.SplitterOf(PaneGutterKeys.Chat).Bounds.Center.X, 1);
            Assert.DoesNotContain(PaneGutterKeys.Chat, rig.Workspace.State.Collapsed);
            Assert.False(rig.Workspace.IsMoving, "inside the give the divider tracks the pointer with no spring");
        }
        rig.Up(from + new Vector(Past(gutter, 50), 0));
        Assert.True(rig.Workspace.IsMoving, "released, the divider gives back what it took on a spring");
        rig.Settle();
        Assert.Equal(limit, rig.Workspace.ChatBounds.Width, 1);
        Assert.Equal(minimum, rig.Card(PaneKind.Browser).Bounds.Width, 1);
        Assert.NotEqual(tile.X, rig.Card(PaneKind.Browser).Bounds.X);
        Assert.Empty(rig.Workspace.State.Collapsed);
    });

    [Fact]
    public void Reduce_Motion_has_no_give() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(reduceMotion: true);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var (from, gutter) = Press(rig);
        rig.Move(from + new Vector(Past(gutter, 40), 0));
        Assert.Equal(gutter.NormalMaximum, rig.Workspace.ChatBounds.Width, 1);
        rig.Up(from + new Vector(Past(gutter, 40), 0));
        rig.Settle();
    });

    [Fact]
    public void A_limit_that_cannot_fold_does_not_give() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.BackgroundTasks);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var key = rig.Expected().Gutters.First(g => !g.CanCollapse).Key;
        var gutter = rig.Expected().Gutter(key)!;
        var from = rig.SplitterCentre(key);
        var axis = gutter.Axis == PaneGutterAxis.X ? new Vector(1, 0) : new Vector(0, 1);
        rig.Down(from);
        rig.Move(from + axis * 3);
        rig.Move(from + axis * (gutter.NormalMaximum - gutter.Value + 40) * gutter.ValueSign);
        var top = rig.Card(PaneKind.BackgroundTasks);
        var size = gutter.Axis == PaneGutterAxis.X ? top.Bounds.Width : top.Bounds.Height;
        Assert.True(size <= (gutter.Axis == PaneGutterAxis.X ? PaneKinds.Size(PaneKind.BackgroundTasks).MaxWidth : gutter.NormalMaximum) + 1);
        Assert.All(rig.Workspace.Cards, card => Assert.True(card.Bounds.Width >= PaneKinds.Size(card.Kind).MinWidth - 0.5 || !card.IsVisible, $"{card.Kind} is under its minimum"));
        rig.Up(from + axis * 3);
        rig.Settle();
    });

    [Fact]
    public void A_fold_is_one_motion_the_panes_fade_where_they_stand_the_chat_takes_their_room_and_the_rail_fades_in() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(reflow: ChatReflow.Live);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Workspace.Open(PaneKind.Changes);
        rig.Settle();
        var tile = rig.Card(PaneKind.Browser).Bounds;
        var chatBefore = rig.Workspace.ChatBounds.Width;
        rig.Workspace.SplitterOf(PaneGutterKeys.Chat).Focus(NavigationMethod.Tab);
        rig.Key(Key.Space);
        rig.Layout();
        Assert.True(rig.Workspace.IsMoving);
        Assert.True(rig.Card(PaneKind.Browser).IsVisible, "the pane is still there while it fades");
        Assert.False(rig.Card(PaneKind.Browser).IsHitTestVisible, "and takes no pointer");
        var rail = rig.Workspace.RailOf(PaneGutterKeys.Chat)!;
        Assert.True(rail.IsVisible);

        var opacities = new List<double>();
        var railOpacities = new List<double>();
        var chats = new List<double>();
        for (var i = 0; i < 60 && rig.Workspace.IsMoving; i++)
        {
            rig.Frame();
            opacities.Add(rig.Card(PaneKind.Browser).IsVisible ? rig.Card(PaneKind.Browser).Opacity : 0);
            railOpacities.Add(rail.Opacity);
            chats.Add(rig.Workspace.ChatBounds.Width);
            if (rig.Card(PaneKind.Browser).IsVisible) Assert.Equal(tile.X, rig.Card(PaneKind.Browser).Bounds.X, 1); // it does not slide
        }
        Assert.True(opacities.Count > 5);
        Assert.True(opacities[2] < 1, "the pane fades");
        Assert.True(railOpacities[2] > 0 && railOpacities[2] < 1, "the rail fades in as the pane fades out");
        Assert.Equal(0, opacities[^1], 3);
        Assert.Equal(1, railOpacities[^1], 3);
        Assert.All(chats, width => Assert.True(width >= PaneKinds.ChatMinWidth - 0.001, $"the chat drew at {width}"));
        Assert.True(chats[^1] > chatBefore + 100);
        Assert.False(rig.Card(PaneKind.Browser).IsVisible);
        Assert.False(rig.Card(PaneKind.Changes).IsVisible);
    });

    [Fact]
    public void Under_Reduce_Motion_the_fold_lands_in_0_18_s_with_no_scale() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(reduceMotion: true);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        rig.Workspace.SplitterOf(PaneGutterKeys.Chat).Focus(NavigationMethod.Tab);
        rig.Key(Key.Space);
        rig.Layout();
        var frames = 0;
        while (rig.Workspace.IsMoving && frames < 60)
        {
            rig.Frame();
            frames++;
            Assert.Equal(1, rig.Card(PaneKind.Browser).Scale, 3);
        }
        Assert.InRange(frames, 9, 14); // 0.18 s at 60 Hz
        Assert.NotNull(rig.Workspace.RailOf(PaneGutterKeys.Chat));
    });

    [Fact]
    public void The_chat_never_draws_under_400_on_any_frame_of_a_fold_or_a_restore() => headless.OnUiThread(() =>
    {
        foreach (var reflow in new[] { ChatReflow.Live, ChatReflow.Clip })
        {
            using var rig = new PaneWorkspaceRig(reflow: reflow);
            rig.Workspace.Open(PaneKind.Browser);
            rig.Workspace.Open(PaneKind.SideChat);
            rig.Settle();
            foreach (var key in new[] { PaneGutterKeys.Side, PaneGutterKeys.Chat })
            {
                rig.Workspace.SplitterOf(key).Focus(NavigationMethod.Tab);
                for (var round = 0; round < 2; round++)
                {
                    rig.Key(Key.Space);
                    rig.Layout();
                    for (var i = 0; i < 80 && rig.Workspace.IsMoving; i++)
                    {
                        rig.Frame();
                        Assert.True(rig.Workspace.ChatBounds.Width >= PaneKinds.ChatMinWidth - 0.001, $"{reflow} {key}: the chat drew at {rig.Workspace.ChatBounds.Width}");
                        Assert.True(rig.ChatColumn.Bounds.Width >= PaneKinds.ChatMinWidth - 0.001, $"{reflow} {key}: the chat column laid out at {rig.ChatColumn.Bounds.Width}");
                    }
                    rig.Settle();
                }
            }
            Assert.True(rig.ChatColumn.Widths.Where(double.IsFinite).All(width => width >= PaneKinds.ChatMinWidth - 0.001), $"{reflow}: the chat column was measured under 400");
        }
    });

    [Fact]
    public void A_rail_icon_restores_the_side_to_its_size_before_the_fold_and_goes_to_that_pane() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Workspace.Open(PaneKind.Changes);
        rig.Settle();
        var gutter = rig.Expected().Gutter(PaneGutterKeys.Chat)!;
        // A size that is neither the default nor a limit.
        rig.Workspace.SplitterOf(PaneGutterKeys.Chat).Focus(NavigationMethod.Tab);
        rig.Key(Key.Right, RawInputModifiers.Shift);
        rig.Key(Key.Right, RawInputModifiers.Shift);
        rig.Settle();
        var width = rig.Workspace.ChatBounds.Width;
        Assert.NotEqual(gutter.DefaultValue, width, 1);
        var tiles = new Dictionary<PaneKind, Rect>
        {
            [PaneKind.Browser] = rig.Card(PaneKind.Browser).Bounds,
            [PaneKind.Changes] = rig.Card(PaneKind.Changes).Bounds,
        };
        Fold(rig);
        var rail = rig.Workspace.RailOf(PaneGutterKeys.Chat)!;
        Assert.Equal(2, rail.IconButtons.Count);

        PaneWorkspaceRig.Click(rail.IconButtons[1]); // the second icon: the second pane
        rig.Settle();
        Assert.Empty(rig.Workspace.State.Collapsed);
        Assert.Null(rig.Workspace.RailOf(PaneGutterKeys.Chat));
        Assert.Equal(width, rig.Workspace.ChatBounds.Width, 1);
        foreach (var (kind, tile) in tiles)
        {
            Assert.True(rig.Card(kind).IsVisible);
            Assert.Equal(tile.X, rig.Card(kind).Bounds.X, 1);
            Assert.Equal(tile.Width, rig.Card(kind).Bounds.Width, 1);
            Assert.Equal(1, rig.Card(kind).Opacity, 3);
        }
        Assert.NotNull(rig.Workspace.FocusedPane); // the clicked rail icon is gone: focus went to a pane
    });

    [Fact]
    public void The_chevron_and_a_double_click_on_the_rail_restore_too() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var width = rig.Workspace.ChatBounds.Width;
        Fold(rig);
        PaneWorkspaceRig.Click(rig.Workspace.RailOf(PaneGutterKeys.Chat)!.Chevron);
        rig.Settle();
        Assert.Empty(rig.Workspace.State.Collapsed);
        Assert.Equal(width, rig.Workspace.ChatBounds.Width, 1);

        Fold(rig);
        var rail = rig.Workspace.RailOf(PaneGutterKeys.Chat)!;
        var bounds = rig.Workspace.RailBounds(PaneGutterKeys.Chat)!.Value;
        var empty = new Point(bounds.Center.X, bounds.Y + (bounds.Height / 2)); // below the icons, above the chevron
        rig.Down(empty);
        rig.Up(empty);
        Assert.Contains(PaneGutterKeys.Chat, rig.Workspace.State.Collapsed); // one click is not a restore
        rig.Down(empty);
        rig.Up(empty);
        rig.Settle();
        Assert.Empty(rig.Workspace.State.Collapsed);
        Assert.Equal(width, rig.Workspace.ChatBounds.Width, 1);
        Assert.NotNull(rail);
    });

    [Fact]
    public void Space_and_Ctrl_Arrow_fold_and_restore_from_the_keyboard() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var width = rig.Workspace.ChatBounds.Width;
        var splitter = rig.Workspace.SplitterOf(PaneGutterKeys.Chat);
        Assert.Equal(SplitterSide.Trailing, splitter.CollapseSide);
        splitter.Focus(NavigationMethod.Tab);

        rig.Key(Key.Left, RawInputModifiers.Control); // away from the panes: nothing to restore
        rig.Settle();
        Assert.Empty(rig.Workspace.State.Collapsed);
        rig.Key(Key.Right, RawInputModifiers.Control); // toward the panes: folds them
        rig.Settle();
        Assert.Contains(PaneGutterKeys.Chat, rig.Workspace.State.Collapsed);
        rig.Key(Key.Right, RawInputModifiers.Control); // toward again: still folded
        rig.Settle();
        Assert.Contains(PaneGutterKeys.Chat, rig.Workspace.State.Collapsed);
        rig.Key(Key.Left, RawInputModifiers.Control); // away: restores
        rig.Settle();
        Assert.Empty(rig.Workspace.State.Collapsed);
        Assert.Equal(width, rig.Workspace.ChatBounds.Width, 1);

        rig.Key(Key.Space);
        rig.Settle();
        Assert.Contains(PaneGutterKeys.Chat, rig.Workspace.State.Collapsed);
        rig.Key(Key.Right); // an arrow does not move a folded side; Space, Ctrl+Arrow, Enter, Home and End bring it back
        rig.Settle();
        Assert.Contains(PaneGutterKeys.Chat, rig.Workspace.State.Collapsed);
        rig.Key(Key.Space);
        rig.Settle();
        Assert.Empty(rig.Workspace.State.Collapsed);
        Assert.Equal(width, rig.Workspace.ChatBounds.Width, 1);

        Fold(rig);
        rig.Key(Key.Home);
        rig.Settle();
        Assert.Empty(rig.Workspace.State.Collapsed);
        Assert.Equal(PaneKinds.ChatMinWidth, rig.Workspace.ChatBounds.Width, 1);
    });

    [Fact]
    public void A_side_that_sat_at_its_minimum_comes_back_at_the_default_not_cramped() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var gutter = rig.Expected().Gutter(PaneGutterKeys.Chat)!;
        var splitter = rig.Workspace.SplitterOf(PaneGutterKeys.Chat);
        splitter.Focus(NavigationMethod.Tab);
        rig.Key(Key.End); // the panes at their minimum
        rig.Settle();
        Assert.Equal(gutter.NormalMaximum, rig.Workspace.ChatBounds.Width, 1);
        Fold(rig);
        Assert.False(rig.Workspace.State.PreCollapse.ContainsKey(PaneGutterKeys.Chat), "nothing to remember from the minimum");
        rig.Key(Key.Space);
        rig.Settle();
        Assert.Equal(gutter.DefaultValue, rig.Workspace.ChatBounds.Width, 1);
    });

    [Fact]
    public void Dragging_back_out_restores_mid_drag_on_a_spring_and_tracking_resumes() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var before = rig.Workspace.ChatBounds.Width;
        var (from, gutter) = Press(rig);
        rig.Move(from + new Vector(Past(gutter, 70), 0));
        Assert.Contains(PaneGutterKeys.Chat, rig.Workspace.State.Collapsed);
        rig.Settle();
        Assert.NotNull(rig.Workspace.RailOf(PaneGutterKeys.Chat));

        // Back over the minimum: the side comes back on a spring to its size before the drag began.
        rig.Move(from + new Vector(Past(gutter, -5), 0));
        Assert.Empty(rig.Workspace.State.Collapsed);
        Assert.True(rig.Workspace.IsMoving);
        rig.Settle();
        Assert.Equal(before, rig.Workspace.ChatBounds.Width, 1);
        // The pointer goes on: the divider follows it one to one again.
        var target = gutter.Value + 50;
        rig.Move(from + new Vector(target - gutter.Value, 0));
        Assert.Equal(target, rig.Workspace.ChatBounds.Width, 1);
        rig.Up(from + new Vector(target - gutter.Value, 0));
        rig.Settle();
        Assert.Equal(target, rig.Workspace.ChatBounds.Width, 1);
        Assert.Empty(rig.Workspace.State.Collapsed);
    });

    [Fact]
    public void A_folded_gutter_reports_its_collapsed_position_inside_a_range_that_includes_it() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var splitter = rig.Workspace.SplitterOf(PaneGutterKeys.Chat);
        var range = Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(splitter));
        var text = Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(splitter));
        Assert.Equal(rig.Expected().Gutter(PaneGutterKeys.Chat)!.Maximum, range.Maximum, 3);
        Assert.NotEqual("Panes collapsed", text.Value);
        Fold(rig);
        Assert.Equal("Panes collapsed", text.Value);
        Assert.Equal(range.Maximum, range.Value, 3);
        Assert.InRange(range.Value, range.Minimum, range.Maximum);
        range.SetValue(range.Minimum + 100); // setting a value inside the range restores
        rig.Settle();
        Assert.Empty(rig.Workspace.State.Collapsed);
        Assert.Equal(range.Minimum + 100, rig.Workspace.ChatBounds.Width, 1);
        range.SetValue(range.Maximum); // the range's collapsed end folds
        rig.Settle();
        Assert.Contains(PaneGutterKeys.Chat, rig.Workspace.State.Collapsed);
    });

    [Fact]
    public void Side_chat_folds_to_its_own_rail_and_the_panes_widen() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Workspace.Open(PaneKind.SideChat);
        rig.Settle();
        var side = rig.Card(PaneKind.SideChat).Bounds;
        var visus = rig.Card(PaneKind.Browser).Bounds;
        var chat = rig.Workspace.ChatBounds.Width;
        Assert.Equal(SplitterSide.Trailing, rig.Workspace.SplitterOf(PaneGutterKeys.Side).CollapseSide);
        Fold(rig, PaneGutterKeys.Side);
        var rail = rig.Workspace.RailOf(PaneGutterKeys.Side)!;
        Assert.Equal([PaneKind.SideChat], rail.Panes);
        Assert.Null(rig.Workspace.RailOf(PaneGutterKeys.Chat));
        Assert.False(rig.Card(PaneKind.SideChat).IsVisible);
        Assert.Equal(chat, rig.Workspace.ChatBounds.Width, 1); // the chat keeps its width
        Assert.True(rig.Card(PaneKind.Browser).Bounds.Width > visus.Width + side.Width - 40, "the panes take Side chat's room");
        Assert.Equal(rig.Expected().Rail(PaneGutterKeys.Side)!.Bounds.X, rig.Workspace.RailBounds(PaneGutterKeys.Side)!.Value.X, 1);

        PaneWorkspaceRig.Click(rail.IconButtons[0]);
        rig.Settle();
        Assert.Empty(rig.Workspace.State.Collapsed);
        Assert.Equal(side.Width, rig.Card(PaneKind.SideChat).Bounds.Width, 1);
        Assert.Equal(visus.Width, rig.Card(PaneKind.Browser).Bounds.Width, 1);
    });

    [Fact]
    public void Opening_a_pane_while_the_panes_are_folded_restores_them() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        Fold(rig);
        rig.Workspace.Open(PaneKind.Changes);
        rig.Settle();
        Assert.Empty(rig.Workspace.State.Collapsed);
        Assert.True(rig.Card(PaneKind.Browser).IsVisible && rig.Card(PaneKind.Changes).IsVisible);
        Assert.Null(rig.Workspace.RailOf(PaneGutterKeys.Chat));
    });

    [Fact]
    public void A_rail_is_a_glass_strip_with_named_icons_that_the_keyboard_reaches() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Workspace.Open(PaneKind.BackgroundTasks);
        rig.Settle();
        Fold(rig);
        var rail = rig.Workspace.RailOf(PaneGutterKeys.Chat)!;
        Assert.Equal(22, rail.Width);
        Assert.Equal(11, rail.CornerRadius.TopLeft);
        Assert.Equal([PaneKind.BackgroundTasks, PaneKind.Browser], rail.Panes);
        Assert.All(rail.IconButtons, button =>
        {
            Assert.Equal(PaneRailStrip.ButtonWidth, button.Bounds.Width, 0.5);
            Assert.Equal(PaneRailStrip.ButtonHeight, button.Bounds.Height, 0.5);
        });
        Assert.Equal(["Restore Tasks", "Restore Visus"], rail.IconButtons.Select(AutomationProperties.GetName));
        Assert.Equal(["Tasks", "Visus"], rail.IconButtons.Select(button => (string?)ToolTip.GetTip(button)));
        Assert.Equal("Restore panes", AutomationProperties.GetName(rail.Chevron));
        Assert.Equal("Collapsed panes", AutomationProperties.GetName(rail));
        Assert.True(rail.IconButtons.All(button => button.Focusable));
        Assert.Equal(rig.Expected().Rail(PaneGutterKeys.Chat)!.Bounds.Height, rig.Workspace.RailBounds(PaneGutterKeys.Chat)!.Value.Height, 1);

        // Stacked from the top: 8 DIP padding, 22 DIP buttons, 4 DIP apart.
        var first = rig.BoundsOf(rail.IconButtons[0]);
        var second = rig.BoundsOf(rail.IconButtons[1]);
        var railBounds = rig.BoundsOf(rail);
        Assert.Equal(railBounds.Y + PaneRailStrip.TopPadding, first.Y, 1.5);
        Assert.Equal(first.Bottom + PaneRailStrip.IconGap, second.Y, 1.5);
    });

    [Fact]
    public void A_right_to_left_window_puts_the_rail_on_the_mirrored_side_and_the_keys_follow_the_eye() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Window.FlowDirection = FlowDirection.RightToLeft;
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var splitter = rig.Workspace.SplitterOf(PaneGutterKeys.Chat);
        splitter.Focus(NavigationMethod.Tab);
        rig.Key(Key.Left, RawInputModifiers.Control); // toward the panes in a mirrored window is the left
        rig.Settle();
        Assert.Contains(PaneGutterKeys.Chat, rig.Workspace.State.Collapsed);
        var rail = rig.Workspace.RailOf(PaneGutterKeys.Chat)!;
        var onScreen = rail.TranslatePoint(default, rig.Window)!.Value; // the window mirrors its subtree: x is read from the window
        Assert.True(onScreen.X < rig.Workspace.Bounds.Width / 2, "the rail sits at the window's left edge");
        rig.Key(Key.Right, RawInputModifiers.Control); // away
        rig.Settle();
        Assert.Empty(rig.Workspace.State.Collapsed);
    });

    [Fact]
    public void F6_and_Shift_F6_visit_the_rail_after_the_splitter() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        Fold(rig);
        rig.Window.FocusManager!.Focus(null);
        var stops = rig.Workspace.FocusStops();
        Assert.Equal(3, stops.Count);
        Assert.Same(rig.Workspace.Chat, stops[0]);
        Assert.Same(rig.Workspace.SplitterOf(PaneGutterKeys.Chat), stops[1]);
        Assert.Same(rig.Workspace.RailOf(PaneGutterKeys.Chat), stops[2]);
        rig.Workspace.CycleFocus(true);
        rig.Workspace.CycleFocus(true);
        rig.Workspace.CycleFocus(true);
        Assert.True(rig.Workspace.RailOf(PaneGutterKeys.Chat)!.IconButtons[0].IsKeyboardFocusWithin);
    });
}
