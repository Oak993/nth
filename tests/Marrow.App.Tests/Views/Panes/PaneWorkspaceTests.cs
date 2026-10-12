using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Input;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Views.Panes;

// nth web surfaces, unit W3a: the pane workspace host (work/design/web-surfaces/DESIGN.md 2.6, 3.1-3.4, 3.12, 4.2, 14
// "W3a done when: open, close, reflow springs, expand/restore and header overflow work at pane minimums"). Every end
// state is the solver's; every frame keeps the chat column at 400 DIP or more; springs bend when a new change comes;
// a resize and a drag snap; pressing a gutter settles everything in flight.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w3a-1")]
public sealed class PaneWorkspaceTests(HeadlessSession headless)
{
    private static readonly PaneKind[] Panes =
    [
        PaneKind.Browser, PaneKind.Artifacts, PaneKind.App, PaneKind.Terminal, PaneKind.BackgroundTasks,
        PaneKind.Changes, PaneKind.Transcript, PaneKind.SideChat,
    ];

    // Layout rounding puts every edge on a device pixel, so a drawn rectangle is within a pixel of the solver's.
    private static void Near(Rect expected, Rect actual, string context, double tolerance = 1)
    {
        Assert.True(Math.Abs(expected.X - actual.X) <= tolerance && Math.Abs(expected.Y - actual.Y) <= tolerance
            && Math.Abs(expected.Width - actual.Width) <= tolerance && Math.Abs(expected.Height - actual.Height) <= tolerance,
            $"{context}: expected {expected}, got {actual}");
    }

    private static void AssertLanded(PaneWorkspaceRig rig, string context)
    {
        Assert.False(rig.Workspace.IsMoving, $"{context}: still moving");
        var expected = rig.Expected();
        var panes = expected.Shown.Any(kind => kind != PaneKind.Chat);
        Assert.Equal(panes ? expected.Chat.Width : rig.Workspace.Bounds.Width, rig.Workspace.ChatBounds.Width, 6);
        Assert.True(Math.Abs(rig.Workspace.ChatBounds.Width - rig.Workspace.Chat.Bounds.Width) <= 1, $"{context}: the chat slot drew {rig.Workspace.Chat.Bounds.Width}");
        foreach (var kind in Panes)
        {
            if (!expected.IsShown(kind) || expected.Expanded is not null) continue;
            var card = rig.Card(kind);
            Near(rig.ExpectedBounds(kind), card.Bounds, $"{context}: {kind}");
            Assert.Equal(1, card.Opacity, 3);
            Assert.Equal(1, card.Scale, 3);
        }
    }

    [Fact]
    public void Every_pane_opens_to_the_solver_layout_and_closes_back_to_the_full_chat() => headless.OnUiThread(() =>
    {
        foreach (var kind in Panes)
        {
            using var rig = new PaneWorkspaceRig(state: PaneWorkspaceState.Default with { TerminalPrefersGrid = true });
            Assert.Equal(1400, rig.Workspace.ChatBounds.Width, 2);
            rig.Workspace.Open(kind);
            rig.Layout();
            Assert.True(rig.Workspace.IsMoving, $"{kind}: opening springs");
            rig.Settle();
            Assert.True(rig.Workspace.State.IsOpen(kind));
            AssertLanded(rig, $"{kind} open");
            Assert.Equal(PaneKinds.Title(kind), rig.Card(kind).Header.TitleBlock.Inlines!.OfType<Avalonia.Controls.Documents.Run>().First().Text);
            rig.Workspace.Close(kind);
            rig.Layout();
            rig.Settle();
            Assert.Null(rig.Workspace.CardOf(kind));
            AssertLanded(rig, $"{kind} closed");
            Assert.Equal(2, rig.Changes.Count);
        }
    });

