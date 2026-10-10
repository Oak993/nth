using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Marrow.UI.Port;

namespace Marrow.App.Views.Composer;

// Each EGL display owns callbacks bound to one immutable folder. Native pointers are copied before return;
// disk access runs on the pool, never on the compositor, with one exception (ED-NEW-launch-fix (1)): until the folder has
// been loaded (Load, a pool thread, at the start of Main), an entry missing from memory is read from its file, so the
// programs Boot B links in its first frames, before the load has finished, are found. A completed drain is required
// before MarkWarm. ED-NEW-launch-fix (2): an entry read from its file and then found is touched (its file's write time, on
// the pool) once a process, so the cache's size cap (GpuProgramCache.Trim) removes the entries no build has used for
// longest (an entry this process kept was written just now).
internal sealed class GpuProgramBlobStore(string folder, Action<string, byte[]>? persist = null)
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetBlob(nint key, nint keySize, nint value, nint valueSize);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetBlob(nint key, nint keySize, nint value, nint valueSize);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetFuncs(nint display, nint put, nint take);
    private readonly ConcurrentDictionary<string, byte[]> blobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> untouched = new(StringComparer.Ordinal); // read from a file, not yet found
    private volatile bool loaded;
    private readonly object pending = new();
    private Task writes = Task.CompletedTask;
    private readonly List<Task> generation = [];
    private SetBlob? put;
    private GetBlob? take;

    /// <summary>True once the folder's entries are in memory (a miss then never reads the disk).</summary>
    internal bool Loaded => loaded;

    internal void Load()
    {
        if (Directory.Exists(folder))
            foreach (var file in Directory.EnumerateFiles(folder, "*.bin"))
            {
                if (blobs.ContainsKey(Path.GetFileName(file))) continue; // kept or read already: never read twice
                try { if (blobs.TryAdd(Path.GetFileName(file), File.ReadAllBytes(file))) untouched.TryAdd(Path.GetFileName(file), 0); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        loaded = true;
    }

    internal void Install(nint display, nint install)
    {
        put ??= Put; take ??= Take;
        Marshal.GetDelegateForFunctionPointer<SetFuncs>(install)(display,
            Marshal.GetFunctionPointerForDelegate(put), Marshal.GetFunctionPointerForDelegate(take));
    }

    internal async Task Drain()
    {
        Task[] queued;
        lock (pending) queued = generation.ToArray();
        try { await Task.WhenAll(queued).ConfigureAwait(false); }
        finally
        {
            lock (pending) foreach (var task in queued) generation.Remove(task);
        }
    }

    internal void Keep(string name, byte[] bytes, bool program)
    {
        if (program) blobs[name] = bytes;
        lock (pending)
        {
            writes = writes.ContinueWith(previous =>
            {
                _ = previous.Exception; // a later attempt may retry; each drain still observes all failures in its generation
                try
                {
                    Directory.CreateDirectory(folder);
                    (persist ?? GpuProgramCache.ReplaceWhole)(Path.Combine(folder, name), bytes);
                    if (program) Interlocked.Increment(ref GpuProgramCache.Writes);
                }
                catch
                {
                    if (program) ((ICollection<KeyValuePair<string, byte[]>>)blobs).Remove(new(name, bytes));
                    throw; // a memory-only blob must never count as persisted on a later attempt
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            generation.Add(writes);
            _ = writes.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    // an entry found: its file's write time made now, once a process, on the pool (a file gone or locked is left as it is)
    private void Touch(string name)
    {
        if (!untouched.TryRemove(name, out _)) return;
        lock (pending)
        {
            writes = writes.ContinueWith(_ =>
            {
                try { File.SetLastWriteTimeUtc(Path.Combine(folder, name), DateTime.UtcNow); }
                catch (Exception) { }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            generation.Add(writes);
        }
    }

    // not yet loaded: the entry's file, kept in memory once read (null when there is none)
    private byte[]? Read(string name)
    {
        if (loaded) return null;
        using var work = AuroraBootTrace.Begin("cache: Take (file)");
        try
        {
            var file = Path.Combine(folder, name);
            if (!File.Exists(file)) return null;
            var bytes = File.ReadAllBytes(file);
            if (blobs.TryAdd(name, bytes)) untouched.TryAdd(name, 0);
            return blobs.TryGetValue(name, out var kept) ? kept : bytes;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string? Name(nint key, nint length)
    {
        if (key == 0 || length <= 0 || length > int.MaxValue) return null;
        var bytes = new byte[(int)length]; Marshal.Copy(key, bytes, 0, bytes.Length);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..40] + ".bin";
    }

    private void Put(nint key, nint keySize, nint value, nint valueSize)
    {
        using var work = AuroraBootTrace.Begin("cache: Put (memory)");
        try
        {
            if (Name(key, keySize) is not { } name || value == 0 || valueSize <= 0 || valueSize > int.MaxValue) return;
            var bytes = new byte[(int)valueSize]; Marshal.Copy(value, bytes, 0, bytes.Length);
            Keep(name, bytes, true);
        }
        catch (Exception) { } // never propagate through the native callback ABI
    }

    private nint Take(nint key, nint keySize, nint value, nint valueSize)
    {
        using var work = AuroraBootTrace.Begin("cache: Take (memory)");
        try
        {
            if (Name(key, keySize) is not { } name || (blobs.TryGetValue(name, out var bytes) ? bytes : Read(name)) is not { } found)
            { Interlocked.Increment(ref GpuProgramCache.Misses); return 0; }
            bytes = found;
            if (value != 0 && valueSize >= bytes.Length)
            { using var hit = AuroraBootTrace.Begin("cache: hit"); Marshal.Copy(bytes, 0, value, bytes.Length); Interlocked.Increment(ref GpuProgramCache.Hits); Touch(name); }
            return bytes.Length;
        }
        catch (Exception) { return 0; }
    }
}
