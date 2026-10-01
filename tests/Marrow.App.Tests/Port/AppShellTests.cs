using Avalonia.Controls.ApplicationLifetimes;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Store;
using Xunit;

namespace Marrow.App.Tests.Port;

// PORT-ONLY unit under test: src/Marrow.App/Port/AppShell.cs, the Windows shell that plays NSApplication's part for
// DroppyCode/App/AppDelegate.swift (B3 52bc792). Expectations come from the source lines and rulings cited per test:
//   OD-0008 C1, C2 and PR-LAUNCH-074: closing the main window (its close control, Alt+F4) quits through the terminate
//     handshake: :44-54 applicationShouldTerminate, the reply at :51, then :56-61 applicationWillTerminate
//   ED-004: a session end asks the same question; while a merge runs the query is refused and the 250 ms polling goes
//     on; WM_ENDSESSION runs the three termination steps at once, whatever is left of the deadline
//   ED-156: the settings store is synchronized on a normal exit, after the termination steps
// The shell's exit is the desktop lifetime's Shutdown in the app (it closes every window and raises Exit, whose handler
// is WillTerminate); here it is recorded. ApplicationWillTerminate ends every server in MCPHub's process-wide list
// (AppDelegate.swift:60), so these tests share AppDelegateTests' collection, which never runs in parallel (R-TEST-5).
[Collection("MCPHub processes")]
public sealed class AppShellTests
{
    private const uint WM_ENDSESSION = 0x0016; // winuser.h

