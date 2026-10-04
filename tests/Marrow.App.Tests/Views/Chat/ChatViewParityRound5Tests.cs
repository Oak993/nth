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

// Round 5 of the ChatView parity unit (2026-10-04): the reopened drawer's focus request, ED-NEW-parity-chatview (19).
[Collection("Headless")]
public sealed class ChatViewParityRound5Tests(HeadlessSession headless)
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

    // SOURCE FACT: TerminalPanel.swift:224-239 the host spends terminalWantsFocus only for a view in its window
    // (`guard runtime.terminalWantsFocus, let window = view.window else { return }`), leaving the request pending otherwise.
    // The terminal closed and, while it is still closing, its column taken out of the window and the terminal reopened there:
    // the request is not spent out of the window and the leaving drawer is still the one in the column; once the column is
    // back in the window the terminal takes the keyboard and the request is spent. This proves the by-construction claim of
    // ED-NEW-parity-chatview (19) (ChatView observes isTerminalVisible only while attached, so TerminalHost.TakeKeyboardIfWanted
    // needs no window guard of its own); it is not a proof of such a guard.
    [Fact]
    [Trait("Req", "PR-TERM-001")] // App/Runtime/ThreadRuntime.swift:240-243; App/Views/Terminal/TerminalPanel.swift:224-239
    public void A_terminal_reopened_while_its_column_is_out_of_the_window_takes_the_keyboard_once_the_column_is_back() => ChatHeadlessFixtures.Run(headless, () =>
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
            h.Main.OnMain(() => runtime.IsTerminalVisible = true);
            Drain(h, window);
            Step(h, frames, window, 1);
            var drawer = Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
            Assert.False(runtime.TerminalWantsFocus);
            h.Main.OnMain(() => runtime.IsTerminalVisible = false);
            Drain(h, window);
            Step(h, frames, window, 0.1);
            // Out of the window, mid-close; reopened there.
            h.Main.OnMain(() => window.Content = null);
            Drain(h, window);
            h.Main.OnMain(() => runtime.IsTerminalVisible = true);
            Drain(h, window);
            Assert.Same(drawer, Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view)));
            Assert.True(runtime.TerminalWantsFocus, "the request was spent on a drawer that is not in a window");
            h.Main.OnMain(() => window.Content = view);
            Drain(h, window);
            Step(h, frames, window, 1);
            Assert.True(Surface().IsFocused, "the terminal back in the window did not take the keyboard");
            Assert.False(runtime.TerminalWantsFocus);
        }
        finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
    });
}
