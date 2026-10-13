using Marrow.Core.Port;

namespace Marrow.App.Tests.Fixtures;

// Holds actual actor posts without starting their bodies. Disposal drops only this test's unstarted opaque work.
internal sealed class CapturedInstallerExecutor(IMainExecutor original) : IMainExecutor, IDisposable
{
    private readonly List<Action> queued = [];
    internal int QueuedCount => queued.Count;
    public bool CheckAccess() => original.CheckAccess();
    public void Enqueue(Action work) => queued.Add(work);
    internal void RunNext()
    {
        var work = queued[0];
        queued.RemoveAt(0);
        work();
    }
    internal void RunUntil(Func<bool> done)
    {
        for (var i = 0; i < 16; i++)
        {
            if (done()) return;
            if (queued.Count == 0) throw new InvalidOperationException("No captured work reaches the requested boundary.");
            RunNext();
        }
        throw new InvalidOperationException("Captured work exceeded the deterministic boundary.");
    }
    public void Dispose() => queued.Clear();
}
