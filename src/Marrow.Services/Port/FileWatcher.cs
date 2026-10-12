// PORT-ONLY: Windows has neither FSEvents nor a vnode dispatch source; FileSystemWatcher (ReadDirectoryChangesW) stands in for both (R-GCD-3, PA-0017).
using System.ComponentModel;
using Marrow.Core.Port;

namespace Marrow.Services.Port;

/// <summary>
/// What one tree-watch delivery hands its handler: the changed paths in arrival order, and whether events may have
/// been lost since the last delivery (the FSEvents loss flags, WorkingTreeWatch.swift:180-189). A path is the root
/// string as given, a backslash, and the name Windows reports relative to that root, unchanged
/// (WorkingTreeWatch.swift:175 takes the paths as they arrive).
/// </summary>
// WorkingTreeWatch.swift:172-179: the callback's (count, eventPaths, eventFlags), reduced to what note(paths:flags:)
// reads: the paths, and whether any flag in `lost` (:180) was set.
public sealed record FileWatcherBatch(IReadOnlyList<string> Paths, bool Everything);

/// <summary>
/// The two Swift file-event sources of the pinned source (B2 1fbf81e) over FileSystemWatcher:
/// <list type="bullet">
/// <item><see cref="WatchTree"/>: the FSEvents stream of WorkingTreeWatch.swift:139-155 (file events, NoDefer,
/// latency 0, since now), delivering batches on the watch's own serial queue (:121, :147).</item>
/// <item><see cref="WatchDirectory"/>: the vnode write source of CommandCodeSession.swift:650-661, delivering on
/// main.</item>
/// </list>
/// ED-115: there is no latency and no timer. The only coalescing is what the two sources do without a clock: events
/// that arrive before the handler runs are handed over together. ED-114: every tree-watch Error means everything;
/// every directory-watch Error runs the handler. ED-113: no order is promised between roots. ED-116: the tree watch
/// delivers on the SerialQueue it is given, the directory watch on main.
/// </summary>
public sealed class FileWatcher
{
    // PR-GIT-072 windows_path: FILE_NOTIFY_CHANGE_FILE_NAME, DIR_NAME, LAST_WRITE, SIZE and ATTRIBUTES, recursive,
    // the Windows form of kFSEventStreamCreateFlagFileEvents (WorkingTreeWatch.swift:142).
    private const NotifyFilters TreeFilter =
        NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes;

    // CommandCodeSession.swift:654 eventMask [.write] on a directory vnode: an entry added, removed or renamed
    // (kqueue EVFILT_VNODE NOTE_WRITE), files and subdirectories alike, not the entries' own contents.
    private const NotifyFilters DirectoryFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName;

    private readonly Mutex<State> state;

    private FileWatcher(FileSystemWatcher[] watchers) => state = new Mutex<State>(new State { Watchers = watchers });

    /// <summary>
    /// The buffer the watch's FileSystemWatcher holds, read back from <see cref="FileSystemWatcher.InternalBufferSize"/>
    /// (ED-114).
    /// </summary>
    public int BufferSize => state.WithLock((ref State s) => s.BufferSize);

