using System.Globalization;
using Marrow.Core.Port;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port;

// Foundation unit F.FSWatcher: IMPLEMENTING.md R-GCD-3 and 4.3 ("File-system watcher": events delivered where the
// source delivers them, ED-116; overflow or any Error means rescan, ED-114; events waiting for the handler are
// batched, no latency, ED-115), probe P8 (porting/probes/p8/RESULT.md), platform adaptation PA-0017. The subject,
// src/Marrow.Services/Port/FileWatcher.cs, is PORT-ONLY: it stands in for the two Swift file-event APIs of the pinned
// source (B2 1fbf81e):
//   - FSEventStreamCreate with kFSEventStreamCreateFlagFileEvents | NoDefer, latency 0, over the working root and
//     the git directory when that lies outside it, delivering (path, flags) batches to one callback on the watch's
//     own serial queue (DroppyCode/Services/Git/WorkingTreeWatch.swift:121, :139-155, callback :172-226).
//     Port: FileWatcher.WatchTree.
//   - DispatchSource.makeFileSystemObjectSource(fileDescriptor: open(directory, O_EVTONLY), eventMask: [.write],
//     queue: .main), handler with no arguments, cancelled by stopWatcher
//     (DroppyCode/Services/Providers/CommandCodeSession.swift:651-667). Port: FileWatcher.WatchDirectory.
//
// The surface these tests fix; the implementation follows it:
//   static FileWatcher? WatchTree(IReadOnlyList<string> roots, SerialQueue queue, Action<FileWatcherBatch> onEvents, int bufferSize = 65536)
//   static FileWatcher? WatchDirectory(string directory, Action onChange)
//   int BufferSize { get; }   (read back from FileSystemWatcher.InternalBufferSize)
//   void Cancel()
//   FileWatcherBatch(IReadOnlyList<string> Paths, bool Everything)   (a sealed record)
// ED-115: no latency parameter and no timer-based coalescing. The only FSEvents caller passes latency 0 with NoDefer
// (WorkingTreeWatch.swift:142, :145) and the dispatch source has none (CommandCodeSession.swift:654), so delivery is
// never held for time and never waits on SwiftClock. The only coalescing is batching: events that arrive before the
// handler runs are handed over together (one batch of paths in order for the tree watch, :172-176; one handler run for
// the directory watch, as a vnode source merges pending events).
// ED-114: WatchTree's bufferSize defaults to 65536 (the largest InternalBufferSize the documented FileSystemWatcher API
// accepts) and every WatchTree Error, overflow or otherwise, maps to Everything = true (P8). WatchDirectory keeps
// FileSystemWatcher's 8192 default, is not recursive, and runs its handler once on main for any Error, merged with a
// run already pending.
// A reported path is the root string as given, a backslash, and the name Windows reports relative to that root,
// unchanged: no case folding, no Unicode normalization (WorkingTreeWatch.swift:175 takes the paths as they arrive;
// :191, :196 and :200 compare them with hasPrefix; :204-205 slice them).
// Everything is true when events may have been lost since the last delivery: FileSystemWatcher.Error carrying
// InternalBufferOverflowException (P8, the Windows form of the three loss flags at WorkingTreeWatch.swift:180-189),
// and any other Error (P8 "Design consequence"; ED-114).
//
// ED-116: each watch delivers where its source does. A tree watch's batches run on the SerialQueue passed to WatchTree,
// one queue.Async item each, FIFO, outside any Swift task (WorkingTreeWatch.swift:121, :147; R-GCD-1). A directory
// watch's handler runs on the main actor through MainActor.Post (CommandCodeSession.swift:654-657). Every WatchTree
// here builds its queue as new SerialQueue("test", runtime.Loop), so ManualMainLoop.RunUntil drives delivery.
//
// ED-113: FileWatcher promises no order between roots. Two FileSystemWatchers (two ReadDirectoryChangesW handles) do not
// order events between them, so the port never relies on it: when the git directory lies outside the root, the
// working-tree watch starts no watcher at all. Across roots only arrival is asserted; within one root, order is.
//
// Every test waits with ManualMainLoop.RunUntil (condition-driven, bounded), never with a fixed sleep (R-TEST-5).
//
// ED-167: the class runs in the FileWatcherTests collection, which xUnit never runs in parallel with anything, so the
// forced-overflow burst cannot starve another class's thread-pool work (Fixtures/FileWatcherCollection.cs).
[Collection(FileWatcherCollection.Name)]
public sealed class FileWatcherTests
{
    [Fact]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Rule", "R-GCD-1")]
    [Trait("Decision", "ED-066")]
    [Trait("Decision", "ED-116")]
    public void A_file_created_in_a_watched_tree_reaches_the_handler_on_its_queue_outside_any_task_with_its_full_path()
    {
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var queue = new SerialQueue("test", runtime.Loop);
        var log = new WatchLog(queue);
        var watch = FileWatcher.WatchTree([folder.Root], queue, log.Record);
        Assert.NotNull(watch);
        try
        {
            var file = folder.Child("a.txt");
            File.WriteAllBytes(file, [1]);
            runtime.Loop.RunUntil(() => log.Saw(file));
            // WorkingTreeWatch.swift:121/:147 deliver on the watch's own serial queue (ED-116): one queue.Async item
            // per batch, which runs outside any Swift task as a GCD block does (R-GCD-1).
            Assert.True(log.AllOnTheirQueueOutsideAnyTask);
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
    [Trait("Decision", "ED-114")]
    public void A_tree_watch_started_with_no_buffer_size_uses_a_64_KB_buffer()
    {
        // ED-114 (a): WatchTree's bufferSize defaults to 65536, the largest InternalBufferSize the documented
        // FileSystemWatcher API accepts; BufferSize reads it back from FileSystemWatcher.InternalBufferSize.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var queue = new SerialQueue("test", runtime.Loop);
        var log = new WatchLog(queue);
        var watch = FileWatcher.WatchTree([folder.Root], queue, log.Record);
        Assert.NotNull(watch);
        try
        {
            Assert.Equal(65536, watch.BufferSize);
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
        var queue = new SerialQueue("test", runtime.Loop);
        var log = new WatchLog(queue);
        var watch = FileWatcher.WatchTree([folder.Root], queue, log.Record);
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
            Assert.True(log.AllOnTheirQueueOutsideAnyTask);
            Assert.False(log.Everything);
        }
        finally
        {
            watch.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-166")]
    [Trait("Oracle", "O-56")]
    public void A_file_created_or_deleted_in_a_subdirectory_is_reported_by_its_own_path_never_by_the_subdirectory_path()
    {
        // ED-166: WorkingTreeWatch.swift:142 kFSEventStreamCreateFlagFileEvents reports the changed entries' own
        // paths; the Swift's cookie files inside the git directory (:195-196, skipped by the gitPrefix match only) and
        // its cached-tree reuse only work if the directory whose entries changed is not reported too. That expectation
        // rests on the open oracle item O-56 (UNVERIFIED on macOS), hence the Oracle trait and no Req. On Windows,
        // ReadDirectoryChangesW with LastWrite in the tree filter reports the holding directory as Changed; WatchTree
        // drops a Changed event whose path is an existing directory. A sentinel written after both changes, in the
        // same root, is delivered after them (ED-113 keeps order within one root), so once it arrives any report of
        // the subdirectory caused by them has arrived too.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var directory = folder.Child("sub");
        Directory.CreateDirectory(directory);
        var deleted = folder.Child("sub", "gone.txt");
        File.WriteAllBytes(deleted, [1]);
        var queue = new SerialQueue("test", runtime.Loop);
        var log = new WatchLog(queue);
        var watch = FileWatcher.WatchTree([folder.Root], queue, log.Record);
        Assert.NotNull(watch);
        try
        {
            var created = folder.Child("sub", "new.txt");
            File.WriteAllBytes(created, [1]);
            File.Delete(deleted);
            var sentinel = folder.Child("sentinel.txt");
            File.WriteAllBytes(sentinel, [1]);
            runtime.Loop.RunUntil(() => log.SawAll([created, deleted, sentinel]));
            runtime.RunUntilIdle();
            Assert.False(log.Saw(directory));
            Assert.False(log.Everything);
            Assert.True(log.AllOnTheirQueueOutsideAnyTask);
        }
        finally
        {
            watch.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-113")]
    public void Changes_under_one_root_are_delivered_in_the_order_they_happened()
    {
        // WorkingTreeWatch.swift:15-18: "events arrive in order, so everything written before [the cookie] has been
        // counted"; settledGeneration (:246-273) rests on that order. ED-113 keeps it asserted within one root.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var queue = new SerialQueue("test", runtime.Loop);
        var log = new WatchLog(queue);
        var watch = FileWatcher.WatchTree([folder.Root], queue, log.Record);
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
    [Trait("Decision", "ED-113")]
    public void Changes_under_every_root_reach_the_one_handler()
    {
        // WorkingTreeWatch.swift:143: roots = gitRoot inside root ? [root] : [root, gitRoot], one stream, one callback.
        // Only arrival is asserted here: FileWatcher promises no order between roots (ED-113).
        using var runtime = new TestRuntime();
        using var root = new ScratchFolder();
        using var gitRoot = new ScratchFolder();
        var queue = new SerialQueue("test", runtime.Loop);
        var log = new WatchLog(queue);
        var watch = FileWatcher.WatchTree([root.Root, gitRoot.Root], queue, log.Record);
        Assert.NotNull(watch);
        try
        {
            var working = root.Child("a.txt");
            var cookie = gitRoot.Child("droppy-code-sync-1");
            File.WriteAllBytes(working, [1]);
            File.WriteAllBytes(cookie, []);
            runtime.Loop.RunUntil(() => log.Saw(working) && log.Saw(cookie));
            Assert.True(log.AllOnTheirQueueOutsideAnyTask);
        }
        finally
        {
            watch.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    public void Names_come_through_exactly_as_the_file_system_holds_them_with_no_case_folding_or_normalization()
    {
        // WorkingTreeWatch.swift:175 takes the event paths as they arrive, :191/:196/:200 test them with hasPrefix
        // and :204-205 slice the relative path: nothing lowercases, normalizes or rebuilds them. Names: mixed case
        // with a precomposed e-acute (U+00E9), a decomposed e-acute (U+0065 U+0301), CJK (U+6F22 U+5B57), and a
        // supplementary-plane character (U+20BB7, the surrogate pair D842 DFB7). Escapes keep the bytes unambiguous.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        string[] names = ["MiéXeD.TXT", "é.txt", "漢字.txt", "𠮷.txt"];
        var expected = names.Select(name => folder.Root + "\\" + name).ToArray();
        var queue = new SerialQueue("test", runtime.Loop);
        var log = new WatchLog(queue);
        var watch = FileWatcher.WatchTree([folder.Root], queue, log.Record);
        Assert.NotNull(watch);
        try
        {
            foreach (var path in expected) File.WriteAllBytes(path, [1]);
            runtime.Loop.RunUntil(() => log.SawAll(expected));
            runtime.RunUntilIdle();
            // Every reported path is one of the exact strings (ordinal): no folded or normalized twin was reported.
            Assert.All(log.Paths, path => Assert.Contains(path, expected, StringComparer.Ordinal));
            // The last name really is a surrogate pair plus ".txt": 2 + 4 UTF-16 code units.
            Assert.Equal(6, names[3].Length);
            Assert.False(log.Everything);
        }
        finally
        {
            watch.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    public void Changes_made_before_the_watch_starts_are_not_reported()
    {
        // WorkingTreeWatch.swift:145 FSEventStreamEventId(kFSEventStreamEventIdSinceNow); PR-GIT-072: events
        // "starting from the moment of creation". No replay of history and no initial scan of what exists. A
        // sentinel created after the watch starts proves the watch is live before the earlier files are judged.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var created = folder.Child("created-before.txt");
        var written = folder.Child("written-before.txt");
        var removed = folder.Child("removed-before.txt");
        var directory = folder.Child("dir-before");
        File.WriteAllBytes(written, [1]);
        File.WriteAllBytes(removed, [1]);
        using (var stream = new FileStream(written, FileMode.Append, FileAccess.Write)) stream.WriteByte(2);
        File.WriteAllBytes(created, [1]);
        File.Delete(removed);
        Directory.CreateDirectory(directory);
        var queue = new SerialQueue("test", runtime.Loop);
        var log = new WatchLog(queue);
        var watch = FileWatcher.WatchTree([folder.Root], queue, log.Record);
        Assert.NotNull(watch);
        try
        {
            var sentinel = folder.Child("sentinel.txt");
            File.WriteAllBytes(sentinel, [1]);
            runtime.Loop.RunUntil(() => log.Saw(sentinel));
            runtime.RunUntilIdle();
            Assert.False(log.Saw(created));
            Assert.False(log.Saw(written));
            Assert.False(log.Saw(removed));
            Assert.False(log.Saw(directory));
            Assert.False(log.Everything);
        }
        finally
        {
            watch.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-115")]
    [Trait("Decision", "ED-116")]
    public void Events_that_arrive_before_the_queue_runs_are_batched_into_fewer_deliveries_and_none_is_lost()
    {
        // WorkingTreeWatch.swift:172-176: the FSEvents callback `{ _, info, count, eventPaths, eventFlags, _ in ... }`
        // hands over `count` paths per call, a batch, and :185 walks them in order. The queue's loop is not pumped
        // while 200 files are written, so the events wait for the queue together; one delivery per event would make
        // at least 200 calls.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var queue = new SerialQueue("test", runtime.Loop);
        var log = new WatchLog(queue);
        var watch = FileWatcher.WatchTree([folder.Root], queue, log.Record);
        Assert.NotNull(watch);
        try
        {
            var files = Enumerable.Range(0, 200).Select(i => folder.Child("c" + i.ToString(CultureInfo.InvariantCulture) + ".txt")).ToArray();
            foreach (var file in files) File.WriteAllBytes(file, [1]);
            runtime.Loop.RunUntil(() => log.SawAll(files));
            Assert.InRange(log.Calls, 1, 199);
            Assert.False(log.AnyEmptyBatch);
            Assert.False(log.Everything);
            Assert.True(log.AllOnTheirQueueOutsideAnyTask);
        }
        finally
        {
            watch.Cancel();
        }
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-114")]
    [Trait("Decision", "ED-116")]
    public void A_forced_buffer_overflow_reaches_the_handler_as_everything_and_the_watch_keeps_reporting()
    {
        // P8 (porting/probes/p8/RESULT.md): with InternalBufferSize at its 4 KB minimum a fast burst overflows and
        // FileSystemWatcher.Error carries InternalBufferOverflowException; that maps to everything = true /
        // changed = nil (WorkingTreeWatch.swift:180-189, :211-212; ED-114). The overflow is timing dependent (P8: 6 of
        // 18 bursts of 10,000 files), so bursts repeat until one does, at most 40. Each burst also waits until every
        // burst file was reported or the loss was signalled: P8's "loss implies signal" (C4); a burst that loses
        // events silently times out here.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var queue = new SerialQueue("test", runtime.Loop);
        var log = new WatchLog(queue);
        var watch = FileWatcher.WatchTree([folder.Root], queue, log.Record, bufferSize: 4096);
        Assert.NotNull(watch);
        try
        {
            Assert.Equal(4096, watch.BufferSize);
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
            Assert.True(log.AllOnTheirQueueOutsideAnyTask);
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
    [Trait("Decision", "ED-114")]
    public void An_error_other_than_overflow_also_reaches_the_handler_as_everything()
    {
        // P8 "Design consequence" and ED-114 (a): every other Error exception type is handled as lost too
        // (WorkingTreeWatch.swift:18-21 "anything the stream could have missed ... means a fresh snapshot"). P8 saw no
        // Error of another type; that deleting the watched root raises one comes from this unit's own check (the
        // F.FSWatcher part 1 report: a scratch console program, FileSystemWatcher with the PR-GIT-072 filter on
        // <temp>\delN\watched, Directory.Delete(root, true), 10 trials: "10 x Win32Exception 5 Access is denied.").
        // Swift reaches everything here too: the root's own path fails the rootPrefix test at :200.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var root = folder.Child("watched");
        Directory.CreateDirectory(folder.Child("watched", "sub"));
        File.WriteAllBytes(folder.Child("watched", "sub", "a.txt"), [1]);
        var queue = new SerialQueue("test", runtime.Loop);
        var log = new WatchLog(queue);
        var watch = FileWatcher.WatchTree([root], queue, log.Record);
        Assert.NotNull(watch);
        try
        {
            Directory.Delete(root, recursive: true);
            runtime.Loop.RunUntil(() => log.Everything);
            Assert.True(log.AllOnTheirQueueOutsideAnyTask);
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
        // WorkingTreeWatch.swift:94-95: for a directory that does not exist the volumeIsLocalKey lookup throws, `try?`
        // makes it nil, and the guard returns nil: no watch, so every snapshot is taken fresh. WatchTree answers null
        // for such a root. CommandCodeSession.swift:652-653: open() failing returns with no source; WatchDirectory
        // answers null.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var queue = new SerialQueue("test", runtime.Loop);
        var treeLog = new WatchLog(queue);
        var directoryLog = new WatchLog();
        Assert.Null(FileWatcher.WatchTree([folder.Child("missing")], queue, treeLog.Record));
        Assert.Null(FileWatcher.WatchDirectory(folder.Child("missing"), directoryLog.RecordCall));
        runtime.RunUntilIdle();
        Assert.Equal(0, treeLog.Calls);
        Assert.Equal(0, directoryLog.Calls);
    }

    [Fact]
    [Trait("Rule", "R-GCD-3")]
    [Trait("Decision", "ED-115")]
    public async Task With_zero_latency_a_steady_stream_of_changes_is_delivered_while_it_lasts()
    {
        // WorkingTreeWatch.swift:145 latency 0 with :142 NoDefer: nothing holds events back until writing stops. The
        // cookie wait gives up after 1 s (:258-261) and then tears the stream down for good (:271, upstream defect 82),
        // so a delivery that waited for a busy build to go quiet would lose the watch (ED-115).
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var queue = new SerialQueue("test", runtime.Loop);
        var log = new WatchLog(queue);
        var watch = FileWatcher.WatchTree([folder.Root], queue, log.Record);
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
    public async Task A_cancelled_tree_watch_reports_nothing_more_and_cancel_may_be_repeated_from_any_thread()
    {
        // WorkingTreeWatch.swift:159-170: tearDown runs from whichever thread gives up on the watch (off main at
        // :271 and :106) and stops the stream for good; a second tearDown finds no stream and does nothing (:160-163).
        // A second watch on the same folder is the sentinel that the later change did happen and was reported.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var cancelledQueue = new SerialQueue("test", runtime.Loop);
        var sentinelQueue = new SerialQueue("test", runtime.Loop);
        var cancelledLog = new WatchLog(cancelledQueue);
        var sentinelLog = new WatchLog(sentinelQueue);
        var cancelled = FileWatcher.WatchTree([folder.Root], cancelledQueue, cancelledLog.Record);
        var sentinel = FileWatcher.WatchTree([folder.Root], sentinelQueue, sentinelLog.Record);
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
    [Trait("Decision", "ED-116")]
    public void A_directory_watch_runs_its_handler_on_main_when_an_entry_is_added_renamed_or_removed()
    {
        // CommandCodeSession.swift:654-657: eventMask [.write] on the directory, queue .main, handler
        // MainActor.assumeIsolated { scanApprovals() }. For a directory, the vnode write event (NOTE_WRITE) is a
        // change to its entries: one added, removed or renamed (SEMANTICS, kqueue EVFILT_VNODE), files and
        // subdirectories alike: a subdirectory is an entry of the watched directory too.
        AssertDirectoryWatchRuns(folder => File.WriteAllBytes(folder.Child("2.request.json"), [1]));
        AssertDirectoryWatchRuns(folder => File.Move(folder.Child("1.request.json"), folder.Child("3.request.json")));
        AssertDirectoryWatchRuns(folder => File.Delete(folder.Child("1.request.json")));
        AssertDirectoryWatchRuns(folder => Directory.CreateDirectory(folder.Child("d")));
        AssertDirectoryWatchRuns(folder => Directory.Move(folder.Child("seed.d"), folder.Child("moved.d")));
        AssertDirectoryWatchRuns(folder => Directory.Delete(folder.Child("seed.d")));
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
        var sentinelQueue = new SerialQueue("test", runtime.Loop);
        var directoryLog = new WatchLog();
        var sentinelLog = new WatchLog(sentinelQueue);
        var watch = FileWatcher.WatchDirectory(folder.Root, directoryLog.RecordCall);
        var sentinel = FileWatcher.WatchTree([folder.Root], sentinelQueue, sentinelLog.Record);
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
    [Trait("Decision", "ED-115")]
    public void A_directory_watch_coalesces_changes_that_arrive_before_main_runs()
    {
        // A dispatch source merges events that arrive while its handler is pending into one handler run (SEMANTICS,
        // GCD: a vnode source ORs the flags of pending events into one dispatch_source_get_data; ED-115). Main is not
        // pumped while 100 requests are written.
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var sentinelQueue = new SerialQueue("test", runtime.Loop);
        var directoryLog = new WatchLog();
        var sentinelLog = new WatchLog(sentinelQueue);
        var watch = FileWatcher.WatchDirectory(folder.Root, directoryLog.RecordCall);
        var sentinel = FileWatcher.WatchTree([folder.Root], sentinelQueue, sentinelLog.Record);
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
    [Trait("Decision", "ED-114")]
    [Trait("Decision", "ED-116")]
    public void Deleting_the_empty_watched_directory_runs_the_directory_handler_on_main_outside_any_task()
    {
        // ED-114 (b): any Error on the directory watch runs the handler once on main, merged with a run already
        // pending. The watched directory is EMPTY, so no entry event can cause the run: only the Error can. That
        // deleting an empty watched directory raises an Error comes from this unit's own check (the F.FSWatcher part 1
        // report: a scratch console program, FileSystemWatcher with NotifyFilter FileName | DirectoryName, not
        // recursive, InternalBufferSize 8192, on an empty <temp>\fsw-emptydel-<guid>\watchedN, Directory.Delete(dir),
        // 10 trials: each "Error Win32Exception 5 Access is denied." and no other event). The run is harmless upstream:
        // scanApprovals returns when the directory cannot be read (CommandCodeSession.swift:672-673).
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        var directory = folder.Child("approvals");
        Directory.CreateDirectory(directory);
        var log = new WatchLog();
        var watch = FileWatcher.WatchDirectory(directory, log.RecordCall);
        Assert.NotNull(watch);
        try
        {
            Directory.Delete(directory);
            runtime.Loop.RunUntil(() => log.Calls >= 1);
            Assert.True(log.AllOnMainOutsideAnyTask);
        }
        finally
        {
            watch.Cancel();
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
        Directory.CreateDirectory(folder.Child("seed.d"));
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
