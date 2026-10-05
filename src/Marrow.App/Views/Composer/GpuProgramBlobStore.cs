using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Marrow.UI.Port;

namespace Marrow.App.Views.Composer;

// Each EGL display owns callbacks bound to one immutable folder. Native pointers are copied before return;
// disk access runs on the pool, never on the compositor. A completed drain is required before MarkWarm.
internal sealed class GpuProgramBlobStore(string folder, Action<string, byte[]>? persist = null)
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetBlob(nint key, nint keySize, nint value, nint valueSize);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetBlob(nint key, nint keySize, nint value, nint valueSize);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetFuncs(nint display, nint put, nint take);
    private readonly ConcurrentDictionary<string, byte[]> blobs = new(StringComparer.Ordinal);
    private readonly object pending = new();
    private Task writes = Task.CompletedTask;
    private readonly List<Task> generation = [];
    private SetBlob? put;
    private GetBlob? take;

    internal void Load()
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*.bin"))
            try { blobs.TryAdd(Path.GetFileName(file), File.ReadAllBytes(file)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
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
            if (Name(key, keySize) is not { } name || !blobs.TryGetValue(name, out var bytes))
            { Interlocked.Increment(ref GpuProgramCache.Misses); return 0; }
            if (value != 0 && valueSize >= bytes.Length)
            { using var hit = AuroraBootTrace.Begin("cache: hit"); Marshal.Copy(bytes, 0, value, bytes.Length); Interlocked.Increment(ref GpuProgramCache.Hits); }
            return bytes.Length;
        }
        catch (Exception) { return 0; }
    }
}
