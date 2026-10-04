using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Engine;

// VT-6, V-2.2: whisper.cpp threads = max(1, physicalCores - 2).
public sealed class ThreadCountTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 6)]
    [InlineData(16, 14)]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void Threads_are_physical_cores_minus_two_and_at_least_one(int cores, int threads)
    {
        Assert.Equal(threads, ThreadCount.For(cores));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void A_core_count_below_one_still_gives_one_thread(int cores)
    {
        Assert.Equal(1, ThreadCount.For(cores));
    }
}
