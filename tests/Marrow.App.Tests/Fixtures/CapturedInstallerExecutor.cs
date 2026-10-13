using Marrow.Core.Port;

namespace Marrow.App.Tests.Fixtures;

// Holds actual actor posts without starting their bodies. Disposal drops only this test's unstarted opaque work.
internal sealed class CapturedInstallerExecutor(IMainExecutor original) : IMainExecutor, IDisposable
{
    private readonly List<Action> queued = [];
    internal int QueuedCount => queued.Count;
    public bool CheckAccess() => original.CheckAccess();
    public void Enqueue(Action work) => queued.Add(work);
    public void Dispose() => queued.Clear();
}
