using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Terminal;
using Marrow.App.Views.Usage;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// PR-TERM-001 against the reference runtime: the terminal open in 2-chat-use.mov (C 132.17-132.56).
[Collection("Headless")]
public sealed class ChatViewParityTerminalTests(HeadlessSession headless)
{
    // The thread view's push still to come, per frame, as the terminal opens in 2-chat-use.mov (the real Mac app 1.7.0):
    // the composer band (x 820-1580 of frames/2-chat-use_131.900-133.700_term, 1 px = 1 pt) matched against the settled
    // frame at every vertical shift (scratch term_push.py). The panel is 260 tall there. Frames whose best match was poor
    // (mean difference above 11 grey levels, while the panel's own fill is still turning opaque) are left out. Evidence:
    // E:\Droppy\work\parity\evidence\ChatView\terminal_open_port_vs_mac.png.
    private static readonly (double Time, double Remaining)[] MacPush =
    [
        (132.2367, 230), (132.2583, 178), (132.3150, 89), (132.3300, 68), (132.3500, 40), (132.3717, 29),
        (132.3933, 20), (132.4150, 14), (132.4367, 7), (132.4583, 4), (132.4800, 3), (132.5000, 1), (132.5433, 0),
    ];

    private const double MacHeight = 260;

    // When the push began in the video: the last still frame is 132.2150 (259 of 260 left) and the next 132.2367; the
    // start is fitted once between them (least squares against the source spring, the frame interval 21.7 ms).
    private const double MacStart = 132.205;

    private static void Drain(AppModelHarness h, AvaloniaWindow window)
    {
        h.Main.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        h.Main.RunUntilIdle();
    }

