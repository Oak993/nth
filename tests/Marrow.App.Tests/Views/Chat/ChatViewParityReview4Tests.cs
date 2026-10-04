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

// REVIEW, scoped adversarial check of round 4 of the ChatView parity unit (2026-10-04): a regression the round 4 fix introduced.
[Collection("Headless")]
public sealed class ChatViewParityReview4Tests(HeadlessSession headless)
{
    private static void Drain(AppModelHarness h, AvaloniaWindow window)
    {
        h.Main.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        h.Main.RunUntilIdle();
    }

    private static void Step(AppModelHarness h, ManualFrameSource frames, AvaloniaWindow window, double seconds)
    {
        h.Main.Advance(TimeSpan.FromSeconds(seconds)); frames.Advance(TimeSpan.Zero); Drain(h, window);
    }

    // SOURCE FACT: ThreadRuntime.swift:240-243 an opened terminal takes the keyboard (`isTerminalVisible` false -> true sets
    // terminalWantsFocus), and TerminalPanel.swift:224-239 the drawer's host makes the terminal view first responder on its
    // update while the flag is set, once. Round 4 (ED-278 (14)) brings the leaving drawer back on a reopen while
    // it is still closing: the SAME TerminalPanel, which never left the tree, so its TerminalHost's attach handler (where the port
    // spent the flag) does not run again. This test proves that the first open takes the keyboard and spends the request, and that
    // the terminal reopened mid-close, the same drawer, takes the keyboard and spends the request too (it failed on round 4's
    // head; fixed in round 5, ED-278 (19), ChatView.RefreshTerminal through TerminalPanel.TakeKeyboardIfWanted).
    [Fact]
    [Trait("Req", "PR-TERM-001")] // App/Runtime/ThreadRuntime.swift:240-243; App/Views/Terminal/TerminalPanel.swift:224-239; OD-0076
    public void REVIEW_The_terminal_reopened_while_it_is_still_closing_takes_the_keyboard_as_an_opened_terminal_does() => ChatHeadlessFixtures.Run(headless, () =>
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
            Control Surface() => Assert.Single(LogicalQuery.SelfAndDescendants<TerminalHostContainer>(view)).HostedView!;
            // Control: the first open takes the keyboard and spends the request.
            h.Main.OnMain(() => runtime.IsTerminalVisible = true);
            Drain(h, window);
            Step(h, frames, window, 1);
            var drawer = Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
            Assert.True(Surface().IsFocused, "the opened terminal did not take the keyboard");
            Assert.False(runtime.TerminalWantsFocus);
            // Close; the keyboard goes to the chat box (the reader clicked into it).
            h.Main.OnMain(() => runtime.IsTerminalVisible = false);
            Drain(h, window);
            var chatBox = LogicalQuery.SelfAndDescendants<ComposerNSTextView>(view).First();
            h.Main.OnMain(() => chatBox.Focus());
            Drain(h, window);
            Assert.True(chatBox.IsFocused);
            Step(h, frames, window, 0.1);
            Assert.False(Surface().IsFocused);
            // Reopen while it is still closing: the same drawer comes back (round 4), and as an opened terminal it takes the keyboard.
            h.Main.OnMain(() => runtime.IsTerminalVisible = true);
            Drain(h, window);
            Step(h, frames, window, 0.04);
            Assert.Same(drawer, Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view)));
            Assert.True(Surface().IsFocused, "the terminal reopened while it was closing did not take the keyboard");
            Assert.False(runtime.TerminalWantsFocus, "the reopen's focus request was left pending");
        }
        finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
    });
}
