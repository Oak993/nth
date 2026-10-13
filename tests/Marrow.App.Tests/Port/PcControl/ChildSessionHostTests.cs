using System.Text.Json.Nodes;
using Marrow.App.Port.PcControl.Workspace;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Workspace;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Port.PcControl;

// PORT-ONLY: Tactus, the separate workspace as the app runs it (unit P18, ED-NEW-pcws-2, ED-NEW-pcws-5, ED-NEW-pcws-7;
// DESIGN.md 2.13). On fakes only: the status reads Windows' child-session switch and never writes it; nothing starts while
// the preview is not released or the switch is off; released and ready, the first launch starts one workspace (one window,
// one pipe, the agent's program line with the token), take-over holds steps until the user gives it back, and when the
// session ends the workspace is Ready again; an agent that never connects is given up after 30 s; no log carries the token.
[Trait("Decision", "ED-NEW-pcws-7")]
public sealed class ChildSessionHostTests
{
    private const string Notepad = "C:\\Apps\\notepad.exe";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static ChildSessionSeams Idle(IChildSessionReadiness readiness, List<int> made, bool released, IPcClock? clock = null, Func<string, CancellationToken, Task<Stream>>? accept = null, List<string>? log = null) => new()
    {
        Readiness = readiness,
        Surface = () =>
        {
            made.Add(1);
            return new FakeWorkspaceSurface();
        },
        Accept = accept ?? ((_, _) =>
        {
            made.Add(2);
            return new TaskCompletionSource<Stream>().Task;
        }),
        Executable = WorkspaceAgentStandIn.Exe,
        Clock = clock ?? new PcManualClock(),
        Log = line =>
        {
            if (log is null) return;
            lock (log) log.Add(line);
        },
        Released = released,
        NewName = () => WorkspaceAgentStandIn.Name,
        NewToken = () => WorkspaceAgentStandIn.Token,
    };

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-2")]
    public async Task The_status_reads_the_child_session_switch_never_writes_it_and_nothing_runs_until_a_launch()
    {
        var sessions = new FakeChildSessions();
        var made = new List<int>();
        using var host = new ChildSessionHost(Idle(sessions, made, released: true));
        Assert.Equal(WorkspaceStatus.NotSetUp, host.Status());
        sessions.On = false;
        Assert.Equal(WorkspaceStatus.NotSetUp, host.Status());
        sessions.On = true;
        Assert.Equal(WorkspaceStatus.Ready, host.Status());
        Assert.Equal(3, sessions.Reads);
        Assert.False(host.Watch());
        Assert.Empty(await host.Windows(CancellationToken.None));
        Assert.Null(await host.Window(0x1a, CancellationToken.None));
        var call = await host.Call(new WorkspaceCall("chat-1", PcToolSchemas.Observe, new JsonObject()), new FakeWorkspaceAsks(), CancellationToken.None);
        Assert.Equal(PcErrorCode.WindowGone, call.Error);
        Assert.Contains(ChildSessionHost.NotRunning, call.Reason, StringComparison.Ordinal);
        Assert.True(host.WhileTakenOver(CancellationToken.None).IsCompleted);
        host.EndTurn("chat-1");
        Assert.Empty(made);
        Assert.Empty(sessions.Writes);
    }

