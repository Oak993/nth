using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// ED-121 (b) with ED-NEW-perf-memory-5: the read-back compares the written file with the bytes in 64 KB pieces instead of reading the whole file
// into a second array, so a save of a long thread does not allocate the file's size again. What it proves (equal bytes, or the attempt fails) is unchanged.
public sealed class FileOpsReadBackTests
{
    private static byte[] Payload(int length)
    {
        var bytes = new byte[length];
        new Random(13).NextBytes(bytes);
        return bytes;
    }

    [Fact]
    [Trait("Rule", "R-TEST-5")]
    [Trait("Decision", "ED-121")]
    public void The_read_back_of_a_large_file_allocates_no_second_copy_of_it()
    {
        using var folder = new ScratchFolder();
        var path = folder.PathOf("big.json");
        var payload = Payload(8_000_000);

        var allocated = AllocationProbe.Measure(() => FileOps.WriteAtomic(path, payload), () => FileOps.WriteAtomic(path, payload));

        Assert.True(allocated < 500_000, $"WriteAtomic allocated {allocated} bytes for an 8 MB file");
        Assert.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(payload));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(65_535)]
    [InlineData(65_536)]
    [InlineData(65_537)]
    [InlineData(300_001)]
    [Trait("Decision", "ED-121")]
    public void Files_of_every_size_around_the_piece_length_land_exactly(int length)
    {
        using var folder = new ScratchFolder();
        var path = folder.PathOf("sizes.json");
        var payload = Payload(length);

        FileOps.WriteAtomic(path, payload);

        Assert.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(payload));
        Assert.Equal(["sizes.json"], folder.Names());
    }

    [Fact]
    [Trait("Decision", "ED-121")]
    public void A_replace_over_an_older_longer_file_leaves_only_the_new_bytes()
    {
        using var folder = new ScratchFolder();
        var path = folder.Seed("old.json", Payload(200_000));
        var fresh = Payload(70_000);

        FileOps.WriteAtomic(path, fresh);

        Assert.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(fresh));
        Assert.Equal(["old.json"], folder.Names());
    }
}
