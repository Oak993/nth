using Marrow.Core.Port;

namespace Marrow.Testing;

/// <summary>
/// Holds background-to-main deliveries until Release, without changing which thread is main.
/// A view test can inspect an in-flight state even if its background work has already finished.
/// </summary>
public sealed class MainExecutorGate(IMainExecutor target) : IMainExecutor, IDisposable
{
    private readonly Lock gate = new();
    private readonly Queue<Action> pending = new();
    private bool released;

    public bool CheckAccess() => target.CheckAccess();

    public void Enqueue(Action work)
    {
        var context = ExecutionContext.Capture();
        Action delivery = () =>
        {
            if (context is null) work();
            else ExecutionContext.Run(context, static callback => ((Action)callback!)(), work);
        };
        // Observable view updates posted from main must still run while the background
        // completion is held, or the test would freeze the very state it wants to inspect.
        if (target.CheckAccess())
        {
            target.Enqueue(delivery);
            return;
        }
        lock (gate)
        {
            if (!released) pending.Enqueue(delivery);
            else target.Enqueue(delivery);
        }
    }

    public void Release()
    {
        lock (gate)
        {
            if (released) return;
            released = true;
            while (pending.TryDequeue(out var work)) target.Enqueue(work);
        }
    }

    public void Dispose() => Release();
}
