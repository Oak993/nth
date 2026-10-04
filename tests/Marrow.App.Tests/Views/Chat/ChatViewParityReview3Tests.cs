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
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW, adversarial round 3 of the ChatView parity unit (2026-10-04): defects left after the round 2 fixes.
[Collection("Headless")]
public sealed class ChatViewParityReview3Tests(HeadlessSession headless)
{
    private static void Drain(AppModelHarness h, AvaloniaWindow window)
    {
        h.Main.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        h.Main.RunUntilIdle();
    }

    // PR-TERM-001 / OD-0076, the mirror of round 2's finding. The drawer reopened while it is still closing (Ctrl+J twice inside the
    // 0.32 s spring). ChatView.swift:276-279,290 runs the drawer's `.move(edge: .bottom).combined(with: .opacity)` and the column's
    // layout under one `.animation(_, value: isTerminalVisible)`; the port's own claim (ChatView.PushTerminal, ED-278
    // (3)) is that the pane's bottom edge is the drawer's top on every frame. Round 1 carries the chat box on from where it is
    // (REVIEW_Reopening_the_terminal_while_it_is_closing_moves_the_thread_view_on_from_where_it_is, which never looks at a drawer),
    // and round 2 made only the close continue (SwiftTransition.RemoveContinuing). On a reopen the port builds a NEW TerminalPanel
    // and inserts it from progress 0 (a full drawer below the window's bottom), while the leaving one keeps sinking on its removal
    // and the chat box rises from where it was: a band between the chat box's bottom and the higher of the two drawers is covered
    // by neither, on every frame of the reopen.
    [Fact]
    [Trait("Req", "PR-TERM-001")] // App/Views/Chat/ChatView.swift:276-279,290; OD-0076
    public void REVIEW_Reopening_the_terminal_while_it_is_still_closing_keeps_a_drawer_under_the_chat_box_on_every_frame()
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
                // The highest drawer edge on screen: whichever drawer (the leaving one or the new one) reaches up furthest.
                double HighestDrawerTop() => LogicalQuery.SelfAndDescendants<TerminalPanel>(view)
                    .Where(panel => panel.IsVisible && panel.Bounds.Height > 0)
                    .Select(panel => panel.TranslatePoint(default, view)!.Value.Y)
                    .DefaultIfEmpty(view.Bounds.Height)
                    .Min();
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
                Assert.InRange(closing, open + 1, resting - 1); // mid-close...
                Assert.Equal(closing, HighestDrawerTop(), tolerance: 2); // ...in lock-step.
                h.Main.OnMain(() => runtime.IsTerminalVisible = true);
                Drain(h, window);
                Assert.Equal(closing, ComposerBottom(), tolerance: 2); // round 1: the chat box goes on from where it is.
                foreach (var step in new[] { 0.0, 0.04, 0.04, 0.04, 0.04 })
                {
                    h.Main.Advance(TimeSpan.FromSeconds(step)); frames.Advance(TimeSpan.Zero); Drain(h, window);
                    var gap = HighestDrawerTop() - ComposerBottom();
                    Assert.True(Math.Abs(gap) <= 2,
                        $"{step:0.00} s step: the chat box's bottom is at {ComposerBottom():0.0} and the highest drawer's top at {HighestDrawerTop():0.0} (a band of {gap:0.0} pt under the chat box shows no drawer)");
                }
            }
            finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
        });
    }
}
