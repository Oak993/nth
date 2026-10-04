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

// The terminal drawer reopened while it is still closing (ED-278 (10), round 4): the leaving drawer comes back from
// where it is, as the same TerminalPanel, in lock-step with the chat box (PR-TERM-001; OD-0076, no change by a cut). The drawer
// reopened after it has left, or for another directory, is a new panel from below as ever.
[Collection("Headless")]
public sealed class ChatViewParityRound4Tests(HeadlessSession headless)
{
    private static void Drain(AppModelHarness h, AvaloniaWindow window)
    {
        h.Main.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        h.Main.RunUntilIdle();
    }

    private void WithChat(Action<AppModelHarness, AppModel, ChatView, ThreadRuntime, ComposerArea, AvaloniaWindow, ManualFrameSource> check) => ChatHeadlessFixtures.Run(headless, () =>
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
            check(h, model, view, runtime, composer, window, frames);
        }
        finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
    });

    private static void Step(AppModelHarness h, ManualFrameSource frames, AvaloniaWindow window, double seconds)
    {
        h.Main.Advance(TimeSpan.FromSeconds(seconds)); frames.Advance(TimeSpan.Zero); Drain(h, window);
    }

    private static double Top(Control control, Visual view) => control.TranslatePoint(default, view)!.Value.Y;

    // PR-TERM-001, OD-0076. Open fully, close for 0.1 s, reopen: from the first frame to the last the drawer on screen is the one
    // that was leaving (one TerminalPanel, the same instance), it goes on from its top and its opacity at the reopen (no frame
    // shows it at its open place or at its closed one), its top is the chat box's bottom on every frame, and it ends open and opaque.
    [Fact]
    [Trait("Req", "PR-TERM-001")] // App/Views/Chat/ChatView.swift:96,276-279,290; OD-0076
    public void The_drawer_reopened_while_it_is_closing_is_the_same_panel_going_on_from_its_top_and_opacity_in_lock_step()
    {
        WithChat((h, model, view, runtime, composer, window, frames) =>
        {
            double ComposerBottom() => composer.TranslatePoint(new Point(0, composer.Bounds.Height), view)!.Value.Y;
            var resting = ComposerBottom();
            h.Main.OnMain(() => runtime.IsTerminalVisible = true);
            Drain(h, window);
            var drawer = Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
            var height = drawer.Bounds.Height;
            Step(h, frames, window, 1);
            Assert.Equal(resting - height, Top(drawer, view), tolerance: 1);
            h.Main.OnMain(() => runtime.IsTerminalVisible = false);
            Drain(h, window);
            Step(h, frames, window, 0.1);
            var topAtReopen = Top(drawer, view);
            var opacityAtReopen = drawer.Opacity;
            Assert.InRange(topAtReopen, resting - height + 1, resting - 1); // mid-close
            Assert.InRange(opacityAtReopen, 0.05, 0.95);
            h.Main.OnMain(() => runtime.IsTerminalVisible = true);
            Drain(h, window);
            Assert.Same(drawer, Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view)));
            Assert.Equal(topAtReopen, Top(drawer, view), tolerance: 1); // no jump on the reopen's first frame
            Assert.Equal(opacityAtReopen, drawer.Opacity, tolerance: 0.02);
            var previous = topAtReopen;
            foreach (var step in new[] { 0.04, 0.04, 0.04, 0.04, 0.04 })
            {
                Step(h, frames, window, step);
                Assert.Same(drawer, Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view)));
                var top = Top(drawer, view);
                Assert.True(Math.Abs(top - ComposerBottom()) <= 2, $"the drawer's top is at {top:0.0}, the chat box's bottom at {ComposerBottom():0.0}");
                Assert.True(top <= previous + 1, $"the drawer went back down from {previous:0.0} to {top:0.0} while it is reopening");
                previous = top;
            }
            Step(h, frames, window, Chrome.PanelSlide.Duration);
            Assert.Same(drawer, Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view)));
            Assert.Equal(resting - height, Top(drawer, view), tolerance: 1);
            Assert.Equal(resting - height, ComposerBottom(), tolerance: 1);
            Assert.Equal(1, drawer.Opacity, tolerance: 0.001);
            // And it closes again as ever: gone, the chat box back at rest.
            h.Main.OnMain(() => runtime.IsTerminalVisible = false);
            Drain(h, window);
            Step(h, frames, window, Chrome.PanelSlide.Duration);
            Assert.Empty(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
            Assert.Equal(resting, ComposerBottom(), tolerance: 1);
        });
    }

    // OD-0076: every interruption goes on from where the drawer is. Open, close while opening, reopen while closing, close while
    // reopening: one TerminalPanel throughout, its top the chat box's bottom on every frame, never a jump larger than a frame's move.
    [Fact]
    [Trait("Req", "PR-TERM-001")] // App/Views/Chat/ChatView.swift:276-279,290; OD-0076
    public void A_chain_of_opens_and_closes_inside_the_spring_keeps_one_drawer_in_lock_step_with_no_jump()
    {
        WithChat((h, model, view, runtime, composer, window, frames) =>
        {
            double ComposerBottom() => composer.TranslatePoint(new Point(0, composer.Bounds.Height), view)!.Value.Y;
            var resting = ComposerBottom();
            h.Main.OnMain(() => runtime.IsTerminalVisible = true);
            Drain(h, window);
            var drawer = Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
            var height = drawer.Bounds.Height;
            Step(h, frames, window, 0.06);
            var before = Top(drawer, view);
            foreach (var (visible, wait) in new[] { (false, 0.04), (true, 0.04), (false, 0.04), (true, 0.04), (false, 0.04) })
            {
                h.Main.OnMain(() => runtime.IsTerminalVisible = visible);
                Drain(h, window);
                Assert.Same(drawer, Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view)));
                Assert.True(Math.Abs(Top(drawer, view) - before) <= 2, $"the toggle to {visible} moved the drawer's top from {before:0.0} to {Top(drawer, view):0.0}");
                Step(h, frames, window, wait);
                Assert.Same(drawer, Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view)));
                Assert.True(Math.Abs(Top(drawer, view) - ComposerBottom()) <= 2,
                    $"after {wait:0.00} s of {(visible ? "opening" : "closing")} the drawer's top is at {Top(drawer, view):0.0}, the chat box's bottom at {ComposerBottom():0.0}");
                Assert.InRange(Top(drawer, view), resting - height - 1, resting + 1);
                before = Top(drawer, view);
            }
            Step(h, frames, window, Chrome.PanelSlide.Duration);
            Assert.Empty(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
            Assert.Equal(resting, ComposerBottom(), tolerance: 1);
        });
    }

    // The leaving drawer is given back only while its removal runs: reopened after it has left, the drawer is a new panel that opens from
    // below the window as ever (no jump of the chat box, the drawer's first frame at the bottom edge).
    [Fact]
    [Trait("Req", "PR-TERM-001")] // App/Views/Chat/ChatView.swift:276-279,290
    public void The_drawer_reopened_after_it_has_left_is_a_new_panel_opening_from_below_the_window()
    {
        WithChat((h, model, view, runtime, composer, window, frames) =>
        {
            double ComposerBottom() => composer.TranslatePoint(new Point(0, composer.Bounds.Height), view)!.Value.Y;
            var resting = ComposerBottom();
            h.Main.OnMain(() => runtime.IsTerminalVisible = true);
            Drain(h, window);
            var first = Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
            Step(h, frames, window, 1);
            h.Main.OnMain(() => runtime.IsTerminalVisible = false);
            Drain(h, window);
            Step(h, frames, window, 1);
            Assert.Empty(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
            h.Main.OnMain(() => runtime.IsTerminalVisible = true);
            Drain(h, window);
            var second = Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
            Assert.NotSame(first, second);
            Assert.Equal(resting, ComposerBottom(), tolerance: 1);
            Assert.Equal(resting, Top(second, view), tolerance: 1); // a full drawer below, nothing moved yet
            Step(h, frames, window, 0.04);
            Assert.Equal(ComposerBottom(), Top(second, view), tolerance: 2);
            Assert.True(ComposerBottom() < resting - 1);
        });
    }

    // The leaving drawer was built for a directory: the thread's working directory changing while it closes (and the drawer opened at the
    // same moment) is a drawer for the new directory, a new panel; the old one leaves as it was.
    [Fact]
    [Trait("Req", "PR-TERM-001")] // App/Views/Chat/ChatView.swift:276-279
    public void The_drawer_reopened_for_another_directory_while_closing_is_a_new_panel_and_the_old_one_keeps_leaving()
    {
        WithChat((h, model, view, runtime, composer, window, frames) =>
        {
            h.Main.OnMain(() => runtime.IsTerminalVisible = true);
            Drain(h, window);
            var old = Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
            Step(h, frames, window, 1);
            h.Main.OnMain(() => runtime.IsTerminalVisible = false);
            Drain(h, window);
            Step(h, frames, window, 0.1);
            var other = h.NewFolder("worktree");
            h.Main.OnMain(() =>
            {
                model.UpdateThread(runtime.ThreadID, t => t with { WorktreePath = other });
                runtime.IsTerminalVisible = true;
            });
            Drain(h, window);
            var panels = LogicalQuery.SelfAndDescendants<TerminalPanel>(view).ToArray();
            Assert.Equal(2, panels.Length);
            Assert.Contains(old, panels);
            Assert.Single(panels, panel => !ReferenceEquals(panel, old));
            Step(h, frames, window, Chrome.PanelSlide.Duration);
            Assert.NotSame(old, Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view)));
        });
    }
}
