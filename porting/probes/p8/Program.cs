// P8, filesystem watch overflow signal (task packet T-M0-007).
//
// QUESTION: on .NET 10 / Windows 11, does System.IO.FileSystemWatcher with InternalBufferSize at
// its 4 KB minimum, watching a repo-shaped tree, raise its Error event carrying
// InternalBufferOverflowException when a burst of file writes loses events, so the port can map
// it to the "we lost events, walk the whole tree" semantic?
//
// SOURCE FACT (pinned Droppy Code dd2b86ef, unchanged at live 1fbf81e6):
//   DroppyCode/Services/Git/WorkingTreeWatch.swift:180 treats kFSEventStreamEventFlagMustScanSubDirs,
//   kFSEventStreamEventFlagKernelDropped and kFSEventStreamEventFlagUserDropped as "lost":
//   :186-189 sets everything = true, :211-212 then sets state.changed = nil, and :308 hands the
//   capture paths == nil, "nil means the whole tree" (:288). Otherwise :197-205 records the
//   directory holding each changed entry, relative to the root, and :196 ignores git's own writes.
//
// PASS CONDITIONS, all checked mechanically at the end of this file. Revised after run 1 (see
// RESULT.md, "Deviations"): run 1 used stricter self-imposed conditions and a control check with a
// probe bug; both are recorded there with the run-1 output.
//   C1 (probe text) At least one InternalBufferOverflowException Error across the 10,000-file burst
//      trials at InternalBufferSize 4096.
//   C2 (probe text) Every trial that took the full-rescan path (taken only because the flag was
//      set) ran it to completion and found every file on disk: missing == 0, extra == 0; and at
//      least one trial took it.
//   C3 CONTROL: a 5-file burst on the same watcher configuration raises NO Error, delivers all 5
//      Created events and its incremental changed-directory set covers every written directory
//      and holds nothing but those directories and their ancestors (a directory's own
//      LastWrite change is reported as a Changed event on the directory, whose holder is its parent).
//      If the Error event fired on any burst regardless of loss, this control fails.
//   C4 Soundness: no 4 KB burst trial is missing a Created event for any burst file without
//      raising the overflow Error (loss implies signal), and at least one trial really lost events.
//      Measured per distinct path since run 3; runs 1-2 compared raw Created counts.
// Measured and reported but not a pass condition: the fraction of trials that overflowed.

using System.Collections.Concurrent;
using System.Diagnostics;

static class Program
{
    const int ModuleCount = 20;
    const int PackagesPerModule = 10;

