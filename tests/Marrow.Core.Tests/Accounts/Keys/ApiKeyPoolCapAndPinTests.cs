using Marrow.Core.Accounts.Keys;

namespace Marrow.Core.Tests.Accounts.Keys;

// NOTES "API providers": optional cap per key, per-thread pin. The cap is monthly (MA-6 task).
public sealed class ApiKeyPoolCapAndPinTests
{
    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T118")]
    [Trait("Rule", "R-TEST-4")]
    public void Spend_is_tracked_on_the_key_the_request_used_and_a_key_over_its_cap_is_skipped()
    {
        var rig = new ApiKeyRig();
        rig.Store.Update("a", k => k with { MonthlyCapUsd = 10 });
        var rotation = rig.Rotation();
        rotation.Current();
        rotation.RecordSpend(6);
        Assert.Equal(6, rig.Spend.SpendSince("a", DateTimeOffset.MinValue));
        Assert.Equal("a", rig.Pick(rotation));
        rotation.RecordSpend(4);
        Assert.Equal("b", rig.Pick(rotation));
        Assert.Single(rig.Events, e => e.Kind == ApiKeyEventKind.CapReached);
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T119")]
    [Trait("Rule", "R-TEST-4")]
    public void The_cap_is_per_calendar_month_and_opens_again_in_the_next_one()
    {
        var rig = new ApiKeyRig(2);
        rig.Store.Update("a", k => k with { MonthlyCapUsd = 5 });
        var rotation = rig.Rotation();
        rotation.Current();
        rotation.RecordSpend(5);
        Assert.Equal("b", rig.Pick(rotation));
        rig.Clock.Advance(TimeSpan.FromDays(40));
        Assert.Equal("a", rig.Pick(rotation));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T120")]
    [Trait("Rule", "R-TEST-4")]
    public void A_key_with_no_cap_never_stops_and_a_cap_event_comes_once()
    {
        var rig = new ApiKeyRig(2);
        var rotation = rig.Rotation();
        rotation.Current();
        rotation.RecordSpend(1_000_000);
        Assert.Equal("a", rig.Pick(rotation));
        Assert.Empty(rig.Events);

        rig.Store.Update("b", k => k with { MonthlyCapUsd = 1 });
        rig.Store.Update("a", k => k with { Allowed = false });
        rotation.Current();
        rotation.RecordSpend(2);
        rotation.RecordSpend(2);
        Assert.Single(rig.Events, e => e.Kind == ApiKeyEventKind.CapReached);
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T121")]
    [Trait("Rule", "R-TEST-4")]
    public void When_every_remaining_key_is_over_its_cap_the_pool_is_all_out_until_the_month_turns()
    {
        var rig = new ApiKeyRig(2);
        rig.Store.Update("a", k => k with { MonthlyCapUsd = 1 });
        rig.Store.Update("b", k => k with { MonthlyCapUsd = 1 });
        rig.Spend.Add("a", 1, rig.Clock.GetUtcNow());
        rig.Spend.Add("b", 1, rig.Clock.GetUtcNow());
        Assert.Null(rig.Rotation().Current());
        Assert.True(rig.Pool.State().AllOut);
        var firstOfNext = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(firstOfNext, rig.Pool.State().ReturnsAt);
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T122")]
    [Trait("Rule", "R-TEST-4")]
    public void A_pinned_thread_uses_its_key_whatever_the_order_and_others_are_unaffected()
    {
        var rig = new ApiKeyRig();
        rig.Pool.Pin(ApiKeyRig.Thread, "c");
        Assert.Equal("c", rig.Pick(rig.Rotation()));
        Assert.Equal("a", rig.Pick(rig.Rotation(ApiKeyRig.Other)));
        rig.Pool.Pin(ApiKeyRig.Thread, null);
        Assert.Equal("a", rig.Pick(rig.Rotation()));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T123")]
    [Trait("Rule", "R-TEST-4")]
    public void A_pinned_key_that_fails_never_falls_to_another_key()
    {
        var rig = new ApiKeyRig();
        rig.Pool.Pin(ApiKeyRig.Thread, "c");
        var rotation = rig.Rotation();
        rotation.Current();
        Assert.Null(rotation.OnFailure(402, null));
        var e = Assert.Single(rig.Events);
        Assert.Equal((ApiKeyEventKind.PinnedKeyOut, "c"), (e.Kind, e.FromKeyId));
        Assert.Null(rotation.Current());
        Assert.Equal("a", rig.Pick(rig.Rotation(ApiKeyRig.Other)));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T124")]
    [Trait("Rule", "R-TEST-4")]
    public void A_pin_on_a_key_that_is_off_or_unknown_gives_no_choice()
    {
        var rig = new ApiKeyRig();
        rig.Store.Update("c", k => k with { Allowed = false });
        rig.Pool.Pin(ApiKeyRig.Thread, "c");
        Assert.Null(rig.Rotation().Current());
        rig.Pool.Pin(ApiKeyRig.Thread, "nope");
        Assert.Null(rig.Rotation().Current());
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T151")]
    [Trait("Rule", "R-TEST-4")]
    public void A_pinned_key_at_its_cap_stops_the_thread_before_sending_and_offers_to_unpin_or_raise_the_cap()
    {
        var rig = new ApiKeyRig();
        rig.Store.Update("c", k => k with { MonthlyCapUsd = 3 });
        rig.Spend.Add("c", 3, rig.Clock.GetUtcNow());
        rig.Pool.Pin(ApiKeyRig.Thread, "c");
        var rotation = rig.Rotation();
        Assert.Null(rotation.Current());
        Assert.Equal("Key C reached its monthly cap. Unpin this thread or raise the cap.", rotation.StopReason);
        var e = Assert.Single(rig.Events);
        Assert.Equal((ApiKeyEventKind.PinnedKeyCapped, "c"), (e.Kind, e.FromKeyId));
        Assert.Equal("a", rig.Pick(rig.Rotation(ApiKeyRig.Other)));

        rig.Store.Update("c", k => k with { MonthlyCapUsd = 10 });
        Assert.Equal("c", rotation.Current()!.Id);
        Assert.Null(rotation.StopReason);
        rig.Store.Update("c", k => k with { MonthlyCapUsd = 3 });
        rig.Pool.Pin(ApiKeyRig.Thread, null);
        Assert.Equal("a", rig.Pick(rotation));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T152")]
    [Trait("Rule", "R-TEST-4")]
    public void A_pinned_key_that_is_out_or_off_says_so_and_offers_to_unpin()
    {
        var rig = new ApiKeyRig();
        rig.Pool.Pin(ApiKeyRig.Thread, "c");
        var rotation = rig.Rotation();
        rotation.Current();
        rotation.OnFailure(402, null);
        Assert.Null(rotation.Current());
        Assert.Equal("Key C is out. Unpin this thread to use another key.", rotation.StopReason);
        Assert.Equal(2, rig.Events.Count(e => e.Kind == ApiKeyEventKind.PinnedKeyOut));
    }
}
