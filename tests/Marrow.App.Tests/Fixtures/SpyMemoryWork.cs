using Marrow.App.Assistant.Views;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-12: the Memory model's file work run on the pool with the thread it ran on recorded, and the outcomes held until a test applies them (the "UI thread").</summary>
public sealed class SpyMemoryWork : IMemoryWork
{
    public List<int> RunThreads { get; } = [];

    public Queue<Action> Posted { get; } = [];

    public Task<T> Run<T>(Func<T> work) => Task.Run(() =>
    {
        lock (RunThreads) RunThreads.Add(Environment.CurrentManagedThreadId);
        return work();
    });

    public void Post(Action ui)
    {
        lock (Posted) Posted.Enqueue(ui);
    }

    public void Drain()
    {
        while (true)
        {
            Action? next;
            lock (Posted) next = Posted.Count > 0 ? Posted.Dequeue() : null;
            if (next is null) return;
            next();
        }
    }
}

/// <summary>Dextra AS-12: file work that waits at a gate, so a test can press again while a write is running.</summary>
public sealed class GatedMemoryWork(Task gate, TaskCompletionSource started) : IMemoryWork
{
    public async Task<T> Run<T>(Func<T> work)
    {
        started.TrySetResult();
        await gate.ConfigureAwait(false);
        return work();
    }

    public void Post(Action ui) => ui();
}
