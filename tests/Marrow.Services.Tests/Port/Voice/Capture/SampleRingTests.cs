using Marrow.Services.Port.Voice.Capture;

namespace Marrow.Services.Tests.Port.Voice.Capture;

// VT-5 (voice-SPEC V-2.2 item 1): the single-producer single-consumer ring between the capture thread and the voice processing thread.
public sealed class SampleRingTests
{
    private static float[] Counting(int start, int count) => Enumerable.Range(start, count).Select(i => (float)i).ToArray();

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // oldest first
    public void Samples_come_out_in_the_order_they_went_in()
    {
        var ring = new SampleRing(16);
        Assert.True(ring.TryWrite(Counting(0, 5)));
        Assert.True(ring.TryWrite(Counting(5, 3)));
        Assert.Equal(8, ring.Count);
        var output = new float[16];
        Assert.Equal(8, ring.Read(output));
        Assert.Equal(Counting(0, 8), output[..8]);
        Assert.Equal(0, ring.Count);
        Assert.Equal(0, ring.Read(output));
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // wrap-around keeps the order
    public void The_ring_wraps_around_without_losing_order()
    {
        var ring = new SampleRing(8);
        var output = new float[8];
        var next = 0;
        for (var round = 0; round < 20; round++)
        {
            Assert.True(ring.TryWrite(Counting(next, 6)));
            Assert.Equal(6, ring.Read(output));
            Assert.Equal(Counting(next, 6), output[..6]);
            next += 6;
        }
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // a write that does not fit is refused whole, so frames never split
    public void A_write_that_does_not_fit_is_refused_whole()
    {
        var ring = new SampleRing(8);
        Assert.True(ring.TryWrite(Counting(0, 6)));
        Assert.False(ring.TryWrite(Counting(6, 4)));
        Assert.Equal(6, ring.Count);
        var output = new float[8];
        Assert.Equal(6, ring.Read(output));
        Assert.True(ring.TryWrite(Counting(6, 8)));
        Assert.Equal(8, ring.Read(output));
        Assert.Equal(Counting(6, 8), output);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // reading into a small span leaves the rest
    public void A_short_read_leaves_the_rest_for_the_next()
    {
        var ring = new SampleRing(16);
        ring.TryWrite(Counting(0, 10));
        var output = new float[4];
        Assert.Equal(4, ring.Read(output));
        Assert.Equal(Counting(0, 4), output);
        Assert.Equal(6, ring.Count);
        Assert.Equal(4, ring.Read(output));
        Assert.Equal(Counting(4, 4), output);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // Clear empties it
    public void Clear_empties_the_ring()
    {
        var ring = new SampleRing(8);
        ring.TryWrite(Counting(0, 5));
        ring.Clear();
        Assert.Equal(0, ring.Count);
        Assert.Equal(0, ring.Read(new float[8]));
        Assert.True(ring.TryWrite(Counting(0, 8)));
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // V-2.2: lock-free, one writer thread and one reader thread, nothing lost or reordered
    public void One_writer_thread_and_one_reader_thread_lose_and_reorder_nothing()
    {
        const int total = 2_000_000;
        var ring = new SampleRing(4096);
        var writer = new Thread(() =>
        {
            var chunk = new float[480];
            var next = 0;
            while (next < total)
            {
                var count = Math.Min(chunk.Length, total - next);
                for (var i = 0; i < count; i++) chunk[i] = next + i;
                if (ring.TryWrite(chunk.AsSpan(0, count))) next += count;
                else Thread.Yield();
            }
        });
        writer.Start();
        var buffer = new float[1000];
        var expected = 0;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (expected < total)
        {
            Assert.True(DateTime.UtcNow < deadline, "the reader starved");
            var read = ring.Read(buffer);
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] != expected + i) Assert.Fail($"sample {expected + i} came out as {buffer[i]}");
            }
            expected += read;
            if (read == 0) Thread.Yield();
        }
        writer.Join();
        Assert.Equal(0, ring.Count);
    }
}
