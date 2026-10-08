using Marrow.Core.Port.ModelsPalette;

namespace Marrow.Core.Tests.Port.ModelsPalette;

[Trait("Decision", "ED-617")]
public sealed class PaletteMathAllocationTests
{
    [Fact]
    public void Mixing_colours_allocates_only_the_result_strings()
    {
        _ = PaletteMath.Mix("#7C36D8", "#E47335", .5);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) _ = PaletteMath.Mix("#7C36D8", "#E47335", i / 1000d);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(bytes < 64_000, $"1000 colour samples allocated {bytes:N0} bytes");
    }
}
