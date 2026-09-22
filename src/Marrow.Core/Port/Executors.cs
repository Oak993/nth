namespace Marrow.Core.Port;

// PORT-ONLY. Where work runs: the main loop, a strand (an actor or a serial queue), the thread pool.
// ED-065, ED-067, ED-069. Source: draft B (proto-B Concurrency/Executors.cs), unchanged in behaviour.

/// <summary>Somewhere work can be enqueued. <see cref="Enqueue"/> runs the work later, never inline.</summary>
public interface IExecutor
{
    void Enqueue(Action work);
}

/// <summary>
/// The main thread's queue. Marrow.App binds one over Avalonia's Dispatcher.UIThread
/// (<c>Marrow.UI.Port.AvaloniaMainExecutor</c>); tests bind a ManualMainLoop. Core never names Avalonia.
/// </summary>
public interface IMainExecutor : IExecutor
{
    /// <summary>True when the calling thread is the main thread (Dispatcher.UIThread.CheckAccess()).</summary>
    bool CheckAccess();
}

/// <summary>The global concurrent pool: where <c>Task.detached</c>, <c>@concurrent</c> and child tasks run.</summary>
public sealed class ThreadPoolExecutor : IExecutor
{
    public static ThreadPoolExecutor Shared { get; } = new();
    private ThreadPoolExecutor() { }

    public void Enqueue(Action work) => ThreadPool.QueueUserWorkItem(static w => w(), work, preferLocal: false);
}

/// <summary>
/// A strand: one work item at a time, FIFO, on a target executor, and the SynchronizationContext of the
/// code it runs, so every await inside that code resumes on the strand. This is a Swift serial executor:
/// an actor, or a serial DispatchQueue. The caller's ExecutionContext flows into each item.
/// </summary>
public sealed class SerialExecutor : SynchronizationContext, IExecutor
{
    [ThreadStatic] private static SerialExecutor? running;

    private readonly Lock gate = new();
    private readonly Queue<(Action Work, ExecutionContext? Context)> queue = new();
    private readonly IExecutor target;
    private bool scheduled;

    public SerialExecutor(IExecutor? target = null) => this.target = target ?? ThreadPoolExecutor.Shared;

    /// <summary>True while the calling thread is running this strand's work.</summary>
    public bool IsCurrent => ReferenceEquals(running, this);

    public void Enqueue(Action work)
    {
        var context = ExecutionContext.Capture();
        bool schedule;
        lock (gate)
        {
            queue.Enqueue((work, context));
            schedule = !scheduled;
            scheduled = true;
        }
        if (schedule) target.Enqueue(Drain);
    }

    public override void Post(SendOrPostCallback d, object? state) => Enqueue(() => d(state));

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (!IsCurrent) throw new SwiftTrapException("A strand cannot be entered synchronously from outside.");
        d(state);
    }

    public override SynchronizationContext CreateCopy() => this;

    private void Drain()
    {
        var previousContext = Current;
        var previousRunning = running;
        SetSynchronizationContext(this);
        running = this;
        try
        {
            while (true)
            {
                (Action Work, ExecutionContext? Context) item;
                lock (gate)
                {
                    if (!queue.TryDequeue(out item))
                    {
                        scheduled = false;
                        return;
                    }
                }
                if (item.Context is null) item.Work();
                else ExecutionContext.Run(item.Context, static w => ((Action)w!)(), item.Work);
                // ExecutionContext.Run restores the thread's context, which resets the ambient
                // SynchronizationContext; put the strand back for the next item.
                SetSynchronizationContext(this);
            }
        }
        finally
        {
            running = previousRunning;
            SetSynchronizationContext(previousContext);
        }
    }
}
