using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Providers;

// Audit fix (svc): UsageLimitSignal.swift:37 Calendar.nextDate(after:matching:matchingPolicy:) leaves repeatedTimePolicy at its default,
// .first, so a wall time that occurs twice at the end of daylight saving resolves to its first occurrence (the daylight-time offset).
public sealed class UsageLimitSignalOverlapTests
{
    [Trait("Req", "PR-PROVREG-221")] // Services/Providers/UsageLimitSignal.swift:31-37
    [Fact]
    public void A_wall_time_that_occurs_twice_resolves_to_its_first_occurrence()
    {
        using var zone = SwiftFormatters.CurrentTimeZone.Override(TimeZoneInfo.Utc);
        var now = new DateTimeOffset(2025, 11, 2, 4, 30, 0, TimeSpan.Zero);

        var result = UsageLimitSignal.ResetTime("Your limit resets 1:30 am (America/New_York)", now);

        Assert.Equal(new DateTimeOffset(2025, 11, 2, 5, 30, 0, TimeSpan.Zero), result);
    }
}
