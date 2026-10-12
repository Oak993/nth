using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-17; DESIGN.md 2.7). The hook's ring: first in, first out across
// the wrap, a full ring drops and counts (the heartbeat then sees what the hooks missed), and one writer and one reader
// on two threads lose nothing in order.
[Trait("Decision", "ED-NEW-pcctl-17")]
public sealed class PcInputRingTests
{
    [Fact]
    public void Records_come_out_in_order_across_the_wrap()
    {
        var ring = new PcInputRing(4);
        var read = new List<uint>();

        for (uint time = 1; time <= 10; time++)
        {
            Assert.True(ring.Write(new PcHookRecord { Time = time }));
            Assert.True(ring.TryRead(out var record));
            read.Add(record.Time);
        }

        Assert.Equal(Enumerable.Range(1, 10).Select(i => (uint)i), read);
        Assert.False(ring.TryRead(out _));
    }

    [Fact]
    public void A_full_ring_drops_and_counts()
    {
        var ring = new PcInputRing(2);

        Assert.True(ring.Write(new PcHookRecord { Time = 1 }));
        Assert.True(ring.Write(new PcHookRecord { Time = 2 }));
        Assert.False(ring.Write(new PcHookRecord { Time = 3 }));

        Assert.Equal(1, ring.Dropped);
        Assert.True(ring.TryRead(out var first));
        Assert.Equal(1u, first.Time);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    public void The_size_is_a_power_of_two(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcInputRing(size));
    }

    [Fact]
    public async Task One_writer_and_one_reader_lose_nothing()
    {
        var ring = new PcInputRing(64);
        const int count = 20_000;
        var writer = Task.Run(() =>
        {
            for (uint time = 1; time <= count; time++)
            {
                while (!ring.Write(new PcHookRecord { Time = time })) Thread.Yield();
            }
        });
        uint expected = 1;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (expected <= count && DateTime.UtcNow < deadline)
        {
            if (ring.TryRead(out var record))
            {
                Assert.Equal(expected, record.Time);
                expected++;
            }
            else
            {
                Thread.Yield();
            }
        }
        await writer;

        Assert.Equal(count + 1u, expected);
    }
}
