using System.Globalization;
using System.Reflection;
using Marrow.Services.Git;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port;

// ED-NEW-lean-18 (lean unit L18a): what one Windows file event costs the working-tree watch's FileSystemWatcher
// handler (FileWatcher.WatchTree) before the batch reaches WorkingTreeWatch's queue. The handlers are raised through
// FileSystemWatcher's own OnCreated/OnChanged, which call them synchronously on this thread, on the watch a real
// WorkingTreeWatch.Shared starts; the event arguments are built once, outside the count. Bytes are counted with
// GC.GetAllocatedBytesForCurrentThread after a warm-up, in a Debug build (R-TEST-5: no clock, no sleep). A Changed
// event also asks the file system whether the path is a directory (ED-166), which is part of the cost.
[Collection(FileWatcherCollection.Name)]
public sealed class FileWatcherEventBudgetTests
{
    private const int Warmup = 20;
    private const int Rounds = 50;

    [Fact]
    [Trait("Decision", "ED-NEW-lean-18")]
    public void A_bin_obj_or_node_modules_event_stays_within_its_byte_budget()
    {
        using var folder = new ScratchFolder();
        var (watcher, root, _) = Watcher(folder);
        var created = Raiser(watcher, "OnCreated");
        var changed = Raiser(watcher, "OnChanged");
        var events = new List<(Action<FileSystemEventArgs> Raise, FileSystemEventArgs Args)>();
        for (var i = 0; i < 100; i++)
        {
            var n = i.ToString(CultureInfo.InvariantCulture);
            events.Add((created, new FileSystemEventArgs(WatcherChangeTypes.Created, root, "src\\App\\obj\\Debug\\net10.0\\Generated" + n + ".cs")));
            events.Add((changed, new FileSystemEventArgs(WatcherChangeTypes.Changed, root, "src\\App\\bin\\Debug\\net10.0\\Assembly" + n + ".dll")));
            events.Add((created, new FileSystemEventArgs(WatcherChangeTypes.Created, root, "node_modules\\package" + (i % 5).ToString(CultureInfo.InvariantCulture) + "\\lib\\file" + n + ".js")));
        }
        var bytes = BytesPerEvent(events);
        // Before the fix (0a946c558, Debug): 558.2 B per event (limit then 840). After the fix: 418.8 B, mostly
        // the delivered path itself; limit ~1.3x.
        Assert.True(bytes <= 545, $"{bytes.ToString("F1", CultureInfo.InvariantCulture)} B per build-output event");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-18")]
    public void A_git_internal_event_stays_within_its_byte_budget()
    {
        using var folder = new ScratchFolder();
        var (watcher, root, gitTail) = Watcher(folder);
        var created = Raiser(watcher, "OnCreated");
        var changed = Raiser(watcher, "OnChanged");
        var events = new List<(Action<FileSystemEventArgs> Raise, FileSystemEventArgs Args)>();
        for (var i = 0; i < 300; i++)
        {
            var name = (i % 4) switch
            {
                0 => gitTail + "\\objects\\" + (i % 256).ToString("x2", CultureInfo.InvariantCulture) + "\\" + new string('a', 30) +
                     i.ToString("x8", CultureInfo.InvariantCulture),
                1 => gitTail + "\\index.lock",
                2 => gitTail + "\\refs\\nth\\checkpoints\\turn-" + i.ToString(CultureInfo.InvariantCulture),
                _ => gitTail + "\\logs\\HEAD",
            };
            events.Add(i % 2 == 0
                ? (created, new FileSystemEventArgs(WatcherChangeTypes.Created, root, name))
                : (changed, new FileSystemEventArgs(WatcherChangeTypes.Changed, root, name)));
        }
        var bytes = BytesPerEvent(events);
        // Before the fix (0a946c558, Debug): 590.5 B per event (limit then 890). After the fix: 0.0 B (git's own
        // writes are dropped before the path is joined); limit 0.5, target 0.
        Assert.True(bytes <= 0.5, $"{bytes.ToString("F1", CultureInfo.InvariantCulture)} B per git-internal event");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-18")]
    public void The_watch_ends_where_the_pre_lean_note_of_every_raised_path_ends()
    {
        // Same events delivered: whatever the stream now drops before joining, the watch's generation, cookie and
        // changed folders come out as the pre-lean NoteBatch gives for every path raised, dropped or not.
        using var folder = new ScratchFolder();
        var (watcher, root, gitTail) = Watcher(folder);
        var watch = WorkingTreeWatch.Shared(folder.Root, folder.Child(".git"))!;
        var gitRoot = root + "\\" + gitTail;
        var git = gitTail + "\\";
        var created = Raiser(watcher, "OnCreated");
        var changed = Raiser(watcher, "OnChanged");
        var deleted = Raiser(watcher, "OnDeleted");
        var names = new[]
        {
            git + "objects\\ab\\cdef", git + "index.lock", "src\\a.cs", git + "droppy-code-sync-41", "obj\\Debug\\x.cs",
            git.ToUpperInvariant() + "HEAD", "node_modules\\p\\i.js", git + "refs\\heads\\main", git + "DROPPY-CODE-SYNC-42",
            "bin\\Debug\\y.dll", git + "logs\\HEAD",
        };
        var oracle = new WorkingTreeNoteOracle(root, gitRoot);
        var startGeneration = watch.Generation;
        var raises = new[] { created, changed, deleted };
        for (var i = 0; i < names.Length; i++)
        {
            var kind = i % 3 == 0 ? WatcherChangeTypes.Created : i % 3 == 1 ? WatcherChangeTypes.Changed : WatcherChangeTypes.Deleted;
            raises[i % 3](new FileSystemEventArgs(kind, root, names[i]));
            oracle.NoteBatch([root + "\\" + names[i]], false);
        }
        var expected = oracle.Changed!.Order(StringComparer.Ordinal).ToArray();
        // Delivery runs on the watch's own queue; wait for it, bounded, on the state itself.
        Assert.True(SpinWait.SpinUntil(() => watch.CookieSeen == oracle.CookieSeen &&
            watch.ChangedPaths is { } paths && paths.SequenceEqual(expected), TimeSpan.FromSeconds(30)));
        Assert.Equal(oracle.CookieSeen, watch.CookieSeen);
        Assert.Equal(expected, watch.ChangedPaths);
        Assert.InRange(watch.Generation - startGeneration, (ulong)1, oracle.Generation - 1);
    }

    private static (FileSystemWatcher Watcher, string Root, string GitTail) Watcher(ScratchFolder folder)
    {
        var git = folder.Child(".git");
        Directory.CreateDirectory(git);
        var watch = WorkingTreeWatch.Shared(folder.Root, git)!;
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        var root = (string)typeof(WorkingTreeWatch).GetField("root", Private)!.GetValue(watch)!;
        var gitRoot = (string)typeof(WorkingTreeWatch).GetField("gitRoot", Private)!.GetValue(watch)!;
        var stream = (FileWatcher)typeof(WorkingTreeWatch).GetField("stream", Private)!.GetValue(watch)!;
        var mutex = typeof(FileWatcher).GetField("state", Private)!.GetValue(stream)!;
        var state = mutex.GetType().GetField("value", Private)!.GetValue(mutex)!;
        var watchers = (FileSystemWatcher[])state.GetType().GetField("Watchers")!.GetValue(state)!;
        return (watchers[0], root, gitRoot[(root.Length + 1)..]);
    }

    private static Action<FileSystemEventArgs> Raiser(FileSystemWatcher watcher, string method) =>
        typeof(FileSystemWatcher).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance, [typeof(FileSystemEventArgs)])!
            .CreateDelegate<Action<FileSystemEventArgs>>(watcher);

    private static double BytesPerEvent(List<(Action<FileSystemEventArgs> Raise, FileSystemEventArgs Args)> events)
    {
        for (var i = 0; i < Warmup; i++)
            foreach (var (raise, args) in events) raise(args);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Rounds; i++)
            foreach (var (raise, args) in events) raise(args);
        var after = GC.GetAllocatedBytesForCurrentThread();
        return (after - before) / (double)(Rounds * events.Count);
    }
}
