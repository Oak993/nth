namespace Marrow.Core.Support;

/// <summary>Why a sweep left something where it was.</summary>
public enum SweepKeptReason
{
    /// <summary>A file in it is open or loaded by a running program (an engine that runs, a native library in use, a setup's claim).</summary>
    InUse,

    /// <summary>It could not be removed for another reason (no permission, a failure part-way); what was left stays for the next sweep.</summary>
    NotRemoved,
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
/// no sweep touches it: it keeps <see cref="SetupFolders.ClaimFileName"/> open exclusively, with delete-on-close. A sweep that
/// finds the file open leaves the whole folder alone. When the holder ends (it releases the claim, exits or crashes) Windows
/// closes the handle, the file goes, and the folder is plain leftovers for the next sweep.
/// </summary>
public sealed class FolderClaim : IDisposable
{
    private FileStream? held;

    private FolderClaim(string folder, FileStream held)
    {
        Folder = folder;
        this.held = held;
    }

    public string Folder { get; }

    /// <summary>Creates <paramref name="folder"/> if needed and claims it. Throws <see cref="IOException"/> if another run already holds it.</summary>
    public static FolderClaim Take(string folder)
    {
        SetupFolders.RequireFull(folder, nameof(folder));
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.CreateDirectory(folder);
                var claim = new FileStream(Path.Combine(folder, SetupFolders.ClaimFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                return new FolderClaim(folder, claim);
            }
            catch (DirectoryNotFoundException) when (attempt < 3)
            {
                // an empty parent was swept between the two calls: make it again
            }
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
/// A <b>unit</b> is a staging folder, a build's folder under <c>runtime\</c>, the uninstaller's temporary folder or a loose
/// file. A unit is removed whole or not at all: if any file in it is open or loaded (a running engine, a library in use,
/// another setup's <see cref="FolderClaim"/>) the whole unit is left, so a run in progress never loses its options file.
/// Nothing outside the two folders is touched (the data folder <c>%LOCALAPPDATA%\nth</c> and the install root are never
/// swept), a link inside is removed without following it, and a link standing where the setup's folder should be is left alone.
/// No method here throws for a file-system failure: it reports what it left in <see cref="SweepResult.Kept"/>.
/// </para>
/// </summary>
public static class SetupSweeper
{
    private const int SharingViolation = 32;
    private const int LockViolation = 33;

    private enum FileState
    {
        Free,
        InUse,
        Denied,
    }

    /// <summary>
    /// Removes every unit under <c>%LOCALAPPDATA%\nth Setup</c> and <c>%TEMP%\nth Uninstall</c> that nothing is using. For the
    /// setup at its start (it holds the setup lock, and its own folders are claimed or loaded, so they are kept) and for the
    /// uninstaller at its end (its own temporary copy is running, so that folder is kept and reported).
    /// </summary>
    public static SweepResult Sweep(string localAppData, string tempPath)
    {
        var root = SetupFolders.Root(localAppData);
        var uninstallTemp = SetupFolders.UninstallTemp(tempPath);
        var tally = new Tally();
        try { SweepSetupRoot(root, tally); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (!ConfirmedMissing(root)) tally.Keep(root, SweepKeptReason.NotRemoved);
        }
        try { if (Exists(uninstallTemp)) RemoveUnit(uninstallTemp, tally); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { tally.Keep(uninstallTemp, SweepKeptReason.NotRemoved); }
        return tally.ToResult();
    }

    /// <summary>
    /// <see cref="Sweep"/>, unless a setup is running (<paramref name="probe"/>). For nth's own start and its before-uninstall
    /// hook, which can run beside a setup or the uninstaller. When neither folder exists (the usual case) it costs two
    /// directory checks and never looks for a setup.
    /// </summary>
    public static SweepResult SweepUnlessSetupRuns(string localAppData, string tempPath, ISetupRunningProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        try { if (!Exists(SetupFolders.Root(localAppData)) && !Exists(SetupFolders.UninstallTemp(tempPath))) return SweepResult.Nothing; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return probe.IsSetupRunning() ? SweepResult.Nothing : Sweep(localAppData, tempPath); }
        return probe.IsSetupRunning() ? SweepResult.Nothing : Sweep(localAppData, tempPath);
    }

    /// <summary>
    /// <see cref="SweepUnlessSetupRuns"/>, but when a setup is running it waits for the setup to end (asking again every
    /// <paramref name="interval"/>, for at most <paramref name="patience"/>) and sweeps then. For nth's start: nth is often opened
    /// from the setup's Ready screen while the setup is still up, and the setup's own native libraries stay loaded until it exits,
    /// so the sweep that nth could not do at its start is done as soon as the setup closes. Gives up quietly when the time is
    /// up or <paramref name="cancel"/> is signalled; the next start sweeps. It waits without holding a thread.
    /// </summary>
    public static async Task<SweepResult> SweepWhenNoSetupRunsAsync(string localAppData, string tempPath, ISetupRunningProbe probe, TimeSpan patience, TimeSpan interval, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        var until = Environment.TickCount64 + (long)Math.Clamp(patience.TotalMilliseconds, 0, 1e12);
        while (true)
        {
            try { if (!Exists(SetupFolders.Root(localAppData)) && !Exists(SetupFolders.UninstallTemp(tempPath))) return SweepResult.Nothing; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* uncertain existence: obey the setup probe before sweeping */ }
            if (!probe.IsSetupRunning()) return Sweep(localAppData, tempPath);
            if (Environment.TickCount64 >= until || cancel.IsCancellationRequested) return SweepResult.Nothing;
            try { await Task.Delay(interval, cancel).ConfigureAwait(false); }
            catch (OperationCanceledException) { return SweepResult.Nothing; }
        }
    }

    /// <summary>
    /// Removes one folder inside <c>%LOCALAPPDATA%\nth Setup</c> (a run's own staging folder) unless something in it is in use,
    /// then each parent that is left empty, up to and including <c>nth Setup</c> itself.
    /// </summary>
    public static SweepResult RemoveFolder(string localAppData, string folder)
    {
        var root = SetupFolders.Root(localAppData);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!SetupFolders.IsInsideRoot(localAppData, full) || string.Equals(full, Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{folder}' is not a folder inside '{root}'.", nameof(folder));
        var tally = new Tally();
        try { SetupFolders.RequireNoLinks(full); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            tally.Keep(full, SweepKeptReason.NotRemoved);
            return tally.ToResult();
        }
        try { if (Exists(full)) RemoveUnit(full, tally); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { tally.Keep(full, SweepKeptReason.NotRemoved); }
        for (var parent = Path.GetDirectoryName(full); parent is not null && SetupFolders.IsInsideRoot(localAppData, parent); parent = Path.GetDirectoryName(parent))
        {
            try { SetupFolders.RequireNoLinks(parent); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                tally.Keep(parent, SweepKeptReason.NotRemoved);
                break;
            }
            TryRemoveEmptyDirectory(parent, tally);
        }
        return tally.ToResult();
    }

    private static void SweepSetupRoot(string root, Tally tally)
    {
        if (!Exists(root)) return;
        if (IsLink(root))
        {
            tally.Keep(root, SweepKeptReason.NotRemoved); // a link where our folder should be: not ours to follow or remove
            return;
        }
        foreach (var entry in Entries(root))
        {
            if (string.Equals(Path.GetFileName(entry), SetupFolders.RuntimeFolderName, StringComparison.OrdinalIgnoreCase) && Directory.Exists(entry) && !IsLink(entry))
            {
                foreach (var build in Entries(entry)) RemoveUnit(build, tally);
                TryRemoveEmptyDirectory(entry, tally);
            }
            else RemoveUnit(entry, tally);
        }
        TryRemoveEmptyDirectory(root, tally);
    }

    private static void RemoveUnit(string path, Tally tally)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                DeleteLink(path, attributes);
                tally.Removed++;
                return;
            }
            if ((attributes & FileAttributes.Directory) == 0)
            {
                var state = Probe(path);
                if (state != FileState.Free) tally.Keep(path, state == FileState.InUse ? SweepKeptReason.InUse : SweepKeptReason.NotRemoved);
                else
                {
                    File.Delete(path);
                    tally.Removed++;
                }
                return;
            }
            var files = new List<string>();
            CollectFiles(path, files);
            foreach (var file in files)
            {
                var state = Probe(file);
                if (state == FileState.Free) continue;
                tally.Keep(path, state == FileState.InUse ? SweepKeptReason.InUse : SweepKeptReason.NotRemoved);
                return;
            }
            DeleteTree(path);
            tally.Removed++;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if ((error is FileNotFoundException or DirectoryNotFoundException) && ConfirmedMissing(path)) return; // the unit itself disappeared
            tally.Keep(path, IsSharing(error) ? SweepKeptReason.InUse : SweepKeptReason.NotRemoved);
        }
    }

    /// <summary>
    /// Whether anything holds the file: opening it for writing with no sharing fails for a file another program has open, for
    /// a running program's image and for a loaded library, and for a <see cref="FolderClaim"/>. Nothing is written.
    /// </summary>
    private static FileState Probe(string file)
    {
        try
        {
            ClearReadOnly(file);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None, 1, FileOptions.None);
            return FileState.Free;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return FileState.Free; // already gone
        }
        catch (IOException)
        {
            return FileState.InUse;
        }
        catch (UnauthorizedAccessException)
        {
            return FileState.Denied;
        }
    }

    private static void CollectFiles(string directory, List<string> files)
    {
        foreach (var entry in Entries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) continue; // a link is removed as a link, never opened or followed
            if ((attributes & FileAttributes.Directory) != 0) CollectFiles(entry, files);
            else files.Add(entry);
        }
    }

    private static void DeleteTree(string directory)
    {
        foreach (var entry in Entries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) DeleteLink(entry, attributes);
            else if ((attributes & FileAttributes.Directory) != 0) DeleteTree(entry);
            else
            {
                ClearReadOnly(entry);
                File.Delete(entry);
            }
        }
        File.SetAttributes(directory, FileAttributes.Normal);
        Directory.Delete(directory, recursive: false);
    }

