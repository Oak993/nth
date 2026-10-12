using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0140, ED-NEW-pcctl-6). Several apps at once: one ordered stream per app, streams of
// different apps side by side, the active set, a full stream, and cancellation that never lets two calls overlap.
[Trait("Decision", "ED-NEW-pcctl-6")]
public sealed class PcLanesTests
{
    private static Func<CancellationToken, Task<string>> Held(TaskCompletionSource gate, List<string> log, string name) => async _ =>
    {
        lock (log) log.Add(name + " start");
        await gate.Task.ConfigureAwait(false);
        lock (log) log.Add(name + " end");
        return name;
    };

    [Fact]
    public async Task Calls_for_one_app_run_in_order_and_never_overlap()
    {
        var lanes = new PcLanes();
        var log = new List<string>();
        var first = new TaskCompletionSource();
        var a = lanes.Run("app:notepad", Held(first, log, "a"), CancellationToken.None);
        var b = lanes.Run("app:notepad", _ =>
        {
            lock (log) log.Add("b start");
            return Task.FromResult("b");
        }, CancellationToken.None);
        await Task.Delay(50);
        Assert.False(b.IsCompleted);
        Assert.Equal(new[] { "a start" }, log);
        first.SetResult();
        Assert.Equal("a", await a);
        Assert.Equal("b", await b);
        Assert.Equal(new[] { "a start", "a end", "b start" }, log);
    }

    [Fact]
    public async Task Calls_for_different_apps_run_side_by_side()
    {
        var lanes = new PcLanes();
        var log = new List<string>();
        var slow = new TaskCompletionSource();
        var a = lanes.Run("app:excel", Held(slow, log, "excel"), CancellationToken.None);
        var b = await lanes.Run("app:paint", _ => Task.FromResult("paint"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("paint", b);
        Assert.False(a.IsCompleted);
        slow.SetResult();
        Assert.Equal("excel", await a);
    }

    [Fact]
    public async Task The_active_set_holds_every_app_with_work_in_flight()
    {
        var lanes = new PcLanes();
        var seen = new List<IReadOnlyList<string>>();
        lanes.ActiveChanged += active => { lock (seen) seen.Add(active); };
        var one = new TaskCompletionSource();
        var two = new TaskCompletionSource();
        var a = lanes.Run("app:one", Held(one, [], "one"), CancellationToken.None);
        var b = lanes.Run("app:two", Held(two, [], "two"), CancellationToken.None);
        Assert.Equal(new[] { "app:one", "app:two" }, lanes.Active);
        one.SetResult();
        await a;
        Assert.Equal(new[] { "app:two" }, lanes.Active);
        two.SetResult();
        await b;
        Assert.Empty(lanes.Active);
        lock (seen) Assert.Equal(new[] { 1, 2, 1, 0 }, seen.Select(s => s.Count));
    }

    [Fact]
    public async Task A_full_stream_refuses_the_next_call()
    {
        var lanes = new PcLanes(maxQueued: 2);
        var gate = new TaskCompletionSource();
        var a = lanes.Run("app:x", Held(gate, [], "a"), CancellationToken.None);
        var b = lanes.Run("app:x", Held(gate, [], "b"), CancellationToken.None);
        var full = Assert.Throws<PcLaneFullException>(() => { _ = lanes.Run("app:x", Held(gate, [], "c"), CancellationToken.None); });
        Assert.Equal("app:x", full.Key);
        // Another app still has room.
        Assert.Equal("y", await lanes.Run("app:y", _ => Task.FromResult("y"), CancellationToken.None));
        gate.SetResult();
        await Task.WhenAll(a, b);
    }

    [Fact]
    public async Task A_call_cancelled_while_waiting_never_lets_the_next_one_overlap()
    {
        var lanes = new PcLanes();
        var log = new List<string>();
        var first = new TaskCompletionSource();
        var a = lanes.Run("app:x", Held(first, log, "a"), CancellationToken.None);
        using var cancel = new CancellationTokenSource();
        var b = lanes.Run("app:x", Held(first, log, "b"), cancel.Token);
        var c = lanes.Run("app:x", _ =>
        {
            lock (log) log.Add("c start");
            return Task.FromResult("c");
        }, CancellationToken.None);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b);
        await Task.Delay(50);
        Assert.False(c.IsCompleted);
        first.SetResult();
        await Task.WhenAll(a, c);
        Assert.Equal(new[] { "a start", "a end", "c start" }, log);
    }

    [Fact]
    public async Task A_failed_call_does_not_block_the_app_stream()
    {
        var lanes = new PcLanes();
        var failed = lanes.Run<string>("app:x", _ => throw new InvalidOperationException("boom"), CancellationToken.None);
        var next = lanes.Run("app:x", _ => Task.FromResult("next"), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed);
        Assert.Equal("next", await next);
        Assert.Empty(lanes.Active);
    }
}
