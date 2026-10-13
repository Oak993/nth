using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (Dextra AS-20): Windows Hello as a script. Each check takes the next scripted answer (Verified when the script
/// is empty) and records what the prompt said it was for. <see cref="Hold"/> keeps the next check open until the test lets
/// it go, for the one-check-at-a-time rule and for what a page shows while Windows asks.
/// </summary>
public sealed class FakeHelloGate : IHelloGate
{
    private readonly Queue<HelloResult> script = new();
    private TaskCompletionSource<HelloResult>? held;
    private TaskCompletionSource<HelloResult>? open;

    public List<string> Reasons { get; } = [];

    public int Asked => Reasons.Count;

    /// <summary>When set, every check throws it (a gate that broke).</summary>
    public Exception? Throws { get; set; }

    public FakeHelloGate Then(params HelloResult[] results)
    {
        foreach (var result in results) script.Enqueue(result);
        return this;
    }

    /// <summary>The next check waits until <see cref="Release"/>.</summary>
    public void Hold()
    {
        open = null;
        held = new TaskCompletionSource<HelloResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Release(HelloResult result) => (open ?? held)?.TrySetResult(result);

    /// <summary>Whether a held check is waiting on <see cref="Release"/>.</summary>
    public bool Waiting => open is { Task.IsCompleted: false };

    public Task<HelloResult> Verify(string reason, CancellationToken cancellationToken)
    {
        Reasons.Add(reason);
        if (Throws is { } error) throw error;
        if (held is { } wait)
        {
            held = null;
            open = wait;
            return wait.Task;
        }
        return Task.FromResult(script.Count > 0 ? script.Dequeue() : HelloResult.Verified);
    }
}
