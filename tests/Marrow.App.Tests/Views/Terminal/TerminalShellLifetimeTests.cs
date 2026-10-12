using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Terminal;

// Source: DroppyCode/App/Views/Terminal/TerminalStore.swift (B3 52bc792) :59-63 and TerminalPanel.swift's LocalProcessTerminalView
// teardown. On macOS a terminated view's pty is closed, and the hang-up ends the shell. A ConPTY shell has no hang-up, and an
// idle PowerShell prompt ignores the graceful step (Ctrl+C), so the view itself must end the shell and everything it started,
// or every closed, restarted or quit tab leaves a PowerShell and a conhost running. These tests drive that on a stand-in
// shell, so the order of the calls and the one-off kill are proved without a process; TerminalStoreTests proves the same
// against a real PowerShell and a program it started.
[Collection("Headless")]
public sealed class TerminalShellLifetimeTests(HeadlessSession headless)
{
    private static (TerminalHostView View, FakeShellProcess Shell) Started(FakeShells shells)
    {
        var view = new TerminalHostView { Font = SwiftFont.MonospacedSystemFont(12, SwiftFont.Weight.Regular) };
        view.StartProcess(Path.Combine(Path.GetTempPath(), "shell.exe"), [], new Dictionary<string, string>(), "-shell.exe", Path.GetTempPath());
        return (view, Assert.Single(shells.Made));
    }

    [Fact]
    [Trait("Req", "PR-TERM-046")] // App/Views/Terminal/TerminalStore.swift:59-63 view.terminate(); the ConPTY shell does not outlive it
    public void Terminating_a_view_gives_its_running_shell_the_graceful_step_and_then_ends_it() => headless.OnUiThread(() =>
    {
        using var shells = TerminalShells.Fake();
        var (view, shell) = Started(shells);
        Assert.True(view.IsRunning);
        Assert.Equal(shell.ProcessIdentifier, view.ProcessId);
        Assert.NotEqual(0, view.ProcessId);

        view.Terminate();

        Assert.Equal(1, shell.GracefulStops); // :61 the graceful step first
        Assert.Equal(1, shell.Kills);         // and the shell is not left to an idle prompt that ignores it
        Assert.False(view.IsRunning);
        Assert.False(shell.IsRunning);
    });

    [Fact]
    [Trait("Req", "PR-TERM-046")] // App/Views/Terminal/TerminalStore.swift:59-63
    public void Terminating_a_view_disarms_the_report_of_the_exit_its_own_kill_causes() => headless.OnUiThread(() =>
    {
        using var shells = TerminalShells.Fake();
        var (view, shell) = Started(shells);
        var delegateOfView = new RecordingProcessDelegate();
        view.ProcessDelegate = delegateOfView;

        view.Terminate();

        Assert.Equal(1, shell.Kills);
        Assert.Empty(delegateOfView.Exits); // the session was marked stopped by Terminate itself; no late callback reaches it
        Assert.Null(view.ProcessDelegate);
    });

    [Fact]
    [Trait("Req", "PR-TERM-047")] // App/Views/Terminal/TerminalStore.swift:97-100 processTerminated; PR-TERM-046 for the terminate that follows
    public void A_shell_that_exits_by_itself_is_reported_once_and_is_not_killed_when_its_view_is_terminated_later() => headless.OnUiThread(() =>
    {
        using var shells = TerminalShells.Fake();
        var (view, shell) = Started(shells);
        var delegateOfView = new RecordingProcessDelegate();
        view.ProcessDelegate = delegateOfView;

        shell.Exit(3);

        Assert.Equal(new int?[] { 3 }, delegateOfView.Exits);
        Assert.False(view.IsRunning);

        view.Terminate();

        Assert.Equal(0, shell.Kills); // nothing was left to end
        Assert.Equal(new int?[] { 3 }, delegateOfView.Exits);
    });

    [Fact]
    [Trait("Req", "PR-TERM-046")] // App/Views/Terminal/TerminalStore.swift:59-63; a view that never started a shell has nothing to end
    public void Terminating_a_view_that_never_started_a_shell_does_nothing() => headless.OnUiThread(() =>
    {
        using var shells = TerminalShells.Fake();
        var view = new TerminalHostView();
        var shell = Assert.Single(shells.Made);

        view.Terminate();

        Assert.Equal(0, shell.Kills);
        Assert.False(view.IsRunning);
        Assert.Equal(0, view.ProcessId);
    });
}
