using Marrow.App.Assistant;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A subscriber that keeps every line and the threads its handler ran on (AS-2). An optional hook runs first.</summary>
public sealed class FeedCollector : IDisposable
{
    private readonly object gate = new();
    private readonly List<FeedEvent> lines = [];
    private readonly HashSet<int> handlerThreads = [];
    private readonly IDisposable subscription;

    public FeedCollector(AssistantEventBus bus) => subscription = bus.Subscribe(Receive);

    /// <summary>Runs on the bus's task for each batch, before the batch is kept (a test blocks here to stall the subscriber).</summary>
    public Action<IReadOnlyList<FeedEvent>>? Hook { get; set; }

    public IReadOnlyList<FeedEvent> Lines
    {
        get
        {
            lock (gate) return [.. lines];
        }
    }

    public IReadOnlySet<int> HandlerThreads
    {
        get
        {
            lock (gate) return new HashSet<int>(handlerThreads);
        }
    }

    public int Batches { get; private set; }

    public void Dispose() => subscription.Dispose();

    /// <summary>Waits (up to ten seconds) until the lines satisfy the test, then returns them.</summary>
    public async Task<IReadOnlyList<FeedEvent>> UntilAsync(Func<IReadOnlyList<FeedEvent>, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var now = Lines;
            if (done(now)) return now;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"The feed never reached the expected lines; it holds {now.Count}.");
            await Task.Delay(5);
        }
    }

    private void Receive(IReadOnlyList<FeedEvent> batch)
    {
        Hook?.Invoke(batch);
        lock (gate)
        {
            lines.AddRange(batch);
            handlerThreads.Add(Environment.CurrentManagedThreadId);
            Batches++;
        }
    }
}
