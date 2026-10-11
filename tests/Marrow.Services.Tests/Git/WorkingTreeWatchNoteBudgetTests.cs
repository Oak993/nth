using System.Globalization;
using System.Reflection;
using Marrow.Services.Git;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Git;

// ED-NEW-lean-18 (lean unit L18a): what one file event costs WorkingTreeWatch.NoteBatch on the watch's queue. A build
// writes thousands of files under bin, obj and node_modules, and every git command writes under .git; before the lean
// pass each path was normalised up to five times (SwiftPath.Normalize, Replace, and two TailUnder calls that normalise
// both sides) before the first prefix test. Bytes are counted with GC.GetAllocatedBytesForCurrentThread after a
// warm-up, in a Debug build (R-TEST-5: no clock, no sleep). The paths are built the way FileWatcher builds them: the
// watch's own resolved root, a backslash, and the name Windows reports.
public sealed class WorkingTreeWatchNoteBudgetTests
{
    private const int Warmup = 20;
    private const int Rounds = 50;

    [Fact]
    [Trait("Decision", "ED-NEW-lean-18")]
    public void A_bin_obj_or_node_modules_event_stays_within_its_byte_budget()
    {
        using var folder = new ScratchFolder();
        var (watch, root, _) = Watch(folder);
        var paths = BuildOutput(root);
        var bytes = BytesPerEvent(watch, paths);
        // Before the fix (0a946c558, Debug): 4,960.7 B per event. Limit ~1.5x of that.
        Assert.True(bytes <= 7440, $"{bytes.ToString("F1", CultureInfo.InvariantCulture)} B per build-output event");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-18")]
    public void A_git_internal_event_stays_within_its_byte_budget()
    {
        using var folder = new ScratchFolder();
        var (watch, _, gitRoot) = Watch(folder);
        var paths = GitInternal(gitRoot);
        var bytes = BytesPerEvent(watch, paths);
        // Before the fix (0a946c558, Debug): 2,862.2 B per event. Limit ~1.5x of that.
        Assert.True(bytes <= 4290, $"{bytes.ToString("F1", CultureInfo.InvariantCulture)} B per git-internal event");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-18")]
    public void Every_batch_leaves_the_watch_exactly_where_the_pre_lean_note_left_it()
    {
        using var folder = new ScratchFolder();
        var (watch, root, gitRoot) = Watch(folder);
        var oracle = new WorkingTreeNoteOracle(root, gitRoot);
        var startGeneration = watch.Generation;
        var random = new Random(18);
        var pool = Pool(root, gitRoot, folder.Root);
        for (var batch = 0; batch < 400; batch++)
        {
            var count = random.Next(0, 12);
            var paths = new string[count];
            for (var i = 0; i < count; i++) paths[i] = pool[random.Next(pool.Count)];
            var everything = random.Next(40) == 0;
            watch.NoteBatch(paths, everything);
            oracle.NoteBatch(paths, everything);
            Assert.Equal(oracle.Generation - 1, watch.Generation - startGeneration);
            Assert.Equal(oracle.CookieSeen, watch.CookieSeen);
            Assert.Equal(oracle.Changed?.Order(StringComparer.Ordinal).ToArray(), watch.ChangedPaths);
            if (oracle.Changed is null)
            {
                // A whole-tree batch empties both sides the same way: the next capture is a full one.
                Assert.Null(watch.ChangedPaths);
            }
        }
    }

    private static (WorkingTreeWatch Watch, string Root, string GitRoot) Watch(ScratchFolder folder)
    {
        var git = folder.Child(".git");
        Directory.CreateDirectory(git);
        var watch = WorkingTreeWatch.Shared(folder.Root, git)!;
        var root = (string)typeof(WorkingTreeWatch).GetField("root", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(watch)!;
        var gitRoot = (string)typeof(WorkingTreeWatch).GetField("gitRoot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(watch)!;
        return (watch, root, gitRoot);
    }

    private static double BytesPerEvent(WorkingTreeWatch watch, string[] paths)
    {
        for (var i = 0; i < Warmup; i++) watch.NoteBatch(paths, false);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Rounds; i++) watch.NoteBatch(paths, false);
        var after = GC.GetAllocatedBytesForCurrentThread();
        return (after - before) / (double)(Rounds * paths.Length);
    }

    // A dotnet build and an npm install: many files in a few directories.
    private static string[] BuildOutput(string root)
    {
        var paths = new string[300];
        for (var i = 0; i < 100; i++)
        {
            paths[i] = root + "\\src\\App\\bin\\Debug\\net10.0\\Assembly" + i.ToString(CultureInfo.InvariantCulture) + ".dll";
            paths[100 + i] = root + "\\src\\App\\obj\\Debug\\net10.0\\Generated" + i.ToString(CultureInfo.InvariantCulture) + ".cs";
            paths[200 + i] = root + "\\node_modules\\package" + (i % 5).ToString(CultureInfo.InvariantCulture) + "\\lib\\file" +
                i.ToString(CultureInfo.InvariantCulture) + ".js";
        }
        return paths;
    }

    // What one git command writes: loose objects, the index lock, refs and logs.
    private static string[] GitInternal(string gitRoot)
    {
        var paths = new string[300];
        for (var i = 0; i < 300; i++)
        {
            paths[i] = (i % 4) switch
            {
                0 => gitRoot + "\\objects\\" + (i % 256).ToString("x2", CultureInfo.InvariantCulture) + "\\" + new string('a', 30) +
                     i.ToString("x8", CultureInfo.InvariantCulture),
                1 => gitRoot + "\\index.lock",
                2 => gitRoot + "\\refs\\nth\\checkpoints\\turn-" + i.ToString(CultureInfo.InvariantCulture),
                _ => gitRoot + "\\logs\\HEAD",
            };
        }
        return paths;
    }

    // Every shape a path can take: the fast forms, case and separator variants, prefixed forms, the roots themselves,
    // cookies (well formed, malformed, other case), paths outside the root and non-ASCII names.
    private static List<string> Pool(string root, string gitRoot, string given)
    {
        var lower = root.Length > 1 && root[1] == ':' ? char.ToLowerInvariant(root[0]) + root[1..] : root;
        var gitTail = gitRoot[(root.Length + 1)..];
        var pool = new List<string>
        {
            root, root + "\\", gitRoot, gitRoot + "\\", lower, given, given + "\\readme",
            root + "\\readme", root + "\\src\\a.cs", root + "\\src\\bin\\x.dll", root + "\\obj\\y.cs", root + "\\node_modules\\p\\i.js",
            root + "/src/slashed.cs", root + "\\src/mixed\\m.cs", lower + "\\src\\lower.cs", lower + "\\" + gitTail + "\\objects\\q",
            "\\\\?\\" + root + "\\src\\long.cs", "\\\\?\\" + gitRoot + "\\index", "//?/" + root + "/src/long2.cs",
            gitRoot + "\\index", gitRoot + "\\objects\\ab\\cd", gitRoot + "/objects/slashed", root + "\\" + gitTail.ToUpperInvariant() + "\\HEAD",
            root + "\\" + gitTail + "x\\not-git", root + "\\" + gitTail + "\\droppy-code-sync-12", root + "\\" + gitTail + "\\droppy-code-sync-3",
            gitRoot + "\\DROPPY-CODE-SYNC-40", gitRoot + "\\droppy-code-sync-", gitRoot + "\\droppy-code-sync-x7", gitRoot + "\\droppy-code-sync-+5",
            gitRoot + "/droppy-code-sync-21", "\\\\?\\" + gitRoot + "\\droppy-code-sync-30",
            "C:\\elsewhere\\file", "Z:\\outside", root + "x\\sibling", root + "\\\u00e9t\u00e9\\caf\u00e9.txt", root + "\\\u0131ndex\\a",
            root + "\\" + gitTail.Replace('i', '\u0131') + "\\HEAD", root + "\\K\u212a\\k", root + "\\deep\\a\\b\\c\\d\\e.txt",
        };
        for (var i = 0; i < 80; i++) pool.Add(root + "\\many" + i.ToString(CultureInfo.InvariantCulture) + "\\f.txt");
        return pool;
    }
}
