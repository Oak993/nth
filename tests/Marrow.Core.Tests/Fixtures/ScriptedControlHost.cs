using Marrow.Core.Support;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>
/// A control host whose answers a test scripts (nth installer PLAN.md 3a, unit U4). It records every request it is asked,
/// and <see cref="Gate"/> lets a test hold one request open while others go by.
/// </summary>
public sealed class ScriptedControlHost(Func<NthControlRequest, NthControlAnswer> answer) : INthControlHost
{
    private readonly object gate = new();
    private readonly List<NthControlRequest> requests = [];

    public IReadOnlyList<NthControlRequest> Requests
    {
        get
        {
            lock (gate) return [.. requests];
        }
    }

    /// <summary>Runs before the answer is given, for every request; awaited.</summary>
    public Func<NthControlRequest, CancellationToken, Task>? Gate { get; init; }

    public async Task<NthControlAnswer> HandleAsync(NthControlRequest request, CancellationToken cancellationToken)
    {
        lock (gate) requests.Add(request);
        if (Gate is { } hold) await hold(request, cancellationToken);
        return answer(request);
    }

    /// <summary>An ok answer from a pretend nth (process 4242).</summary>
    public static NthControlResponse Ok(NthWorkKind? deferred = null) =>
        new(NthControlReply.Ok, 4242, @"C:\Users\ana\AppData\Local\Programs\nth\current\nth.exe", [], 0, deferred);
}
