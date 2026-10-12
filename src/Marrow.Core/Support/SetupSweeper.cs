namespace Marrow.Core.Support;

/// <summary>Why a sweep left something where it was.</summary>
public enum SweepKeptReason
{
    /// <summary>A file in it is open or loaded by a running program (an engine that runs, a native library in use, a setup's claim).</summary>
    InUse,

    /// <summary>It could not be removed for another reason (no permission, a failure part-way); what was left stays for the next sweep.</summary>
    NotRemoved,

    /// <summary>The shared time, entry-count or depth budget stopped traversal; the next start may try again.</summary>
    LimitReached,
}

/// <summary>One top-level entry (a staging folder, a build's runtime folder, a loose file) a sweep did not remove.</summary>
public sealed record SweepKept(string Path, SweepKeptReason Reason);

/// <summary>What a sweep did: how many top-level entries it removed and which it left.</summary>
public sealed record SweepResult(int Removed, IReadOnlyList<SweepKept> Kept)
{
    /// <summary>Nothing was there, or nothing was swept.</summary>
    public static SweepResult Nothing { get; } = new(0, []);

    /// <summary>Everything that was found has gone.</summary>
    public bool Clean => Kept.Count == 0;
}

/// <summary>Whether an nth Setup window (or the uninstaller's) is open right now.</summary>
public interface ISetupRunningProbe
{
    bool IsSetupRunning();
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 5, "Leftovers"; unit U9, ED-NEW-inst-u9-1). A setup is running while its named lock
/// (<see cref="SetupFolders.SetupLockName"/>) exists: the setup and the uninstaller both hold it for as long as their window
/// is open. Opening the lock does not take it. A lock that exists but cannot be opened, or a lookup that fails, counts as
/// running: when it cannot be told, nothing is swept.
/// </summary>
public sealed class NamedLockSetupProbe(string name = SetupFolders.SetupLockName) : ISetupRunningProbe
{
    public bool IsSetupRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting(name, out var existing)) return false;
            existing.Dispose();
            return true;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException or ArgumentException)
        {
            return true;
        }
    }
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 5, "Leftovers"; unit U9, ED-NEW-inst-u9-1). A folder a running setup is using, marked so
/// no sweep touches it: it holds <see cref="SetupFolders.ClaimFileName"/> exclusively. Only a newly created claim uses
/// delete-on-close; an existing exact nth claim is validated without deletion flags and remains for an ownership-checked
/// sweep after release. Foreign claims and nonempty unmarked units are refused.
/// </summary>
public sealed class FolderClaim : IDisposable
{
    private FileStream? held;
    private FolderClaim(string folder, FileStream stream) { Folder = folder; held = stream; }
    public string Folder { get; }

    /// <summary>Claims a new/empty or already nth-owned unit. Foreign content and every reparse-point ancestor are refused.</summary>
    public static FolderClaim Take(string folder, SweepLimits? limits = null, TimeProvider? clock = null) =>
        TakeWithBudget(folder, new SweepBudget(limits, clock));

