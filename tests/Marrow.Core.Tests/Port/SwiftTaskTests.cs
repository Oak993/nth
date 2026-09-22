using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// R-TASK rules (IMPLEMENTING.md 3.3) and the ED-066 ambient task model. New in this guide; the sleep and
// handle cases come from draft B (proto-B ClockTests, TaskTests).
public sealed class SwiftTaskTests
{
    [Fact]
    [Trait("Rule", "R-TASK-2")]
    [Trait("Decision", "ED-066")]
    public void Task_isCancelled_is_visible_to_any_function_called_inside_the_task()
    {
        using var runtime = new TestRuntime();
        var seen = new List<bool>();
        var handle = MainActor.Spawn(async () =>
        {
            seen.Add(CancellationProbe.DeepCheck());
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(10));
            seen.Add(CancellationProbe.DeepCheck()); // a synchronous helper, no token parameter
        });
        runtime.RunUntilIdle();
        handle.Cancel();
        runtime.RunUntilIdle();
        Assert.Equal([false, true], seen);
    }

    [Fact]
    [Trait("Rule", "R-TASK-2")]
    [Trait("Decision", "ED-066")]
    public void Outside_any_task_isCancelled_is_false_and_checkCancellation_does_not_throw()
    {
        Assert.False(SwiftTask.IsInTask);
        Assert.False(SwiftTask.IsCancelled);
        SwiftTask.CheckCancellation();
        Assert.False(SwiftTask.CancellationToken.CanBeCanceled);
    }

    [Fact]
    [Trait("Rule", "R-TASK-2")]
    [Trait("Decision", "ED-066")]
    public void Work_posted_from_a_cancelled_task_runs_outside_any_task()
    {
        // DispatchQueue.main.async from inside a task: the block is not part of the task.
        using var runtime = new TestRuntime();
        bool? postedSawTask = null;
        var handle = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(10));
            MainActor.Post(() => postedSawTask = SwiftTask.IsInTask || SwiftTask.IsCancelled);
        });
        runtime.RunUntilIdle();
        handle.Cancel();
        runtime.RunUntilIdle();
        Assert.False(postedSawTask);
    }

    [Fact]
    [Trait("Rule", "R-TASK-1")]
    [Trait("Decision", "ED-066")]
    public void Cancelling_a_task_does_not_cancel_tasks_it_spawned()
    {
        using var runtime = new TestRuntime();
        TaskHandle? child = null;
        var parent = MainActor.Spawn(async () =>
        {
            child = MainActor.Spawn(() => SwiftTask.Sleep(TimeSpan.FromSeconds(1)));
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(10));
        });
        runtime.RunUntilIdle();
        parent.Cancel();
        runtime.RunUntilIdle();
        Assert.True(parent.Completion.IsCompletedSuccessfully);
        Assert.False(child!.IsCancelled);
        runtime.Advance(TimeSpan.FromSeconds(1));
        Assert.True(child.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    [Trait("Rule", "R-TASK-3")]
    public void TrySleep_returns_false_on_cancel_and_the_code_after_it_runs()
    {
        using var runtime = new TestRuntime();
        var log = new List<string>();
        var handle = MainActor.Spawn(async () =>
        {
            var slept = await SwiftTask.TrySleep(TimeSpan.FromSeconds(10));
            log.Add($"after sleep, slept={slept}, cancelled={SwiftTask.IsCancelled}");
        });
        runtime.RunUntilIdle();
        handle.Cancel();
        runtime.RunUntilIdle();
        Assert.Equal(["after sleep, slept=False, cancelled=True"], log);
        Assert.True(handle.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    [Trait("Rule", "R-TASK-3")]
    public void Sleep_throws_CancellationError_when_the_task_was_cancelled_before_it_started()
    {
        using var runtime = new TestRuntime();
        var handle = MainActor.SpawnThrowing(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(5));
            await SwiftTask.Sleep(TimeSpan.Zero); // already cancelled: throws before suspending
            return 1;
        });
        runtime.RunUntilIdle();
        handle.Cancel();
        runtime.RunUntilIdle();
        Assert.True(handle.Value.IsFaulted);
        Assert.IsType<CancellationError>(handle.Value.Exception!.InnerException);
    }

    [Fact]
    [Trait("Rule", "R-TASK-3")]
    [Trait("Oracle", "O-21")]
    public void A_negative_or_zero_sleep_suspends_once_instead_of_throwing_or_waiting_forever()
    {
        // The .NET hazards the helper avoids.
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = Task.Delay(TimeSpan.FromMilliseconds(-2)); });
        using (var probe = new CancellationTokenSource())
        {
            var forever = Task.Delay(TimeSpan.FromMilliseconds(-1), probe.Token);
            Assert.False(forever.IsCompleted);
            probe.Cancel();
        }
        using var runtime = new TestRuntime();
        var log = new List<string>();
        runtime.Loop.Enqueue(() =>
        {
            MainActor.Spawn(async () =>
            {
                log.Add("sleep");
                // AutoContinue.swift:105 passes resumeAt.timeIntervalSinceNow, negative once the reset passed.
                await SwiftTask.Sleep(SwiftTask.Seconds(-30));
                log.Add("resumed");
            });
            MainActor.Post(() => log.Add("other work"));
        });
        runtime.RunUntilIdle();
        Assert.Equal(["sleep", "other work", "resumed"], log);
    }

    [Fact]
    [Trait("Rule", "R-TASK-3")]
    public void A_sixty_day_sleep_is_accepted_and_completes()
    {
        using var runtime = new TestRuntime();
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = Task.Delay(TimeSpan.FromDays(60)); });
        var handle = MainActor.Spawn(() => SwiftTask.Sleep(TimeSpan.FromDays(60)));
        runtime.RunUntilIdle();
        runtime.Advance(TimeSpan.FromDays(49.7));
        Assert.False(handle.Completion.IsCompleted);
        runtime.Advance(TimeSpan.FromDays(10.3));
        Assert.True(handle.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    [Trait("Rule", "R-TASK-3")]
    [Trait("Oracle", "O-22")]
    public void Seconds_traps_on_a_non_finite_interval_and_saturates_a_huge_one()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(1500), SwiftTask.Seconds(1.5));
        Assert.Equal(TimeSpan.FromSeconds(-30), SwiftTask.Seconds(-30));
        Assert.Equal(TimeSpan.MaxValue, SwiftTask.Seconds(1e300));
        Assert.Throws<SwiftTrapException>(() => SwiftTask.Seconds(double.NaN));
        Assert.Throws<SwiftTrapException>(() => SwiftTask.Seconds(double.PositiveInfinity));
    }

    [Fact]
    [Trait("Rule", "R-TASK-4")]
    public void Cancellation_handler_runs_before_the_operation_when_already_cancelled()
    {
        using var runtime = new TestRuntime();
        var log = new List<string>();
        var handle = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(1));
            await SwiftTask.WithCancellationHandler(() => { log.Add("operation"); return Task.CompletedTask; }, () => log.Add("onCancel"));
        });
        runtime.RunUntilIdle();
        handle.Cancel();
        runtime.RunUntilIdle();
        Assert.Equal(["onCancel", "operation"], log);
    }

    [Fact]
    [Trait("Rule", "R-TASK-4")]
    public void Cancellation_handler_runs_inline_on_the_cancelling_thread()
    {
        using var runtime = new TestRuntime();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerThread = 0;
        var handle = MainActor.Spawn(() => SwiftTask.WithCancellationHandler(() => release.Task,
            () => handlerThread = Environment.CurrentManagedThreadId));
        runtime.RunUntilIdle();
        handle.Cancel();
        Assert.Equal(Environment.CurrentManagedThreadId, handlerThread);
        release.SetResult();
        runtime.RunUntilIdle();
    }

    [Fact]
    [Trait("Rule", "R-TASK-1")]
    public void Task_handles_compare_by_identity()
    {
        var a = TaskHandle.Detached(() => Task.CompletedTask);
        var b = TaskHandle.Detached(() => Task.CompletedTask);
        Assert.False(ReferenceEquals(a, b));
        Assert.True(a.Equals(a));
        Assert.False(a.Equals(b));
    }

    [Fact]
    [Trait("Rule", "R-ERR-1")]
    [Trait("Decision", "ED-078")]
    public void A_throwing_task_delivers_its_error_and_a_trap_in_a_task_escapes_to_the_main_loop()
    {
        using var runtime = new TestRuntime();
        var throwing = MainActor.SpawnThrowing<int>(() => throw new InvalidDataException("thrown"));
        runtime.RunUntilIdle();
        Assert.IsType<InvalidDataException>(throwing.Value.Exception!.InnerException);
        MainActor.Spawn(() => throw new SwiftTrapException("trap"));
        Assert.Throws<SwiftTrapException>(() => runtime.RunUntilIdle());
    }

    [Fact]
    [Trait("Rule", "R-TASK-5")]
    public async Task Task_group_children_see_parent_cancellation_and_the_scope_waits_for_every_child()
    {
        var finished = 0;
        var sawCancellation = 0;
        var parent = TaskHandle.Detached(() => TaskGroup.Run(group =>
        {
            for (var i = 0; i < 3; i++)
                group.AddTask(async () =>
                {
                    await SwiftTask.TrySleep(TimeSpan.FromMinutes(5));
                    if (SwiftTask.IsCancelled) Interlocked.Increment(ref sawCancellation);
                    await Task.Delay(20, CancellationToken.None); // cleanup after the cancel
                    Interlocked.Increment(ref finished);
                });
            return Task.CompletedTask;
        }));
        await Task.Delay(50);
        parent.Cancel();
        await parent.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, finished);
        Assert.Equal(3, sawCancellation);
    }

    [Fact]
    [Trait("Rule", "R-TASK-5")]
    public async Task Task_group_next_returns_results_in_completion_order_and_nil_when_empty()
    {
        var order = await TaskGroup<int>.Run(async group =>
        {
            var gates = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            for (var i = 0; i < 3; i++)
            {
                var index = i;
                group.AddTask(async () => { await gates[index].Task; return index; });
            }
            var results = new List<int>();
            gates[2].SetResult();
            results.Add((await group.Next()).Value);
            gates[0].SetResult();
            results.Add((await group.Next()).Value);
            gates[1].SetResult();
            results.Add((await group.Next()).Value);
            Assert.False((await group.Next()).HasValue);
            return results;
        });
        Assert.Equal([2, 0, 1], order);
    }

    [Fact]
    [Trait("Rule", "R-TASK-5")]
    public async Task Async_let_children_run_concurrently_and_an_unread_one_is_cancelled_at_scope_exit()
    {
        var unreadWasCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var parent = TaskHandle.Detached<int>(async () =>
        {
            await using var scope = new AsyncLetScope();
            var first = scope.Start(async () => { await Task.Yield(); return 20; });
            _ = scope.Start(async () =>
            {
                var slept = await SwiftTask.TrySleep(TimeSpan.FromMinutes(5));
                unreadWasCancelled.SetResult(!slept && SwiftTask.IsCancelled);
                return 0;
            });
            return await first + 22;
        });
        Assert.Equal(42, await parent.Value.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(await unreadWasCancelled.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
