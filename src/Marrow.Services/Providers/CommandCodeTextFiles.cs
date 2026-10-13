// PORT-ONLY: CU-DX-OPEN3 item 2; ED-NEW-dextra-open-12. Own only a generated private answer tree.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Store;
using Microsoft.Win32.SafeHandles;

namespace Marrow.Services.Providers;

internal sealed class CommandCodeTextFiles : IDisposable
{
    private const string Prefix = ".commandcode-text-";
    private const string Suffix = ".cleanup-v1"; // Empty lease file; format version is in its name. No question or credential.
    private const uint ReadAttributes = 0x80, DeleteAccess = 0x10000;
    private static readonly Logger log = new(Logger.Subsystem, "commandcode-text");
    private readonly List<SafeFileHandle> parents;
    private readonly SafeFileHandle record;
    private SafeFileHandle? tree;
    private readonly string id;
    private bool disposed;

    private CommandCodeTextFiles(string root, string id, List<SafeFileHandle> parents, SafeFileHandle record)
    {
        this.id = id; this.parents = parents; this.record = record;
        Folder = Path.Combine(root, "commandcode-text-" + id);
    }

    internal string Folder { get; }

    internal static CommandCodeTextFiles Begin(string root, string id)
    {
        if (!Guid.TryParseExact(id, "N", out _) || id.Length != 32) throw new ArgumentException("A generated answer identity is required.", nameof(id));
        root = Path.GetFullPath(root);
        StorePaths.EnsureAncestors(root);
        Directory.CreateDirectory(root);
        var parents = PinParents(root);
        CommandCodeTextFiles? owned = null;
        try
        {
            Retry(root);
            // Durable intent before any child files. No delete-sharing: another start/process cannot replay a live lease.
            var record = Open(Path.Combine(root, Prefix + id + Suffix), 0x40000000 | ReadAttributes | DeleteAccess, 0, 1);
            owned = new(root, id, parents, record);
            if (!FlushFileBuffers(record)) throw Failure();
            Directory.CreateDirectory(owned.Folder);
            // The child may write under HOME, but cannot rename or replace the root we own, even if Stop throws.
            owned.tree = Open(owned.Folder, ReadAttributes | DeleteAccess, 3);
            if (IsReparse(owned.tree)) throw new IOException("The private answer root is a link.");
            return owned;
        }
        catch
        {
            if (owned is not null) owned.Dispose();
            else Close(parents);
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (tree is not null)
            {
                // Together: the live root handle denies rename/delete; this guard denies mutation of its reparse data.
                using (var fence = Open(Folder, ReadAttributes, 5)) DeleteTree(Folder, tree, Stopwatch.StartNew(), 0, new Counter());
                tree.Dispose(); tree = null;
            }
            else DeleteTreeIfPresent(Folder);
            DeleteOpened(record);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            RecordFailure(id, error); // Keep the durable record for the next start; never log a child path or exception message.
        }
        finally { tree?.Dispose(); record.Dispose(); Close(parents); }
    }