    internal static FolderClaim TakeWithBudget(string folder, SweepBudget work)
    {
        SetupFolders.RequireFull(folder, nameof(folder));
        work.AddRoot(folder);
        work.Visit(folder);
        SetupFolders.PrepareOwnedUnit(folder, () => work.Check(folder), at => work.Visit(at));
        var path = Path.Combine(folder, SetupFolders.ClaimFileName);
        work.Visit(path);
        work.Guard(path);
        FileStream stream;
        try
        {
            stream = work.Io(path, () => new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose));
            try
            {
                work.Io(path, () => stream.Write(System.Text.Encoding.UTF8.GetBytes(SetupFolders.ClaimMarker)));
                work.Io(path, () => stream.Flush(flushToDisk: true));
                work.Guard(path);
                return new FolderClaim(folder, stream);
            }
            catch { stream.Dispose(); throw; }
        }
        catch (SweepLimitException) { throw; }
        catch (IOException)
        {
            if (!work.Io(path, () => File.Exists(path))) throw;
            work.Guard(path);
            // An existing file is never opened for delete. Read and hold only a claim that nth actually marked.
            stream = work.Io(path, () => new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None));
            try
            {
                work.Visit(path);
                var marker = System.Text.Encoding.UTF8.GetBytes(SetupFolders.ClaimMarker);
                if (work.Io(path, () => stream.Length) != marker.Length) throw new IOException($"The claim is not nth's: '{path}'.");
                var actual = new byte[marker.Length];
                work.Io(path, () => stream.ReadExactly(actual));
                if (!actual.AsSpan().SequenceEqual(marker)) throw new IOException($"The claim is not nth's: '{path}'.");
                work.Guard(path);
                return new FolderClaim(folder, stream);
            }
            catch { stream.Dispose(); throw; }
        }
    }

    public void Dispose() => Interlocked.Exchange(ref held, null)?.Dispose();
}
/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 step 4, 5 "Leftovers" and 8; unit U9, ED-NEW-inst-u9-1). The sweeps for what nth Setup
/// leaves outside the install root: <c>%LOCALAPPDATA%\nth Setup\</c> (staging folders, the native runtime) and
/// <c>%TEMP%\nth Uninstall\</c> (the uninstaller's copy). A crash or power loss leaves at most these folders; the setup at
/// its start (step 4), nth at its start, nth's before-uninstall hook and the uninstaller remove them. No <c>RunOnce</c> entry
/// or delayed <c>cmd</c> is used.
/// <para>
/// An owned <b>unit</b> is a marked staging folder, a build's folder under <c>runtime\</c>, or the uninstaller's temporary
/// folder. Preflight preserves the unit if known payload is open or loaded, including another setup's <see cref="FolderClaim"/>.
/// Loose files, unknown content and links are retained and reported; the data folder <c>%LOCALAPPDATA%\nth</c> and the
/// install root are never swept. Deletion is not atomic: a filesystem failure or cleanup cutoff can leave partly removed
/// owned payload. Cleanup reports affected retained units or remnants in <see cref="SweepResult.Kept"/> for a later sweep.
/// </para>
/// </summary>
public static class SetupSweeper
{
    private const int SharingViolation = 32;
    private const int LockViolation = 33;
    private enum FileState { Free, InUse, Denied }

    public static SweepResult Sweep(string localAppData, string tempPath, SweepLimits? limits = null, TimeProvider? clock = null) =>
        SweepWithBudget(localAppData, tempPath, new SweepBudget(limits, clock));

    internal static SweepResult SweepWithBudget(string localAppData, string tempPath, SweepBudget work)
    {
        var root = SetupFolders.Root(localAppData);
        var uninstall = SetupFolders.UninstallTemp(tempPath);
        work.AddRoot(root);
        work.AddRoot(uninstall);
        var tally = new Tally(work);
        tally.Run(root, () => SweepSetupRoot(root, tally));
        tally.Run(uninstall, () => { work.Guard(uninstall); if (Exists(uninstall, work)) RemoveUnit(uninstall, tally); });
        return tally.ToResult();
    }

