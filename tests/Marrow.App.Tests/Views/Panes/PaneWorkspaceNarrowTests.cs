using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Panes;
using Marrow.UI.Web;

namespace Marrow.App.Tests.Views.Panes;

// nth web surfaces, unit W3c (work/design/web-surfaces/DESIGN.md 3.8 "Narrow window", 13.3 "narrow tabs"): below the width the chat
// and one pane need side by side, the workspace shows one surface at a time under a glass tab strip in the title band (Chat, then
// the open panes in order). A switch cross-fades with an 8 DIP slide on interruptible springs, opening a pane chooses its tab,
// closing it returns to the chat, the surface not chosen is out of the layout and the accessibility tree, and the strip is a
// tab list for the keyboard and for assistive technology.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w3c-2")]
public sealed class PaneWorkspaceNarrowTests(HeadlessSession headless)
{
    private const double NarrowWidth = 600;

    private static double Slide(Control control) => ((TranslateTransform)control.RenderTransform!).X;

    private static double CardSlide(PaneCard card) => card.Slide;

    private static PaneWorkspaceRig NarrowRig(PaneKind open = PaneKind.BackgroundTasks, double width = NarrowWidth, bool reduceMotion = false, ChatReflow reflow = ChatReflow.Live)
    {
        var rig = new PaneWorkspaceRig(width: width, height: 800, reduceMotion: reduceMotion, reflow: reflow);
        rig.Workspace.Open(open);
        rig.Settle();
        return rig;
    }

    [Fact]
    public void Below_the_narrow_threshold_the_workspace_shows_one_surface_under_a_tab_strip() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(width: NarrowWidth, height: 800);
        Assert.False(rig.Workspace.IsNarrow);
        Assert.False(rig.Workspace.NarrowStrip.IsVisible);
        Assert.True(rig.Workspace.Chat.IsVisible);
        Assert.Equal(0, rig.Workspace.ChatBounds.Y);

        rig.Workspace.Open(PaneKind.BackgroundTasks); // 400 + 8 + 280 = 688 DIP are needed side by side
        rig.Settle();
        var strip = rig.Workspace.NarrowStrip;
        Assert.True(rig.Workspace.IsNarrow);
        Assert.Equal(PaneKind.BackgroundTasks, rig.Workspace.NarrowSurface);
        Assert.True(strip.IsVisible);
        Assert.Equal(1, strip.Opacity, 3);
        Assert.Equal(new[] { PaneKind.Chat, PaneKind.BackgroundTasks }, strip.Kinds);
        Assert.Equal(PaneKind.BackgroundTasks, strip.Selected);
        Assert.Equal(new[] { "Chat", "Tasks" }, strip.TabButtons.Select(button => ((TextBlock)((StackPanel)((Border)button.Content!).Child!).Children[1]).Text).ToArray());

        // One surface: the pane fills the solver's area; the chat is out of the layout.
        Assert.False(rig.Workspace.Chat.IsVisible);
        Assert.True(rig.Card(PaneKind.BackgroundTasks).IsVisible);
        Assert.Equal(1, rig.Card(PaneKind.BackgroundTasks).Opacity, 3);
        Assert.Equal(0, rig.Card(PaneKind.BackgroundTasks).Slide, 3);
        var area = rig.Area;
        var card = rig.Card(PaneKind.BackgroundTasks).Bounds;
        Assert.True(Math.Abs(card.X - area.X) <= 1 && Math.Abs(card.Y - area.Y) <= 1 && Math.Abs(card.Width - area.Width) <= 1 && Math.Abs(card.Height - area.Height) <= 1, $"{card} vs {area}");
        Assert.False(rig.Card(PaneKind.BackgroundTasks).Header.CanExpand);
        Assert.False(rig.Card(PaneKind.BackgroundTasks).IsExpandedLook);

