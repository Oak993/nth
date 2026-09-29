# Owned App hang diagnosis and fixture draft

Worker-9, 2026-09-29. Worktree: `E:\Droppy\wt\oc\chatgpt-1`. Source reviewed at HEAD `0f8b8b519e5d045d487b6971212b9e29ca429e1e`. This file is the only worker write. The patches and test below are review drafts embedded in notes; they have not been applied to compiled files or executed.

## Observed result

The current test is blocked in setup behind an earlier asynchronous shared DiskWriter write. This is observed in both captures, independently of the last passed test. The active method is the Theory `Marrow.App.Tests.AppModelHydraMergeCoordinatorTests.Settlement_only_comes_from_legacy_held_empty_work_or_every_share_already_on_main_without_strays(string)`. The argument value is not present in either stack, so no particular InlineData row is identified.

Evidence directory: `E:\Droppy\wt\oc\chatgpt-1\_packet\revision\evidence\owned-live-stack-2026-09-29T19-14-55-450Z-215a72`.

| Evidence file | Exact evidence |
| --- | --- |
| `E:\Droppy\wt\oc\chatgpt-1\_packet\revision\evidence\owned-live-stack-2026-09-29T19-14-55-450Z-215a72\request.json:2-17` | Pinned owned PID 45788, start `2026-09-29T19:02:22.4397057Z`, nonce `215a72513c67431f8ebe0ab7065be24d`, matching HEAD, ordinary log and last-write time. |
| `E:\Droppy\wt\oc\chatgpt-1\_packet\revision\evidence\owned-live-stack-2026-09-29T19-14-55-450Z-215a72\capture-1-stacks.txt:51-60` | Thread 80148, 1339 samples: test -> HydraMergeProofFixture.Start -> AppModelHarness.WriteLibrary -> DiskWriter.WriteSynchronously -> SerialQueue.Sync -> ManualResetEventSlim.Wait. |
| `E:\Droppy\wt\oc\chatgpt-1\_packet\revision\evidence\owned-live-stack-2026-09-29T19-14-55-450Z-215a72\capture-1-stacks.txt:256-272` | Thread 121944, 1339 samples: thread-pool executor -> SerialExecutor.Drain -> captured ExecutionContext -> SerialQueue.Async -> SwiftTask.RunOutsideTask -> DiskWriter.EncodeAndWrite callback -> DiskWriter.Write -> FileOps.WriteAtomic -> FileOps.Wait -> ManualResetEventSlim.Wait. |
| `E:\Droppy\wt\oc\chatgpt-1\_packet\revision\evidence\owned-live-stack-2026-09-29T19-14-55-450Z-215a72\capture-2-stacks.txt:51-60` | Same test/setup wait and same thread, 1337 samples. |
| `E:\Droppy\wt\oc\chatgpt-1\_packet\revision\evidence\owned-live-stack-2026-09-29T19-14-55-450Z-215a72\capture-2-stacks.txt:249-265` | Same asynchronous writer and FileOps.Wait, same thread, 1337 samples. |
| `E:\Droppy\wt\oc\chatgpt-1\_packet\revision\evidence\owned-live-stack-2026-09-29T19-14-55-450Z-215a72\captures.json:7-27,31-51` | Captures at 19:14:55.575Z and 19:14:58.896Z. Both parsers ended, consumed every byte, reported no parse error and zero unresolved managed frames. Both collector records also report `read EPIPE`. |
| `E:\Droppy\wt\oc\chatgpt-1\_packet\revision\evidence\owned-live-stack-2026-09-29T19-14-55-450Z-215a72\supervisor-result.json:7-13` | Capture process exited 1, within bound; target remained alive. `ordinary_pass` is false. These are diagnostic results, not a passed test run. |

