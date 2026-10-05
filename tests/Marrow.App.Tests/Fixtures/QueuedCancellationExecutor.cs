using Marrow.Core.Port;
using System.Diagnostics;

namespace Marrow.App.Tests.Fixtures;

public sealed class QueuedCancellationExecutor(IMainExecutor inner, CancellationTokenSource cancellation) : IMainExecutor
{
    public bool Armed { get; set; }
    public bool CheckAccess()
    {
        // Preserve actor access for property reads while forcing the next Run to take its queued path.
        var caller = Armed ? new StackFrame(1).GetMethod() : null;
        return !(caller?.DeclaringType == typeof(MainActor) && caller.Name == nameof(MainActor.Run)) && inner.CheckAccess();
    }
    public void Enqueue(Action work)
    {
        if (!Armed) { inner.Enqueue(work); return; }
        cancellation.Cancel();
        inner.Enqueue(() => { Armed = false; work(); });
    }
}