    public static SweepResult SweepUnlessSetupRuns(string localAppData, string tempPath, ISetupRunningProbe probe, SweepLimits? limits = null, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var root = SetupFolders.Root(localAppData);
        var uninstall = SetupFolders.UninstallTemp(tempPath);
        var work = new SweepBudget(limits, clock);
        work.AddRoot(root);
        work.AddRoot(uninstall);
        var tally = new Tally(work);
        try
        {
            work.Visit(root);
            work.Visit(uninstall);
            work.Guard(root);
            work.Guard(uninstall);
            if (!Exists(root, work) && !Exists(uninstall, work)) return SweepResult.Nothing;
        }
        catch (SweepLimitException) { tally.Keep(root, SweepKeptReason.LimitReached); return tally.ToResult(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Uncertain presence still observes the setup-running veto before any deletion.
        }
        try
        {
            if (work.Io(root, probe.IsSetupRunning)) return SweepResult.Nothing;
        }
        catch (SweepLimitException) { tally.Keep(root, SweepKeptReason.LimitReached); return tally.ToResult(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { tally.Keep(root, SweepKeptReason.NotRemoved); return tally.ToResult(); }
        return SweepWithBudget(localAppData, tempPath, work);
    }

    public static async Task<SweepResult> SweepWhenNoSetupRunsAsync(string localAppData, string tempPath, ISetupRunningProbe probe, TimeSpan patience, TimeSpan interval, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        var root = SetupFolders.Root(localAppData);
        var uninstall = SetupFolders.UninstallTemp(tempPath);
        var until = Environment.TickCount64 + (long)Math.Clamp(patience.TotalMilliseconds, 0, 1e12);
        while (true)
        {
            var poll = new SweepBudget();
            poll.AddRoot(root);
            poll.AddRoot(uninstall);
            try
            {
                poll.Visit(root);
                poll.Visit(uninstall);
                poll.Guard(root);
                poll.Guard(uninstall);
                if (!Exists(root, poll) && !Exists(uninstall, poll)) return SweepResult.Nothing;
            }
            catch (SweepLimitException) { return new(0, [new(root, SweepKeptReason.LimitReached)]); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            try
            {
                // Presence, the setup probe and the actual deletion share this poll's budget when the setup has ended.
                if (!poll.Io(root, probe.IsSetupRunning)) return SweepWithBudget(localAppData, tempPath, poll);
            }
            catch (SweepLimitException) { return new(0, [new(root, SweepKeptReason.LimitReached)]); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return new(0, [new(root, SweepKeptReason.NotRemoved)]); }
            if (Environment.TickCount64 >= until || cancel.IsCancellationRequested) return SweepResult.Nothing;
            try { await Task.Delay(interval, cancel).ConfigureAwait(false); }
            catch (OperationCanceledException) { return SweepResult.Nothing; }
        }
    }
    public static SweepResult RemoveFolder(string localAppData, string folder, SweepLimits? limits = null, TimeProvider? clock = null) =>
        RemoveFolderWithBudget(localAppData, folder, new SweepBudget(limits, clock));

    internal static SweepResult RemoveFolderWithBudget(string localAppData, string folder, SweepBudget work)
    {
        var root = SetupFolders.Root(localAppData);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!SetupFolders.IsInsideRoot(localAppData, full) || string.Equals(full, Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{folder}' is not a folder inside '{root}'.", nameof(folder));
        work.AddRoot(root);
        var tally = new Tally(work);
        if (!tally.Run(full, () => { work.Guard(full); if (Exists(full, work)) RemoveUnit(full, tally); })) return tally.ToResult();
        for (var parent = Path.GetDirectoryName(full); parent is not null && SetupFolders.IsInsideRoot(localAppData, parent); parent = Path.GetDirectoryName(parent))
        {
            var at = parent;
            if (!tally.Run(at, () => { work.Guard(at); TryRemoveEmptyDirectory(at, tally); })) break;
        }
        return tally.ToResult();
    }

    private static void SweepSetupRoot(string root, Tally tally)
    {
        var work = tally.Work;
        work.Guard(root);
        if (!Exists(root, work)) return;
        foreach (var entry in Entries(root, tally, 0))
        {
            if (Path.GetFileName(entry) == SetupFolders.OwnerFileName && Owned(root, tally)) continue;
            work.Guard(entry);
            var attributes = work.Io(entry, () => File.GetAttributes(entry));
            if (Path.GetFileName(entry) == SetupFolders.RuntimeFolderName && (attributes & FileAttributes.Directory) != 0)
            {
                foreach (var build in Entries(entry, tally, 1))
                    if (Path.GetFileName(build) != SetupFolders.OwnerFileName || !Owned(entry, tally)) RemoveUnit(build, tally);
                TryRemoveEmptyDirectory(entry, tally);
            }
            else RemoveUnit(entry, tally);
        }
        TryRemoveEmptyDirectory(root, tally);
    }

    private static void RemoveUnit(string path, Tally tally)
    {
        tally.Run(path, () =>
        {
            var work = tally.Work;
            work.Guard(path);
            var attributes = work.Io(path, () => File.GetAttributes(path));
            if ((attributes & FileAttributes.Directory) == 0 || !Owned(path, tally)) { tally.Keep(path, SweepKeptReason.NotRemoved); return; }
            var files = new List<string>();
            CollectFiles(path, files, path, tally, 0);
            foreach (var file in files)
            {
                var state = Probe(file, tally);
                if (state == FileState.Free) continue;
                tally.Keep(path, state == FileState.InUse ? SweepKeptReason.InUse : SweepKeptReason.NotRemoved);
                return;
            }
            DeleteTree(path, path, tally, 0);
            tally.Removed++;
        });
    }

    private static FileState Probe(string file, Tally tally)
    {
        var work = tally.Work;
        try
        {
            work.Guard(file);
            ClearReadOnly(file, tally);
            using var stream = work.Io(file, () => new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None, 1, FileOptions.None));
            return FileState.Free;
        }
        catch (SweepLimitException) { throw; }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return FileState.Free; }
        catch (IOException) { return FileState.InUse; }
        catch (UnauthorizedAccessException) { return FileState.Denied; }
    }

    private static bool Owned(string folder, Tally tally) => SetupFolders.HasOwnMarker(folder, () => tally.Work.Check(folder));

    private static bool KnownPayload(string unit, string file)
    {
        var relative = Path.GetRelativePath(unit, file);
        var name = Path.GetFileName(relative);
        if (Path.GetFileName(unit) == SetupFolders.UninstallTempFolderName)
            return !relative.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) && name is "nth Uninstall.exe" or "setup.log" or "install.log";
        if (Path.GetFileName(Path.GetDirectoryName(unit)) == SetupFolders.RuntimeFolderName)
            return !relative.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) && name is "libSkiaSharp.dll" or "libHarfBuzzSharp.dll" or "av_libglesv2.dll";
        if (relative.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            var directories = relative.Split(Path.DirectorySeparatorChar)[..^1];
            return directories[0] == "logs" && directories.Skip(1).All(part => part.Length > 0 && part.All(char.IsAsciiDigit))
                && name is "setup.log" or "install.log" or "nth Setup Engine.log";
        }
        if (name is SetupFolders.EngineFileName or "nth Setup Engine.exe.partial" or "install-options.json" or "install-result.json"
            or "nth Setup Engine.log" or "setup.log" or "install.log") return true;
        foreach (var target in new[] { "install-options.json", "install-result.json" })
        {
            var prefix = "." + target + ".";
            if (name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".tmp", StringComparison.Ordinal)
                && name.Length == prefix.Length + 32 + 4 && !name.AsSpan(prefix.Length, 32).ContainsAnyExcept("0123456789abcdef")) return true;
        }
        return false;
    }