`HydraMergeProofFixture.Start` first calls H.WriteLibrary at `tests/Marrow.App.Tests/Fixtures/HydraMergeProofFixture.cs:83`. It does not create its AppModel until line 85. Its constructor, AddProject, and AddLead paths do not enqueue an asynchronous DiskWriter write before this call. The observed writer callback is EncodeAndWrite, whereas the current call is WriteSynchronously. The blocked asynchronous write therefore predates this fixture's first library setup write; the active settlement test is the waiting consumer, not an identified producer.

There is no observed SyncDefaultBranch/FileOps call on the test thread, no observed 400 ms Hydra waiter, and no observed landing-completion await in this chain. The main loop has not yet entered this fixture's Start/EnsureLoaded pump. Here the 120-second loop bound is not active at all; the synchronous setup call has its own unbounded SerialQueue.Sync wait.

## Source mechanism

All source paths below are relative to `E:\Droppy\wt\oc\chatgpt-1`.

| Source | Mechanism |
| --- | --- |
| `src/Marrow.Services/Store/Storage.cs:341-385` | One process-wide DiskWriter.Shared owns a FIFO SerialQueue. EncodeAndWrite takes its snapshot out of the pending map before Write executes. An empty pending map does not prove an idle writer. |
| `src/Marrow.Services/Store/Storage.cs:388-413` | WriteSynchronously and WaitForQueuedWrites enqueue synchronous work behind the same active writer. |
| `src/Marrow.Services/Store/Storage.cs:418-446` | Write calls synchronous FileOps.WriteAtomic; error handling cannot proceed until that call returns. |
| `src/Marrow.Core/Port/Actor.cs:63-94` | Sync waits at line 88 without a timeout. Async runs outside a Swift task. |
| `src/Marrow.Core/Port/Executors.cs:50-61,73-103` | Each queued item captures its caller's ExecutionContext, then executes under that context. A blocked action prevents the one drain from reaching the next item. |
| `src/Marrow.Core/Port/SwiftTask.cs:119-130` | RunOutsideTask clears Swift task cancellation state only; it does not remove clock or storage overrides. |
| `src/Marrow.Core/Port/Ambient.cs:9-35` | Overrides are AsyncLocal. Restoring an override in the disposing test does not alter an already captured writer context. |
| `src/Marrow.Core/Port/FileOps.cs:50-93,153-170` | WriteAtomic retries through an ambient SwiftClock timer and a synchronous ManualResetEventSlim.Wait. Releasing a handle does not signal that event; its timer must fire. The delays are 100 through 700 ms, measured by the selected provider. |
| `tests/Marrow.Testing/ManualTimeProvider.cs:44-80,101-113` | A manual timer advances only when its owning provider is advanced. ActiveTimers includes all due timers, not just FileOps retries. |
| `tests/Marrow.Testing/TestRuntime.cs:17-28,48-57` | TestRuntime installs its own manual clock. Dispose restores ambient scopes without driving pending timers or draining disk writes. |
| `tests/Marrow.App.Tests/Fixtures/AppModelFixtures.cs:195-264` | Harness disposal drains only FileIndex work, then restores scopes and deletes the scratch tree. It has no shared DiskWriter fence before retiring the fixture. |
| `tests/Marrow.App.Tests/AssemblyParallelism.cs:14` | App collections are already sequential. Sequential execution does not drain work left alive by the preceding fixture. |

The retained old manual-clock explanation is a strong source-backed inference from the two stalled writer stacks and the fixture lifetime above. Samples do not expose the provider object, failing path, exception HResult, enqueueing caller, or prior test identity. Do not report those as observed locals. A missing parent directory alone is not the demonstrated retry trigger: FileOps treats not-found errors as nontransient. The capture identifies the FileOps wait, not which replace/read-back operation originally failed.

Changing SwiftClock only in the new fixture or around its barrier does not release the old writer's captured clock. Coalescing also preserves the context of the originally enqueued callback even when a newer snapshot replaces the pending value.

## Concrete fixture correction draft

Limit the change to `tests/Marrow.App.Tests/Fixtures/AppModelFixtures.cs`. Finish queued writes while the owning harness still has its clock and storage directories. Run the synchronous queue fence on background work and advance the existing manual provider from the disposing fixture thread. Do not move disposal, main-loop execution, or actor state to that background task.

