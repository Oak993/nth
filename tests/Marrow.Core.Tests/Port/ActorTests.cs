using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// R-ACTOR-1 (IMPLEMENTING.md 3.4). Source: draft B (proto-B ActorTests).
public sealed class ActorTests
{
    [Fact]
    [Trait("Rule", "R-ACTOR-1")]
    [Trait("Decision", "ED-067")]
    public async Task Concurrent_refreshes_join_one_flight()
    {
        var gate = new OAuthRefreshGate();
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshes = 0;
        Task<string> Refresh()
        {
            Interlocked.Increment(ref refreshes);
            return release.Task;
        }
        var first = gate.Token("s", Refresh);
        await WaitFor(() => Volatile.Read(ref refreshes) == 1);
        var second = gate.Token("s", Refresh);
        release.SetResult("token-1");
        Assert.Equal(["token-1", "token-1"], await Task.WhenAll(first, second));
        Assert.Equal(1, refreshes);
    }

    [Fact]
    [Trait("Rule", "R-ACTOR-1")]
    public async Task A_lock_held_across_await_breaks_the_join()
    {
        // The pitfall: the SemaphoreSlim port serializes the callers, so the second one refreshes again.
        var gate = new SerializedRefreshGate();
        var refreshes = 0;
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string> Refresh() => Interlocked.Increment(ref refreshes) == 1 ? release.Task : Task.FromResult("token-2");
        var first = gate.Token("s", Refresh);
        await WaitFor(() => Volatile.Read(ref refreshes) == 1);
        var second = gate.Token("s", Refresh);
        release.SetResult("token-1");
        Assert.Equal(["token-1", "token-2"], await Task.WhenAll(first, second));
        Assert.Equal(2, refreshes);
    }

    [Fact]
    [Trait("Rule", "R-ACTOR-1")]
    [Trait("Rule", "R-TASK-6")]
    public void Launch_gate_spaces_a_batch_by_reserving_slots_before_sleeping()
    {
        using var runtime = new TestRuntime();
        var gate = new ACPLaunchGate(1.5, runtime.Loop);
        var waits = Enumerable.Range(0, 3).Select(_ => gate.Wait()).ToArray();
        runtime.RunUntilIdle();
        Assert.Equal([true, false, false], waits.Select(w => w.IsCompleted));
        runtime.Advance(TimeSpan.FromSeconds(1.5));
        Assert.Equal([true, true, false], waits.Select(w => w.IsCompleted));
        runtime.Advance(TimeSpan.FromSeconds(1.5));
        Assert.All(waits, w => Assert.True(w.IsCompletedSuccessfully));
    }

    [Fact]
    [Trait("Rule", "R-ACTOR-1")]
    public async Task Actor_segments_never_overlap_and_a_same_actor_call_runs_inline()
    {
        var counter = new CounterActor();
        await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(counter.Increment)));
        Assert.Equal(200, await counter.Value());
        Assert.Equal(1, counter.MaxInside);
        Assert.Equal(202, await counter.Twice());
    }

    [Fact]
    [Trait("Rule", "R-ACTOR-1")]
    [Trait("Decision", "ED-066")]
    public void An_actor_call_stays_in_the_callers_task_and_sees_its_cancellation()
    {
        using var runtime = new TestRuntime();
        var counter = new CounterActor();
        bool? seen = null;
        var handle = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(1));
            seen = await counter.SeesCallerCancellation();
        });
        runtime.RunUntilIdle();
        handle.Cancel();
        runtime.Loop.RunUntil(() => handle.Completion.IsCompleted);
        Assert.True(seen);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(1);
        }
    }
}
