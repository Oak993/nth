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
    public static FolderClaim Take(string folder)
    {
        SetupFolders.RequireFull(folder, nameof(folder));
        SetupFolders.PrepareOwnedUnit(folder);
        var path = Path.Combine(folder, SetupFolders.ClaimFileName);
        SetupFolders.RequireNoLinks(path);
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            try
            {
                stream.Write(System.Text.Encoding.UTF8.GetBytes(SetupFolders.ClaimMarker));
                stream.Flush(flushToDisk: true);
                SetupFolders.RequireNoLinks(path);
                return new FolderClaim(folder, stream);
            }
            catch { stream.Dispose(); throw; }
        }
        catch (IOException) when (File.Exists(path))
        {
            SetupFolders.RequireNoLinks(path);
            // An existing file is never opened for delete. Read and hold only a claim that nth actually marked.
            stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            try
            {
                var marker = System.Text.Encoding.UTF8.GetBytes(SetupFolders.ClaimMarker);
                if (stream.Length != marker.Length) throw new IOException($"The claim is not nth's: '{path}'.");
                var actual = new byte[marker.Length];
                stream.ReadExactly(actual);
                if (!actual.AsSpan().SequenceEqual(marker)) throw new IOException($"The claim is not nth's: '{path}'.");
                SetupFolders.RequireNoLinks(path);
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
        try { SetupFolders.RequireNoLinks(root); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { tally.Keep(root, SweepKeptReason.NotRemoved); return; }
        if (IsLink(root))
        {
            tally.Keep(root, SweepKeptReason.NotRemoved); // a link where our folder should be: not ours to follow or remove
            return;
        }
        foreach (var entry in Entries(root))
        {
            if (Path.GetFileName(entry) == SetupFolders.OwnerFileName && SetupFolders.HasOwnMarker(root)) continue;
            if (string.Equals(Path.GetFileName(entry), SetupFolders.RuntimeFolderName, StringComparison.OrdinalIgnoreCase) && Directory.Exists(entry) && !IsLink(entry))
            {
                foreach (var build in Entries(entry))
                    if (Path.GetFileName(build) != SetupFolders.OwnerFileName || !SetupFolders.HasOwnMarker(entry)) RemoveUnit(build, tally);
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
            SetupFolders.RequireNoLinks(path);
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) == 0 || !SetupFolders.HasOwnMarker(path))
            {
                tally.Keep(path, SweepKeptReason.NotRemoved); // a name or location alone never proves that nth created it
                return;
            }            var files = new List<string>();
            CollectFiles(path, files, path);
            foreach (var file in files)
            {
                var state = Probe(file);
                if (state == FileState.Free) continue;
                tally.Keep(path, state == FileState.InUse ? SweepKeptReason.InUse : SweepKeptReason.NotRemoved);
                return;
            }
            DeleteTree(path, path);
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
            return directories[0] == "logs" && directories.Skip(1).All(part => part.All(char.IsAsciiDigit))
                && name is "setup.log" or "install.log" or "nth Setup Engine.log";
        }
        if (name is SetupFolders.EngineFileName or "nth Setup Engine.exe.partial" or "install-options.json" or "install-result.json"
            or "nth Setup Engine.log" or "setup.log" or "install.log") return true;
        foreach (var target in new[] { "install-options.json", "install-result.json" })
        {
            var prefix = "." + target + ".";
            if (name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".tmp", StringComparison.Ordinal)
                && name.Length == prefix.Length + 32 + 4 && name.AsSpan(prefix.Length, 32).ContainsAnyExcept("0123456789abcdef") == false) return true;
        }
        return false;
    }

    private static bool KnownDirectory(string unit, string directory)
    {
        var parts = Path.GetRelativePath(unit, directory).Split(Path.DirectorySeparatorChar);
        return parts[0] == "logs" && parts.Skip(1).All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
    }

    private static void RequireOwnedFile(string unit, string file)
    {
        SetupFolders.RequireNoLinks(file);
        if (string.Equals(file, Path.Combine(unit, SetupFolders.OwnerFileName), StringComparison.OrdinalIgnoreCase))
        {
            if (SetupFolders.HasOwnMarker(unit)) return;
        }
        else if (string.Equals(file, Path.Combine(unit, SetupFolders.ClaimFileName), StringComparison.OrdinalIgnoreCase))
        {
            if (SetupFolders.MatchesMarker(file, SetupFolders.ClaimMarker)) return;
        }
        else if (KnownPayload(unit, file)) return;
        throw new IOException($"The sweep cannot prove nth owns '{file}'.");
    }

    private static void CollectFiles(string directory, List<string> files, string unit)
    {
        SetupFolders.RequireNoLinks(directory);
        foreach (var entry in Entries(directory))
        {
            SetupFolders.RequireNoLinks(entry);
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (!KnownDirectory(unit, entry)) throw new IOException($"The sweep cannot prove nth owns '{entry}'.");
                CollectFiles(entry, files, unit);
            }
            else
            {
                RequireOwnedFile(unit, entry);
                var state = Probe(entry);
                if (state == FileState.InUse) throw new IOException("A proven-owned payload is in use.", unchecked((int)0x80070020));
                if (state == FileState.Denied) throw new UnauthorizedAccessException("A proven-owned payload cannot be probed.");
                files.Add(entry);
            }
        }
    }

    private static void DeleteTree(string directory, string unit)
    {
        SetupFolders.RequireNoLinks(directory);
        foreach (var entry in Entries(directory))
        {
            if (string.Equals(entry, Path.Combine(unit, SetupFolders.OwnerFileName), StringComparison.OrdinalIgnoreCase)) continue; // provenance goes last
            SetupFolders.RequireNoLinks(entry);
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (!KnownDirectory(unit, entry)) throw new IOException($"The sweep cannot prove nth owns '{entry}'.");
                DeleteTree(entry, unit);
            }
            else
            {
                RequireOwnedFile(unit, entry);
                ClearReadOnly(entry);
                SetupFolders.RequireNoLinks(entry);
                File.Delete(entry);
            }
        }
        SetupFolders.RequireNoLinks(directory);
        if (directory == unit)
        {
            var marker = Path.Combine(unit, SetupFolders.OwnerFileName);
            RequireOwnedFile(unit, marker);
            ClearReadOnly(marker);
            SetupFolders.RequireNoLinks(marker);
            File.Delete(marker);
        }
        File.SetAttributes(directory, FileAttributes.Normal);
        SetupFolders.RequireNoLinks(directory);
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
        try
        {
            SetupFolders.RequireNoLinks(directory);
            var entries = Entries(directory);
            if (entries.Any(entry => Path.GetFileName(entry) != SetupFolders.OwnerFileName)) return;
            if (!SetupFolders.HasOwnMarker(directory)) { tally.Keep(directory, SweepKeptReason.NotRemoved); return; }
            var marker = Path.Combine(directory, SetupFolders.OwnerFileName);
            ClearReadOnly(marker);
            if (!SetupFolders.HasOwnMarker(directory)) { tally.Keep(directory, SweepKeptReason.NotRemoved); return; }
            File.Delete(marker);
            SetupFolders.RequireNoLinks(directory);
            File.SetAttributes(directory, FileAttributes.Normal);
            Directory.Delete(directory, recursive: false);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        { if (!ConfirmedMissing(directory)) tally.Keep(directory, SweepKeptReason.NotRemoved); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { tally.Keep(directory, SweepKeptReason.NotRemoved); }
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