Drain once before FileIndex cleanup, because cleanup may pump an actor callback that synchronously touches storage. Drain again after that cleanup, including its exceptional exit, to cover writes its callbacks enqueue. Use `Time.Advance`, not `TestRuntime.Advance`: the latter also pumps the manual actor and can re-enter a blocking callback. This draft deliberately does not pump unrelated callbacks merely to wait for a storage fence. Keep all existing test-body timestamps and assertions.

```diff
--- a/tests/Marrow.App.Tests/Fixtures/AppModelFixtures.cs
+++ b/tests/Marrow.App.Tests/Fixtures/AppModelFixtures.cs
@@
     public void Dispose()
     {
-        try { FinishFileIndexBuilds(); }
+        try
+        {
+            FinishDiskWrites();
+            try { FinishFileIndexBuilds(); }
+            finally { FinishDiskWrites(); }
+        }
         finally
         {
             for (var index = scopes.Count - 1; index >= 0; index--) scopes[index].Dispose();
             Runtime?.Dispose();
             try { Directory.Delete(baseFolder, recursive: true); }
             catch (IOException) { }
             catch (UnauthorizedAccessException) { }
         }
     }
+
+    // PORT-ONLY, R-TEST-5: queued writes retain this fixture's manual clock.
+    // Keep that clock and the scratch tree alive until the existing writer fence returns.
+    private void FinishDiskWrites()
+    {
+        var barrier = Task.Run(DiskWriter.Shared.WaitForQueuedWrites);
+        var elapsed = System.Diagnostics.Stopwatch.StartNew();
+        var poll = TimeSpan.FromMilliseconds(5);
+        var clockStep = FileOps.RetryDelays[0];
+        while (!barrier.IsCompleted)
+        {
+            var remaining = Bound - elapsed.Elapsed;
+            if (remaining <= TimeSpan.Zero)
+                throw new TimeoutException("The harness disk writer did not drain before disposal.");
+            if (barrier.Wait(remaining < poll ? remaining : poll)) break;
+            // Advance only this fixture's clock, on its disposing thread. Do not pump
+            // the actor from the fence task or replace the clock with TimeProvider.System.
+            if (Runtime is { } runtime && runtime.Time.ActiveTimers > 0)
+                runtime.Time.Advance(clockStep);
+        }
+        barrier.GetAwaiter().GetResult();
+    }
```

This is a FIFO fence for already submitted writes, not a claim of quiescence for every possible future producer. Virtual time progresses during teardown only, while waiting for the fence. Advancing the owner provider can make other timers due; the draft avoids pumping their manual-main-loop continuations inside the fence wait. FileIndex cleanup retains its existing actor/dispatcher behavior. Its later writes receive the second fence. Prime should review this interaction in the ordinary suite, especially harnesses with their own nested clock override or without a TestRuntime; an unrelated retained provider cannot be driven by this harness. A real-time bound failure is a failed fixture, never acceptance or evidence that an unresolved writer was drained.

There is no change to production FileOps, retry counts, queue ordering, persistence, MainActor, or the shared TestRuntime clock policy. The existing narrowly scoped System clock in `tests/Marrow.App.Tests/AppModelHydraMergeSyncTests.cs:105-136` handles a different, intentional direct synchronous write test and is not a general fix for this capture.

## Regression draft

Proposed new test file: `tests/Marrow.App.Tests/AppModelHarnessDisposalTests.cs`. This code is inert here. It requires no AppModel, bootstrap, provider, git repository, new project, package, or process. It uses the real shared DiskWriter and a real held FileStream in an isolated harness tree. A fresh harness with no model has no unrelated manual timers, so its first ActiveTimer identifies the prescribed retry.

