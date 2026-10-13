using System.Collections.Concurrent;
using System.Reflection;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.App.Tests.Fixtures;

// The actual shared Shell entrypoint is replaced for this test's whole fixture lifetime, including detached git work.
internal sealed class C7OfflineShell : IDisposable
{
    private readonly IDisposable scope;
    internal ConcurrentQueue<ShellInvocation> Calls { get; } = new();
    internal C7OfflineShell()
    {
        var boundary = (Ambient<Func<ShellInvocation, Task<ShellResult>>>)typeof(Shell)
            .GetProperty("RunForTesting", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        scope = boundary.Override(call =>
        {
            Calls.Enqueue(call);
            return Task.FromResult(new ShellResult(1, [], []));
        });
    }
    public void Dispose() => scope.Dispose();
}