    [Fact]
    public void The_chat_column_never_draws_under_400_on_any_frame_of_any_change() => headless.OnUiThread(() =>
    {
        foreach (var reflow in new[] { ChatReflow.Live, ChatReflow.Clip })
        {
            using var rig = new PaneWorkspaceRig(width: 1180, reflow: reflow);
            var steps = new Action[]
            {
                () => rig.Workspace.Open(PaneKind.Browser),
                () => rig.Workspace.Open(PaneKind.Artifacts),
                () => rig.Workspace.Open(PaneKind.SideChat),
                () => rig.Workspace.Close(PaneKind.Browser),
                () => rig.Workspace.Open(PaneKind.Changes),
                () => rig.Workspace.Close(PaneKind.SideChat),
            };
            foreach (var step in steps)
            {
                step();
                rig.Layout();
                for (var i = 0; i < 4; i++)
                {
                    rig.Frame();
                    Assert.True(rig.Workspace.ChatBounds.Width >= PaneKinds.ChatMinWidth - 1e-6, $"{reflow}: the chat drew {rig.Workspace.ChatBounds.Width}");
                    Assert.True(rig.Workspace.Chat.Bounds.Width >= PaneKinds.ChatMinWidth - 1e-6, $"{reflow}: the chat slot drew {rig.Workspace.Chat.Bounds.Width}");
                    Assert.True(rig.ChatColumn.Bounds.Width >= PaneKinds.ChatMinWidth - 1e-6, $"{reflow}: the chat column was laid out at {rig.ChatColumn.Bounds.Width}");
                }
            }
            while (rig.Workspace.IsMoving || rig.Frames.Pending > 0)
            {
                rig.Frame();
                Assert.True(rig.Workspace.ChatBounds.Width >= PaneKinds.ChatMinWidth - 1e-6);
            }
            rig.Settle();
            AssertLanded(rig, $"{reflow} after the sequence");
            Assert.True(rig.ChatColumn.Widths.Where(double.IsFinite).All(width => width >= PaneKinds.ChatMinWidth - 1e-6));
        }
    });

    [Fact]
    public void An_opening_pane_fades_in_from_0_96_and_a_closing_one_fades_toward_0_96_then_leaves() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Layout();
        var card = rig.Card(PaneKind.Browser);
        Assert.Equal(0, card.Opacity, 3);
        Assert.Equal(PaneWorkspace.OpenScale, card.Scale, 3);
        Near(rig.ExpectedBounds(PaneKind.Browser), card.Bounds, "an opening pane is at its final rectangle from the first frame");
        double opacity = card.Opacity, scale = card.Scale;
        var frames = 0;
        while (rig.Workspace.IsMoving)
        {
            rig.Frame();
            frames++;
            // One way, up to the spring's settling ripple (damping 0.9 overshoots by 0.15 %, clamped at 1).
            Assert.True(card.Opacity >= opacity - 1e-3 && card.Scale >= scale - 1e-3, "the fade and the growth run one way");
            opacity = card.Opacity;
            scale = card.Scale;
        }
        Assert.InRange(frames, 12, 60); // the panel slide (response 320 ms) and its sub-pixel tail, at 60 Hz
        Assert.Equal(1, card.Opacity, 3);
        Assert.Equal(1, card.Scale, 3);

