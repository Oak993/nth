using Marrow.Core.Accounts.Keys;

namespace Marrow.Core.Tests.Accounts.Keys;

// Lead decision (2026-10-05): the monthly cap follows the user's LOCAL calendar month, not UTC.
public sealed class ApiKeyPoolLocalMonthTests
{
    private static readonly TimeZoneInfo West = TimeZoneInfo.CreateCustomTimeZone("west", TimeSpan.FromHours(-8), "west", "west");
    private static readonly TimeZoneInfo East = TimeZoneInfo.CreateCustomTimeZone("east", TimeSpan.FromHours(5.5), "east", "east");

    private static ApiKeyRig Make(TimeZoneInfo zone, DateTimeOffset start)
    {
        var rig = new ApiKeyRig(2, zone: zone, start: start);
        rig.Store.Update("a", k => k with { MonthlyCapUsd = 5 });
        return rig;
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T147")]
    [Trait("Rule", "R-TEST-4")]
    public void West_of_UTC_the_old_month_goes_on_until_local_midnight_and_the_key_returns_then()
    {
        // 20:00Z on Sept 30 is noon local; 03:00Z on Oct 1 is 19:00 local on Sept 30: still September for this user.
        var rig = Make(West, new DateTimeOffset(2026, 9, 30, 20, 0, 0, TimeSpan.Zero));
        rig.Spend.Add("a", 5, rig.Clock.GetUtcNow());
        rig.Clock.Advance(TimeSpan.FromHours(7));
        Assert.Equal("b", rig.Pick(rig.Rotation()));
        rig.Store.Update("b", k => k with { Allowed = false });
        Assert.Null(rig.Rotation(ApiKeyRig.Other).Current());
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), rig.Pool.State().ReturnsAt);
        rig.Clock.Advance(TimeSpan.FromHours(5));
        Assert.Equal("a", rig.Pick(rig.Rotation(ApiKeyRig.Other)));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T148")]
    [Trait("Rule", "R-TEST-4")]
    public void East_of_UTC_spend_made_after_local_midnight_counts_in_the_new_month_and_resets_at_the_next_local_one()
    {
        // 20:00Z on Sept 30 is 01:30 on Oct 1 local.
        var rig = Make(East, new DateTimeOffset(2026, 9, 30, 20, 0, 0, TimeSpan.Zero));
        rig.Spend.Add("a", 5, rig.Clock.GetUtcNow());
        rig.Store.Update("b", k => k with { Allowed = false });
        Assert.Null(rig.Rotation().Current());
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 18, 30, 0, TimeSpan.Zero), rig.Pool.State().ReturnsAt);
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T149")]
    [Trait("Rule", "R-TEST-4")]
    public void Spend_from_the_previous_local_month_does_not_count()
    {
        var rig = Make(West, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        rig.Spend.Add("a", 5, new DateTimeOffset(2026, 10, 1, 7, 0, 0, TimeSpan.Zero));
        Assert.Equal("a", rig.Pick(rig.Rotation()));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T150")]
    [Trait("Rule", "R-TEST-4")]
    public void The_cap_event_comes_once_per_local_month_and_again_in_the_next()
    {
        var rig = Make(West, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var rotation = rig.Rotation();
        rotation.Current();
        rotation.RecordSpend(5);
        rotation.RecordSpend(1);
        Assert.Single(rig.Events, e => e.Kind == ApiKeyEventKind.CapReached);
        rig.Clock.Advance(TimeSpan.FromDays(31));
        rotation.Current();
        rotation.RecordSpend(6);
        Assert.Equal(2, rig.Events.Count(e => e.Kind == ApiKeyEventKind.CapReached));
    }
}
