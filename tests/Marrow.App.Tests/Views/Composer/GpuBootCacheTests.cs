using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Xunit;

namespace Marrow.App.Tests.Views.Composer;

[Collection("Headless")]
public sealed class GpuBootCacheTests
{
    [Fact, Trait("Decision", "ED-252")]
    public async Task Native_callback_copies_data_and_returns_while_persistence_is_blocked()
    {
        using var cache = new GpuProgramCacheFolder();
        using var writing = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var store = new GpuProgramBlobStore(cache.Folder, (file, bytes) =>
        {
            writing.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            GpuProgramCache.ReplaceWhole(file, bytes);
        });
        var bytes = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
        try
        {
            await Task.Run(() => GpuBlobNativeCall.Put(store, bytes)).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(writing.Wait(TimeSpan.FromSeconds(10))); Assert.False(store.Drain().IsCompleted);
            Array.Fill(bytes, (byte)255); // source and native buffers have already been changed/freed
            Assert.Equal(Enumerable.Range(0, 64).Select(i => (byte)i), GpuBlobNativeCall.Take(store));
        }
        finally { release.Set(); await store.Drain(); }
        Assert.Equal(Enumerable.Range(0, 64).Select(i => (byte)i), File.ReadAllBytes(Assert.Single(Directory.GetFiles(cache.Folder, "*.bin"))));
    }

    [Fact, Trait("Decision", "ED-252")]
    public async Task Each_display_callback_keeps_its_binding_and_reads_without_disk_access()
    {
        using var cache = new GpuProgramCacheFolder();
        var a = new GpuProgramBlobStore(Path.Combine(cache.Folder, "a")); var b = new GpuProgramBlobStore(Path.Combine(cache.Folder, "b"));
        await Task.WhenAll(Task.Run(() => GpuBlobNativeCall.Put(a, [10, 20])), Task.Run(() => GpuBlobNativeCall.Put(b, [30, 40])));
        await Task.WhenAll(a.Drain(), b.Drain());
        Directory.Delete(Path.Combine(cache.Folder, "a"), true); Directory.Delete(Path.Combine(cache.Folder, "b"), true);
        Assert.Equal(new byte[] { 10, 20 }, GpuBlobNativeCall.Take(a)); Assert.Equal(new byte[] { 30, 40 }, GpuBlobNativeCall.Take(b));
    }

    [Fact, Trait("Decision", "ED-252")]
    public async Task Persistence_failure_is_visible_at_the_drain_boundary()
    {
        using var cache = new GpuProgramCacheFolder();
        var writes = GpuProgramCache.Writes;
        var store = new GpuProgramBlobStore(cache.Folder, (_, _) => throw new IOException("scripted disk failure"));
        GpuBlobNativeCall.Put(store, [1, 2, 3]); // native ABI returns normally
        await Assert.ThrowsAsync<IOException>(() => store.Drain());
        Assert.Equal(writes, GpuProgramCache.Writes); // never counted as persisted/prepared
    }

    [Fact, Trait("Decision", "ED-252")]
    public async Task A_failed_blob_is_not_returned_as_durable_and_a_later_write_can_retry()
    {
        using var cache = new GpuProgramCacheFolder(); var fail = true;
        var store = new GpuProgramBlobStore(cache.Folder, (file, bytes) =>
        { if (fail) throw new IOException("first write fails"); GpuProgramCache.ReplaceWhole(file, bytes); });
        GpuBlobNativeCall.Put(store, [1, 2]); await Assert.ThrowsAsync<IOException>(() => store.Drain());
        Assert.Empty(GpuBlobNativeCall.Take(store));
        fail = false; GpuBlobNativeCall.Put(store, [3, 4]); await store.Drain();
        Assert.Equal(new byte[] { 3, 4 }, GpuBlobNativeCall.Take(store));
        Assert.Equal(new byte[] { 3, 4 }, File.ReadAllBytes(Assert.Single(Directory.GetFiles(cache.Folder, "*.bin"))));
    }

    [Fact, Trait("Decision", "ED-252")]
    public async Task Concurrent_atomic_writes_to_one_program_leave_one_complete_blob_and_no_parts()
    {
        using var cache = new GpuProgramCacheFolder();
        var file = Path.Combine(cache.Folder, "one.bin");
        await Task.WhenAll(Enumerable.Range(1, 32).Select(i => Task.Run(() => GpuProgramCache.ReplaceWhole(file, Enumerable.Repeat((byte)i, 65536).ToArray()))));
        var bytes = File.ReadAllBytes(file); Assert.Equal(65536, bytes.Length); Assert.InRange(bytes[0], (byte)1, (byte)32);
        Assert.All(bytes, b => Assert.Equal(bytes[0], b)); Assert.Empty(Directory.GetFiles(cache.Folder, "*.part"));
    }
}