The size assertion after Dispose proves that the actual queued write succeeded before its directory disappeared: DiskWriter records that size only after Write succeeds. Merely showing that a marker eventually returns after failed writes would be insufficient. The old implementation returns with an armed retry timer, so the first post-disposal assertion fails before the rescue. The rescue is unconditional, holds a reference to the original clock, and runs before rethrowing a failing assertion, preventing the expected pre-fix failure from poisoning the next test.

```csharp
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Store;
using Marrow.Testing;

namespace Marrow.App.Tests;

public sealed class AppModelHarnessDisposalTests
{
    [Fact]
    [Trait("Rule", "R-TEST-5")]
    public void Dispose_finishes_a_queued_retry_before_cleanup_and_the_next_harness_can_write()
    {
        var h = new AppModelHarness();
        var originalClock = h.Main.Time;
        var originalRoot = h.Root;
        var id = Guid.NewGuid();
        var path = Path.Combine(originalRoot, "queued-retry.json");
        var initial = new ThreadDocument(id);
        var latest = initial with
        {
            FollowUps = new ValueList<FollowUpPrompt>(
                [new FollowUpPrompt { Text = "queued before fixture disposal" }]),
        };
        var expectedSize = Coding.Encode(latest).Length;
        FileStream? held = null;
        var disposalInvoked = false;
        try
        {
            Directory.CreateDirectory(originalRoot);
            File.WriteAllBytes(path, Coding.Encode(initial));
            held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            DiskWriter.Shared.EncodeAndWrite(latest, path);
            Assert.True(SpinWait.SpinUntil(() => originalClock.ActiveTimers == 1,
                TimeSpan.FromSeconds(10)), "The real queued write did not arm its retry timer.");
            Assert.Null(DiskWriter.Shared.LastWrittenSize(path));

            held.Dispose();
            held = null;
            // Releasing the handle does not advance the manual retry timer.
            Assert.Equal(1, originalClock.ActiveTimers);
            disposalInvoked = true;
            h.Dispose();

            Assert.Equal(0, originalClock.ActiveTimers);
            Assert.Equal(expectedSize, DiskWriter.Shared.LastWrittenSize(path));
            Assert.False(Directory.Exists(originalRoot));

            // Await the actual shared fence under a separate real-time bound.
            var fence = Task.Run(DiskWriter.Shared.WaitForQueuedWrites);
            Assert.True(fence.Wait(TimeSpan.FromSeconds(10)), "The shared writer remained blocked.");
            fence.GetAwaiter().GetResult();

            using var next = new AppModelHarness();
            // Running only the synchronous writer on the pool retains next's storage scope;
            // model/actor construction and disposal remain on the fixture thread.
            var nextWrite = Task.Run(() => next.WriteLibrary([], []));
            try
            {
                CompleteWithOriginalClock(next.Main.Time, nextWrite, TimeSpan.FromSeconds(10));
                var loaded = Assert.IsType<LibraryLoadResult.Loaded>(Storage.LoadLibrary());
                Assert.Empty(loaded.Library.Projects);
                Assert.Empty(loaded.Library.Threads);
            }
            finally
            {
                CompleteWithOriginalClock(next.Main.Time, nextWrite, TimeSpan.FromSeconds(10));
            }
        }
        finally
        {
            held?.Dispose();
            // Deliberately independent of h.Dispose: the pre-fix test must fail without
            // abandoning its real shared writer. Never substitute the current/new clock.
            var rescue = Task.Run(DiskWriter.Shared.WaitForQueuedWrites);
            CompleteWithOriginalClock(originalClock, rescue, TimeSpan.FromSeconds(10));
            if (!disposalInvoked) h.Dispose();
        }
    }

    private static void CompleteWithOriginalClock(ManualTimeProvider originalClock, Task task, TimeSpan bound)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var poll = TimeSpan.FromMilliseconds(5);
        while (!task.IsCompleted)
        {
            var remaining = bound - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException("The test-owned writer did not finish while its original clock was driven.");
            if (task.Wait(remaining < poll ? remaining : poll)) break;
            if (originalClock.ActiveTimers > 0)
                originalClock.Advance(FileOps.RetryDelays[0]);
        }
        task.GetAwaiter().GetResult();
    }
}
```

