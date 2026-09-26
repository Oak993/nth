using Marrow.Core.Port;

namespace Marrow.Testing;

/// <summary>
/// ED-207. Records every <c>Shell.Run</c> call the test's own async flow makes, in call order, while the real
/// processes run: <c>using var shell = ShellRecording.Start();</c>, then read <see cref="Calls"/>. Other tests'
/// flows are never recorded, so test classes still run in parallel (R-TEST-5).
/// </summary>
public sealed class ShellRecording : IDisposable
{
    private readonly object gate = new();
    private readonly List<ShellInvocation> calls = [];
    private readonly IDisposable scope;

    private ShellRecording() => scope = ShellObserver.Observe(Record);

    /// <summary>Starts recording for the calling async flow until the result is disposed.</summary>
    public static ShellRecording Start() => new();

    /// <summary>The calls recorded so far, in the order they were made.</summary>
    public IReadOnlyList<ShellInvocation> Calls
    {
        get
        {
            lock (gate) return calls.ToArray();
        }
    }

    public void Dispose() => scope.Dispose();

    private void Record(ShellInvocation call)
    {
        lock (gate) calls.Add(call);
    }
}
