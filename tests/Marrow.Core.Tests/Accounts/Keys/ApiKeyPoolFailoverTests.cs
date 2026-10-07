using Marrow.Core.Accounts.Keys;

namespace Marrow.Core.Tests.Accounts.Keys;

// NOTES "API providers": silent fallback on 402/429 within the same turn; MA-6 task: a 'Continued on <name>' event and an 'all keys out' state.
public sealed class ApiKeyPoolFailoverTests
{
    [Theory]
    [InlineData(402)]
    [InlineData(429)]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T108")]
    [Trait("Rule", "R-TEST-4")]
    public void Out_of_credits_or_rate_limited_continues_the_same_request_on_the_next_allowed_key_with_a_continued_event(int status)
    {
        var rig = new ApiKeyRig();
        var rotation = rig.Rotation();
        Assert.Equal("a", rotation.Current()!.Id);
        var next = rotation.OnFailure(status, null);
        Assert.Equal(("b", "sk-b"), (next!.Id, next.Secret));
        var e = Assert.Single(rig.Events);
        Assert.Equal((ApiKeyEventKind.Continued, "a", "Key A", "b", "Key B", status), (e.Kind, e.FromKeyId, e.FromName, e.ToKeyId, e.ToName, e.Status));
        Assert.Equal("Continued on Key B", e.Message);
        Assert.DoesNotContain("sk-", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T109")]
    [Trait("Rule", "R-TEST-4")]
    public void The_next_request_does_not_go_back_to_a_key_that_is_out()
    {
        var rig = new ApiKeyRig();
        var rotation = rig.Rotation();
        rotation.Current();
        rotation.OnFailure(402, null);
        Assert.Equal("b", rotation.Current()!.Id);
        Assert.Equal("b", rig.Pick(rig.Rotation(ApiKeyRig.Other)));
    }

    [Theory]
    [InlineData(500)]
    [InlineData(401)]
    [InlineData(400)]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T110")]
    [Trait("Rule", "R-TEST-4")]
    public void Any_other_status_neither_rotates_nor_marks_the_key_out(int status)
    {
        var rig = new ApiKeyRig();
        var rotation = rig.Rotation();
        rotation.Current();
        Assert.Null(rotation.OnFailure(status, null));
        Assert.Empty(rig.Events);
        Assert.Equal("a", rotation.Current()!.Id);
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T111")]
    [Trait("Rule", "R-TEST-4")]
    public void A_rate_limited_key_returns_after_the_retry_after_it_was_given_or_after_sixty_seconds()
    {
        var rig = new ApiKeyRig(2);
        var rotation = rig.Rotation();
        rotation.Current();
        rotation.OnFailure(429, TimeSpan.FromSeconds(10));
        rig.Clock.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal("b", rig.Pick(rotation));
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("a", rig.Pick(rotation));

        rotation.OnFailure(429, null);
        rig.Clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal("b", rig.Pick(rotation));
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("a", rig.Pick(rotation));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T112")]
    [Trait("Rule", "R-TEST-4")]
    public void An_out_of_credits_key_returns_when_credits_are_reported_again_or_after_thirty_minutes()
    {
        var rig = new ApiKeyRig(2);
        rig.Credits.Set("a", 0);
        var rotation = rig.Rotation();
        rotation.Current();
        rotation.OnFailure(402, null);
        Assert.Equal("b", rig.Pick(rotation));
        rig.Credits.Set("a", 5);
        Assert.Equal("a", rig.Pick(rotation));

        rig.Credits.Set("a", null);
        rotation.OnFailure(402, null);
        rig.Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal("b", rig.Pick(rotation));
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal("a", rig.Pick(rotation));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T113")]
    [Trait("Rule", "R-TEST-4")]
    public void When_every_key_has_failed_the_request_ends_with_an_all_keys_out_event_and_state()
    {
        var rig = new ApiKeyRig(2);
        var rotation = rig.Rotation();
        rotation.Current();
        Assert.Equal("b", rotation.OnFailure(429, TimeSpan.FromSeconds(40))!.Id);
        Assert.Null(rotation.OnFailure(402, null));
        var all = rig.Events.Last();
        Assert.Equal(ApiKeyEventKind.AllKeysOut, all.Kind);
        Assert.Equal(rig.Clock.GetUtcNow().AddSeconds(40), all.ReturnsAt);
        var state = rig.Pool.State();
        Assert.True(state.AllOut);
        Assert.Equal(rig.Clock.GetUtcNow().AddSeconds(40), state.ReturnsAt);
        Assert.Equal(0, state.Available);
        Assert.Null(rotation.Current());
        rig.Clock.Advance(TimeSpan.FromSeconds(40));
        Assert.False(rig.Pool.State().AllOut);
        Assert.Equal("a", rig.Pick(rotation));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T114")]
    [Trait("Rule", "R-TEST-4")]
    public void A_key_is_not_tried_twice_for_one_request_and_a_new_request_starts_clean()
    {
        var rig = new ApiKeyRig(3);
        var rotation = rig.Rotation();
        rotation.Current();
        Assert.Equal("b", rotation.OnFailure(429, TimeSpan.FromSeconds(1))!.Id);
        Assert.Equal("c", rotation.OnFailure(429, TimeSpan.FromSeconds(1))!.Id);
        rig.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(["Key B", "Key C"], rig.Events.Where(e => e.Kind == ApiKeyEventKind.Continued).Select(e => e.ToName).ToList());
        Assert.Equal("a", rotation.Current()!.Id);
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T115")]
    [Trait("Rule", "R-TEST-4")]
    public void A_key_turned_off_while_a_request_is_running_is_not_the_one_continued_on()
    {
        var rig = new ApiKeyRig();
        var rotation = rig.Rotation();
        rotation.Current();
        rig.Store.Update("b", k => k with { Allowed = false });
        Assert.Equal("c", rotation.OnFailure(402, null)!.Id);
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T116")]
    [Trait("Rule", "R-TEST-4")]
    public void One_threads_outage_is_every_threads_but_the_tried_set_is_its_own()
    {
        var rig = new ApiKeyRig();
        var first = rig.Rotation();
        var second = rig.Rotation(ApiKeyRig.Other);
        first.Current();
        second.Current();
        first.OnFailure(402, null);
        Assert.Equal("b", second.Current()!.Id);
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T117")]
    [Trait("Rule", "R-TEST-4")]
    public void A_failure_before_any_current_key_is_ignored()
    {
        var rig = new ApiKeyRig();
        Assert.Null(rig.Rotation().OnFailure(429, null));
        Assert.Empty(rig.Events);
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T138")]
    [Trait("Rule", "R-TEST-4")]
    public void A_retry_after_of_zero_still_never_sends_one_request_twice_on_the_same_key()
    {
        var rig = new ApiKeyRig(2);
        var rotation = rig.Rotation();
        rotation.Current();
        Assert.Equal("b", rotation.OnFailure(429, TimeSpan.Zero)!.Id);
        Assert.Null(rotation.OnFailure(429, TimeSpan.Zero));
        Assert.Equal(ApiKeyEventKind.AllKeysOut, rig.Events.Last().Kind);
    }
}