    /// <summary>Removes the link and nothing it points at: a folder link by RemoveDirectory, a file link by DeleteFile.</summary>
    private static void DeleteLink(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(path, recursive: false);
        else
        {
            ClearReadOnly(path);
            File.Delete(path);
        }
    }

    private static void ClearReadOnly(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // the open or delete that follows reports what is wrong
        }
    }

    private static List<string> Entries(string directory) => [.. Directory.EnumerateFileSystemEntries(directory)];

    private static void TryRemoveEmptyDirectory(string directory, Tally tally)
    {
        if (IsLink(directory)) return;
        try
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any()) return; // deliberately retained contents are not a deletion failure
            Directory.Delete(directory, recursive: false);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            // it disappeared before we looked
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            tally.Keep(directory, SweepKeptReason.NotRemoved);
        }
    }
    private static bool ConfirmedMissing(string path)
    {
        try { _ = File.GetAttributes(path); return false; }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
    private static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }

    private static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Whether the setup's folder, or any folder between it and <paramref name="path"/>, is a link.</summary>
    private static bool IsLinkInPath(string root, string path)
    {
        for (var at = path; at is not null; at = Path.GetDirectoryName(at))
        {
            if (IsLink(at)) return true;
            if (string.Equals(Path.TrimEndingDirectorySeparator(at), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase)) break;
        }
        return false;
    }

    private static bool IsSharing(Exception error) => error is IOException io && ((io.HResult & 0xFFFF) is SharingViolation or LockViolation);

    private sealed class Tally
    {
        public int Removed;
        private readonly List<SweepKept> kept = [];

        public void Keep(string path, SweepKeptReason reason) => kept.Add(new SweepKept(path, reason));

        public SweepResult ToResult() => new(Removed, kept);
    }
}
