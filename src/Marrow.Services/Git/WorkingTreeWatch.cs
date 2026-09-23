// Source: DroppyCode/Services/Git/WorkingTreeWatch.swift (B2 1fbf81e), :1-353.
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Marrow.Core.Port;
using Marrow.Services.Port;

[assembly: InternalsVisibleTo("Marrow.Services.Tests")]

namespace Marrow.Services.Git;

public sealed class WorkingTreeWatch
{
    private const int ChangedLimit = 64; // :82
    private const int Limit = 32; // :87
    private static readonly Lock RegistryGate = new();
    private static readonly List<(string Directory, WorkingTreeWatch Watch)> Watches = [];

    private readonly Lock gate = new();
    private readonly SerialQueue queue = new("droppy.working-tree-watch"); // :121
    private readonly string root;
    private readonly string gitRoot;
    private readonly string rootPrefix;
    private readonly string gitPrefix;
    private readonly string cookiePrefix;
    private readonly List<(ulong Id, ulong Cookie, TaskCompletionSource<bool> Completion)> waiters = [];
    private readonly TaskCompletionSource teardownCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private HashSet<string>? changed = new(StringComparer.Ordinal);
    private ulong generation = 1;
    private ulong cookiesWritten;
    private ulong cookieSeen;
    private ulong waitersAdded;
    private Snapshot? snapshot;
    private (ulong Generation, ulong Version, Task<string?> Task)? capture;
    private FileWatcher? stream;
    private ulong teardownVersion;