        rig.Workspace.Close(PaneKind.Browser);
        rig.Layout();
        Assert.Same(card, rig.Workspace.CardOf(PaneKind.Browser));
        Assert.True(card.IsLeaving);
        Assert.False(card.IsHitTestVisible);
        while (rig.Workspace.IsMoving)
        {
            rig.Frame();
            Assert.True(card.Opacity <= opacity + 1e-3 && card.Scale <= scale + 1e-3, "the fade out runs one way");
            opacity = card.Opacity;
            scale = card.Scale;
        }
        Assert.Equal(PaneWorkspace.OpenScale, card.Scale, 2);
        rig.Settle();
        Assert.Null(rig.Workspace.CardOf(PaneKind.Browser));
        Assert.DoesNotContain(card, rig.Workspace.Children);
    });

    [Fact]
    public void Under_Reduce_Motion_panes_fade_without_scaling_and_land_in_0_18_s() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(reduceMotion: true);
        rig.Workspace.Open(PaneKind.Artifacts);
        rig.Layout();
        var card = rig.Card(PaneKind.Artifacts);
        var frames = 0;
        while (rig.Workspace.IsMoving)
        {
            Assert.Equal(1, card.Scale, 6);
            rig.Frame();
            frames++;
        }
        Assert.InRange(frames, 10, 12); // 0.18 s at 60 Hz
        AssertLanded(rig, "reduced");
        rig.Workspace.Close(PaneKind.Artifacts);
        rig.Layout();
        while (rig.Workspace.IsMoving)
        {
            Assert.Equal(1, card.Scale, 6);
            rig.Frame();
        }
        rig.Settle();
        Assert.Null(rig.Workspace.CardOf(PaneKind.Artifacts));
    });

    [Fact]
    public void A_new_change_mid_spring_bends_the_motion_from_where_it_is_instead_of_restarting() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Layout();
        var widths = new List<double>();
        for (var i = 0; i < 4; i++)
        {
            rig.Frame();
            widths.Add(rig.Workspace.ChatBounds.Width);
        }
        Assert.True(widths[^1] < widths[^2], "the chat is shrinking");
        rig.Workspace.Close(PaneKind.Browser);
        rig.Layout();
        Assert.Equal(widths[^1], rig.Workspace.ChatBounds.Width, 6); // no jump when the target changes
        rig.Frame();
        var after = rig.Workspace.ChatBounds.Width;
        Assert.True(after < widths[^1], "the spring keeps its velocity for a moment, so the motion bends rather than restarting");
        Assert.True(widths[^1] - after < widths[^2] - widths[^1], "and it is slowing down");
        var previous = after;
        while (rig.Workspace.IsMoving)
        {
            rig.Frame();
            Assert.True(Math.Abs(rig.Workspace.ChatBounds.Width - previous) < 120, "no frame jumps");
            previous = rig.Workspace.ChatBounds.Width;
        }
        rig.Settle();
        AssertLanded(rig, "after the bend");
        Assert.Equal(1400, rig.Workspace.ChatBounds.Width, 2);
    });

    [Fact]
    public void A_window_resize_snaps_to_the_new_layout_without_springs() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Workspace.Open(PaneKind.Changes);
        rig.Settle();
        rig.Window.Width = 1100;
        rig.Window.Height = 760;
        rig.Layout();
        Assert.Equal(1100, rig.Workspace.Bounds.Width, 1);
        AssertLanded(rig, "resized");
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    public void Pressing_a_gutter_settles_every_spring_in_flight_in_that_frame() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        rig.Workspace.Open(PaneKind.Artifacts);
        rig.Layout();
        rig.Frame();
        rig.Frame();
        Assert.True(rig.Workspace.IsMoving);
        var centre = rig.SplitterCentre(PaneGutterKeys.Chat);
        rig.Down(centre);
        Assert.False(rig.Workspace.IsMoving);
        AssertLanded(rig, "after the press");
        rig.Up(centre);
    });

    [Fact]
    public void A_gutter_drag_tracks_the_pointer_one_to_one_and_commits_once_at_release() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var start = rig.Workspace.ChatBounds.Width;
        var changes = rig.Changes.Count;
        var centre = rig.SplitterCentre(PaneGutterKeys.Chat);
        rig.Down(centre);
        foreach (var travel in new[] { 3.0, 40, 120, -60, -150 })
        {
            rig.Move(centre + new Vector(travel, 0));
            Assert.False(rig.Workspace.IsMoving, "no spring while the pointer is down");
            Assert.True(rig.Resize.IsWorkspaceResizing, "the chat reshapes as under a sidebar drag");
            Assert.Equal(start + travel, rig.Workspace.ChatBounds.Width, 1);
            Assert.Equal(start + travel + PaneKinds.Gap, rig.Card(PaneKind.Browser).Bounds.X, 1);
            Assert.True(rig.Card(PaneKind.Browser).Header.IsHeld);
        }
        // Past the chat's minimum it clamps (the rubber band and the collapse are W3b's).
        rig.Move(centre + new Vector(PaneKinds.ChatMinWidth - start - 90, 0));
        Assert.Equal(PaneKinds.ChatMinWidth, rig.Workspace.ChatBounds.Width, 1);
        Assert.Equal(changes, rig.Changes.Count);
        rig.Up(centre + new Vector(PaneKinds.ChatMinWidth - start - 90, 0));
        Assert.Equal(changes + 1, rig.Changes.Count);
        Assert.False(rig.Resize.IsWorkspaceResizing);
        Assert.False(rig.Card(PaneKind.Browser).Header.IsHeld);
        AssertLanded(rig, "after the drag");
    });

    [Fact]
    public void Keyboard_steps_spring_and_the_automation_value_stays_inside_the_gutter_range() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var splitter = rig.Workspace.SplitterOf(PaneGutterKeys.Chat);
        var gutter = rig.Expected().Gutter(PaneGutterKeys.Chat)!;
        Assert.Equal(gutter.Label, splitter.Label);
        var start = rig.Workspace.ChatBounds.Width;
        splitter.Focus(NavigationMethod.Tab);
        rig.Key(Key.Right);
        Assert.True(rig.Workspace.IsMoving, "a key step springs");
        rig.Settle();
        Assert.Equal(start + SplitterLimits.DefaultStep, rig.Workspace.ChatBounds.Width, 1);
        rig.Key(Key.Left, RawInputModifiers.Shift);
        rig.Settle();
        Assert.Equal(start + SplitterLimits.DefaultStep - SplitterLimits.DefaultLargeStep, rig.Workspace.ChatBounds.Width, 1);
        var range = Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(splitter));
        Assert.Equal(gutter.NormalMinimum, range.Minimum, 3);
        Assert.Equal(gutter.NormalMaximum, range.Maximum, 3);
        Assert.InRange(range.Value, range.Minimum, range.Maximum);
        rig.Key(Key.Home);
        rig.Settle();
        Assert.Equal(PaneKinds.ChatMinWidth, rig.Workspace.ChatBounds.Width, 1);
        Assert.Equal(range.Minimum, range.Value, 3);
        rig.Key(Key.End);
        rig.Settle();
        Assert.Equal(range.Maximum, range.Value, 3);
        Assert.Equal(range.Maximum, rig.Workspace.ChatBounds.Width, 1);
        rig.Key(Key.Enter);
        rig.Settle();
        Assert.Equal(gutter.DefaultValue, rig.Workspace.ChatBounds.Width, 1);
        range.SetValue(10_000);
        rig.Settle();
        Assert.InRange(range.Value, range.Minimum, range.Maximum);
        Assert.Equal(7, rig.Changes.Count); // the open, then one per key and the set value
        AssertLanded(rig, "after the keys");
    });

    [Fact]
    public void Expand_springs_a_pane_over_the_workspace_and_Esc_in_it_restores() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Workspace.Open(PaneKind.Changes);
        rig.Settle();
        var tile = rig.Card(PaneKind.Browser).Bounds;
        var chat = rig.Workspace.ChatBounds;
        PaneWorkspaceRig.Click(rig.Card(PaneKind.Browser).Header.ExpandButton);
        rig.Layout();
        Assert.Equal(PaneKind.Browser, rig.Workspace.State.Expanded);
        Assert.All(PaneGutterKeys.All, key => Assert.False(rig.Workspace.SplitterOf(key).IsVisible));
        Assert.True(rig.Workspace.IsMoving);
        Assert.Equal(10, rig.Card(PaneKind.Browser).ZIndex);
        rig.Settle();
        var card = rig.Card(PaneKind.Browser);
        Near(rig.Area, card.Bounds, "expanded");
        Assert.True(card.Header.IsExpanded);
        Assert.True(card.IsExpandedLook);
        Near(chat, rig.Workspace.ChatBounds, "the chat does not reflow under an expanded pane", 0.5);

        rig.Workspace.IsChatWorking = true;
        Assert.True(card.Header.ShowsWorking);
        Assert.False(rig.Card(PaneKind.Changes).Header.ShowsWorking);

        rig.Bodies[PaneKind.Browser].Focus();
        rig.Key(Key.Escape);
        Assert.Null(rig.Workspace.State.Expanded);
        Assert.False(card.Header.ShowsWorking);
        rig.Settle();
        Near(tile, card.Bounds, "restored");
        Assert.False(card.IsExpandedLook);
        Assert.Equal(0, card.ZIndex);
        Assert.True(rig.Workspace.SplitterOf(PaneGutterKeys.Chat).IsVisible);
        Assert.Equal(4, rig.Changes.Count);
    });

    [Fact]
    public void The_working_chip_restores_the_expanded_pane_and_returns_focus_to_the_chat() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Artifacts);
        rig.Workspace.ToggleExpanded(PaneKind.Artifacts);
        rig.Settle();
        rig.Workspace.IsChatWorking = true;
        rig.Layout();
        var header = rig.Card(PaneKind.Artifacts).Header;
        Assert.True(header.WorkingChip.IsVisible);
        PaneWorkspaceRig.Click(header.WorkingChip);
        rig.Settle();
        Assert.Null(rig.Workspace.State.Expanded);
        Assert.Same(rig.ChatColumn.Composer, rig.Focused);
    });

    [Fact]
    public void Closing_the_focused_pane_gives_focus_to_the_chat() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Changes);
        rig.Settle();
        rig.Bodies[PaneKind.Changes].Focus();
        rig.Layout();
        Assert.True(rig.Card(PaneKind.Changes).IsFocusedLook);
        PaneWorkspaceRig.Click(rig.Card(PaneKind.Changes).Header.CloseButton);
        rig.Layout();
        Assert.Same(rig.ChatColumn.Composer, rig.Focused);
        Assert.False(rig.Workspace.State.IsOpen(PaneKind.Changes));
    });

    [Fact]
    public void A_cell_with_tabs_shows_one_pane_and_a_tab_choice_swaps_at_once() => headless.OnUiThread(() =>
    {
        var state = PaneWorkspaceState.Default.Open(PaneKind.Browser).Open(PaneKind.Artifacts).JoinAsTab(PaneKind.Artifacts, PaneKind.Browser);
        using var rig = new PaneWorkspaceRig(state: state);
        var cell = Assert.Single(rig.Expected().Cells);
        Assert.True(cell.HasTabs);
        var shown = rig.Card(cell.Shown);
        var hidden = rig.Card(cell.Tabs.Single(kind => kind != cell.Shown));
        Assert.True(shown.IsVisible);
        Assert.False(hidden.IsVisible);
        Assert.True(shown.Header.TabStrip.IsVisible);
        var tab = shown.Header.TabStrip.Children.OfType<Avalonia.Controls.Button>().Single(button => !button.Classes.Contains("chosen"));
        PaneWorkspaceRig.Click(tab);
        rig.Layout();
        Assert.True(hidden.IsVisible);
        Assert.False(shown.IsVisible);
        Assert.Equal(1, hidden.Opacity, 6); // a tab swap is not an opening
        Near(rig.ExpectedBounds(hidden.Kind), hidden.Bounds, "the chosen tab");
        Assert.Single(rig.Changes);
    });

    [Fact]
    public void A_pane_this_build_cannot_draw_is_never_shown_and_leaves_the_chat_its_room() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(contents: kind => kind == PaneKind.App ? null : new PaneContent(new Avalonia.Controls.Border(), "Pane"));
        rig.Workspace.Open(PaneKind.App);
        rig.Settle();
        Assert.True(rig.Workspace.State.IsOpen(PaneKind.App)); // the thread's state keeps it for a build that has it
        Assert.Null(rig.Workspace.CardOf(PaneKind.App));
        Assert.Equal(1400, rig.Workspace.ChatBounds.Width, 2);
        rig.Workspace.Open(PaneKind.Changes);
        rig.Settle();
        Assert.NotNull(rig.Workspace.CardOf(PaneKind.Changes));
        Assert.False(rig.Workspace.Layout!.IsShown(PaneKind.App));
    });

    [Fact]
    public void Headers_keep_their_buttons_while_anything_moves_and_refit_once_it_settles() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var header = rig.Card(PaneKind.Browser).Header;
        Assert.False(header.IsHeld);
        rig.Workspace.Open(PaneKind.SideChat);
        rig.Layout();
        rig.Frame();
        Assert.True(header.IsHeld);
        Assert.True(rig.Card(PaneKind.SideChat).Header.IsHeld);
        rig.Settle();
        Assert.False(header.IsHeld);
        Assert.Equal(header.Bounds.Width, header.FitWidth, 1);
    });

    [Fact]
    public void The_chat_chrome_row_keeps_clear_of_the_caption_buttons_only_while_it_runs_under_them() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig();
        Assert.True(double.IsNaN(ChatView.GetChromeTrailingReserve(rig.ChatColumn)));
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var chatRight = rig.Workspace.ChatBounds.Right;
        var expected = WindowChrome.CaptionTrailingReserve - (rig.Workspace.Bounds.Width - chatRight);
        Assert.Equal(expected, ChatView.GetChromeTrailingReserve(rig.ChatColumn), 2);
        Assert.Equal(Chrome.ChromeHorizontalPadding, ChatView.ChromeTrailingFor(expected), 3);
        rig.Workspace.Close(PaneKind.Browser);
        rig.Settle();
        Assert.True(double.IsNaN(ChatView.GetChromeTrailingReserve(rig.ChatColumn)));
        Assert.Equal(WindowChrome.CaptionTrailingReserve, ChatView.ChromeTrailingFor(double.NaN), 3);
    });
}