    static int Main(string[] args)
    {
        var dataDir = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Log($"MARROW_PORT_DATA_DIR={dataDir ?? "(unset)"}");
        if (string.IsNullOrEmpty(dataDir))
        {
            Log("REFUSING TO RUN: MARROW_PORT_DATA_DIR is not set.");
            return 2;
        }
        Log($"Runtime {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, OS {Environment.OSVersion.VersionString}, logical CPUs {Environment.ProcessorCount}");

        // What the watcher does with a request below the minimum, read back rather than assumed.
        using (var clampCheck = new FileSystemWatcher(Path.GetTempPath()))
        {
            int defaultSize = clampCheck.InternalBufferSize;
            clampCheck.InternalBufferSize = 1;
            Log($"InternalBufferSize default={defaultSize}, after setting 1 it reads back {clampCheck.InternalBufferSize}");
        }

        int trials = args.Length > 0 && int.TryParse(args[0], out var t) ? t : 3;
        int writers = args.Length > 1 && int.TryParse(args[1], out var w) ? Math.Max(1, w) : 1;
        Log($"Trials {trials}, burst writer threads {writers}");
        var parent = Path.Combine(Path.GetTempPath(), "marrow-p8-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        Log($"Temp parent {parent}");

        var results = new List<CaseResult>();
        try
        {
            results.Add(RunCase(parent, "control-5", 5, 4096, 1));
            for (int i = 1; i <= trials; i++)
                results.Add(RunCase(parent, $"burst-10000-trial{i}", 10_000, 4096, writers));
            // Informational only, not a pass condition: the same burst at the 64 KB maximum.
            results.Add(RunCase(parent, "info-burst-10000-buffer65536", 10_000, 65536, writers));
        }
        finally
        {
            DeleteTree(parent);
            Log($"Temp parent deleted: {!Directory.Exists(parent)}");
        }

        Log("");
        Log("SUMMARY");
        Log("case | buffer | written | created events | distinct created | overflow errors | other errors | path | rescan ms | on disk | missing | extra");
        foreach (var r in results)
            Log($"{r.Name} | {r.Buffer} | {r.Written} | {r.CreatedEvents} | {r.DistinctCreatedBurstFiles} | {r.OverflowErrors} | {r.OtherErrors} | {r.PathTaken} | {r.RescanMs} | {r.OnDisk} | {r.Missing} | {r.Extra}");

        var control = results[0];
        var bursts = results.Where(r => r.Name.StartsWith("burst-", StringComparison.Ordinal)).ToList();
        bool c1 = bursts.Sum(r => r.OverflowErrors) >= 1;
        bool c2 = bursts.Any(r => r.PathTaken == "full-rescan")
                  && bursts.Where(r => r.PathTaken == "full-rescan").All(r => r.RescanCompleted && r.Missing == 0 && r.Extra == 0);
        bool c3 = control.OverflowErrors == 0 && control.OtherErrors == 0 && control.DistinctCreatedBurstFiles == control.Written
                  && control.PathTaken == "incremental" && control.IncrementalSetExact;
        bool c4 = bursts.All(r => r.DistinctCreatedBurstFiles == r.Written || r.OverflowErrors >= 1) && bursts.Any(r => r.DistinctCreatedBurstFiles < r.Written);
        Log($"Overflowed trials: {bursts.Count(r => r.OverflowErrors >= 1)} of {bursts.Count}; trials missing a Created event for some burst file: {bursts.Count(r => r.DistinctCreatedBurstFiles < r.Written)}");
        Log($"C1 overflow Error at least once across 4 KB burst trials: {c1}");
        Log($"C2 every full rescan completed and found every file (and one ran): {c2}");
        Log($"C3 control: no Error, all events, correct incremental set: {c3}");
        Log($"C4 loss implies signal in every trial, and real loss occurred: {c4}");
        bool pass = c1 && c2 && c3 && c4;
        Log($"P8 RESULT: {(pass ? "PASS" : "FAIL")}");
        return pass ? 0 : 1;
    }

    sealed class CaseResult
    {
        public string Name = "";
        public int Buffer;
        public int Written;
        public long CreatedEvents;
        public int DistinctCreatedBurstFiles;
        public long OverflowErrors;
        public long OtherErrors;
        public string PathTaken = "";
        public bool RescanCompleted;
        public long RescanMs;
        public int OnDisk;
        public int Missing;
        public int Extra;
        public bool IncrementalSetExact;
    }

    // The watcher's side of WorkingTreeWatch.State: a generation, a changed-directory set, and the
    // "everything" (changed == nil) state that a lost-events signal forces.
    sealed class WatchState
    {
        public readonly string RootPrefix;
        public readonly string GitPrefix;
        public long Generation = 1;
        public long Created, Changed, Deleted, Renamed;
        public long ChangedOnFiles, ChangedOnDirs;
        public readonly ConcurrentDictionary<string, byte> CreatedPaths = new(StringComparer.OrdinalIgnoreCase);
        public long OverflowErrors, OtherErrors;
        public volatile bool Everything;
        public readonly ConcurrentDictionary<string, byte> ChangedDirs = new(StringComparer.Ordinal);
        public readonly ConcurrentQueue<string> ErrorLog = new();
        public long LastSignalTicks = Stopwatch.GetTimestamp();

        public WatchState(string root)
        {
            RootPrefix = root.TrimEnd('\\') + "\\";
            GitPrefix = RootPrefix + ".git\\";
        }

        public void Note(string fullPath)
        {
            Interlocked.Exchange(ref LastSignalTicks, Stopwatch.GetTimestamp());
            if (fullPath.StartsWith(GitPrefix, StringComparison.OrdinalIgnoreCase)) return; // :196
            Interlocked.Increment(ref Generation);
            if (!fullPath.StartsWith(RootPrefix, StringComparison.OrdinalIgnoreCase) || fullPath.Length <= RootPrefix.Length)
            {
                Everything = true;
                return;
            }
            var relative = fullPath.Substring(RootPrefix.Length);
            int slash = relative.LastIndexOf('\\');
            ChangedDirs.TryAdd(slash >= 0 ? relative.Substring(0, slash) : relative, 0);
        }

        public void Lost(Exception? ex)
        {
            Interlocked.Exchange(ref LastSignalTicks, Stopwatch.GetTimestamp());
            if (ex is InternalBufferOverflowException)
            {
                Interlocked.Increment(ref OverflowErrors);
                Interlocked.Increment(ref Generation);
                Everything = true; // WorkingTreeWatch.swift:186-189, :211-212
            }
            else
            {
                Interlocked.Increment(ref OtherErrors);
            }
            if (ErrorLog.Count < 5)
                ErrorLog.Enqueue($"{ex?.GetType().FullName ?? "(null)"}: {ex?.Message}");
        }
    }

    static CaseResult RunCase(string parent, string name, int burstCount, int bufferSize, int writers)
    {
        var r = new CaseResult { Name = name, Written = burstCount };
        var root = Path.Combine(parent, name);
        Log("");
        Log($"=== CASE {name}: burst {burstCount} files, requested InternalBufferSize {bufferSize}, writer threads {writers}");

        // Repo-shaped tree, seeded BEFORE the watcher starts so seeding produces no events.
        var expected = SeedRepoShapedTree(root);
        Log($"Seeded {root}: {expected.Count} working files, {ModuleCount * PackagesPerModule} package dirs, plus .git");

        var state = new WatchState(root);
        using var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = bufferSize,
        };
        r.Buffer = watcher.InternalBufferSize;
        Log($"Watcher InternalBufferSize reads back {r.Buffer}");
        watcher.Created += (_, e) => { Interlocked.Increment(ref state.Created); state.CreatedPaths.TryAdd(e.FullPath, 0); state.Note(e.FullPath); };
        watcher.Changed += (_, e) =>
        {
            Interlocked.Increment(ref state.Changed);
            if (e.FullPath.EndsWith(".cs", StringComparison.Ordinal)) Interlocked.Increment(ref state.ChangedOnFiles);
            else Interlocked.Increment(ref state.ChangedOnDirs);
            state.Note(e.FullPath);
        };
        watcher.Deleted += (_, e) => { Interlocked.Increment(ref state.Deleted); state.Note(e.FullPath); };
        watcher.Renamed += (_, e) => { Interlocked.Increment(ref state.Renamed); state.Note(e.FullPath); };
        watcher.Error += (_, e) => state.Lost(e.GetException());
        watcher.EnableRaisingEvents = true;
        Thread.Sleep(300); // let the first ReadDirectoryChangesW call be issued before the burst

        // The burst: new files spread over the package directories, as fast as possible.
        var burstDirs = new HashSet<string>(StringComparer.Ordinal);
        var burstFiles = new string[burstCount];
        for (int i = 0; i < burstCount; i++)
        {
            int dirIndex = i % (ModuleCount * PackagesPerModule);
            var relDir = $"src\\mod{dirIndex / PackagesPerModule:D2}\\pkg{dirIndex % PackagesPerModule}";
            burstFiles[i] = $"{relDir}\\file_{i:D5}.cs";
            burstDirs.Add(relDir);
        }
        var start = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();
        Parallel.For(0, burstCount, new ParallelOptions { MaxDegreeOfParallelism = writers }, i =>
        {
            using var fs = new FileStream(Path.Combine(root, burstFiles[i]), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            fs.WriteByte((byte)'x');
        });
        sw.Stop();
        foreach (var f in burstFiles) expected.Add(f);
        Log($"Burst started {start:O}, wrote {burstCount} files in {sw.ElapsedMilliseconds} ms ({burstCount * 1000.0 / Math.Max(1, sw.ElapsedMilliseconds):F0} files/s)");

        // Quiescence: no event and no error for 2 s (cap 60 s).
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < TimeSpan.FromSeconds(60))
        {
            Thread.Sleep(100);
            var idle = Stopwatch.GetElapsedTime(Interlocked.Read(ref state.LastSignalTicks));
            if (idle > TimeSpan.FromSeconds(2)) break;
        }
        watcher.EnableRaisingEvents = false;
        Log($"Quiescent after {waited.ElapsedMilliseconds} ms; watcher stopped {DateTimeOffset.UtcNow:O}");

        r.CreatedEvents = Interlocked.Read(ref state.Created);
        r.DistinctCreatedBurstFiles = burstFiles.Count(f => state.CreatedPaths.ContainsKey(Path.Combine(root, f)));
        r.OverflowErrors = Interlocked.Read(ref state.OverflowErrors);
        r.OtherErrors = Interlocked.Read(ref state.OtherErrors);
        Log($"Events: Created={r.CreatedEvents} (distinct burst files reported {r.DistinctCreatedBurstFiles}/{burstCount}) Changed={state.Changed} (on files {state.ChangedOnFiles}, on dirs {state.ChangedOnDirs}) Deleted={state.Deleted} Renamed={state.Renamed}; Errors: overflow={r.OverflowErrors} other={r.OtherErrors}; generation={state.Generation}; everything={state.Everything}");
        foreach (var line in state.ErrorLog) Log($"  Error event: {line}");

        if (state.Everything)
        {
            // The full-rescan path: taken ONLY because the lost-events flag is set, as
            // WorkingTreeWatch.swift:308 passes paths == nil to the capture. The walk covers the
            // whole working tree and skips .git, as `git add -A` does.
            r.PathTaken = "full-rescan";
            var rescan = Stopwatch.StartNew();
            var found = FullRescan(root);
            rescan.Stop();
            r.RescanCompleted = true;
            r.RescanMs = rescan.ElapsedMilliseconds;
            r.OnDisk = found.Count;
            r.Missing = expected.Count(p => !found.Contains(p));
            r.Extra = found.Count(p => !expected.Contains(p));
            Log($"Full rescan completed in {r.RescanMs} ms: found {found.Count}, expected {expected.Count}, missing {r.Missing}, extra {r.Extra}");
        }
        else
        {
            r.PathTaken = "incremental";
            var got = state.ChangedDirs.Keys.ToHashSet(StringComparer.Ordinal);
            var ancestors = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in burstDirs)
                for (int cut = d.LastIndexOf('\\'); cut > 0; cut = d.LastIndexOf('\\', cut - 1))
                    ancestors.Add(d.Substring(0, cut));
            bool covers = burstDirs.IsSubsetOf(got);
            var outside = got.Where(x => !burstDirs.Contains(x) && !ancestors.Contains(x)).ToList();
            r.IncrementalSetExact = covers && outside.Count == 0;
            var shown = got.OrderBy(x => x, StringComparer.Ordinal).Take(8);
            Log($"Incremental path: changed dirs {got.Count} (first: {string.Join(", ", shown)}), written dirs {burstDirs.Count}, covers all={covers}, outside written dirs and ancestors={outside.Count}, correct={r.IncrementalSetExact}");
            // Oracle only: confirm the tree on disk matches too.
            var found = FullRescan(root);
            r.OnDisk = found.Count;
            r.Missing = expected.Count(p => !found.Contains(p));
            r.Extra = found.Count(p => !expected.Contains(p));
            Log($"Oracle walk (not the decision path): found {found.Count}, missing {r.Missing}, extra {r.Extra}");
        }

