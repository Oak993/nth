using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// R-ISO rules (IMPLEMENTING.md 3.2). Source: draft B (proto-B MainActorTests), on the ED-066 task model.
public sealed class MainActorTests
{
    [Fact]
    [Trait("Rule", "R-ISO-4")]
    [Trait("Decision", "ED-065")]
    public void Post_never_runs_inline_even_from_main()
    {
        using var runtime = new TestRuntime();
        var log = new List<string>();
        runtime.Loop.Enqueue(() =>
        {
            MainActor.Post(() => log.Add("posted"));
            log.Add("after post");
        });
        runtime.RunUntilIdle();
        Assert.Equal(["after post", "posted"], log);
    }

    [Fact]
    [Trait("Rule", "R-ISO-4")]
    public void Spawned_main_task_starts_after_the_creator_returns()
    {
        using var runtime = new TestRuntime();
        var log = new List<string>();
        runtime.Loop.Enqueue(() =>
        {
            log.Add("before");
            MainActor.Spawn(() => { log.Add("body"); return Task.CompletedTask; });
            log.Add("after");
        });
        runtime.RunUntilIdle();
        Assert.Equal(["before", "after", "body"], log);
    }

    [Fact]
    [Trait("Rule", "R-ISO-4")]
    public void Calling_the_async_lambda_directly_reorders_the_creator()
    {
        // The pitfall: running the body inline runs it up to its first await before the creator's next line.
        var log = new List<string>();
        Func<Task> body = () => { log.Add("body"); return Task.CompletedTask; };
        _ = body();
        log.Add("after");
        Assert.Equal(["body", "after"], log);
    }

    [Fact]
    [Trait("Rule", "R-ISO-2")]
    public void Awaits_inside_a_main_body_resume_on_main()
    {
        using var runtime = new TestRuntime();
        var checks = new List<bool>();
        var handle = MainActor.Spawn(async () =>
        {
            checks.Add(MainActor.IsIsolated);
            await Task.Run(() => checks.Add(MainActor.IsIsolated));
            checks.Add(MainActor.IsIsolated);
        });
        runtime.Loop.RunUntil(() => handle.Completion.IsCompleted);
        Assert.Equal([true, false, true], checks);
    }

    [Fact]
    [Trait("Rule", "R-ISO-2")]
    public void ConfigureAwait_false_leaves_the_main_actor()
    {
        // The pitfall R-ISO-2 forbids: the rest of the body resumes on the pool, a race Swift makes impossible.
        using var runtime = new TestRuntime();
        var checks = new List<bool>();
        var handle = MainActor.Spawn(async () =>
        {
            await Task.Delay(1).ConfigureAwait(false);
            checks.Add(MainActor.IsIsolated);
        });
        runtime.Loop.RunUntil(() => handle.Completion.IsCompleted);
        Assert.Equal([false], checks);
    }

    [Fact]
    [Trait("Rule", "R-ISO-2")]
    public void A_plain_async_call_runs_synchronously_up_to_its_first_await_and_Task_Run_does_not()
    {
        using var runtime = new TestRuntime();
        var log = new List<string>();
        async Task Nonsending(string name)
        {
            log.Add($"{name} start isolated={MainActor.IsIsolated}");
            await Task.Yield();
            log.Add($"{name} resumed isolated={MainActor.IsIsolated}");
        }
        var handle = MainActor.Spawn(async () =>
        {
            var direct = Nonsending("direct");
            log.Add("caller after direct call");
            await direct;
            var pooled = Task.Run(() => Nonsending("concurrent"));
            log.Add("caller after Task.Run");
            await pooled;
        });
        runtime.Loop.RunUntil(() => handle.Completion.IsCompleted);
        Assert.Equal("direct start isolated=True", log[0]);
        Assert.Equal("caller after direct call", log[1]);
        Assert.Equal("direct resumed isolated=True", log[2]);
        Assert.Contains("concurrent start isolated=False", log);
        Assert.Contains("concurrent resumed isolated=False", log);
    }