The main lifetime oracle is the armed old timer, successful recorded write size, and removed original tree before a fresh harness writes. `Coding` is imported from `Marrow.Core.Support`; `LibraryLoadResult.Loaded` is the concrete result defined in `src/Marrow.Services/Store/Storage.cs:159-167`.

Prime validation to perform serially: baseline regression must fail at the post-disposal timer assertion and still rescue the old writer; fixed regression must pass; existing coordinator rows retain their exact timestamps/booking/weak-resume assertions; then the required ordinary App/UI/Core and repository Gate D. No such execution is claimed here. The 167 completed mutation trials are not rerun by this diagnostic assignment.

## Review of prime's concurrent integration

After writing this draft, worker-9 read prime's current `tests/Marrow.App.Tests/Fixtures/AppModelFixtures.cs:195-234` (modified 19:21:16.848Z) and entire `tests/Marrow.App.Tests/AppModelHarnessLifetimeTests.cs:1-61` (modified 19:20:13.555Z). These are prime's changes, not this worker's writes. The earlier source line references describe the recorded baseline HEAD; the integration ranges describe this later worktree snapshot.

The current harness contains the two fences around FileIndex cleanup, advances only the owning manual Time, and uses `TaskCreationOptions.LongRunning` with `TaskScheduler.Default` for the barrier. That dedicated background task is suitable here and avoids dependence on another available thread-pool worker for the fence itself. The disposing thread retains actor cleanup and scope restoration. No expectation or clock-policy change appears in this inspected range.

The integrated lifetime test forces an actual sharing violation, observes the owner's retry timer, requires successful LastWrittenSize before the scratch tree disappears, awaits a shared fence, and proves a fresh harness can load its written library. Its finally retains and advances the original clock even if the old implementation already disposed its ambient scopes. This directly targets the observed lifetime failure. It does not identify the unknown enqueueing test in the capture.

Small robustness suggestions for prime's integrated test: put File.WriteAllText and construction of sharingGuard inside its existing try so an unexpected setup I/O exception cannot bypass fixture cleanup; additionally assert zero original ActiveTimers after successful disposal and empty projects/threads in the concrete Loaded library. The proposed draft above bounds the next harness's write separately and drives that next harness's own clock, avoiding a fresh unbounded setup wait during the regression. These suggestions do not weaken any existing assertions. Compilation and execution of the integrated code remain prime-owned and unverified by this worker.

## Read scope and limits

Root AGENTS, WORKER_BRIEF, PORT_CONTRACT and contract_amendments were read. The repository instruction search returned only root AGENTS. Entire files read: Storage.cs, Actor.cs, Ambient.cs, Executors.cs, FileOps.cs, SwiftClock.cs, SwiftTask.cs, MainActor.cs, ManualMainLoop.cs, ManualTimeProvider.cs, TestRuntime.cs, AppModelFixtures.cs, HydraMergeProofFixture.cs, HydraMergeReviewFixture.cs, HydraMergeQueueObserver.cs, AppModelHydraMergeCoordinatorTests.cs, AppModelHydraMergeSyncTests.cs, AppModel+HydraMerge.cs, AssemblyParallelism.cs, ThreadRuntimeStorageScratch.cs, SerialQueueTests.cs, FileOpsTests.cs, FileOpsFixtures.cs. Additional ranges read: ThreadRuntime.cs 540-670, 792-893, 3580-3675; AppModel.cs 1265-1370; StorageSecondTests.cs 1-180, 160-234, 228-329. Both supplied stack reports and their request/capture/supervisor JSON were read in full.

No raw Swift source was read by this worker. Swift references in comments were not treated as fresh source inspection. No binary trace parser was run by this worker, no process was queried or controlled, and no production file, compiled test, revision script, test result, or commit was changed. The saved decoded reports establish the wait chain; their collector errors and missing object/argument data remain explicit limits.