    private sealed record IndexStamp(DateTimeOffset? Modified, long? Size)
    {
        // :25-34
        public static IndexStamp Read(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return new(null, null);
                return new(info.LastWriteTimeUtc, info.Length);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return new(null, null);
            }
        }
    }

    private sealed record Snapshot(string Tree, ulong Generation, IndexStamp Index, string? Commit);

    // :92-108
    public static WorkingTreeWatch? Shared(string directory, string gitDirectory)
    {
        lock (RegistryGate)
        {
            var existing = Watches.FirstOrDefault(w => string.Equals(w.Directory, directory, StringComparison.Ordinal)).Watch;
            if (existing is not null) return existing;
        }
        if (!Directory.Exists(directory) || !IsLocal(directory)) return null;
        var watch = new WorkingTreeWatch(directory, gitDirectory);
        WorkingTreeWatch shared;
        WorkingTreeWatch? dropped;
        lock (RegistryGate)
        {
            var existing = Watches.FirstOrDefault(w => string.Equals(w.Directory, directory, StringComparison.Ordinal)).Watch;
            if (existing is not null)
            {
                shared = existing;
                dropped = watch;
            }
            else
            {
                Watches.Add((directory, watch));
                shared = watch;
                dropped = Watches.Count > Limit ? Watches[0].Watch : null;
                if (dropped is not null) Watches.RemoveAt(0);
            }
        }
        if (dropped is not null && !ReferenceEquals(dropped, shared)) dropped.TearDown();
        return shared;
    }

    // :130-155; ED-113
    private WorkingTreeWatch(string directory, string gitDirectory)
    {
        DirectoryPath = directory;
        root = Real(directory);
        gitRoot = Real(gitDirectory);
        rootPrefix = root + "\\";
        gitPrefix = gitRoot + "\\";
        cookiePrefix = gitPrefix + "droppy-code-sync-";
        ScratchPath = System.IO.Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR") ?? System.IO.Path.GetTempPath(),
            "droppy-code-index-" + SwiftUUID.UuidString(Guid.NewGuid()));
        if (SwiftPath.TailUnder(gitRoot, root) is { Length: > 0 })
            stream = FileWatcher.WatchTree([root], queue, batch => NoteBatch(batch.Paths, batch.Everything));
    }

    public string DirectoryPath { get; } // :110
    internal string ScratchPath { get; } // :120
    internal bool HasStream { get { lock (gate) return stream is not null; } }
    internal Task TeardownCompleted => teardownCompleted.Task;
    internal ulong Generation { get { lock (gate) return generation; } }
    internal ulong CookieSeen { get { lock (gate) return cookieSeen; } }
    internal IReadOnlyList<string>? ChangedPaths
    {
        get { lock (gate) return changed?.Order(StringComparer.Ordinal).ToArray(); }
    }

    // :124-128; explicit realpath request, distinct from lexical standardization (ED-141).
    private static string Real(string path)
    {
        var full = System.IO.Path.GetFullPath(path).TrimEnd('\\', '/');
        using var handle = CreateFile(full, 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) return full;
        var buffer = new char[32768];
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, 0);
        return length is > 0 and < 32768 ? SwiftPath.Normalize(new string(buffer, 0, (int)length)).Replace('/', '\\') : full;
    }

    private static bool IsLocal(string directory)
    {
        var root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(directory));
        // DRIVE_REMOVABLE, DRIVE_FIXED, DRIVE_CDROM and DRIVE_RAMDISK are local volumes.
        return root is not null && GetDriveType(root) is 2 or 3 or 5 or 6;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetDriveType(string rootPathName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, FileShare shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, char[] path, uint pathLength, uint flags);

    // :159-170
    internal void TearDown()
    {
        FileWatcher? taken;
        lock (gate)
        {
            taken = stream;
            stream = null;
            if (taken is not null) teardownVersion++;
        }
        if (taken is null) return;
        queue.Async(() =>
        {
            try
            {
                taken.Cancel();
                try { FileOps.RemoveItem(ScratchPath); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            finally { teardownCompleted.TrySetResult(); }
        });
    }

    // :179-226; FileWatcherBatch.Everything maps the three loss flags to one case (ED-114).
    internal void NoteBatch(IReadOnlyList<string> paths, bool everything)
    {
        var didChange = everything;
        var directories = new List<string>();
        ulong cookie = 0;
        foreach (var path in paths)
        {
            var normalized = SwiftPath.Normalize(path).Replace('/', '\\');
            if (normalized.StartsWith(cookiePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = normalized[cookiePrefix.Length..];
                if (ulong.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) cookie = Math.Max(cookie, number);
                continue;
            }
            if (SwiftPath.TailUnder(normalized, gitRoot) is { Length: > 0 }) continue;
            didChange = true;
            var relative = SwiftPath.TailUnder(normalized, root);
            if (relative is null || relative.Length == 0)
            {
                everything = true;
                continue;
            }
            var separator = relative.LastIndexOfAny(['\\', '/']);
            directories.Add((separator < 0 ? relative : relative[..separator]).Replace('\\', '/'));
        }
        if (!didChange && cookie == 0) return;
        List<TaskCompletionSource<bool>> ready;
        lock (gate)
        {
            if (didChange) generation = unchecked(generation + 1);
            cookieSeen = Math.Max(cookieSeen, cookie);
            if (everything) changed = null;
            else if (changed is not null)
            {
                foreach (var directory in directories) changed.Add(changed.Count >= ChangedLimit ? TopLevel(directory) : directory);
                if (changed.Count > ChangedLimit) changed = new(changed.Select(TopLevel), StringComparer.Ordinal);
            }
            ready = waiters.Where(w => w.Cookie <= cookieSeen).Select(w => w.Completion).ToList();
            waiters.RemoveAll(w => w.Cookie <= cookieSeen);
        }
        foreach (var completion in ready) completion.TrySetResult(true);
    }

    // :228-239
    internal static IReadOnlyList<string> Pathspecs(IEnumerable<string> paths)
    {
        var result = new List<string>();
        foreach (var path in paths.Order(StringComparer.Ordinal))
            if (result.Count == 0 || !path.StartsWith(result[^1] + "/", StringComparison.Ordinal)) result.Add(path);
        return result;
    }

    private static string TopLevel(string directory)
    {
        var slash = directory.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? directory : directory[..slash];
    }

    // :246-281
    // A write is counted when its handle closes, on NTFS as on FSEvents (O-55).
    private async Task<ulong?> SettledGeneration()
    {
        ulong cookie;
        ulong waiterId;
        lock (gate)
        {
            if (stream is null) return null;
            cookie = ++cookiesWritten;
            waiterId = ++waitersAdded;
        }
        var path = cookiePrefix + cookie.ToString(CultureInfo.InvariantCulture);
        // :254; ED-161: a cookie file that already exists (a crash leftover, or two watches
        // reaching the same number) is deleted once and CreateNew retried; any other failure
        // returns nil as :254 does. Delete-then-create, never FileMode.Create.
        var created = CreateCookie(path);
        if (created == false)
        {
            try { FileOps.RemoveItem(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
            created = CreateCookie(path);
        }
        if (created != true) return null;
        try
        {
            using var timeout = new CancellationTokenSource();
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate)
            {
                if (cookieSeen >= cookie) completion.TrySetResult(true);
                else waiters.Add((waiterId, cookie, completion));
            }
            _ = ExpireAfterOneSecond(waiterId, timeout.Token);
            var arrived = await completion.Task;
            timeout.Cancel();
            if (!arrived) { TearDown(); return null; }
            lock (gate) return generation;
        }
        finally
        {
            try { FileOps.RemoveItem(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    // ED-161: true when the cookie was created, false when the file already exists
    // (ERROR_FILE_EXISTS, 0x80070050), null on any other failure.
    private static bool? CreateCookie(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (IOException error) when (error.HResult == unchecked((int)0x80070050)) { return false; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private async Task ExpireAfterOneSecond(ulong waiterId, CancellationToken token)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(1), SwiftClock.Provider.Current, token); }
        catch (OperationCanceledException) { return; }
        TaskCompletionSource<bool>? expired;
        lock (gate)
        {
            var index = waiters.FindIndex(w => w.Id == waiterId);
            expired = index < 0 ? null : waiters[index].Completion;
            if (index >= 0) waiters.RemoveAt(index);
        }
        expired?.TrySetResult(false);
    }

    // :290-337
    public async Task<string> CurrentTree(string indexPath, Func<string?, IReadOnlyList<string>?, Task<string>> captureTree)
    {
        var settled = await SettledGeneration();
        if (settled is not { } generationAtStart) return await captureTree(null, null);
        var index = IndexStamp.Read(indexPath);
        while (true)
        {
            string? cached = null;
            Task<string?>? task = null;
            var own = false;
            var sameGeneration = false;
            ulong captureVersion = 0;
            lock (gate)
            {
                if (snapshot is { } saved && saved.Generation == generationAtStart && saved.Index == index) cached = saved.Tree;
                else if (capture is { } running)
                {
                    task = running.Task;
                    sameGeneration = running.Generation == generationAtStart;
                    captureVersion = running.Version;
                }
                else
                {
                    var paths = snapshot?.Index == index && changed is not null ? Pathspecs(changed) : null;
                    changed = new(StringComparer.Ordinal);
                    var startVersion = teardownVersion;
                    captureVersion = startVersion;
                    task = Task.Run(async () =>
                    {
                        try
                        {
                            var tree = await captureTree(ScratchPath, paths);
                            lock (gate) return teardownVersion == startVersion ? tree : null;
                        }
                        catch (Exception error) when (SwiftTry.IsThrownError(error)) { return null; }
                    });
                    capture = (generationAtStart, startVersion, task);
                    own = true;
                }
            }
            if (cached is not null) return cached;
            if (task is null) throw new InvalidOperationException("Missing capture task.");
            var treeResult = await task;
            if (!own)
            {
                if (treeResult is not null && sameGeneration)
                {
                    lock (gate) if (teardownVersion == captureVersion) return treeResult;
                }
                continue;
            }
            lock (gate)
            {
                if (capture?.Task == task) capture = null;
                if (teardownVersion != captureVersion) treeResult = null;
                if (treeResult is not null) snapshot = new(treeResult, generationAtStart, index, null);
                else changed = null;
            }
            if (treeResult is null) throw new CocoaError(CocoaError.ErrorCode.FileReadUnknown);
            return treeResult;
        }
    }

    // :339-351
    public string? CommitOf(string tree)
    {
        lock (gate) return snapshot?.Tree == tree ? snapshot.Commit : null;
    }

    public void Remember(string commit, string tree)
    {
        lock (gate) if (snapshot?.Tree == tree) snapshot = snapshot with { Commit = commit };
    }
}
