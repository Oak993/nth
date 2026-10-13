using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

[Trait("Decision", "ED-NEW-dextra-open-2")]
public sealed class ModelCallReadingBoundaryTests
{
    [Fact]
    public void A_booking_made_after_a_reading_with_the_same_clock_tick_still_counts()
    {
        var now = new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);
        var account = new ReserveAccount("fake", "Fake", "codex", (_, _) => new CalibratedRate(100_000, 100_000, 1, 10, true));
        account.Observe([new ReserveWindow("five", ReserveWindowKind.FiveHour, 50, now, now.AddHours(4))]);
        account.Spend(now, 200_000, "fake", afterReading: true);
        Assert.Equal(52, account.Estimate("five", now));
    }
}
