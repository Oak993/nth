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

// REVIEW, adversarial round 2 of the ChatView parity unit (2026-10-03): defects left after the round 1 fixes.
[Collection("Headless")]
public sealed class ChatViewParityReview2Tests(HeadlessSession headless)
{
    private static void Drain(AppModelHarness h, AvaloniaWindow window)
    {
        h.Main.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        h.Main.RunUntilIdle();
    }

    // PR-TERM-001 / OD-0076. The drawer closed while it is still opening (Ctrl+J twice inside the 0.32 s spring). ChatView.swift:276-279,290: the
    // drawer's `.move(edge: .bottom).combined(with: .opacity)` and the column's layout run under one `.animation(_, value: isTerminalVisible)`, and
    // SwiftUI retargets an interrupted transition from its presentation, so the drawer goes back down from where it is, its top still on the chat
    // view's bottom (the lock-step the unit's own open test asserts frame by frame). The round 1 fix carries the chat view on from where it is
    // (Closing_the_terminal_while_it_is_still_opening_carries_the_chat_box_and_its_panel_on_from_where_they_are), but that test never looks at the
    // drawer: the port's removal restarts the drawer's transition from its identity (SwiftTransition.Remove: Present(child, 0, inserting: false)),
    // so on the close's first frame the half-risen drawer jumps up to its fully open place, over the bottom of the chat box, and turns opaque.
    [Fact]
    [Trait("Req", "PR-TERM-001")] // App/Views/Chat/ChatView.swift:276-279,290; OD-0076
    public void REVIEW_Closing_the_terminal_while_it_is_still_opening_takes_the_drawer_back_down_from_where_it_is()
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
                var resting = ComposerBottom();
                h.Main.OnMain(() => runtime.IsTerminalVisible = true);
                Drain(h, window);
                var terminal = Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
                double TerminalTop() => terminal.TranslatePoint(default, view)!.Value.Y;
                h.Main.Advance(TimeSpan.FromSeconds(0.06)); frames.Advance(TimeSpan.Zero); Drain(h, window);
                var risingTop = TerminalTop();
                var risingBottom = ComposerBottom();
                Assert.InRange(risingBottom, resting - terminal.Bounds.Height + 1, resting - 1); // mid-open...
                Assert.Equal(risingBottom, risingTop, tolerance: 1); // ...in lock-step.
                h.Main.OnMain(() => runtime.IsTerminalVisible = false);
                Drain(h, window);
                Assert.Equal(risingBottom, ComposerBottom(), tolerance: 2); // the chat view goes on from where it is (round 1)...
                Assert.True(Math.Abs(TerminalTop() - risingTop) <= 2,
                    $"the close's first frame moved the drawer's top from {risingTop:0.0} to {TerminalTop():0.0} (the chat box's bottom is at {ComposerBottom():0.0})");
                foreach (var step in new[] { 0.04, 0.04, 0.04 })
                {
                    h.Main.Advance(TimeSpan.FromSeconds(step)); frames.Advance(TimeSpan.Zero); Drain(h, window);
                    Assert.True(Math.Abs(TerminalTop() - ComposerBottom()) <= 2,
                        $"the drawer's top is at {TerminalTop():0.0} and the chat box's bottom at {ComposerBottom():0.0}");
                }
            }
            finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
        });
    }
}