    /// <summary>
    /// <c>FSEventStreamCreate(nil, callback, &amp;context, roots, kFSEventStreamEventIdSinceNow, 0, flags)</c>,
    /// <c>FSEventStreamSetDispatchQueue(stream, queue)</c> and <c>FSEventStreamStart</c> (WorkingTreeWatch.swift:139-155).
    /// Answers null where the stream could not be made or started (:144-146, :148-154) or the directory does not
    /// exist (:94-95), so no watch is kept. Each batch runs as one <c>queue.Async</c> item, FIFO, outside any Swift
    /// task (ED-116, R-GCD-1).
    /// <para>
    /// ED-NEW-lean-18: <paramref name="skip"/>, when given, is asked first with the root and the name Windows reported;
    /// an event whose every path it answers true is dropped before the path is joined or the file system is asked
    /// anything. A caller passes only names its handler would ignore without effect (WorkingTreeWatch: git's own
    /// writes), so what the handler does is unchanged. Errors are never skipped.
    /// </para>
    /// </summary>
    // WorkingTreeWatch.swift:139-155
    public static FileWatcher? WatchTree(IReadOnlyList<string> roots, SerialQueue queue, Action<FileWatcherBatch> onEvents, int bufferSize = 65536,
        Func<string, string?, bool>? skip = null)
    {
        // :144-146: FSEventStreamCreate over no path makes no stream, and the guard returns.
        if (roots.Count == 0) return null;
        // ED-116: the start's ExecutionContext carries the process-wide bindings (in tests, the TestRuntime's) to the
        // FileSystemWatcher threads, as MainActor.Post captures it.
        var context = ExecutionContext.Capture();
        var watchers = new List<FileSystemWatcher>(roots.Count);
        FileWatcher? watch = null;
        try
        {
            // DEVIATION PA-0017: one FileSystemWatcher per root, and no order between them (ED-113). FSEvents orders
            // one stream across all its roots (:15-18); the port's only caller passes one root (ED-113).
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) return Discard(watchers);
                watchers.Add(new FileSystemWatcher(root)
                {
                    NotifyFilter = TreeFilter,
                    IncludeSubdirectories = true,
                    // ED-114 (a): 65536 by default; tests force an overflow at 4096 (P8).
                    InternalBufferSize = bufferSize,
                });
            }
            watch = new FileWatcher([.. watchers]);
            var tree = new TreeDelivery(watch, queue, onEvents, context);
            for (var i = 0; i < watchers.Count; i++)
            {
                var root = roots[i];
                var watcher = watchers[i];
                // :172-176 the callback's paths, each the changed entry's own path (:142 FileEvents).
                watcher.Created += (_, e) =>
                {
                    if (skip?.Invoke(root, e.Name) == true) return;
                    tree.Note(Join(root, e.Name));
                };
                watcher.Deleted += (_, e) =>
                {
                    if (skip?.Invoke(root, e.Name) == true) return;
                    tree.Note(Join(root, e.Name));
                };
                // ED-166 (oracle O-56): ReadDirectoryChangesW reports a directory as Changed (its last write) whenever
                // an entry inside it is created or deleted; FSEvents file events report only the entries (:142), and
                // the cookie inside the git directory (:195-196) relies on that. A Changed event whose path is an
                // existing directory is dropped; the directory's own create, delete and rename still arrive.
                watcher.Changed += (_, e) =>
                {
                    if (skip?.Invoke(root, e.Name) == true) return;
                    var path = Join(root, e.Name);
                    if (Directory.Exists(path)) return;
                    tree.Note(path);
                };
                // FSEvents reports ItemRenamed on both the old and the new path.
                watcher.Renamed += (_, e) =>
                {
                    if (skip is not null && skip(root, e.OldName) && skip(root, e.Name)) return;
                    tree.Note([Join(root, e.OldName), Join(root, e.Name)], lost: false);
                };
                // :180-189 the loss flags, and ED-114 (a): an overflow (InternalBufferOverflowException, P8) and every
                // other Error mean everything may have changed.
                watcher.Error += (_, _) => tree.Note([], lost: true);
            }
            // :145 kFSEventStreamEventIdSinceNow: only what happens from here on.
            foreach (var watcher in watchers) watcher.EnableRaisingEvents = true;
            return watch;
        }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException or Win32Exception)
        {
            // :148-154: FSEventStreamStart failed, so the stream is invalidated and released and no watch is kept.
            watch?.Cancel();
            return Discard(watchers);
        }
    }

    /// <summary>
    /// <c>DispatchSource.makeFileSystemObjectSource(fileDescriptor: open(directory, O_EVTONLY), eventMask: [.write],
    /// queue: .main)</c> with its handler (CommandCodeSession.swift:650-661). Answers null where <c>open</c> fails
    /// (:652-653). The handler runs on main, outside any Swift task, once for all the events that arrived while a run
    /// was pending (ED-115, ED-116). Not recursive, FileSystemWatcher's 8192 buffer; any Error runs the handler too
    /// (ED-114 (b)).
    /// </summary>
    // CommandCodeSession.swift:650-661
    public static FileWatcher? WatchDirectory(string directory, Action onChange)
    {
        // :652-653 open() failing: no descriptor, no source.
        if (!Directory.Exists(directory)) return null;
        var context = ExecutionContext.Capture();
        var watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = DirectoryFilter,
            IncludeSubdirectories = false,
        };
        var watch = new FileWatcher([watcher]);
        var source = new DirectoryDelivery(watch, onChange, context);
        watcher.Created += (_, _) => source.Signal();
        watcher.Deleted += (_, _) => source.Signal();
        watcher.Renamed += (_, _) => source.Signal();
        // ED-114 (b): any Error, overflow or otherwise, runs the handler once, merged with a run already pending.
        watcher.Error += (_, _) => source.Signal();
        try
        {
            // :659 source.resume()
            watcher.EnableRaisingEvents = true;
            return watch;
        }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException or Win32Exception)
        {
            watch.Cancel();
            return null;
        }
    }

    /// <summary>
    /// Stops the watch for good: nothing more reaches the handler, including a delivery already waiting for its
    /// queue or for main. Safe from any thread, and again (a second call finds no stream and does nothing).
    /// </summary>
    // WorkingTreeWatch.swift:159-170 tearDown; CommandCodeSession.swift:663-666 stopWatcher (dispatch_source_cancel).
    public void Cancel()
    {
        // :160-163: take the stream under the lock and clear it; a second call finds none.
        var watchers = state.WithLock((ref State s) =>
        {
            s.Cancelled = true;
            var taken = s.Watchers;
            s.Watchers = null;
            return taken;
        });
        if (watchers is null) return;
        // :164-168 FSEventStreamStop, Invalidate, Release; CommandCodeSession.swift:658 close(descriptor).
        foreach (var watcher in watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
    }

    private bool IsCancelled => state.WithLock((ref State s) => s.Cancelled);

    private static FileWatcher? Discard(List<FileSystemWatcher> watchers)
    {
        foreach (var watcher in watchers) watcher.Dispose();
        return null;
    }

    // WorkingTreeWatch.swift:175: the root as given, a separator, and the name relative to it, unchanged.
    private static string Join(string root, string? name) =>
        root.EndsWith('\\') || root.EndsWith('/') ? root + name : root + "\\" + name;

    private static void RunUnder(ExecutionContext? context, Action work)
    {
        if (context is null) work();
        else ExecutionContext.Run(context, static w => ((Action)w!)(), work);
    }

    private struct State
    {
        public FileSystemWatcher[]? Watchers;
        public bool Cancelled;

        public readonly int BufferSize => Watchers is { Length: > 0 } w ? w[0].InternalBufferSize : 0;
    }

    /// <summary>
    /// The tree watch's pending batch. Paths that arrive before the queue runs the delivery are handed over together,
    /// in arrival order, as the FSEvents callback receives <c>count</c> paths (WorkingTreeWatch.swift:172-176;
    /// ED-115).
    /// </summary>
    private sealed class TreeDelivery(FileWatcher owner, SerialQueue queue, Action<FileWatcherBatch> onEvents, ExecutionContext? context)
    {
        // ED-NEW-lean-18: a plain lock over the pending batch, so noting a path captures nothing (Mutex<T>.WithLock
        // takes a closure, one allocation per event).
        private readonly Lock gate = new();
        private List<string> paths = [];
        private bool everything;
        private bool scheduled;

        // ED-NEW-lean-18: one path, with no array around it.
        public void Note(string path)
        {
            if (owner.IsCancelled) return;
            bool schedule;
            lock (gate)
            {
                paths.Add(path);
                schedule = !scheduled;
                scheduled = true;
            }
            // :147 FSEventStreamSetDispatchQueue(stream, queue): one queue.Async item per batch (ED-116).
            if (schedule) RunUnder(context, () => queue.Async(Deliver));
        }

        // WorkingTreeWatch.swift:172-176 callback, on a FileSystemWatcher thread.
        public void Note(string[] noted, bool lost)
        {
            if (owner.IsCancelled) return;
            bool schedule;
            lock (gate)
            {
                paths.AddRange(noted);
                if (lost) everything = true;
                schedule = !scheduled;
                scheduled = true;
            }
            // :147 FSEventStreamSetDispatchQueue(stream, queue): one queue.Async item per batch (ED-116).
            if (schedule) RunUnder(context, () => queue.Async(Deliver));
        }

        // WorkingTreeWatch.swift:179-226 note(paths:flags:) receives the batch.
        private void Deliver()
        {
            List<string> taken;
            bool all;
            lock (gate)
            {
                taken = paths;
                all = everything;
                paths = [];
                everything = false;
                scheduled = false;
            }
            if (owner.IsCancelled) return;
            // FSEvents never calls back with no event.
            if (taken.Count == 0 && !all) return;
            onEvents(new FileWatcherBatch(taken, all));
        }
    }

    /// <summary>
    /// The directory watch's pending run: a vnode dispatch source merges the events that arrive while its handler is
    /// pending into one run (ED-115), on main (CommandCodeSession.swift:654-657; ED-116).
    /// </summary>
    private sealed class DirectoryDelivery(FileWatcher owner, Action onChange, ExecutionContext? context)
    {
        private readonly Mutex<bool> scheduled = new(false);

        // CommandCodeSession.swift:654 an event on the source, on a FileSystemWatcher thread.
        public void Signal()
        {
            if (owner.IsCancelled) return;
            var schedule = scheduled.WithLock((ref bool s) =>
            {
                if (s) return false;
                s = true;
                return true;
            });
            // :654 queue: .main
            if (schedule) RunUnder(context, () => MainActor.Post(Run));
        }

        // CommandCodeSession.swift:655-657 setEventHandler { MainActor.assumeIsolated { self?.scanApprovals() } }
        private void Run()
        {
            scheduled.WithLock((ref bool s) => { s = false; });
            // dispatch_source_cancel: no handler run after cancel, even one whose event already arrived (:663-666).
            if (owner.IsCancelled) return;
            MainActor.AssumeIsolated(onChange);
        }
    }
}