    // A model over one project with one thread whose runtime is alive, as AppDelegateTests builds it.
    private static (AppModel Model, ThreadRuntime Runtime) ModelWithLiveRuntime(AppModelHarness h)
    {
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var runtime = h.Main.OnMain(() => model.Runtime(thread.Id));
        return (model, runtime);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-074")] // App/AppDelegate.swift:44-45; owner_decisions.md OD-0008 C1
    [Trait("Req", "PR-LAUNCH-049")] // App/AppDelegate.swift:45
    [Trait("Req", "PR-WIN-073")] // App/AppDelegate.swift:45; owner_decisions.md OD-0036
    public void Closing_the_main_window_with_no_merge_running_quits_at_once()
    {
        using var h = new AppModelHarness();
        var (model, _) = ModelWithLiveRuntime(h);
        var recorder = new ShellRecorder();
        var shell = new AppShell(new AppDelegate { Model = model }, recorder.Exit, recorder.AfterWillTerminate);

        h.Main.OnMain(shell.Terminate);

        Assert.Equal(["exit"], recorder.Calls); // .terminateNow: no wait, no dialog
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-074")] // App/AppDelegate.swift:44-54; owner_decisions.md OD-0008 C1
    [Trait("Req", "PR-LAUNCH-050")] // App/AppDelegate.swift:46-53; owner_decisions.md OD-0036
    [Trait("Req", "PR-WIN-073")] // App/AppDelegate.swift:46-53; owner_decisions.md OD-0036
    public void Closing_the_main_window_during_a_merge_waits_for_it_polling_every_250_ms_then_quits()
    {
        using var h = new AppModelHarness();
        var (model, runtime) = ModelWithLiveRuntime(h);
        h.Main.OnMain(() => runtime.IsHydraMerging = true);
        var recorder = new ShellRecorder();
        var shell = new AppShell(new AppDelegate { Model = model }, recorder.Exit, recorder.AfterWillTerminate);

        h.Main.OnMain(shell.Terminate);
        Assert.Empty(recorder.Calls); // .terminateLater: the app stays up (:53)
        h.Main.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(recorder.Calls);
        h.Main.OnMain(() => runtime.IsHydraMerging = false);
        h.Main.AdvanceMilliseconds(249);
        Assert.Empty(recorder.Calls);
        h.Main.AdvanceMilliseconds(1); // the poll at 1.25 s replies true (:51) and the quit goes on

        Assert.Equal(["exit"], recorder.Calls);
        h.Main.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(["exit"], recorder.Calls);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Source-derived partial proof; the STOP prevents a whole Windows contract claim.
    [Trait("Req", "PR-LAUNCH-050")] // App/AppDelegate.swift:47-51; owner_decisions.md OD-0036
    [Trait("Req", "PR-WIN-073")] // App/AppDelegate.swift:47-51; owner_decisions.md OD-0036
    public void A_merge_that_never_ends_holds_the_close_for_exactly_4_minutes()
    {
        using var h = new AppModelHarness();
        var (model, runtime) = ModelWithLiveRuntime(h);
        h.Main.OnMain(() => runtime.IsHydraMerging = true);
        var recorder = new ShellRecorder();
        var shell = new AppShell(new AppDelegate { Model = model }, recorder.Exit, recorder.AfterWillTerminate);

        h.Main.OnMain(shell.Terminate);
        h.Main.Advance(TimeSpan.FromSeconds(4 * 60) - TimeSpan.FromMilliseconds(1));
        Assert.Empty(recorder.Calls);
        h.Main.AdvanceMilliseconds(1);

        Assert.Equal(["exit"], recorder.Calls);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-074")] // App/AppDelegate.swift:44-54; owner_decisions.md OD-0008 C1, C2
    public void A_close_asked_again_while_the_quit_waits_asks_nothing_more_and_the_app_quits_once()
    {
        using var h = new AppModelHarness();
        var (model, runtime) = ModelWithLiveRuntime(h);
        h.Main.OnMain(() => runtime.IsHydraMerging = true);
        var recorder = new ShellRecorder();
        var shell = new AppShell(new AppDelegate { Model = model }, recorder.Exit, recorder.AfterWillTerminate);

        h.Main.OnMain(shell.Terminate);
        h.Main.Advance(TimeSpan.FromSeconds(1));
        h.Main.OnMain(shell.Terminate); // Alt+F4 while the first close waits
        h.Main.OnMain(() => runtime.IsHydraMerging = false);
        h.Main.AdvanceMilliseconds(250);

        Assert.Equal(["exit"], recorder.Calls);
        h.Main.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(["exit"], recorder.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-004")] // engineering_decisions.md ED-004 (2); App/AppDelegate.swift:44-54
    [Trait("Req", "PR-LAUNCH-050")] // App/AppDelegate.swift:46-53 (windows_path: the shutdown path offers the same deferral); owner_decisions.md OD-0036
    public void A_session_end_during_a_merge_is_refused_and_the_app_quits_when_the_merge_ends()
    {
        using var h = new AppModelHarness();
        var (model, runtime) = ModelWithLiveRuntime(h);
        h.Main.OnMain(() => runtime.IsHydraMerging = true);
        var recorder = new ShellRecorder();
        var shell = new AppShell(new AppDelegate { Model = model }, recorder.Exit, recorder.AfterWillTerminate);

        var first = new ShutdownRequestedEventArgs();
        h.Main.OnMain(() => shell.ShutdownRequested(first));
        Assert.True(first.Cancel); // refused while the merge runs
        var again = new ShutdownRequestedEventArgs();
        h.Main.OnMain(() => shell.ShutdownRequested(again));
        Assert.True(again.Cancel); // still waiting: refused again, no second question
        Assert.Empty(recorder.Calls);
        h.Main.OnMain(() => runtime.IsHydraMerging = false);
        h.Main.AdvanceMilliseconds(250);

        Assert.Equal(["exit"], recorder.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-004")] // engineering_decisions.md ED-004 (2); App/AppDelegate.swift:45
    [Trait("Req", "PR-LAUNCH-049")] // App/AppDelegate.swift:45 (windows_path: the shutdown handler answers "may close" at once)
    public void A_session_end_with_no_merge_running_is_let_through_for_the_platform_to_close_the_app()
    {
        using var h = new AppModelHarness();
        var (model, _) = ModelWithLiveRuntime(h);
        var recorder = new ShellRecorder();
        var shell = new AppShell(new AppDelegate { Model = model }, recorder.Exit, recorder.AfterWillTerminate);

        var request = new ShutdownRequestedEventArgs();
        h.Main.OnMain(() => shell.ShutdownRequested(request));

        Assert.False(request.Cancel); // the lifetime closes the windows and raises Exit itself
        Assert.Empty(recorder.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-156")] // engineering_decisions.md ED-156 (the start-up unit synchronizes on a normal exit)
    [Trait("Req", "PR-LAUNCH-051")] // App/AppDelegate.swift:56-61
    public void Termination_runs_the_three_steps_once_and_then_flushes_the_settings()
    {
        using var h = new AppModelHarness();
        var (model, _) = ModelWithLiveRuntime(h);
        var ledger = h.Main.OnMain(FreshTokenLedger.Make);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        h.Main.OnMain(() => ledger.Record(1234)); // a debounced live save is pending (TokenLedger.swift:138-144)
        var liveSpendWrittenFirst = false;
        var recorder = new ShellRecorder();
        var shell = new AppShell(new AppDelegate { Model = model }, recorder.Exit, () =>
        {
            liveSpendWrittenFirst = File.Exists(FreshTokenLedger.LiveSpendFile); // :58 ran before the flush
            recorder.AfterWillTerminate();
        });

        h.Main.OnMain(shell.WillTerminate);
        h.Main.OnMain(shell.WillTerminate); // the lifetime's Exit after a session end already ran them

        Assert.True(liveSpendWrittenFirst);
        Assert.Equal(["after will terminate"], recorder.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-004")] // engineering_decisions.md ED-004 (2)
    public void A_session_that_ends_runs_the_termination_steps_at_once_during_a_merge_and_only_once()
    {
        using var h = new AppModelHarness();
        var (model, runtime) = ModelWithLiveRuntime(h);
        h.Main.OnMain(() => runtime.IsHydraMerging = true);
        var recorder = new ShellRecorder();
        var shell = new AppShell(new AppDelegate { Model = model }, recorder.Exit, recorder.AfterWillTerminate);
        h.Main.OnMain(() => shell.ShutdownRequested(new ShutdownRequestedEventArgs())); // refused, polling

        h.Main.OnMain(() =>
        {
            var handled = false;
            Assert.Equal(0, shell.WndProc(0, WM_ENDSESSION, 0, 0, ref handled)); // wParam FALSE: the session goes on
            Assert.False(handled);
        });
        Assert.Empty(recorder.Calls);
        h.Main.OnMain(() =>
        {
            var handled = false;
            Assert.Equal(0, shell.WndProc(0, WM_ENDSESSION, 1, 0, ref handled)); // wParam TRUE: the session is ending
            Assert.False(handled);
        });

        Assert.Equal(["after will terminate"], recorder.Calls); // at once, 4 minutes or not
        h.Main.OnMain(shell.WillTerminate); // an Exit that follows does not run them again
        Assert.Equal(["after will terminate"], recorder.Calls);
    }
}