    [Fact]
    public async Task While_the_preview_is_not_released_a_launch_makes_no_window_and_no_pipe()
    {
        var sessions = new FakeChildSessions { On = true };
        var made = new List<int>();
        using var host = new ChildSessionHost(Idle(sessions, made, released: false));
        var launched = await host.Launch(Notepad, CancellationToken.None);
        Assert.False(launched.Started);
        Assert.Equal(WorkspacePreview.NotReleasedLine, launched.Problem);
        Assert.False(host.Released);
        Assert.Empty(made);
        Assert.Empty(sessions.Writes);
        using var shipped = new ChildSessionHost(new ChildSessionSeams { Readiness = sessions, Surface = () => new FakeWorkspaceSurface(), Accept = (_, _) => new TaskCompletionSource<Stream>().Task, Executable = WorkspaceAgentStandIn.Exe });
        Assert.Equal(WorkspacePreview.Released, shipped.Released);
        Assert.False(shipped.Released);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-2")]
    public async Task Released_but_not_set_up_a_launch_says_where_to_set_it_up_and_changes_nothing()
    {
        var sessions = new FakeChildSessions { On = false };
        var made = new List<int>();
        using var host = new ChildSessionHost(Idle(sessions, made, released: true));
        var launched = await host.Launch(Notepad, CancellationToken.None);
        Assert.False(launched.Started);
        Assert.Contains("not set up", launched.Problem, StringComparison.Ordinal);
        Assert.Contains("Separate workspace", launched.Problem, StringComparison.Ordinal);
        Assert.Empty(made);
        Assert.Empty(sessions.Writes);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-5")]
    public async Task Released_and_ready_the_first_launch_starts_one_workspace_that_runs_until_its_session_ends()
    {
        await using var child = new WorkspaceAgentStandIn();
        var sessions = new FakeChildSessions { On = true };
        using var host = new ChildSessionHost(child.Seams(sessions));

        var first = host.Launch(Notepad, CancellationToken.None);
        var second = host.Launch("C:\\Apps\\calc.exe", CancellationToken.None);
        var notepad = await first.WaitAsync(Patience);
        var calculator = await second.WaitAsync(Patience);
        Assert.True(notepad.Started, notepad.Problem);
        Assert.True(calculator.Started, calculator.Problem);
        Assert.Contains(notepad.Windows, window => window.Info.Handle == 0x1a);
        Assert.Single(child.Made);
        Assert.Equal(new[] { WorkspaceAgentStandIn.Name }, child.Names);
        Assert.Equal(WorkspacePipe.StartProgram(WorkspaceAgentStandIn.Exe, WorkspaceAgentStandIn.Name, WorkspaceAgentStandIn.Token), child.First.Programs.Single());
        Assert.Equal(2, (await host.Windows(CancellationToken.None)).Count);
        Assert.Equal(new WorkspaceStatus(WorkspaceState.Running, 2), host.Status());
        Assert.Equal("Calculator", (await host.Window(0x2b, CancellationToken.None))!.Info.Title);

        Assert.True(host.Watch());
        Assert.Equal(1, child.First.Watches);
        child.First.TakeOver(true);
        var held = host.WhileTakenOver(CancellationToken.None);
        Assert.True(await PcTestWait.StillWaiting(held));
        child.First.TakeOver(false);
        await held.WaitAsync(Patience);

        child.First.End();
        Assert.Equal(WorkspaceStatus.Ready, host.Status());
        Assert.True(child.First.Disposed);
        Assert.False(host.Watch());
        Assert.Equal(WorkspaceAgent.ExitDone, await child.Serving!.WaitAsync(Patience));

        var again = await host.Launch(Notepad, CancellationToken.None);
        Assert.False(again.Started);
        Assert.Equal(WorkspaceAgentStandIn.LaterProblem, again.Problem);
        Assert.Equal(2, child.Made.Count);
        Assert.True(child.Made[1].Disposed);
        Assert.Equal(WorkspaceStatus.Ready, host.Status());

        Assert.Contains("workspace.running", child.Log);
        Assert.Contains("workspace.session.ended", child.Log);
        Assert.Contains("workspace.start.failed", child.Log);
        Assert.DoesNotContain(child.Log.Concat(child.AgentLog), line => line.Contains(WorkspaceAgentStandIn.Token, StringComparison.Ordinal));
        Assert.Empty(sessions.Writes);
    }

    [Fact]
    public async Task Closing_nth_ends_the_running_workspace_and_its_window()
    {
        await using var child = new WorkspaceAgentStandIn();
        var sessions = new FakeChildSessions { On = true };
        using var host = new ChildSessionHost(child.Seams(sessions));
        Assert.True((await host.Launch(Notepad, CancellationToken.None)).Started);
        child.First.TakeOver(true);
        var held = host.WhileTakenOver(CancellationToken.None);
        host.Dispose();
        await held.WaitAsync(Patience);
        Assert.True(child.First.Disposed);
        Assert.Equal(WorkspaceStatus.Ready, host.Status());
        Assert.Equal(WorkspaceAgent.ExitDone, await child.Serving!.WaitAsync(Patience));
        Assert.Contains("workspace.host.disposed", child.Log);
        var after = await host.Launch(Notepad, CancellationToken.None);
        Assert.False(after.Started);
        Assert.Single(child.Made);
    }

    [Fact]
    public async Task An_agent_that_never_connects_is_given_up_after_30_s_and_its_window_goes()
    {
        var sessions = new FakeChildSessions { On = true };
        var made = new List<int>();
        var clock = new PcManualClock();
        var pending = new TaskCompletionSource<Stream>();
        var log = new List<string>();
        using var host = new ChildSessionHost(Idle(sessions, made, released: true, clock, (_, cancellationToken) =>
        {
            cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
            return pending.Task;
        }, log));
        var launching = host.Launch(Notepad, CancellationToken.None);
        await PcTestWait.Until(() => clock.Pending == 1);
        clock.Advance(ChildSessionHost.AcceptTimeoutMs - 1);
        Assert.True(await PcTestWait.StillWaiting(launching));
        clock.Advance(1);
        var launched = await launching.WaitAsync(Patience);
        Assert.False(launched.Started);
        Assert.Equal("the workspace's agent did not connect within 30 s", launched.Problem);
        Assert.True(pending.Task.IsCanceled);
        Assert.Equal(WorkspaceStatus.Ready, host.Status());
        Assert.Contains("workspace.agent.silent", log);
        Assert.Equal(new[] { 1 }, made);
        Assert.Empty(sessions.Writes);
    }
}
