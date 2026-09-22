using Marrow.Core.Port;

namespace Marrow.Core.Tests.Fixtures;

// Literal ports of the Swift shapes the concurrency rules are about, used by the tests. Helper types live in
// fixture files, never inside a test class (R-TEST-3). Each cites the Swift it transcribes.

/// <summary>Services/MCP/MCPOAuth.swift:62-74, OAuthRefreshGate, ported literally onto the Actor base.</summary>
public sealed class OAuthRefreshGate(IExecutor? target = null) : Actor(target)
{
    private readonly Dictionary<string, TaskHandle<string>> flights = new(StringComparer.Ordinal);

    public Task<string> Token(string serverID, Func<Task<string>> refresh) => Isolated(async () =>
    {
        if (flights.TryGetValue(serverID, out var existing)) return await existing.Value;
        var flight = TaskHandle.DetachedThrowing(refresh); // Task.detached { try await refresh() }
        flights[serverID] = flight;
        try { return await flight.Value; }
        finally { flights.Remove(serverID); } // defer: runs when the FIRST caller resumes
    });
}

/// <summary>The tempting wrong port of OAuthRefreshGate: a SemaphoreSlim held across the await.</summary>
public sealed class SerializedRefreshGate
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, Task<string>> flights = new(StringComparer.Ordinal);

    public async Task<string> Token(string serverID, Func<Task<string>> refresh)
    {
        await gate.WaitAsync();
        try
        {
            if (flights.TryGetValue(serverID, out var flight)) return await flight;
            flight = Task.Run(refresh);
            flights[serverID] = flight;
            try { return await flight; }
            finally { flights.Remove(serverID); }
        }
        finally { gate.Release(); }
    }
}

/// <summary>Services/Providers/ACPSession.swift:7-21, ACPLaunchGate: the slot is reserved BEFORE the sleep.</summary>
public sealed class ACPLaunchGate(double spacing, IExecutor? target = null) : Actor(target)
{
    private DateTimeOffset nextSlot = SwiftDate.DistantPast;

    public Task Wait() => Isolated(async () =>
    {
        var now = SwiftDate.Now;
        var slot = now > nextSlot ? now : nextSlot; // max(now, nextSlot)
        nextSlot = SwiftDate.AddingTimeInterval(slot, spacing);
        if (slot > now) await SwiftTask.TrySleep(SwiftTask.Seconds(SwiftDate.TimeIntervalSince(slot, now)));
    });
}

/// <summary>An actor that counts overlapping segments, to prove one segment at a time and inline same-actor calls.</summary>
public sealed class CounterActor : Actor
{
    private int value;
    private int inside;

    public int MaxInside { get; private set; }

    public Task Increment() => Isolated(async () =>
    {
        Enter();
        var read = value;
        value = read + 1; // one synchronous segment
        Exit();
        await Task.Yield(); // the actor is released here
        Enter();
        Exit();
    });

    public Task<int> Value() => Isolated(() => value);

    public Task<int> Twice() => Isolated(async () =>
    {
        await Increment(); // a same-actor call: inline, no hop, no deadlock
        await Increment();
        return value;
    });

    public Task<bool> SeesCallerCancellation() => Isolated(() => SwiftTask.IsCancelled);

    private void Enter()
    {
        AssertIsolated();
        MaxInside = Math.Max(MaxInside, Interlocked.Increment(ref inside));
    }

    private void Exit() => Interlocked.Decrement(ref inside);
}

/// <summary>App/Views/Sidebar/SidebarView.swift:391-403 noteListScroll, as a main-actor model.</summary>
public sealed class SidebarScrollModel
{
    private TaskHandle? scrollSettle;

    public bool IsListScrolling { get; private set; }

    public int RaiseCount { get; private set; }

    public void NoteListScroll()
    {
        MainActor.AssumeIsolated();
        if (!IsListScrolling)
        {
            IsListScrolling = true;
            RaiseCount++;
        }
        scrollSettle?.Cancel();
        scrollSettle = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(150));
            if (SwiftTask.IsCancelled || !IsListScrolling) return;
            IsListScrolling = false;
        });
    }
}

/// <summary>App/Views/Terminal/TerminalPanel.swift:414-425 resizeSubviews: a 50 ms work item, NOT re-armed while pending.</summary>
public sealed class TerminalResizeModel
{
    private WorkItem? pendingSync;

    public int Syncs { get; private set; }

    public void ResizeSubviews()
    {
        if (pendingSync is not null) return;
        pendingSync = MainActor.AsyncAfter(TimeSpan.FromMilliseconds(50), () =>
        {
            pendingSync = null;
            Syncs++;
        });
    }
}

/// <summary>Services/Store/Storage.swift:337-356 DiskWriter.encodeAndWrite: a queued save is replaced by the newest snapshot.</summary>
public sealed class DiskWriterSample
{
    private readonly SerialQueue queue = new("droppycode.storage.write");
    private readonly Mutex<Dictionary<string, string>> pending = new(new(StringComparer.Ordinal));

    public List<string> Written { get; } = [];

    public void EncodeAndWrite(string value, string url)
    {
        var queued = pending.WithLock((ref Dictionary<string, string> p) =>
        {
            var wasQueued = p.ContainsKey(url);
            p[url] = value;
            return wasQueued;
        });
        if (queued) return;
        queue.Async(() =>
        {
            var next = pending.WithLock((ref Dictionary<string, string> p) => p.Remove(url, out var v) ? v : null);
            if (next is not null) Written.Add(next);
        });
    }

    public void Block(Task until) => queue.Async(() => until.Wait());

    public void WaitForQueuedWrites() => queue.Sync(() => { });
}

/// <summary>A synchronous helper that checks Task.isCancelled, as Swift code may anywhere inside a task.</summary>
public static class CancellationProbe
{
    public static bool DeepCheck() => SwiftTask.IsCancelled;
}
