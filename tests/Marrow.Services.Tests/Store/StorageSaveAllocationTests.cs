using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Store;

// ED-NEW-perf-memory-5: a periodic save of a long thread (Services/Store/Storage.swift:376-386) built a JsonElement, a StringBuilder, a string and
// a byte array, and read the written file back into one more array: about 6 to 8 times the file's size in transient large-object-heap memory on
// every save while a chat streams. The document is now written to pooled chunks and the read-back compares in pieces.
public sealed class StorageSaveAllocationTests
{
    private static ThreadDocument LongThread(Guid id)
    {
        var random = new Random(5);
        var turns = Enumerable.Range(0, 120).Select(index => new TurnRecord
        {
            Id = Guid.NewGuid(),
            Index = index,
            Status = TurnStatus.Completed,
            CompletedAt = SwiftDate.Now,
            ProviderDiff = string.Create(60_000, random.Next(), static (span, seed) =>
            {
                var local = new Random(seed);
                for (var at = 0; at < span.Length; at++) span[at] = at % 61 == 60 ? '\n' : (char)local.Next('a', 'z');
            }),
        }).ToList();
        return new ThreadDocument(id) { Turns = new ValueList<TurnRecord>(turns) };
    }

    [Fact]
    [Trait("Req", "PR-STORE-174")] // Services/Store/Storage.swift:376-386 (the write returns the byte count)
    public void A_save_of_a_long_thread_allocates_about_the_files_size_once_not_six_to_eight_times_it()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        var url = Storage.ThreadURL(id);
        var document = LongThread(id);
        var write = typeof(DiskWriter).GetMethod("Write", BindingFlags.NonPublic | BindingFlags.Static)!;

        // Two warm-up saves: the pool of rented buffers needs a second pass to hold every size the save uses.
        var allocated = AllocationProbe.Measure(() => { write.Invoke(null, [document, url]); write.Invoke(null, [document, url]); }, () => write.Invoke(null, [document, url]));

        var size = new FileInfo(url).Length;
        Assert.True(size > 7_000_000, "the probe thread is several megabytes");
        Assert.True(allocated < size * 2.2, $"a save allocated {allocated} bytes for a {size} byte file");
        var back = Storage.DecodeDocument(id)!; // and what it wrote still reads back whole
        Assert.Equal(120, back.Turns.Count);
        Assert.Equal(document.Turns[7].ProviderDiff, back.Turns[7].ProviderDiff);
    }
}
