using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Terminal;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW, adversarial round 1 of the ChatView parity unit (2026-10-03): defects the parity pass left in place.
[Collection("Headless")]
public sealed class ChatViewParityReviewTests(HeadlessSession headless)
{
    private static void Drain(AppModelHarness h, AvaloniaWindow window)
    {
        h.Main.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        h.Main.RunUntilIdle();
    }

    // UI-CHAT-006. ChatView.swift:1094-1101 puts `.transition(...)` after `.padding(.bottom, 10).frame(height: 0, alignment:
    // .bottom)`, so `.scale(scale: 0.6)` scales the zero-height frame about ITS centre: a point 10 + 16 = 26 pt below the
    // circle's centre. On the first frame the circle's centre therefore sits 26 * (1 - 0.6) + 10 = 20.4 pt below its rest,
    // not 10. The reference runtime agrees (2-chat-use.mov C 10.285-10.47, crop 160:100:1332:1196, every frame): the ring's
    // radius and its centre's drop fit r = 16.75 s and drop = 20.4 (1 - p) on one curve (10.285: r 11.75, drop 14.0;
    // 10.300: 12.75, 12.0; 10.357: 15.0, 5.5; 10.375: 15.5, 4.0; 10.397: 16.0, 2.5; 10.432: 16.5, 1.0), which a scale about
    // the circle's own centre (drop = 10 (1 - p) <= 10) cannot produce. The port scales the circle about its own centre.
    [Fact]
    [Trait("Req", "UI-CHAT-006")] // App/Views/Chat/ChatView.swift:1094-1101
    public void REVIEW_Jump_to_latest_grows_from_the_zero_height_frames_centre_not_from_its_own_centre()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var frames = new ManualFrameSource();
            using var clock = FrameClock.Source.Override(frames);
            TimelineScrollState state = null!;
            JumpToLatestButton view = null!;
            AvaloniaWindow window = null!;
            h.Main.OnMain(() =>
            {
                state = new TimelineScrollState();
                view = new JumpToLatestButton(state);
                window = new AvaloniaWindow { Content = view, Width = 300, Height = 200 };
                ChatHeadlessFixtures.Track(window);
                window.Show();
                Transaction.WithAnimation(Chrome.PanelSlide, () => state.ShowsJumpButton = true);
            });
            Drain(h, window);
            var circle = Assert.Single(LogicalQuery.SelfAndDescendants<ChromeCircleButton>(view));
            Point Centre() => circle.TranslatePoint(new Point(circle.Bounds.Width / 2, circle.Bounds.Height / 2), window)!.Value;
            var anchor = view.TranslatePoint(default, window)!.Value.Y; // the zero-height frame: its centre is on its top edge.
            Assert.Equal(0, view.Bounds.Height);
            var first = Centre();
            h.Main.Advance(TimeSpan.FromSeconds(2)); frames.Advance(TimeSpan.Zero); Drain(h, window);
            var rest = Centre();
            Assert.Equal(26, anchor - rest.Y, 0.5); // 10 pt of padding plus the 32 pt circle's half.
            Assert.Equal(rest.X, first.X, 0.5);
            Assert.Equal(anchor - (anchor - rest.Y) * 0.6 + 10, first.Y, 0.5);
        });
    }

    // UI-CHAT-006, the removal: `.scale(scale: 0.85)` with `.offset(y: 6)` about the same zero-height frame, so a removal at
    // progress q drops the circle's centre by 26 * 0.15 q + 6 q = 9.9 q, not 6 q. The reference runtime agrees (2-chat-use.mov
    // C 13.447-13.51, same crop: radius 16.75 -> 16.25 -> 15.75 -> 15.5 while the centre drops 0.5 -> 2.0 -> 3.5 -> 4.5,
    // i.e. drop = 9.9 q with r = 16.75 (1 - 0.15 q); a drop of 6 q would need r = 14.9 at 13.51).
    [Fact]
    [Trait("Req", "UI-CHAT-006")] // App/Views/Chat/ChatView.swift:1094-1101
    public void REVIEW_Jump_to_latest_leaves_toward_the_zero_height_frames_centre()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var frames = new ManualFrameSource();
            using var clock = FrameClock.Source.Override(frames);
            TimelineScrollState state = null!;
            JumpToLatestButton view = null!;
            AvaloniaWindow window = null!;
            h.Main.OnMain(() =>
            {
                state = new TimelineScrollState();
                view = new JumpToLatestButton(state);
                window = new AvaloniaWindow { Content = view, Width = 300, Height = 200 };
                ChatHeadlessFixtures.Track(window);
                window.Show();
                state.ShowsJumpButton = true;
            });
            Drain(h, window);
            h.Main.Advance(TimeSpan.FromSeconds(2)); frames.Advance(TimeSpan.Zero); Drain(h, window);
            var circle = Assert.Single(LogicalQuery.SelfAndDescendants<ChromeCircleButton>(view));
            Point Centre() => circle.TranslatePoint(new Point(circle.Bounds.Width / 2, circle.Bounds.Height / 2), window)!.Value;
            var rest = Centre();
            Assert.Equal(1, circle.Opacity, 3);
            h.Main.OnMain(() => Transaction.WithAnimation(Chrome.PanelSlide, () => state.ShowsJumpButton = false));
            Drain(h, window);
            h.Main.AdvanceMilliseconds(100); frames.Advance(TimeSpan.Zero); Drain(h, window);
            var q = 1 - circle.Opacity; // the removal's progress: opacity, scale and offset share one curve.
            Assert.InRange(q, 0.2, 0.9);
            Assert.Equal(9.9 * q, Centre().Y - rest.Y, 0.5);
        });
    }

    // PR-TERM-001 (the push fix). On the Mac the bottom-docked panels ride the push with the composer: in 2-chat-use.mov the
    // "head four" panel's bottom row and the composer field (frames/2-chat-use_131.900-133.700_term, matched against the settled
    // frame 132.7366) still have 72/68 px to go at 132.33, 46/40 at 132.35, 33/29 at 132.372, 23/20 at 132.393, 16/14 at 132.415,
    // 8/7 at 132.437 and 0/0 from 132.543: one curve, a few px apart at most. ChatView.swift:156-241 hangs the panels on the
    // timeline's overlays, whose frame the column's `.animation(_, value: isTerminalVisible)` animates with the timeline's, and
    // their rest comes from the final paneSize under the same panelSlide. The port's push animates the pane's margin, so the
    // pane's measured size changes on every frame; each change re-places the panel with a fresh panelSlide settle from where it
    // is, and the panel trails the composer by far more than the Mac's few pixels.
    [Fact]
    [Trait("Req", "PR-TERM-001")] // App/Views/Chat/ChatView.swift:156-241,276-279,290
    public void REVIEW_A_bottom_docked_panel_rises_with_the_terminal_push_in_step_with_the_composer()
    {
        ChatHeadlessFixtures.Run(headless, () =>
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
                model.Settings.UsagePanelDock = PanelDockCorner.BottomLeading;
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
                var composer = Assert.Single(LogicalQuery.SelfAndDescendants<ComposerArea>(view));
                var panel = Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.App.Views.Usage.UsageFloatingPanel>(view));
                double ComposerBottom() => composer.TranslatePoint(new Point(0, composer.Bounds.Height), view)!.Value.Y;
                double PanelBottom() => panel.TranslatePoint(new Point(0, panel.Bounds.Height), view)!.Value.Y;
                var resting = ComposerBottom();
                var gap = resting - PanelBottom();
                h.Main.OnMain(() => runtime.IsTerminalVisible = true);
                Drain(h, window);
                var elapsed = 0.0;
                foreach (var at in new[] { 0.03, 0.06, 0.09, 0.12, 0.16, 0.2 })
                {
                    h.Main.Advance(TimeSpan.FromSeconds(at - elapsed)); elapsed = at;
                    frames.Advance(TimeSpan.Zero);
                    Drain(h, window);
                    Assert.True(resting - ComposerBottom() > 1, "the composer is being pushed");
                    Assert.True(Math.Abs(ComposerBottom() - PanelBottom() - gap) <= 8,
                        $"at {at:0.00} s the composer has risen {resting - ComposerBottom():0.0} and the panel {resting - gap - PanelBottom():0.0}");
                }
                h.Main.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.Zero); Drain(h, window);
                Assert.Equal(gap, ComposerBottom() - PanelBottom(), 1);
            }
            finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
        });
    }

    // PR-USAGE-074: "the panel falls back to the content-reported height (PR-USAGE-072), animating to it". ChatView.swift:574-577
    // clears usagePanelHeight inside withAnimation(Chrome.panelSlide), so the mounted panel's frame glides to the new height.
    // The port writes the setting inside the transaction (that much is tested), but the mounted panel's surface takes the new
    // size at once (UpdatePanelPlacement sets Host/Surface Height directly): 500 -> 129 on the reset's first frame.
    [Fact]
    [Trait("Req", "PR-USAGE-074")] // App/Views/Chat/ChatView.swift:574-577
    public void REVIEW_Resetting_the_usage_height_glides_the_mounted_panel_to_its_content_height()
    {
        ChatHeadlessFixtures.Run(headless, () =>
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
            h.Main.OnMain(() =>
            {
                model.Settings.HydraEnabled = false;
                model.Settings.UsagePanelDock = PanelDockCorner.TopLeading;
                model.Settings.UsagePanelHeight = 500;
                model.Settings.ShowsUsagePanel = true;
                view = new ChatView(model.Runtime(model.NewThread(model.Projects[0])!.Id));
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
                var panel = Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.App.Views.Usage.UsageFloatingPanel>(view));
                var before = panel.Bounds.Height;
                Assert.Equal(500, before, 1);
                h.Main.OnMain(() =>
                {
                    var resizer = (PanelResize)typeof(ChatView).GetMethod("CurrentResizer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(view, [new FloatingPanelID.Usage()])!;
                    resizer.OnReset(); // the grip's double-click.
                });
                Drain(h, window);
                Assert.Null(model.Settings.UsagePanelHeight);
                Assert.Equal(before, panel.Bounds.Height, 1); // the reset's first frame: not yet moved.
                h.Main.AdvanceMilliseconds(90); frames.Advance(TimeSpan.Zero); Drain(h, window);
                var mid = panel.Bounds.Height;
                h.Main.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.Zero); Drain(h, window);
                var after = panel.Bounds.Height;
                Assert.True(after < before - 50, $"the content height {after} is well under the stored {before}");
                Assert.InRange(mid, after + 1, before - 1); // gliding, not cut.
            }
            finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
        });
    }

    // PR-TERM-001 / OD-0076. The drawer reopened while it is still closing (Ctrl+J twice inside the 0.32 s spring): SwiftUI
    // retargets the column's animated layout from where it is (ChatView.swift:290), so the thread view never jumps. The port
    // restarts the push from a full drawer's height below (PushTerminal: `from = showing ? -height : ...`), so on the re-open's
    // first frame the composer drops back to the bottom of the window before it rises again: a cut of nearly half a drawer.
    [Fact]
    [Trait("Req", "PR-TERM-001")] // App/Views/Chat/ChatView.swift:96,276-279,290
    public void REVIEW_Reopening_the_terminal_while_it_is_closing_moves_the_thread_view_on_from_where_it_is()
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
                h.Main.OnMain(() => runtime.IsTerminalVisible = true);
                Drain(h, window);
                h.Main.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.Zero); Drain(h, window);
                var open = ComposerBottom();
                Assert.True(resting - open > 100);
                h.Main.OnMain(() => runtime.IsTerminalVisible = false);
                Drain(h, window);
                h.Main.Advance(TimeSpan.FromSeconds(0.1)); frames.Advance(TimeSpan.Zero); Drain(h, window);
                var closing = ComposerBottom();
                Assert.InRange(closing, open + 1, resting - 1); // mid-close.
                h.Main.OnMain(() => runtime.IsTerminalVisible = true);
                Drain(h, window);
                Assert.Equal(closing, ComposerBottom(), tolerance: 2); // the re-open's first frame: no cut.
                h.Main.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.Zero); Drain(h, window);
                Assert.Equal(open, ComposerBottom(), tolerance: 1);
            }
            finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
        });
    }
}