    [Fact]
    [Trait("Req", "PR-TERM-001")] // App/Views/Chat/ChatView.swift:96,276-279,290
    public void The_terminal_slides_up_pushing_the_thread_in_lock_step_on_the_Macs_measured_curve_and_back_down_on_close()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            h.Main.Loop.Run(() => Marrow.Core.Support.LoginEnvironment.Load(), AppModelHarness.Bound);
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
            var frames = new ManualFrameSource();
            using var clock = FrameClock.Source.Override(frames);
            var directory = h.NewFolder("project");
            h.WriteLibrary([new Project("Project", directory)], []);
            var model = h.MakeModel();
            h.PrepareBootstrap(model);
            AvaloniaWindow window = null!;
            ChatView view = null!;
            ThreadRuntime runtime = null!;
            h.Main.OnMain(() =>
            {
                model.Settings.ShowsUsagePanel = false;
                model.Settings.HydraEnabled = false;
                runtime = model.Runtime(model.NewThread(model.Projects[0])!.Id);
                view = new ChatView(runtime);
                window = new AvaloniaWindow { Content = view, Width = 1400, Height = 900 };
                AppEnvironment.SetAppModel(window, model);
                AppEnvironment.SetWindowLiveResize(window, new WindowLiveResize(window));
                ChatHeadlessFixtures.Track(window);
                window.Show();
            });
            try
            {
                Drain(h, window);
                var composer = Assert.Single(LogicalQuery.SelfAndDescendants<ComposerArea>(view));
                double ComposerBottom() => composer.TranslatePoint(new Point(0, composer.Bounds.Height), view)!.Value.Y;
                var resting = ComposerBottom();
                Assert.Equal(view.Bounds.Height, resting, tolerance: 1);
                h.Main.OnMain(() => runtime.IsTerminalVisible = true);
                Drain(h, window);
                var terminal = Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
                var height = terminal.Bounds.Height;
                Assert.True(height > 100);
                double TerminalTop() => terminal.TranslatePoint(default, view)!.Value.Y;
                // The first frame: nothing has moved yet (no cut of the thread view, OD-0076).
                Assert.Equal(resting, ComposerBottom(), tolerance: 1);
                Assert.Equal(resting, TerminalTop(), tolerance: 1);
                var elapsed = 0.0;
                foreach (var (time, remaining) in MacPush)
                {
                    var at = time - MacStart;
                    h.Main.Advance(TimeSpan.FromSeconds(at - elapsed)); elapsed = at;
                    frames.Advance(TimeSpan.Zero);
                    Drain(h, window);
                    var port = (resting - ComposerBottom()) / height;
                    var mac = 1 - remaining / MacHeight;
                    Assert.True(Math.Abs(port - mac) <= 0.05, $"at {at:0.000} s the port has pushed {port:0.000} of the panel, the Mac {mac:0.000}");
                    Assert.Equal(ComposerBottom(), TerminalTop(), tolerance: 1); // lock-step: the thread's bottom is the panel's top.
                }
                h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration)); frames.Advance(TimeSpan.Zero); Drain(h, window);
                Assert.Equal(resting - height, ComposerBottom(), tolerance: 1);
                Assert.Equal(resting - height, TerminalTop(), tolerance: 1);
                Assert.Equal(1, terminal.Opacity);
                // Close: the same spring reversed, the thread following the panel back down.
                h.Main.OnMain(() => runtime.IsTerminalVisible = false);
                Drain(h, window);
                Assert.Equal(resting - height, ComposerBottom(), tolerance: 1); // the first frame of the close.
                h.Main.Advance(TimeSpan.FromSeconds(0.1)); frames.Advance(TimeSpan.Zero); Drain(h, window);
                var mid = ComposerBottom();
                Assert.InRange(mid, resting - height + 1, resting - 1);
                Assert.Equal(mid, TerminalTop(), tolerance: 1);
                h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration)); frames.Advance(TimeSpan.Zero); Drain(h, window);
                Assert.Empty(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
                Assert.Equal(resting, ComposerBottom(), tolerance: 1);
            }
            finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
        });
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(owner)!;

    // The column of the tests below: a usage panel docked at a corner, no sheet, the terminal closed. The check gets the thread's chat box, the
    // panel and the runtime, and the manual clock the push runs on.
    private void WithColumn(PanelDockCorner dock, Action<AppModelHarness, ChatView, ThreadRuntime, ComposerArea, UsageFloatingPanel, AvaloniaWindow, ManualFrameSource> check) => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var h = new AppModelHarness();
        h.Main.Loop.Run(() => Marrow.Core.Support.LoginEnvironment.Load(), AppModelHarness.Bound);
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        var frames = new ManualFrameSource();
        using var clock = FrameClock.Source.Override(frames);
        h.WriteLibrary([new Project("Project", h.NewFolder("project"))], []);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        AvaloniaWindow window = null!;
        ChatView view = null!;
        ThreadRuntime runtime = null!;
        h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = false;
            model.Settings.UsagePanelDock = dock;
            model.Settings.ShowsUsagePanel = true;
            runtime = model.Runtime(model.NewThread(model.Projects[0])!.Id);
            view = new ChatView(runtime);
            window = new AvaloniaWindow { Content = view, Width = 1400, Height = 900 };
            AppEnvironment.SetAppModel(window, model);
            AppEnvironment.SetWindowLiveResize(window, new WindowLiveResize(window));
            ChatHeadlessFixtures.Track(window);
            window.Show();
        });
        try
        {
            Drain(h, window);
            h.Main.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.Zero); Drain(h, window);
            check(h, view, runtime, Assert.Single(LogicalQuery.SelfAndDescendants<ComposerArea>(view)), Assert.Single(LogicalQuery.SelfAndDescendants<UsageFloatingPanel>(view)), window, frames);
        }
        finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
    });

    private static void Advance(AppModelHarness h, AvaloniaWindow window, ManualFrameSource frames, double seconds)
    {
        h.Main.Advance(TimeSpan.FromSeconds(seconds)); frames.Advance(TimeSpan.Zero); Drain(h, window);
    }

    // PR-TERM-001, the drawer closing (ChatView.swift:276-279,290): the layout change is animated both ways, so a bottom-docked panel sinks with the
    // chat box as the drawer leaves, as it rose with it on the way in; and the pane's size is read once at its final value, not on the way.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // ChatView.swift:156-241,276-279,290; the Mac's frames are in REVIEW_A_bottom_docked_panel_rises_with_the_terminal_push_in_step_with_the_composer; ED-NEW-parity-chatview (3)
    public void A_bottom_docked_panel_sinks_with_the_chat_box_as_the_terminal_closes_and_the_scene_reads_the_pane_once()
        => WithColumn(PanelDockCorner.BottomLeading, (h, view, runtime, composer, panel, window, frames) =>
        {
            double ComposerBottom() => composer.TranslatePoint(new Point(0, composer.Bounds.Height), view)!.Value.Y;
            double PanelBottom() => panel.TranslatePoint(new Point(0, panel.Bounds.Height), view)!.Value.Y;
            var resting = ComposerBottom();
            var gap = resting - PanelBottom();
            h.Main.OnMain(() => runtime.IsTerminalVisible = true);
            Drain(h, window);
            var heading = Field<Size>(view, "paneSize"); // the first frame: the scene already holds the size the push settles at...
            Assert.True(heading.Height < resting - 100, "the pane the scene reads is the one above the drawer");
            var passed = 0.0;
            foreach (var at in new[] { 0.03, 0.09, 0.2 })
            {
                Advance(h, window, frames, at - passed); passed = at;
                Assert.Equal(heading, Field<Size>(view, "paneSize")); // ...and holds it on every frame of the push.
            }
            Advance(h, window, frames, 1);
            var open = ComposerBottom();
            Assert.Equal(heading.Height, open, 1);
            Assert.Equal(gap, open - PanelBottom(), 1);
            h.Main.OnMain(() => runtime.IsTerminalVisible = false);
            Drain(h, window);
            var settled = Field<Size>(view, "paneSize");
            Assert.Equal(resting, settled.Height, 1); // the closing's final size, read at the first frame of the close.
            Assert.Equal(open, ComposerBottom(), 1); // nothing has moved yet.
            Assert.Equal(gap, ComposerBottom() - PanelBottom(), 1);
            var elapsed = 0.0;
            foreach (var at in new[] { 0.03, 0.06, 0.09, 0.12, 0.16, 0.2 })
            {
                Advance(h, window, frames, at - elapsed); elapsed = at;
                Assert.True(ComposerBottom() > open + 1, "the chat box is sinking back");
                Assert.True(Math.Abs(ComposerBottom() - PanelBottom() - gap) <= 4, $"at {at:0.00} s the chat box has sunk {ComposerBottom() - open:0.0} and the panel {PanelBottom() - (open - gap):0.0}");
                Assert.Equal(settled, Field<Size>(view, "paneSize"));
            }
            Advance(h, window, frames, 1);
            Assert.Equal(resting, ComposerBottom(), 1);
            Assert.Equal(gap, resting - PanelBottom(), 1);
        });

    // PR-TERM-001, the drawer opening: a top-docked panel keeps its place through the push (it hangs from the pane's top edge, which does not
    // move), while the chat box rises.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // ChatView.swift:156-241,276-279,290; ED-NEW-parity-chatview (3)
    public void A_top_docked_panel_stays_where_it_is_while_the_terminal_pushes_the_chat_box_up()
        => WithColumn(PanelDockCorner.TopLeading, (h, view, runtime, composer, panel, window, frames) =>
        {
            double ComposerBottom() => composer.TranslatePoint(new Point(0, composer.Bounds.Height), view)!.Value.Y;
            double PanelTop() => panel.TranslatePoint(default, view)!.Value.Y;
            var resting = ComposerBottom();
            var top = PanelTop();
            h.Main.OnMain(() => runtime.IsTerminalVisible = true);
            Drain(h, window);
            var elapsed = 0.0;
            foreach (var at in new[] { 0.03, 0.09, 0.16, 0.3 })
            {
                Advance(h, window, frames, at - elapsed); elapsed = at;
                Assert.True(resting - ComposerBottom() > 1, "the chat box is rising");
                Assert.Equal(top, PanelTop(), 0.5);
            }
        });

    // PR-TERM-001 / OD-0076, an interrupted push: the drawer closed while it is still opening goes on from where the thread view is, and the
    // panel with it; neither jumps (SwiftUI retargets the animated layout from its presentation).
    [Fact]
    [Trait("Rule", "R-TEST-4")] // ChatView.swift:276-279,290; OD-0076; ED-NEW-parity-chatview (3)
    public void Closing_the_terminal_while_it_is_still_opening_carries_the_chat_box_and_its_panel_on_from_where_they_are()
        => WithColumn(PanelDockCorner.BottomLeading, (h, view, runtime, composer, panel, window, frames) =>
        {
            double ComposerBottom() => composer.TranslatePoint(new Point(0, composer.Bounds.Height), view)!.Value.Y;
            double PanelBottom() => panel.TranslatePoint(new Point(0, panel.Bounds.Height), view)!.Value.Y;
            var resting = ComposerBottom();
            var gap = resting - PanelBottom();
            h.Main.OnMain(() => runtime.IsTerminalVisible = true);
            Drain(h, window);
            Advance(h, window, frames, 0.1);
            var rising = ComposerBottom();
            var panelRising = PanelBottom();
            Assert.InRange(rising, 1, resting - 1);
            Assert.True(Math.Abs(rising - panelRising - gap) <= 4);
            h.Main.OnMain(() => runtime.IsTerminalVisible = false);
            Drain(h, window);
            Assert.Equal(rising, ComposerBottom(), 2); // the first frame of the close: no jump.
            Assert.Equal(panelRising, PanelBottom(), 2);
            var elapsed = 0.0;
            foreach (var at in new[] { 0.06, 0.12, 0.2 })
            {
                Advance(h, window, frames, at - elapsed); elapsed = at;
                Assert.True(Math.Abs(ComposerBottom() - PanelBottom() - gap) <= 6, $"at {at:0.00} s after the close the chat box is at {ComposerBottom():0.0} and the panel at {PanelBottom():0.0}");
            }
            Advance(h, window, frames, 1);
            Assert.Equal(resting, ComposerBottom(), 1);
            Assert.Equal(gap, resting - PanelBottom(), 1);
        });
}
