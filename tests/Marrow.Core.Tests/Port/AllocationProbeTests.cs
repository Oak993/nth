using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

public sealed class AllocationProbeTests
{
    [Fact]
    [Trait("Rule", "R-TEST-5")]
    public void The_probe_excludes_setup_but_counts_an_allocation_inside_the_measured_work()
    {
        object? kept = null;
        Assert.Equal(0, AllocationProbe.Measure(() => kept = new object(), () => { }));
        Assert.True(AllocationProbe.Measure(() => { }, () => kept = new byte[4096]) >= 4096);
        GC.KeepAlive(kept);
    }
}