    [Fact]
    [Trait("Rule", "R-ISO-4")]
    public async Task AssumeIsolated_traps_off_main_and_runs_inline_on_main()
    {
        using var runtime = new TestRuntime();
        await Assert.ThrowsAsync<SwiftTrapException>(() => Task.Run(() => MainActor.AssumeIsolated(() => { })));
        var ran = 0;
        runtime.Loop.Enqueue(() => ran = MainActor.AssumeIsolated(() => 42));
        runtime.RunUntilIdle();
        Assert.Equal(42, ran);
    }

    [Fact]
    [Trait("Rule", "R-ISO-4")]
    public async Task Run_is_inline_when_already_on_main_and_one_hop_otherwise()
    {
        using var runtime = new TestRuntime();
        var completedSynchronously = false;
        runtime.Loop.Enqueue(() => completedSynchronously = MainActor.Run(() => 1).IsCompleted);
        runtime.RunUntilIdle();
        Assert.True(completedSynchronously);
        var fromPool = Task.Run(() => MainActor.Run(() => MainActor.IsIsolated));
        runtime.Loop.RunUntil(() => fromPool.IsCompleted);
        Assert.True(await fromPool);
    }

    [Fact]
    [Trait("Rule", "R-ISO-4")]
    public async Task Invoke_runs_a_main_actor_async_body_on_main_across_its_awaits()
    {
        using var runtime = new TestRuntime();
        var checks = new List<bool>();
        var fromPool = Task.Run(() => MainActor.Invoke(async () =>
        {
            checks.Add(MainActor.IsIsolated);
            await Task.Yield();
            checks.Add(MainActor.IsIsolated);
            return checks.Count;
        }));
        runtime.Loop.RunUntil(() => fromPool.IsCompleted);
        Assert.Equal(2, await fromPool);
        Assert.Equal([true, true], checks);
    }

    [Fact]
    [Trait("Rule", "R-ISO-3")]
    public void An_await_added_between_read_and_write_loses_an_update()
    {
        using var runtime = new TestRuntime();
        var faithful = 0;
        var withExtraAwait = 0;
        for (var i = 0; i < 2; i++)
        {
            MainActor.Spawn(async () =>
            {
                await Task.Yield();
                var read = faithful; // Swift: a synchronous read-modify-write on main
                faithful = read + 1;
            });
            MainActor.Spawn(async () =>
            {
                var read = withExtraAwait;
                await Task.Yield(); // an async wrapper the Swift code did not have
                withExtraAwait = read + 1;
            });
        }
        runtime.RunUntilIdle();
        Assert.Equal(2, faithful);
        Assert.Equal(1, withExtraAwait);
    }

    [Fact]
    [Trait("Rule", "R-GCD-2")]
    public void AsyncAfter_work_item_cancelled_after_the_timer_fired_does_not_run()
    {
        using var runtime = new TestRuntime();
        var ran = 0;
        WorkItem? item = null;
        runtime.Loop.Enqueue(() => item = MainActor.AsyncAfter(TimeSpan.FromMilliseconds(50), () => ran++));
        runtime.RunUntilIdle();
        runtime.Time.Advance(TimeSpan.FromMilliseconds(50)); // the timer fires and posts; the loop is not pumped yet
        Assert.Equal(1, runtime.Loop.Pending);
        item!.Cancel();
        runtime.RunUntilIdle();
        Assert.Equal(0, ran);
    }

    [Fact]
    [Trait("Rule", "R-TEST-5")]
    public async Task The_main_actor_override_is_scoped_to_the_async_flow()
    {
        var first = new ManualMainLoop();
        var second = new ManualMainLoop();
        async Task<IMainExecutor> Observe(ManualMainLoop loop)
        {
            using var scope = MainActor.Executor.Override(loop);
            await Task.Yield();
            return MainActor.Executor.Current;
        }
        var seen = await Task.WhenAll(Task.Run(() => Observe(first)), Task.Run(() => Observe(second)));
        Assert.Same(first, seen[0]);
        Assert.Same(second, seen[1]);
    }

    [Fact]
    [Trait("Rule", "R-ISO-1")]
    public void An_unbound_main_actor_traps_instead_of_running_nowhere()
    {
        Assert.False(MainActor.IsIsolated);
        Assert.Throws<SwiftTrapException>(() => MainActor.Post(() => { }));
    }
}