        watcher.Dispose();
        DeleteTree(root);
        Log($"Case tree deleted: {!Directory.Exists(root)}");
        return r;
    }

    static HashSet<string> SeedRepoShapedTree(string root)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        Directory.CreateDirectory(root);
        void Put(string rel, string content, bool working = true)
        {
            var full = Path.Combine(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
            if (working) files.Add(rel);
        }
        Put(".git\\HEAD", "ref: refs/heads/main\n", working: false);
        Put(".git\\config", "[core]\n\trepositoryformatversion = 0\n", working: false);
        Put(".git\\refs\\heads\\main", "0000000000000000000000000000000000000000\n", working: false);
        Put(".git\\objects\\ab\\cdef0123456789", "x", working: false);
        Put("README.md", "# probe\n");
        Put(".gitignore", "bin/\nobj/\n");
        for (int m = 0; m < ModuleCount; m++)
            for (int p = 0; p < PackagesPerModule; p++)
                Put($"src\\mod{m:D2}\\pkg{p}\\seed.cs", "// seed\n");
        return files;
    }

    static HashSet<string> FullRescan(string root)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var prefix = root.TrimEnd('\\') + "\\";
        var gitDir = prefix + ".git";
        var options = new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = 0, IgnoreInaccessible = false };
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir, "*", options))
                if (!string.Equals(sub, gitDir, StringComparison.OrdinalIgnoreCase)) pending.Push(sub);
            foreach (var file in Directory.EnumerateFiles(dir, "*", options))
                found.Add(file.Substring(prefix.Length));
        }
        return found;
    }

    static void DeleteTree(string path)
    {
        for (int attempt = 0; attempt < 5 && Directory.Exists(path); attempt++)
        {
            try { Directory.Delete(path, recursive: true); }
            catch (IOException) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) { Thread.Sleep(200); }
        }
    }

    static void Log(string line) => Console.WriteLine($"[{DateTimeOffset.UtcNow:HH:mm:ss.fff}Z] {line}");
}
