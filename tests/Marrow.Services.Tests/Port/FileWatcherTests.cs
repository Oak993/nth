using System.Globalization;
using Marrow.Core.Port;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port;

// Foundation unit F.FSWatcher: IMPLEMENTING.md R-GCD-3 and 4.3 ("File-system watcher: events posted to main,
// overflow means rescan, coalescing; first tests on a real temporary folder"), probe P8
// (porting/probes/p8/RESULT.md). The subject, src/Marrow.Services/Port/FileWatcher.cs, is PORT-ONLY: it stands in
// for the two Swift file-event APIs of the pinned source (B2 1fbf81e):
//   - FSEventStreamCreate with kFSEventStreamCreateFlagFileEvents | NoDefer, latency 0, over the working root and
//     the git directory when that lies outside it, delivering (path, flags) batches to one callback
//     (DroppyCode/Services/Git/WorkingTreeWatch.swift:139-155, callback :172-226). Port: FileWatcher.WatchTree.
//   - DispatchSource.makeFileSystemObjectSource(fileDescriptor: open(directory, O_EVTONLY), eventMask: [.write],
//     queue: .main), handler with no arguments, cancelled by stopWatcher
//     (DroppyCode/Services/Providers/CommandCodeSession.swift:651-667). Port: FileWatcher.WatchDirectory.
//
// The surface these tests fix; the implementation follows it:
//   static FileWatcher? WatchTree(IReadOnlyList<string> roots, TimeSpan latency, Action<FileWatcherBatch> onEvents, int bufferSize = <default>)
//   static FileWatcher? WatchDirectory(string directory, Action onChange)
//   void Cancel()
//   FileWatcherBatch(IReadOnlyList<string> Paths, bool Everything)   (a sealed record)
// A reported path is the root string as given, a backslash, and the name Windows reports relative to that root.
// Everything is true when events may have been lost since the last delivery: FileSystemWatcher.Error carrying
// InternalBufferOverflowException (P8, the Windows form of the three loss flags at WorkingTreeWatch.swift:180-189),
// and any other Error (P8 "Design consequence", an ENGINEERING DECISION recorded there).
//
// Events arrive on FileSystemWatcher's threads. They reach the main executor and the clock that were current where
// the watch was started (the ExecutionContext is captured then, as MainActor.AsyncAfter and ManualTimeProvider do),
// which in the app are bound process-wide and in these tests are the TestRuntime's. A zero latency never waits on
// SwiftClock (Swift passes 0 with NoDefer: delivery is not held for time); a nonzero latency is the port's own
// coalescing, a TaskHandle re-armed by each event (R-GCD-3), timed by SwiftClock.
//
// Every test waits with ManualMainLoop.RunUntil (condition-driven, bounded), never with a fixed sleep (R-TEST-5).
public sealed class FileWatcherTests
{
    [Fact]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-065")]
    [Trait("Decision", "ED-066")]
    public void A_file_created_in_a_watched_tree_reaches_the_handler_on_main_outside_any_task_with_its_full_path()
    {
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var log = new WatchLog();
        var watch = FileWatcher.WatchTree([folder.Root], TimeSpan.Zero, log.Record);
        Assert.NotNull(watch);
        try
        {
            var file = folder.Child("a.txt");
            File.WriteAllBytes(file, [1]);
            runtime.Loop.RunUntil(() => log.Saw(file));
            // WorkingTreeWatch.swift:121/:147 deliver on the watch's queue; R-GCD-3 marshals FileSystemWatcher events
            // with MainActor.Post, which runs outside any Swift task as a GCD block does (R-TASK-2).
            Assert.True(log.AllOnMainOutsideAnyTask);
            Assert.False(log.Everything);
            // FSEvents never calls back with no event: a delivery always carries a path or the loss signal.
            Assert.False(log.AnyEmptyBatch);
        }
        finally
        {
            watch.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    public void Creation_rename_content_write_deletion_attribute_change_and_nested_entries_are_each_reported_by_full_path()
    {
        // WorkingTreeWatch.swift:142 kFSEventStreamCreateFlagFileEvents: one event per changed entry, the entry's own
        // path, recursively (:197-205 derives the holding directory from it). A rename names both the old and the
        // new path (FSEvents reports ItemRenamed on each). The Windows filter is PR-GIT-072's windows_path:
        // file name, directory name, last write, size and attributes, recursive.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var renamedFrom = folder.Child("old.txt");
        var renamedTo = folder.Child("new.txt");
        var written = folder.Child("keep.txt");
        var deleted = folder.Child("gone.txt");
        var attributed = folder.Child("attr.txt");
        foreach (var file in new[] { renamedFrom, written, deleted, attributed }) File.WriteAllBytes(file, [1]);
        var log = new WatchLog();
        var watch = FileWatcher.WatchTree([folder.Root], TimeSpan.Zero, log.Record);
        Assert.NotNull(watch);
        try
        {
            File.Move(renamedFrom, renamedTo);
            using (var stream = new FileStream(written, FileMode.Append, FileAccess.Write)) stream.WriteByte(2);
            File.Delete(deleted);
            File.SetAttributes(attributed, FileAttributes.ReadOnly);
            var directory = folder.Child("sub");
            Directory.CreateDirectory(directory);
            var nested = folder.Child("sub", "n.txt");
            File.WriteAllBytes(nested, [1]);
            string[] expected = [renamedFrom, renamedTo, written, deleted, attributed, directory, nested];
            runtime.Loop.RunUntil(() => log.SawAll(expected));
            Assert.True(log.AllOnMainOutsideAnyTask);
            Assert.False(log.Everything);
        }
        finally
        {
            watch.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    public void Changes_under_one_root_are_delivered_in_the_order_they_happened()
    {
        // WorkingTreeWatch.swift:15-18: "events arrive in order, so everything written before [the cookie] has been
        // counted"; settledGeneration (:246-273) rests on that order.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var log = new WatchLog();
        var watch = FileWatcher.WatchTree([folder.Root], TimeSpan.Zero, log.Record);
        Assert.NotNull(watch);
        try
        {
            var files = Enumerable.Range(1, 20).Select(i => folder.Child("f" + i.ToString(CultureInfo.InvariantCulture) + ".txt")).ToArray();
            foreach (var file in files) File.WriteAllBytes(file, [1]);
            runtime.Loop.RunUntil(() => log.SawAll(files));
            var positions = files.Select(log.FirstIndex).ToArray();
            Assert.Equal(positions.Order().ToArray(), positions);
        }
        finally
        {
            watch.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    public void Changes_under_every_root_reach_the_one_handler()
    {
        // WorkingTreeWatch.swift:143: roots = gitRoot inside root ? [root] : [root, gitRoot], one stream, one callback.
        using var runtime = new TestRuntime();
        using var root = new ScratchFolder();
        using var gitRoot = new ScratchFolder();
        var log = new WatchLog();
        var watch = FileWatcher.WatchTree([root.Root, gitRoot.Root], TimeSpan.Zero, log.Record);
        Assert.NotNull(watch);
        try
        {
            var working = root.Child("a.txt");
            var cookie = gitRoot.Child("droppy-code-sync-1");
            File.WriteAllBytes(working, [1]);
            File.WriteAllBytes(cookie, []);
            runtime.Loop.RunUntil(() => log.Saw(working) && log.Saw(cookie));
            Assert.True(log.AllOnMainOutsideAnyTask);
        }
        finally
        {
            watch.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    public void Events_that_arrive_before_main_runs_are_coalesced_into_fewer_deliveries_and_none_is_lost()
    {
        // R-GCD-3 "coalescing": main is not pumped while 200 files are written, so the events wait for main together.
        // One delivery per event would make at least 200 calls.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var log = new WatchLog();
        var watch = FileWatcher.WatchTree([folder.Root], TimeSpan.Zero, log.Record);
        Assert.NotNull(watch);
        try
        {
            var files = Enumerable.Range(0, 200).Select(i => folder.Child("c" + i.ToString(CultureInfo.InvariantCulture) + ".txt")).ToArray();
            foreach (var file in files) File.WriteAllBytes(file, [1]);
            runtime.Loop.RunUntil(() => log.SawAll(files));
            Assert.InRange(log.Calls, 1, 199);
            Assert.False(log.AnyEmptyBatch);
            Assert.False(log.Everything);
            Assert.True(log.AllOnMainOutsideAnyTask);
        }
        finally
        {
            watch.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-065")]
    public void A_forced_buffer_overflow_reaches_the_handler_as_everything_and_the_watch_keeps_reporting()
    {
        // P8 (porting/probes/p8/RESULT.md): with InternalBufferSize at its 4 KB minimum a fast burst overflows and
        // FileSystemWatcher.Error carries InternalBufferOverflowException; that maps to everything = true /
        // changed = nil (WorkingTreeWatch.swift:180-189, :211-212). The overflow is timing dependent (P8: 6 of 18
        // bursts; this test's shape overflowed about 1 in 3 in a scratch run), so bursts repeat until one does, at
        // most 40. Each burst also waits until every burst file was reported or the loss was signalled: P8's
        // "loss implies signal" (C4); a burst that loses events silently times out here.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var log = new WatchLog();
        var watch = FileWatcher.WatchTree([folder.Root], TimeSpan.Zero, log.Record, bufferSize: 4096);
        Assert.NotNull(watch);
        try
        {
            var stem = new string('n', 120);
            for (var burst = 0; burst < 40 && !log.Everything; burst++)
            {
                log.ForgetPaths();
                var prefix = stem + "-" + burst.ToString(CultureInfo.InvariantCulture) + "-";
                var names = Enumerable.Range(0, 5000).Select(i => folder.Child(prefix + i.ToString(CultureInfo.InvariantCulture))).ToArray();
                var writers = Task.Run(() => Parallel.For(0, names.Length, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
                {
                    File.WriteAllBytes(names[i], [1]);
                    File.Delete(names[i]);
                }));
                runtime.Loop.RunUntil(() => writers.IsCompleted, TimeSpan.FromSeconds(120));
                Assert.Null(writers.Exception);
                runtime.Loop.RunUntil(() => log.Everything || log.SawAll(names), TimeSpan.FromSeconds(60));
            }
            Assert.True(log.Everything, "No burst overflowed the 4 KB buffer in 40 tries.");
            Assert.True(log.AllOnMainOutsideAnyTask);
            // FSEvents keeps delivering after a loss (the stream is not torn down by it, :179-226); so does the watch.
            var after = folder.Child("after.txt");
            File.WriteAllBytes(after, [1]);
            runtime.Loop.RunUntil(() => log.Saw(after));
        }
        finally
        {
            watch.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    public void An_error_other_than_overflow_also_reaches_the_handler_as_everything()
    {
        // P8 "Design consequence": every other Error exception type is handled as lost too (an ENGINEERING DECISION
        // recorded there; WorkingTreeWatch.swift:18-21 "anything the stream could have missed ... means a fresh
        // snapshot"). Deleting the watched root raises Error with Win32Exception 5 (Access is denied): 10 of 10 in a
        // scratch run on this machine. Swift reaches everything here too: the root's own path fails the
        // rootPrefix test at :200.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var root = folder.Child("watched");
        Directory.CreateDirectory(folder.Child("watched", "sub"));
        File.WriteAllBytes(folder.Child("watched", "sub", "a.txt"), [1]);
        var log = new WatchLog();
        var watch = FileWatcher.WatchTree([root], TimeSpan.Zero, log.Record);
        Assert.NotNull(watch);
        try
        {
            Directory.Delete(root, recursive: true);
            runtime.Loop.RunUntil(() => log.Everything);
            Assert.True(log.AllOnMainOutsideAnyTask);
        }
        finally
        {
            watch.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    public void Watching_a_directory_that_does_not_exist_gives_no_watch()
    {
        // WorkingTreeWatch.swift:144-154: a stream that cannot be created or started leaves no stream (every snapshot
        // is then taken fresh, :246-247). CommandCodeSession.swift:652-653: open() failing returns with no source.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var log = new WatchLog();
        Assert.Null(FileWatcher.WatchTree([folder.Child("missing")], TimeSpan.Zero, log.Record));
        Assert.Null(FileWatcher.WatchDirectory(folder.Child("missing"), log.RecordCall));
        runtime.RunUntilIdle();
        Assert.Equal(0, log.Calls);
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    public async Task With_zero_latency_a_steady_stream_of_changes_is_delivered_while_it_lasts()
    {
        // WorkingTreeWatch.swift:145 latency 0 with :142 NoDefer: nothing holds events back until writing stops. The
        // cookie wait gives up after 1 s (:258-261) and then tears the stream down for good (:271, upstream defect 82),
        // so a delivery that waited for a busy build to go quiet would lose the watch.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var log = new WatchLog();
        var watch = FileWatcher.WatchTree([folder.Root], TimeSpan.Zero, log.Record);
        Assert.NotNull(watch);
        try
        {
            const int cap = 20000;
            var stop = 0;
            var written = 0;
            var writer = Task.Run(() =>
            {
                for (var i = 0; i < cap && Volatile.Read(ref stop) == 0; i++)
                {
                    var file = folder.Child("s" + i.ToString(CultureInfo.InvariantCulture));
                    File.WriteAllBytes(file, [1]);
                    File.Delete(file);
                    Interlocked.Increment(ref written);
                }
            });
            runtime.Loop.RunUntil(() => log.Calls > 0 || writer.IsCompleted, TimeSpan.FromSeconds(30));
            Volatile.Write(ref stop, 1);
            await writer;
            Assert.True(log.Calls > 0, "Nothing was delivered.");
            Assert.True(Volatile.Read(ref written) < cap, "Nothing was delivered until the writer stopped.");
        }
        finally
        {
            watch.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Rule", "R-TASK-6")]
    [Trait("Decision", "ED-068")]
    public void A_nonzero_latency_delivers_exactly_when_the_latency_has_passed_after_the_event()
    {
        // R-GCD-3: "the latency coalescing is the port's own, a TaskHandle re-armed by each event", on SwiftClock.
        // A new directory at the root raises exactly one event (Created), so one wait is armed.
        using var runtime = new TestRuntime();
        var counting = new CountingTimeProvider(runtime.Time);
        using var clock = SwiftClock.Provider.Override(counting);
        using var folder = new ScratchFolder();
        var log = new WatchLog();
        var latency = TimeSpan.FromMilliseconds(100);
        var watch = FileWatcher.WatchTree([folder.Root], latency, log.Record);
        Assert.NotNull(watch);
        try
        {
            var created = folder.Child("a");
            Directory.CreateDirectory(created);
            runtime.Loop.RunUntil(() => counting.TimersCreated >= 1);
            runtime.RunUntilIdle();
            Assert.Equal(0, log.Calls);
            runtime.Advance(latency - TimeSpan.FromTicks(1));
            Assert.Equal(0, log.Calls);
            runtime.Advance(TimeSpan.FromTicks(1));
            runtime.Loop.RunUntil(() => log.Calls >= 1);
            Assert.True(log.Saw(created));
            Assert.True(log.AllOnMainOutsideAnyTask);
        }
        finally
        {
            watch.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Rule", "R-TASK-6")]
    [Trait("Decision", "ED-068")]
    public void Each_event_re_arms_the_latency_and_the_held_events_arrive_together_in_order()
    {
        // R-GCD-3: a TaskHandle re-armed by each event (the debounce shape of R-TASK-6, tested on both sides of each
        // boundary). Event a at 0 arms a wait due at L; event b at L - 1 ms re-arms it to 2L - 1 ms.
        using var runtime = new TestRuntime();
        var counting = new CountingTimeProvider(runtime.Time);
        using var clock = SwiftClock.Provider.Override(counting);
        using var folder = new ScratchFolder();
        var log = new WatchLog();
        var latency = TimeSpan.FromMilliseconds(100);
        var millisecond = TimeSpan.FromMilliseconds(1);
        var watch = FileWatcher.WatchTree([folder.Root], latency, log.Record);
        Assert.NotNull(watch);
        try
        {
            var first = folder.Child("a");
            Directory.CreateDirectory(first);
            runtime.Loop.RunUntil(() => counting.TimersCreated >= 1);
            runtime.Advance(latency - millisecond);
            Assert.Equal(0, log.Calls);
            var second = folder.Child("b");
            Directory.CreateDirectory(second);
            runtime.Loop.RunUntil(() => counting.TimersCreated >= 2);
            runtime.Advance(millisecond);
            runtime.RunUntilIdle();
            Assert.Equal(0, log.Calls);
            runtime.Advance(latency - millisecond - millisecond);
            Assert.Equal(0, log.Calls);
            runtime.Advance(millisecond);
            runtime.Loop.RunUntil(() => log.Calls >= 1);
            Assert.Equal(1, log.Calls);
            Assert.True(log.Saw(first));
            Assert.True(log.Saw(second));
            Assert.True(log.FirstIndex(first) < log.FirstIndex(second));
        }
        finally
        {
            watch.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    public async Task A_cancelled_tree_watch_reports_nothing_more_and_cancel_may_be_repeated_from_any_thread()
    {
        // WorkingTreeWatch.swift:159-170: tearDown runs from whichever thread gives up on the watch (off main at
        // :271 and :106) and stops the stream for good; a second tearDown finds no stream and does nothing (:160-163).
        // A second watch on the same folder is the sentinel that the later change did happen and was reported.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var cancelledLog = new WatchLog();
        var sentinelLog = new WatchLog();
        var cancelled = FileWatcher.WatchTree([folder.Root], TimeSpan.Zero, cancelledLog.Record);
        var sentinel = FileWatcher.WatchTree([folder.Root], TimeSpan.Zero, sentinelLog.Record);
        Assert.NotNull(cancelled);
        Assert.NotNull(sentinel);
        try
        {
            var before = folder.Child("before.txt");
            File.WriteAllBytes(before, [1]);
            runtime.Loop.RunUntil(() => cancelledLog.Saw(before) && sentinelLog.Saw(before));
            await Task.Run(cancelled.Cancel);
            cancelled.Cancel();
            var later = folder.Child("later.txt");
            File.WriteAllBytes(later, [1]);
            runtime.Loop.RunUntil(() => sentinelLog.Saw(later));
            runtime.RunUntilIdle();
            Assert.False(cancelledLog.Saw(later));
        }
        finally
        {
            cancelled.Cancel();
            sentinel.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-065")]
    [Trait("Decision", "ED-066")]
    public void A_directory_watch_runs_its_handler_on_main_when_an_entry_is_added_renamed_or_removed()
    {
        // CommandCodeSession.swift:654-657: eventMask [.write] on the directory, queue .main, handler
        // MainActor.assumeIsolated { scanApprovals() }. For a directory, the vnode write event (NOTE_WRITE) is a
        // change to its entries: one added, removed or renamed (SEMANTICS, kqueue EVFILT_VNODE).
        AssertDirectoryWatchRuns(folder => File.WriteAllBytes(folder.Child("2.request.json"), [1]));
        AssertDirectoryWatchRuns(folder => File.Move(folder.Child("1.request.json"), folder.Child("3.request.json")));
        AssertDirectoryWatchRuns(folder => File.Delete(folder.Child("1.request.json")));
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    public void A_directory_watch_ignores_content_writes_and_changes_inside_subdirectories()
    {
        // CommandCodeSession.swift:654: the source watches the directory vnode for .write only. Writing into an
        // existing file changes that file's vnode, and a change inside a subdirectory changes the subdirectory's,
        // not the watched directory's (SEMANTICS, kqueue EVFILT_VNODE NOTE_WRITE). A tree watch over the same folder
        // is the sentinel that both changes happened and were reported.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var existing = folder.Child("existing.request.json");
        File.WriteAllBytes(existing, [1]);
        Directory.CreateDirectory(folder.Child("sub"));
        var directoryLog = new WatchLog();
        var sentinelLog = new WatchLog();
        var watch = FileWatcher.WatchDirectory(folder.Root, directoryLog.RecordCall);
        var sentinel = FileWatcher.WatchTree([folder.Root], TimeSpan.Zero, sentinelLog.Record);
        Assert.NotNull(watch);
        Assert.NotNull(sentinel);
        try
        {
            using (var stream = new FileStream(existing, FileMode.Append, FileAccess.Write)) stream.WriteByte(2);
            var nested = folder.Child("sub", "n.txt");
            File.WriteAllBytes(nested, [1]);
            runtime.Loop.RunUntil(() => sentinelLog.Saw(existing) && sentinelLog.Saw(nested));
            runtime.RunUntilIdle();
            Assert.Equal(0, directoryLog.Calls);
        }
        finally
        {
            watch.Cancel();
            sentinel.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    public void A_directory_watch_coalesces_changes_that_arrive_before_main_runs()
    {
        // A dispatch source merges events that arrive while its handler is pending into one handler run (SEMANTICS,
        // GCD); R-GCD-3 "coalescing". Main is not pumped while 100 requests are written.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var directoryLog = new WatchLog();
        var sentinelLog = new WatchLog();
        var watch = FileWatcher.WatchDirectory(folder.Root, directoryLog.RecordCall);
        var sentinel = FileWatcher.WatchTree([folder.Root], TimeSpan.Zero, sentinelLog.Record);
        Assert.NotNull(watch);
        Assert.NotNull(sentinel);
        try
        {
            var files = Enumerable.Range(0, 100).Select(i => folder.Child(i.ToString(CultureInfo.InvariantCulture) + ".request.json")).ToArray();
            foreach (var file in files) File.WriteAllBytes(file, [1]);
            runtime.Loop.RunUntil(() => sentinelLog.SawAll(files) && directoryLog.Calls >= 1);
            runtime.RunUntilIdle();
            Assert.InRange(directoryLog.Calls, 1, 99);
            Assert.True(directoryLog.AllOnMainOutsideAnyTask);
        }
        finally
        {
            watch.Cancel();
            sentinel.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    public void Cancelling_a_directory_watch_drops_a_handler_run_already_waiting_for_main()
    {
        // CommandCodeSession.swift:663-666 stopWatcher: approvalWatcher?.cancel(). Cancelling a dispatch source
        // prevents any further invocation of its event handler (SEMANTICS, GCD dispatch_source_cancel), including
        // one whose event has already arrived. Main is not pumped until after the cancel.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var log = new WatchLog();
        var watch = FileWatcher.WatchDirectory(folder.Root, log.RecordCall);
        Assert.NotNull(watch);
        try
        {
            File.WriteAllBytes(folder.Child("a.request.json"), [1]);
            Assert.True(SpinWait.SpinUntil(() => runtime.Loop.Pending > 0, TimeSpan.FromSeconds(10)), "The event never reached main.");
            watch.Cancel();
            runtime.RunUntilIdle();
            Assert.Equal(0, log.Calls);
        }
        finally
        {
            watch.Cancel();
        }
    }

    private static void AssertDirectoryWatchRuns(Action<ScratchFolder> change)
    {
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        File.WriteAllBytes(folder.Child("1.request.json"), [1]);
        var log = new WatchLog();
        var watch = FileWatcher.WatchDirectory(folder.Root, log.RecordCall);
        Assert.NotNull(watch);
        try
        {
            change(folder);
            runtime.Loop.RunUntil(() => log.Calls >= 1);
            Assert.True(log.AllOnMainOutsideAnyTask);
        }
        finally
        {
            watch.Cancel();
        }
    }
}