    /// <summary>
    /// ED-NEW-secfinal-23: the retry of earlier runs' leftovers, by itself, for Dextra's start. Same rules as the retry inside <see cref="Begin"/>:
    /// the folder's ancestors are pinned while it works, a live answer's lease is skipped, a record of another version is kept. A folder that
    /// does not exist is left missing. Never throws for the file system; a start must not fail for it.
    /// </summary>
    internal static void RetryPending(string root)
    {
        List<SafeFileHandle>? parents = null;
        try
        {
            root = Path.GetFullPath(root);
            if (!Directory.Exists(root)) return;
            parents = PinParents(root);
            Retry(root);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            log.Error("commandcode-text-cleanup-deferred: start retry could not run; code=" + error.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        }
        finally
        {
            if (parents is not null) Close(parents);
        }
    }

    private static void Retry(string root)
    {
        var clock = Stopwatch.StartNew();
        var count = 0;
        foreach (var path in Directory.EnumerateFiles(root, Prefix + "*" + Suffix))
        {
            if (++count > 256 || clock.Elapsed > TimeSpan.FromSeconds(10))
            {
                log.Error("commandcode-text-cleanup-deferred: retry limit reached; remaining ownership records retained.");
                break;
            }
            var name = Path.GetFileName(path);
            var id = name[Prefix.Length..^Suffix.Length];
            if (id.Length != 32 || !Guid.TryParseExact(id, "N", out _)) continue;
            SafeFileHandle lease;
            try { lease = Open(path, ReadAttributes | DeleteAccess, 0); }
            catch (IOException error) when (error.InnerException is Win32Exception { NativeErrorCode: 32 }) { continue; } // Live head.
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { RecordFailure(id, error); continue; }
            using (lease)
            {
                try
                {
                    if (IsReparse(lease)) throw new IOException("A cleanup ownership record is a link.");
                    DeleteTreeIfPresent(Path.Combine(root, "commandcode-text-" + id));
                    DeleteOpened(lease);
                    log.Info("commandcode-text-cleanup-retried: " + id);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { RecordFailure(id, error); }
            }
        }
    }

    private static void RecordFailure(string id, Exception error) =>
        log.Error("commandcode-text-cleanup-failed: " + id + "; ownership record retained for next start; code=" + error.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));

    private static List<SafeFileHandle> PinParents(string root)
    {
        var handles = new List<SafeFileHandle>();
        try
        {
            var paths = new Stack<string>();
            for (string? path = root; path is not null; path = Path.GetDirectoryName(path))
            {
                if (paths.Count >= 128) throw new IOException("Private answer ancestor limit reached.");
                paths.Push(path);
            }
            while (paths.TryPop(out var path))
            {
                var handle = Open(path, ReadAttributes, 1); // No write or delete-sharing: names/reparse data cannot change.
                handles.Add(handle);
                if (IsReparse(handle)) throw new IOException("A private answer ancestor is a link.");
            }
            return handles;
        }
        catch { Close(handles); throw; }
    }

    private static void Close(List<SafeFileHandle> handles)
    {
        foreach (var handle in handles) handle.Dispose();
    }

    private sealed class Counter { internal int Items; }

    private static void DeleteTreeIfPresent(string folder)
    {
        SafeFileHandle handle;
        try { handle = Open(folder, ReadAttributes | DeleteAccess, 1); }
        catch (IOException error) when (error.InnerException is Win32Exception { NativeErrorCode: 2 or 3 }) { return; }
        using (handle) DeleteTree(folder, handle, Stopwatch.StartNew(), 0, new Counter());
    }

    private static void DeleteTree(string path, SafeFileHandle handle, Stopwatch clock, int depth, Counter count)
    {
        if (depth > 128 || ++count.Items > 100_000 || clock.Elapsed > TimeSpan.FromSeconds(10)) throw new IOException("Private answer cleanup limit reached.");
        var attributes = Attributes(handle);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory)
        {
            // Open each leaf itself without following reparse points and pin it before inspecting/enumerating it.
            // StoreDeletion's static ancestor check alone cannot prevent replacement by a still-live failed child.
            foreach (var child in Directory.EnumerateFileSystemEntries(path))
            {
                using var held = Open(child, ReadAttributes | DeleteAccess, 1);
                DeleteTree(child, held, clock, depth + 1, count);
            }
        }
        DeleteOpened(handle); // A reparse point is just this opened link, never its target.
    }

    private static bool IsReparse(SafeFileHandle handle) => (Attributes(handle) & FileAttributes.ReparsePoint) != 0;

    private static FileAttributes Attributes(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(handle, 9, out var information, 8)) throw Failure();
        return (FileAttributes)information.Attributes;
    }

    private static SafeFileHandle Open(string path, uint access, uint share, uint creation = 3)
    {
        var handle = CreateFileW(path, access, share, IntPtr.Zero, creation, 0x02200000, IntPtr.Zero); // BACKUP_SEMANTICS | OPEN_REPARSE_POINT.
        if (!handle.IsInvalid) return handle;
        var error = Failure(); handle.Dispose(); throw error;
    }

    private static void DeleteOpened(SafeFileHandle handle)
    {
        var disposition = 0x11u; // FILE_DISPOSITION_DELETE | IGNORE_READONLY_ATTRIBUTE (Windows 11); close commits deletion.
        if (!SetFileInformationByHandle(handle, 21, ref disposition, 4)) throw Failure();
    }

    private static IOException Failure() => new("Private answer file operation failed.", new Win32Exception(Marshal.GetLastWin32Error()));

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushFileBuffers(SafeFileHandle handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTag { public uint Attributes; public uint Tag; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out AttributeTag information, uint size);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, ref uint information, uint size);
}
