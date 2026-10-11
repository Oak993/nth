using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// ED-NEW-lean-2: allocation budgets for the two SwiftDate conversions the per-frame callers run (StreamVeil, HydraShimmer, HangWatchdog,
// HydraIdleSeconds, SwiftFormatters): ReferenceSeconds (Date.timeIntervalSinceReferenceDate) and FromReferenceSeconds, whose work is
// NearestTicks (Date(timeIntervalSinceReferenceDate:)). 1,000 fixed-seed present-day dates and second values (2024-2027, sub-second
// fractions), measured on a dedicated thread after warm-up (AllocationProbe), no clock. Numbers are for the Debug build the test runs use;
// the ledger entry records the before and after values the limits come from.
[Trait("Decision", "ED-NEW-lean-2")]
public sealed class SwiftDateLeanBudgetTests
{
    private const int Calls = 1_000;
    private const long ReferenceSecondsBytesPerCallLimit = 3_700; // Debug: 2,464 before the BigInteger-free paths
    private const long NearestTicksBytesPerCallLimit = 180;         // Debug: 119 before the BigInteger-free paths

    [Fact]
    public void ReferenceSeconds_stays_within_its_allocation_budget()
    {
        var random = new Random(5002);
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks;
        var span = new DateTimeOffset(2027, 12, 31, 0, 0, 0, TimeSpan.Zero).UtcTicks - start;
        var dates = new DateTimeOffset[Calls];
        for (var i = 0; i < Calls; i++) dates[i] = new DateTimeOffset(start + random.NextInt64(span), TimeSpan.Zero);
        var sum = 0d;
        void Pass() { foreach (var date in dates) sum += SwiftDate.ReferenceSeconds(date); }
        var bytes = AllocationProbe.Measure(() => { for (var i = 0; i < 20; i++) Pass(); }, Pass);
        Assert.True(sum > 0);
        var perCall = bytes / Calls;
        Assert.True(perCall <= ReferenceSecondsBytesPerCallLimit, $"{perCall} bytes per ReferenceSeconds ({bytes} for {Calls} calls)");
    }

    [Fact]
    public void NearestTicks_stays_within_its_allocation_budget()
    {
        var random = new Random(5003);
        var start = SwiftDate.ReferenceSeconds(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var seconds = new double[Calls];
        for (var i = 0; i < Calls; i++) seconds[i] = start + random.NextDouble() * 4 * 365.25 * 86_400;
        var ticks = 0L;
        void Pass() { foreach (var value in seconds) ticks ^= SwiftDate.FromReferenceSeconds(value).UtcTicks; }
        var bytes = AllocationProbe.Measure(() => { for (var i = 0; i < 20; i++) Pass(); }, Pass);
        var perCall = bytes / Calls;
        Assert.True(perCall <= NearestTicksBytesPerCallLimit, $"{perCall} bytes per FromReferenceSeconds/NearestTicks ({bytes} for {Calls} calls)");
    }
}
