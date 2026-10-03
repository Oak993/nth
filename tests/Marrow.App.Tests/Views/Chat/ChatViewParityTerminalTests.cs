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
}
