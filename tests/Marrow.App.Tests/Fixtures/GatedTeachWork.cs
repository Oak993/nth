using Marrow.App.Assistant.Views;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-19: the Teach model's file work held until a test lets it run, one piece at a time, so ordering and the moment an outcome lands can be seen.</summary>
internal sealed class GatedTeachWork : ITeachWork
{
    private readonly Queue<Action> waiting = new();
    private readonly List<(int Count, TaskCompletionSource Signal)> watchers = [];

    public int Waiting
    {
        get
        {
            lock (waiting) return waiting.Count;
        }
    }

    public Task<T> Run<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (waiting)
        {
            waiting.Enqueue(() =>
            {
                try { done.SetResult(work()); }
                catch (Exception error) { done.SetException(error); }
            });
            foreach (var watcher in watchers.Where(w => w.Count <= waiting.Count).ToList())
            {
                watchers.Remove(watcher);
                watcher.Signal.TrySetResult();
            }
        }
        return done.Task;
    }

    /// <summary>Completes when at least this many pieces of work are waiting.</summary>
    public Task WaitFor(int count)
    {
        lock (waiting)
        {
            if (waiting.Count >= count) return Task.CompletedTask;
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            watchers.Add((count, signal));
            return signal.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    /// <summary>Runs the oldest piece of work that is waiting; false when there is none.</summary>
    public bool ReleaseNext()
    {
        Action? next;
        lock (waiting) next = waiting.Count > 0 ? waiting.Dequeue() : null;
        next?.Invoke();
        return next is not null;
    }
}
