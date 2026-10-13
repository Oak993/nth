using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Workspace;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: Tactus's separate workspace for engine tests (unit P18, ED-NEW-pcws-1; DESIGN.md 5.2 FakeWorkspaceHost). Released
// and ready unless a test says otherwise; its windows are the list a test fills (their handles are the child session's own,
// untagged); launches, tool calls and ended turns are recorded; a call answers what OnCall says (by default a plain
// success); a test holds "Take over" with TakenOver.
public sealed class FakeWorkspaceHost : IWorkspaceHost
{
    public bool Released { get; set; } = true;

    public WorkspaceStatus Current { get; set; } = WorkspaceStatus.Ready;

    public List<WorkspaceWindow> Inside { get; } = [];

    public List<string> Launched { get; } = [];

    /// <summary>What a launch does; by default it starts and shows no window yet.</summary>
    public Func<string, WorkspaceLaunch>? OnLaunch { get; set; }

    public List<WorkspaceCall> Calls { get; } = [];

    public Func<WorkspaceCall, IWorkspaceAsks, CancellationToken, Task<PcResult>>? OnCall { get; set; }

    /// <summary>Set while the user has taken over; completing it gives the workspace back.</summary>
    public TaskCompletionSource? TakenOver { get; set; }

    public List<string> EndedTurns { get; } = [];

    public WorkspaceStatus Status() => Current;

    public Task<WorkspaceLaunch> Launch(string appIdOrPath, CancellationToken cancellationToken)
    {
        lock (Launched) Launched.Add(appIdOrPath);
        return Task.FromResult(OnLaunch?.Invoke(appIdOrPath) ?? new WorkspaceLaunch(true, [], null));
    }

    public Task<IReadOnlyList<WorkspaceWindow>> Windows(CancellationToken cancellationToken)
    {
        lock (Inside) return Task.FromResult<IReadOnlyList<WorkspaceWindow>>([.. Inside]);
    }

    public Task<WorkspaceWindow?> Window(long handle, CancellationToken cancellationToken)
    {
        lock (Inside) return Task.FromResult(Inside.FirstOrDefault(w => w.Info.Handle == handle));
    }

    public Task<PcResult> Call(WorkspaceCall call, IWorkspaceAsks asks, CancellationToken cancellationToken)
    {
        lock (Calls) Calls.Add(call);
        return OnCall?.Invoke(call, asks, cancellationToken) ?? Task.FromResult(PcResult.Success(body: "done in the workspace"));
    }

    public Task WhileTakenOver(CancellationToken cancellationToken) => TakenOver?.Task.WaitAsync(cancellationToken) ?? Task.CompletedTask;

    public void EndTurn(string thread)
    {
        lock (EndedTurns) EndedTurns.Add(thread);
    }

    /// <summary>A window inside the workspace, by its own (untagged) handle.</summary>
    public WorkspaceWindow Add(nint handle, string title, string exe, int processId, bool elevated = false)
    {
        var window = new WorkspaceWindow(new PcWindowInfo(handle, title, "Window", processId, "C:\\Apps\\" + exe), elevated);
        lock (Inside) Inside.Add(window);
        return window;
    }
}
