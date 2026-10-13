using Marrow.Core.Support;
using Marrow.Setup.Support;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>
/// A running nth whose every answer the test gives by hand (unit fix ED-NEW-inst-fix-1): each call waits until
/// <see cref="Answer"/> or <see cref="Report"/> is called for it, so a test can hold Update on Ask, let the patience run out,
/// or answer while the progress already shows. <see cref="CancelAsync"/> settles a waiting call as Cancelled, as the handshake does.
/// </summary>
public sealed class GateNth : IRunningNth
{
    private TaskCompletionSource<RunningNthStatus>? pending;

    public List<string> Calls { get; } = [];

    public bool Open { get; set; } = true;

    public RunningNthStatus Status { get; private set; } = RunningNthStatus.Initial;

    public event Action<RunningNthStatus>? StatusChanged;

    /// <summary>The call waiting for its answer, or null.</summary>
    public string? Waiting => pending is { Task.IsCompleted: false } ? Calls[^1] : null;

    public Task<bool> IsNthOpenAsync(CancellationToken cancellationToken = default) => Task.FromResult(Open);

    public Task<RunningNthStatus> AskAsync(CancellationToken cancellationToken = default) => Start("ask");

    public Task<RunningNthStatus> WaitForWorkToFinishAsync(CancellationToken cancellationToken = default) => Start("wait");

    public Task<RunningNthStatus> StopWorkAndQuitAsync(CancellationToken cancellationToken = default) => Start("stop");

    public Task<RunningNthStatus> EndItAsync(CancellationToken cancellationToken = default) => Start("end");

    public Task<RunningNthStatus> KeepWaitingAsync(CancellationToken cancellationToken = default) => Start("keep");

    public Task CancelAsync()
    {
        Calls.Add("cancel");
        if (pending is { Task.IsCompleted: false } waiting)
        {
            Status = new RunningNthStatus(RunningNthState.Cancelled, [], 0);
            StatusChanged?.Invoke(Status);
            waiting.TrySetResult(Status);
        }
        return Task.CompletedTask;
    }

    /// <summary>A report while the call still runs (Closing, Waiting with its threads).</summary>
    public void Report(RunningNthState state, int threads = 0)
    {
        Status = new RunningNthStatus(state, Work(threads), threads);
        StatusChanged?.Invoke(Status);
    }

    /// <summary>The waiting call's answer.</summary>
    public void Answer(RunningNthState state, int threads = 0)
    {
        var waiting = pending ?? throw new InvalidOperationException("No call waits for an answer.");
        Status = new RunningNthStatus(state, Work(threads), threads);
        StatusChanged?.Invoke(Status);
        waiting.TrySetResult(Status);
    }

    private static NthWorkItem[] Work(int threads) => [.. Enumerable.Range(1, threads).Select(i => new NthWorkItem("Thread " + i, NthWorkKind.Thread, 90))];

    private Task<RunningNthStatus> Start(string call)
    {
        Calls.Add(call);
        pending = new TaskCompletionSource<RunningNthStatus>();
        return pending.Task;
    }
}