    private static bool KnownDirectory(string unit, string directory)
    {
        var parts = Path.GetRelativePath(unit, directory).Split(Path.DirectorySeparatorChar);
        return parts[0] == "logs" && parts.Skip(1).All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
    }

    private static void RequireOwnedFile(string unit, string file, Tally tally)
    {
        tally.Work.Guard(file);
        if (string.Equals(file, Path.Combine(unit, SetupFolders.OwnerFileName), StringComparison.OrdinalIgnoreCase))
        {
            if (Owned(unit, tally)) return;
        }
        else if (string.Equals(file, Path.Combine(unit, SetupFolders.ClaimFileName), StringComparison.OrdinalIgnoreCase))
        {
            if (SetupFolders.MatchesMarker(file, SetupFolders.ClaimMarker, () => tally.Work.Check(file))) return;
        }
        else if (KnownPayload(unit, file)) return;
        throw new IOException($"The sweep cannot prove nth owns '{file}'.");
    }

    private static void CollectFiles(string directory, List<string> files, string unit, Tally tally, int depth)
    {
        tally.Work.Check(directory, depth);
        tally.Work.Guard(directory);
        foreach (var entry in Entries(directory, tally, depth))
        {
            tally.Work.Guard(entry);
            var attributes = tally.Work.Io(entry, () => File.GetAttributes(entry), depth + 1);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (!KnownDirectory(unit, entry)) throw new IOException($"The sweep cannot prove nth owns '{entry}'.");
                CollectFiles(entry, files, unit, tally, depth + 1);
            }
            else
            {
                RequireOwnedFile(unit, entry, tally);
                var state = Probe(entry, tally);
                if (state == FileState.InUse) throw new IOException("A proven-owned payload is in use.", unchecked((int)0x80070020));
                if (state == FileState.Denied) throw new UnauthorizedAccessException("A proven-owned payload cannot be probed.");
                files.Add(entry);
            }
        }
    }

    private static void DeleteTree(string directory, string unit, Tally tally, int depth)
    {
        var work = tally.Work;
        work.Check(directory, depth);
        work.Guard(directory);
        foreach (var entry in Entries(directory, tally, depth))
        {
            if (string.Equals(entry, Path.Combine(unit, SetupFolders.OwnerFileName), StringComparison.OrdinalIgnoreCase)) continue;
            work.Guard(entry);
            var attributes = work.Io(entry, () => File.GetAttributes(entry), depth + 1);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (!KnownDirectory(unit, entry)) throw new IOException($"The sweep cannot prove nth owns '{entry}'.");
                DeleteTree(entry, unit, tally, depth + 1);
            }
            else
            {
                RequireOwnedFile(unit, entry, tally);
                ClearReadOnly(entry, tally);
                work.Guard(entry);
                work.Io(entry, () => File.Delete(entry), depth + 1);
            }
        }
        work.Guard(directory);
        work.Io(directory, () => File.SetAttributes(directory, FileAttributes.Normal), depth);
        if (directory == unit) RemoveOwnerAndDirectory(directory, tally, depth);
        else { work.Guard(directory); work.Io(directory, () => Directory.Delete(directory, recursive: false), depth); }
    }

    private static void RemoveOwnerAndDirectory(string directory, Tally tally, int depth)
    {
        var marker = Path.Combine(directory, SetupFolders.OwnerFileName);
        RequireOwnedFile(directory, marker, tally);
        ClearReadOnly(marker, tally);
        tally.Work.Guard(marker);
        // Provenance is removed last. A failure after this may leave an empty unmarked directory; later sweeps preserve/report it.
        tally.Work.Io(marker, () => File.Delete(marker), depth);
        tally.Work.Guard(directory);
        tally.Work.Io(directory, () => Directory.Delete(directory, recursive: false), depth);
    }

    private static void ClearReadOnly(string path, Tally tally)
    {
        var work = tally.Work;
        work.Guard(path);
        var attributes = work.Io(path, () => File.GetAttributes(path));
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            work.Guard(path);
            work.Io(path, () => File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly));
        }
    }

    private static IEnumerable<string> Entries(string directory, Tally tally, int depth)
    {
        var work = tally.Work;
        work.Check(directory, depth);
        work.Guard(directory);
        using var entries = work.Io(directory, () => Directory.EnumerateFileSystemEntries(directory).GetEnumerator(), depth);
        while (work.Io(directory, entries.MoveNext, depth))
        {
            var entry = entries.Current;
            work.Visit(entry, depth + 1);
            yield return entry;
        }
    }

    private static void TryRemoveEmptyDirectory(string directory, Tally tally)
    {
        tally.Run(directory, () =>
        {
            tally.Work.Guard(directory);
            foreach (var entry in Entries(directory, tally, 0))
                if (Path.GetFileName(entry) != SetupFolders.OwnerFileName) return;
            if (!Owned(directory, tally)) { tally.Keep(directory, SweepKeptReason.NotRemoved); return; }
            tally.Work.Guard(directory);
            tally.Work.Io(directory, () => File.SetAttributes(directory, FileAttributes.Normal));
            RemoveOwnerAndDirectory(directory, tally, 0);
        });
    }

    private static bool Exists(string path, SweepBudget work)
    {
        try { _ = work.Io(path, () => File.GetAttributes(path)); return true; }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }

    private static bool IsSharing(Exception error) => error is IOException io && ((io.HResult & 0xFFFF) is SharingViolation or LockViolation);

    private sealed class Tally(SweepBudget work)
    {
        public SweepBudget Work { get; } = work;
        public int Removed;
        private readonly List<SweepKept> kept = [];
        public void Keep(string path, SweepKeptReason reason)
        {
            if (!kept.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase) && item.Reason == reason)) kept.Add(new(path, reason));
        }
        public bool Run(string path, Action operation)
        {
            try { Work.Visit(path); operation(); return true; }
            catch (SweepLimitException) { Keep(path, SweepKeptReason.LimitReached); return false; }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                try
                {
                    if (!Exists(path, Work)) return true;
                    Keep(path, SweepKeptReason.NotRemoved);
                }
                catch (SweepLimitException) { Keep(path, SweepKeptReason.LimitReached); }
                catch (Exception lookup) when (lookup is IOException or UnauthorizedAccessException) { Keep(path, SweepKeptReason.NotRemoved); }
                return false;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Keep(path, IsSharing(error) ? SweepKeptReason.InUse : SweepKeptReason.NotRemoved);
                return false;
            }
        }
        public SweepResult ToResult() => new(Removed, kept);
    }
}