        // The strip stands in the title band, clear of the caption buttons and centred in the room it has.
        var room = rig.Workspace.StripBounds;
        Assert.True(room.Right <= NarrowWidth - WindowChrome.CaptionTrailingReserve + 1e-6);
        Assert.True(room.Bottom <= PaneWorkspace.TopBand);
        var pill = strip.Pill.Bounds;
        Assert.Equal(NarrowTabStrip.PillHeight, pill.Height, 3);
        var pillRect = new Rect(strip.TranslatePoint(pill.Position, rig.Workspace) ?? default, pill.Size);
        Assert.True(pillRect.Left >= room.Left - 1 && pillRect.Right <= room.Right + 1);
        Assert.InRange(pillRect.Center.X, room.Center.X - 1, room.Center.X + 1);
        Assert.InRange(pillRect.Center.Y, room.Center.Y - 1, room.Center.Y + 1);
        // The pane's own header is the card's: the strip does not touch it.
        Assert.True(rig.Card(PaneKind.BackgroundTasks).Header.CloseButton.IsVisible);
    });

    [Fact]
    public void The_chat_column_starts_below_the_title_band_in_narrow_mode_and_at_the_top_otherwise() => headless.OnUiThread(() =>
    {
        using var rig = NarrowRig();
        rig.Workspace.Select(PaneKind.Chat);
        rig.Settle();
        Assert.True(rig.Workspace.Chat.IsVisible);
        Assert.Equal(WindowChrome.TitlebarHeight, rig.Workspace.ChatBounds.Y, 3);
        Assert.Equal(800 - WindowChrome.TitlebarHeight, rig.Workspace.ChatBounds.Height, 3);
        Assert.Equal(NarrowWidth, rig.Workspace.ChatBounds.Width, 3);
        Assert.Equal(WindowChrome.TitlebarHeight, rig.Workspace.Chat.Bounds.Y, 1);
        // Nothing runs under the caption buttons there: the chat's chrome row keeps only its own padding.
        Assert.Equal(0, ChatView.GetChromeTrailingReserve(rig.Workspace.Chat), 3);
        rig.Workspace.Close(PaneKind.BackgroundTasks);
        rig.Settle();
        Assert.False(rig.Workspace.IsNarrow);
        Assert.Equal(0, rig.Workspace.ChatBounds.Y, 3);
        Assert.Equal(800, rig.Workspace.ChatBounds.Height, 3);
        Assert.True(double.IsNaN(ChatView.GetChromeTrailingReserve(rig.Workspace.Chat)), "the whole window's width again: the chrome row keeps clear of the caption buttons");
    });

    [Fact]
    public void Choosing_a_tab_cross_fades_with_an_8_DIP_slide_and_the_old_surface_leaves_the_layout() => headless.OnUiThread(() =>
    {
        using var rig = NarrowRig();
        var tasks = rig.Card(PaneKind.BackgroundTasks);
        var strip = rig.Workspace.NarrowStrip;
        var changes = rig.Changes.Count;

        PaneWorkspaceRig.Click(strip.TabButtons[0]); // Chat: an earlier tab
        rig.Layout();
        Assert.Equal(PaneKind.Chat, rig.Workspace.State.NarrowSelection);
        Assert.Equal(changes + 1, rig.Changes.Count);
        Assert.True(rig.Workspace.IsMoving);
        var chat = rig.Workspace.Chat;
        Assert.True(chat.IsVisible && tasks.IsVisible, "both are drawn while they cross-fade");
        Assert.False(chat.IsHitTestVisible && tasks.IsHitTestVisible, "only one takes the pointer");
        Assert.True(chat.IsHitTestVisible);

        double chatOpacity = chat.Opacity, tasksOpacity = tasks.Opacity, chatSlide = Slide(chat), tasksSlide = tasks.Slide;
        Assert.Equal(-PaneWorkspace.NarrowSlide, chatSlide, 1); // comes in from the start side
        Assert.Equal(1, tasksOpacity, 1);
        var frames = 0;
        var sawMiddle = false;
        while (rig.Workspace.IsMoving && frames < 200)
        {
            rig.Frame();
            frames++;
            if (!tasks.IsVisible) break; // the old surface has left the layout: nothing of it is drawn any more
            // The spring (damping 0.9) overshoots its rest by under 0.2 %, which is not a turn.
            Assert.True(chat.Opacity >= chatOpacity - 0.01, "the chat only fades in");
            Assert.True(tasks.Opacity <= tasksOpacity + 0.01, "the old surface only fades out");
            Assert.True(Math.Abs(Slide(chat)) <= PaneWorkspace.NarrowSlide + 0.5 && Math.Abs(tasks.Slide) <= PaneWorkspace.NarrowSlide + 0.5, "the slide stays within 8 DIP");
            // The spring (damping 0.9) may overshoot its rest by a fraction of a DIP; it never turns back by more.
            Assert.True(Math.Abs(Slide(chat)) <= Math.Abs(chatSlide) + 0.25, "the chat slides toward rest");
            Assert.True(tasks.Slide >= tasksSlide - 0.25, "the old surface leaves toward the end side");
            sawMiddle |= chat.Opacity is > 0.2 and < 0.8 && tasks.Opacity is > 0.2 and < 0.8;
            chatOpacity = chat.Opacity;
            tasksOpacity = tasks.Opacity;
            chatSlide = Slide(chat);
            tasksSlide = tasks.Slide;
        }
        Assert.True(sawMiddle, "the two are both partly visible at once, as a cross-fade is");
        rig.Settle();
        Assert.InRange(frames, 8, 60);
        Assert.False(tasks.IsVisible, "the old surface leaves the layout (and the accessibility tree)");
        Assert.True(chat.IsVisible);
        Assert.Equal(1, chat.Opacity, 3);
        Assert.Equal(0, Slide(chat), 3);
        Assert.Equal(WindowChrome.TitlebarHeight, rig.Workspace.ChatBounds.Y, 3);
        Assert.Equal(PaneKind.Chat, rig.Workspace.NarrowSurface);
        Assert.Equal(PaneKind.Chat, strip.Selected);

        // And back: the pane comes in from the end side.
        PaneWorkspaceRig.Click(strip.TabButtons[1]);
        rig.Layout();
        Assert.Equal(PaneWorkspace.NarrowSlide, tasks.Slide, 1);
        Assert.Equal(0, tasks.Opacity, 1);
        rig.Settle();
        Assert.True(tasks.IsVisible);
        Assert.False(chat.IsVisible);
        Assert.Equal(1, tasks.Opacity, 3);
        Assert.Equal(0, tasks.Slide, 3);
    });

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
    public void A_tab_switch_over_a_web_pane_shows_its_snapshot_first_and_the_fade_waits_for_the_capture() => headless.OnUiThread(() =>
    {
        var surface = new PaneWebSurface();
        WebViewHost? host = null;
        PaneContent? Contents(PaneKind kind)
        {
            Control body;
            if (kind == PaneKind.Browser)
            {
                host = new WebViewHost(surface, _ => new Border { Focusable = true, Background = Brushes.Red });
                body = host;
            }
            else body = new Border { Child = new TextBox { Text = kind.ToString() } };
            return new PaneContent(body, PaneKinds.Title(kind));
        }

        using var rig = new PaneWorkspaceRig(width: NarrowWidth, height: 800, contents: Contents);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        PumpUntil(() => host!.State == WebViewHostState.Live);
        surface.HeldGesture = new TaskCompletionSource<Marrow.Core.Web.WebGestureFrame>();
        var browser = rig.Card(PaneKind.Browser);

        PaneWorkspaceRig.Click(rig.Workspace.NarrowStrip.TabButtons[0]); // the chat: the page is about to fade
        rig.Layout();
        PumpUntil(() => surface.Captures == 1);
        for (var i = 0; i < 3; i++) rig.Frame();
        Assert.Equal(1, browser.Opacity, 3); // the capture is held: nothing fades over a live page
        Assert.Equal(0, rig.Workspace.Chat.Opacity, 3);
        Assert.False(host!.IsShowingSnapshot);

        surface.HeldGesture.SetResult(new Marrow.Core.Web.WebGestureFrame(surface.LastSnapshot, null));
        PumpUntil(() => host.IsShowingSnapshot && rig.Frames.Pending > 0); // the captures are in: the springs ask for their frames
        rig.Frame();
        rig.Frame();
        Assert.True(browser.Opacity < 1 && rig.Workspace.Chat.Opacity > 0, "the fade runs over the snapshot");
        rig.Settle();
        Assert.False(browser.IsVisible);
        PumpUntil(() => !host.IsShowingSnapshot);
        Assert.Equal(WebViewHostState.Live, host.State);
        Assert.Equal(1, surface.Captures);
    });

    [Fact]
    public void A_switch_can_be_turned_back_mid_fade_without_a_jump() => headless.OnUiThread(() =>
    {
        using var rig = NarrowRig();
        var tasks = rig.Card(PaneKind.BackgroundTasks);
        var chat = rig.Workspace.Chat;
        PaneWorkspaceRig.Click(rig.Workspace.NarrowStrip.TabButtons[0]);
        rig.Layout();
        for (var i = 0; i < 4; i++) rig.Frame();
        double chatOpacity = chat.Opacity, tasksOpacity = tasks.Opacity, tasksSlide = tasks.Slide;
        Assert.InRange(chatOpacity, 0.05, 0.95);
        PaneWorkspaceRig.Click(rig.Workspace.NarrowStrip.TabButtons[1]); // back to the pane while both are partly shown
        rig.Layout();
        Assert.Equal(chatOpacity, chat.Opacity, 3);
        Assert.Equal(tasksOpacity, tasks.Opacity, 3);
        Assert.Equal(tasksSlide, tasks.Slide, 3);
        rig.Frame();
        Assert.True(Math.Abs(chat.Opacity - chatOpacity) < 0.5 && Math.Abs(tasks.Opacity - tasksOpacity) < 0.5, "no frame jumps");
        rig.Settle();
        Assert.True(tasks.IsVisible);
        Assert.False(chat.IsVisible);
        Assert.Equal(1, tasks.Opacity, 3);
        Assert.Equal(0, tasks.Slide, 3);
        Assert.Equal(PaneKind.BackgroundTasks, rig.Workspace.NarrowSurface);
    });

    [Fact]
    public void Under_Reduce_Motion_a_switch_fades_in_0_18_s_with_no_slide() => headless.OnUiThread(() =>
    {
        using var rig = NarrowRig(reduceMotion: true);
        var tasks = rig.Card(PaneKind.BackgroundTasks);
        var chat = rig.Workspace.Chat;
        PaneWorkspaceRig.Click(rig.Workspace.NarrowStrip.TabButtons[0]);
        rig.Layout();
        var frames = 0;
        while (rig.Workspace.IsMoving && frames < 100)
        {
            rig.Frame();
            frames++;
            Assert.Equal(0, Slide(chat), 6);
            Assert.Equal(0, tasks.Slide, 6);
            Assert.Equal(1, tasks.Scale, 6);
        }
        Assert.InRange(frames, 9, 14); // 0.18 s at 60 Hz
        rig.Settle();
        Assert.True(chat.IsVisible);
        Assert.False(tasks.IsVisible);
    });

    [Fact]
    public void Opening_a_pane_chooses_its_tab_and_closing_it_returns_to_the_chat() => headless.OnUiThread(() =>
    {
        using var rig = NarrowRig();
        rig.Workspace.Select(PaneKind.Chat);
        rig.Settle();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Layout();
        Assert.Equal(PaneKind.Browser, rig.Workspace.State.NarrowSelection);
        rig.Settle();
        Assert.Equal(PaneKind.Browser, rig.Workspace.NarrowSurface);
        Assert.Equal(new[] { PaneKind.Chat, PaneKind.BackgroundTasks, PaneKind.Browser }, rig.Workspace.NarrowStrip.Kinds);
        Assert.Equal(PaneKind.Browser, rig.Workspace.NarrowStrip.Selected);
        Assert.False(rig.Workspace.Chat.IsVisible);
        Assert.False(rig.Card(PaneKind.BackgroundTasks).IsVisible);
        Assert.True(rig.Card(PaneKind.Browser).IsVisible);
        Assert.Equal(1, rig.Card(PaneKind.Browser).Opacity, 3);

        rig.Workspace.Close(PaneKind.Browser);
        rig.Layout();
        Assert.Equal(PaneKind.Chat, rig.Workspace.State.NarrowSelection);
        rig.Settle();
        Assert.Null(rig.Workspace.CardOf(PaneKind.Browser));
        Assert.Equal(PaneKind.Chat, rig.Workspace.NarrowSurface);
        Assert.Equal(new[] { PaneKind.Chat, PaneKind.BackgroundTasks }, rig.Workspace.NarrowStrip.Kinds);
        Assert.True(rig.Workspace.Chat.IsVisible);
        Assert.Equal(1, rig.Workspace.Chat.Opacity, 3);

        // The last pane closed: no tabs are left to choose from, the strip fades away and the chat returns to the top.
        rig.Workspace.Close(PaneKind.BackgroundTasks);
        rig.Layout();
        var fading = false;
        for (var i = 0; i < 4; i++)
        {
            rig.Frame();
            fading |= rig.Workspace.NarrowStrip.IsVisible && rig.Workspace.NarrowStrip.Opacity < 1;
        }
        Assert.True(fading, "the strip fades out");
        rig.Settle();
        Assert.False(rig.Workspace.IsNarrow);
        Assert.False(rig.Workspace.NarrowStrip.IsVisible);
        Assert.Equal(0, rig.Workspace.ChatBounds.Y, 3);
        Assert.Equal(1, rig.Workspace.Chat.Opacity, 3);
    });

    [Fact]
    public void The_chosen_tab_is_kept_in_the_state_so_a_restart_shows_it() => headless.OnUiThread(() =>
    {
        using var rig = NarrowRig(PaneKind.BackgroundTasks);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        rig.Changes.Clear();
        PaneWorkspaceRig.Click(rig.Workspace.NarrowStrip.TabButtons[1]);
        rig.Settle();
        var state = Assert.Single(rig.Changes);
        Assert.Equal(PaneKind.BackgroundTasks, state.NarrowSelection);
        Assert.DoesNotContain(PaneKind.Chat, state.RecentTabs); // the chat is not a tab of a cell
        // A new window opened on that state shows the same tab at once.
        using var again = new PaneWorkspaceRig(width: NarrowWidth, height: 800, state: state);
        Assert.Equal(PaneKind.BackgroundTasks, again.Workspace.NarrowSurface);
        Assert.Equal(PaneKind.BackgroundTasks, again.Workspace.NarrowStrip.Selected);
        Assert.False(again.Workspace.Chat.IsVisible);
        Assert.True(again.Card(PaneKind.BackgroundTasks).IsVisible);
        Assert.Equal(0, again.Frames.Pending);
    });

    [Fact]
    public void The_keyboard_moves_the_choice_along_the_strip_with_one_tab_stop() => headless.OnUiThread(() =>
    {
        using var rig = NarrowRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Workspace.Open(PaneKind.Artifacts);
        rig.Settle();
        var strip = rig.Workspace.NarrowStrip;
        Assert.Equal(new[] { PaneKind.Chat, PaneKind.BackgroundTasks, PaneKind.Artifacts, PaneKind.Browser }, strip.Kinds);
        Assert.Equal(PaneKind.Artifacts, strip.Selected); // the last opened is the one chosen
        Assert.Equal(new[] { false, false, true, false }, strip.TabButtons.Select(button => button.IsTabStop).ToArray());
        strip.TabButtons[2].Focus();
        rig.Layout();
        rig.Key(Key.Right);
        Assert.Equal(PaneKind.Browser, rig.Workspace.State.NarrowSelection);
        Assert.Same(strip.TabButtons[3], rig.Focused);
        rig.Key(Key.Right); // the end: it stays
        Assert.Equal(PaneKind.Browser, rig.Workspace.State.NarrowSelection);
        rig.Key(Key.Home);
        Assert.Equal(PaneKind.Chat, rig.Workspace.State.NarrowSelection);
        Assert.Same(strip.TabButtons[0], rig.Focused);
        rig.Key(Key.End);
        Assert.Equal(PaneKind.Browser, rig.Workspace.State.NarrowSelection);
        rig.Key(Key.Left);
        Assert.Equal(PaneKind.Artifacts, rig.Workspace.State.NarrowSelection);
        Assert.Equal(new[] { false, false, true, false }, strip.TabButtons.Select(button => button.IsTabStop).ToArray());
        rig.Settle();
        Assert.True(rig.Card(PaneKind.Artifacts).IsVisible);
    });

    [Fact]
    public void F6_visits_the_tab_strip_and_then_the_one_surface_shown() => headless.OnUiThread(() =>
    {
        using var rig = NarrowRig();
        var strip = rig.Workspace.NarrowStrip;
        var tasks = rig.Card(PaneKind.BackgroundTasks);
        Assert.Equal(new Control[] { strip, tasks }, rig.Workspace.FocusStops());
        rig.Workspace.CycleFocus(forward: true);
        rig.Layout();
        Assert.True(strip.IsKeyboardFocusWithin, "F6 reaches the strip, at its chosen tab");
        Assert.Same(strip.TabButtons[1], rig.Focused);
        rig.Workspace.CycleFocus(forward: true);
        rig.Layout();
        Assert.True(tasks.IsKeyboardFocusWithin);
        rig.Workspace.CycleFocus(forward: true); // wraps
        rig.Layout();
        Assert.True(strip.IsKeyboardFocusWithin);

        // With the chat chosen, the chat is the surface (and a hidden chat is never a stop).
        rig.Workspace.Select(PaneKind.Chat);
        rig.Settle();
        Assert.Equal(new Control[] { strip, rig.Workspace.Chat }, rig.Workspace.FocusStops());
        rig.Workspace.Select(PaneKind.BackgroundTasks);
        rig.Settle();
        Assert.DoesNotContain(rig.Workspace.Chat, rig.Workspace.FocusStops());
    });

    [Fact]
    public void Choosing_a_tab_moves_focus_out_of_the_surface_that_is_leaving() => headless.OnUiThread(() =>
    {
        using var rig = NarrowRig();
        rig.Bodies[PaneKind.BackgroundTasks].Focus();
        rig.Layout();
        Assert.True(rig.Card(PaneKind.BackgroundTasks).IsKeyboardFocusWithin);
        PaneWorkspaceRig.Click(rig.Workspace.NarrowStrip.TabButtons[0]);
        rig.Layout();
        Dispatcher.UIThread.RunJobs();
        rig.Settle();
        Assert.Same(rig.ChatColumn.Composer, rig.Focused);
        // And into a pane: its first focusable part.
        PaneWorkspaceRig.Click(rig.Workspace.NarrowStrip.TabButtons[1]);
        rig.Layout();
        Dispatcher.UIThread.RunJobs();
        rig.Settle();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(rig.Bodies[PaneKind.BackgroundTasks], rig.Focused);
    });

    [Fact]
    public void The_strip_is_a_tab_list_whose_tabs_are_selectable_items() => headless.OnUiThread(() =>
    {
        using var rig = NarrowRig();
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var strip = rig.Workspace.NarrowStrip;
        var peer = ControlAutomationPeer.CreatePeerForElement(strip);
        Assert.Equal(AutomationControlType.Tab, peer.GetAutomationControlType());
        Assert.Equal(NarrowTabStrip.StripName, peer.GetName());
        var list = Assert.IsAssignableFrom<ISelectionProvider>(peer);
        Assert.False(list.CanSelectMultiple);
        Assert.True(list.IsSelectionRequired);
        var tabs = strip.TabButtons.Select(button => ControlAutomationPeer.CreatePeerForElement(button)).ToList();
        Assert.All(tabs, tab => Assert.Equal(AutomationControlType.TabItem, tab.GetAutomationControlType()));
        Assert.Equal(new[] { "Chat", "Background tasks", "Browser, Visus" }, tabs.Select(tab => tab.GetName()).ToArray());
        Assert.Equal(new[] { false, false, true }, tabs.Select(tab => Assert.IsAssignableFrom<ISelectionItemProvider>(tab).IsSelected).ToArray());
        Assert.Same(tabs[2], Assert.Single(list.GetSelection()));
        // Assistive technology chooses a tab as a click does.
        Assert.IsAssignableFrom<ISelectionItemProvider>(tabs[1]).Select();
        rig.Layout();
        Assert.Equal(PaneKind.BackgroundTasks, rig.Workspace.State.NarrowSelection);
        Assert.IsAssignableFrom<IInvokeProvider>(tabs[0]).Invoke();
        rig.Layout();
        Assert.Equal(PaneKind.Chat, rig.Workspace.State.NarrowSelection);
        rig.Settle();
        // A surface that is not chosen is out of the tree: nothing of it is visible, so nothing of it is read.
        Assert.False(rig.Card(PaneKind.BackgroundTasks).IsEffectivelyVisible);
        Assert.False(rig.Card(PaneKind.Browser).IsEffectivelyVisible);
        Assert.True(rig.Workspace.Chat.IsEffectivelyVisible);
    });

    [Fact]
    public void When_the_tabs_do_not_fit_the_pill_takes_the_strips_width_and_the_tabs_scroll_with_the_choice() => headless.OnUiThread(() =>
    {
        var state = PaneWorkspaceState.Default.Open(PaneKind.BackgroundTasks).Open(PaneKind.Changes).Open(PaneKind.Artifacts).Open(PaneKind.Browser)
            .Open(PaneKind.App).Open(PaneKind.Transcript).Open(PaneKind.SideChat).Select(PaneKind.BackgroundTasks);
        using var rig = new PaneWorkspaceRig(width: 460, height: 800, state: state);
        var strip = rig.Workspace.NarrowStrip;
        Assert.True(rig.Workspace.IsNarrow);
        Assert.Equal(8, strip.Kinds.Count);
        var room = rig.Workspace.StripBounds;
        Assert.True(strip.NaturalWidth > room.Width, $"{strip.NaturalWidth} vs {room.Width}");
        Assert.Equal(room.Width, strip.Pill.Bounds.Width, 1);
        Assert.Equal(0, strip.Scroller.Offset.X, 3);
        Assert.True(strip.Scroller.Extent.Width > strip.Scroller.Viewport.Width + 1);
        // Choosing the last tab brings it into view; no scroll bar is drawn.
        PaneWorkspaceRig.Click(strip.TabButtons[^1]);
        rig.Settle();
        Dispatcher.UIThread.RunJobs();
        rig.Layout();
        Assert.True(strip.Scroller.Offset.X > 0, "scrolled");
        var tab = strip.TabButtons[^1];
        var inViewport = tab.TranslatePoint(default, strip.Scroller) ?? default;
        Assert.True(inViewport.X >= -1 && inViewport.X + tab.Bounds.Width <= strip.Scroller.Viewport.Width + 1, $"the chosen tab is in view at {inViewport}");
        Assert.Equal(PaneKind.SideChat, strip.Selected);
    });

    [Fact]
    public void A_pane_this_build_cannot_draw_has_no_tab() => headless.OnUiThread(() =>
    {
        using var rig = new PaneWorkspaceRig(width: NarrowWidth, height: 800, contents: kind => kind == PaneKind.Browser ? null : new PaneContent(new Border(), PaneKinds.Title(kind)));
        rig.Workspace.Open(PaneKind.Browser);
        rig.Workspace.Open(PaneKind.BackgroundTasks);
        rig.Settle();
        Assert.Equal(new[] { PaneKind.Chat, PaneKind.BackgroundTasks }, rig.Workspace.NarrowStrip.Kinds);
        Assert.True(rig.Workspace.State.IsOpen(PaneKind.Browser), "the state still holds it for a build that has it");
    });

    [Fact]
    public void Widening_the_window_snaps_back_to_the_tiles_and_narrowing_it_again_to_the_tabs() => headless.OnUiThread(() =>
    {
        using var rig = NarrowRig();
        Assert.True(rig.Workspace.IsNarrow);
        rig.Window.Width = 1400;
        rig.Layout();
        Assert.False(rig.Workspace.IsNarrow);
        Assert.Equal(0, rig.Frames.Pending);
        Assert.True(rig.Workspace.Chat.IsVisible);
        Assert.Equal(0, rig.Workspace.ChatBounds.Y, 3);
        Assert.True(rig.Card(PaneKind.BackgroundTasks).IsVisible);
        Assert.Equal(1, rig.Card(PaneKind.BackgroundTasks).Opacity, 3);
        Assert.Equal(0, rig.Card(PaneKind.BackgroundTasks).Slide, 3);
        Assert.Equal(0, rig.Workspace.NarrowStrip.Opacity, 3);
        Assert.False(rig.Workspace.NarrowStrip.IsVisible);
        var tile = rig.ExpectedBounds(PaneKind.BackgroundTasks);
        Assert.True(Math.Abs(tile.Width - rig.Card(PaneKind.BackgroundTasks).Bounds.Width) <= 1);
        Assert.True(rig.Card(PaneKind.BackgroundTasks).Header.CanExpand);

        rig.Window.Width = NarrowWidth;
        rig.Layout();
        Assert.True(rig.Workspace.IsNarrow);
        Assert.Equal(0, rig.Frames.Pending);
        Assert.Equal(PaneKind.BackgroundTasks, rig.Workspace.NarrowSurface);
        Assert.False(rig.Workspace.Chat.IsVisible);
        Assert.True(rig.Workspace.NarrowStrip.IsVisible);
        Assert.Equal(1, rig.Workspace.NarrowStrip.Opacity, 3);
    });

    [Fact]
    public void Under_a_right_to_left_window_a_surface_comes_from_the_side_its_tab_lies_on() => headless.OnUiThread(() =>
    {
        double OnScreenStart(FlowDirection direction)
        {
            using var rig = NarrowRig();
            rig.Window.FlowDirection = direction;
            rig.Layout();
            PaneWorkspaceRig.Click(rig.Workspace.NarrowStrip.TabButtons[0]); // toward the first tab
            rig.Layout();
            // The chat comes in from the start side of the strip: on screen, to the left of where it rests in a left-to-right
            // window and to the right of it in a right-to-left one.
            var chat = rig.Workspace.Chat;
            var shown = chat.TranslatePoint(default, rig.Window) ?? default;
            rig.Settle();
            var rest = chat.TranslatePoint(default, rig.Window) ?? default;
            return shown.X - rest.X;
        }

        var left = OnScreenStart(FlowDirection.LeftToRight);
        var right = OnScreenStart(FlowDirection.RightToLeft);
        Assert.True(left < -1, $"left to right: {left}");
        Assert.True(right > 1, $"right to left: {right}");
    });
}
