using Marrow.Core.Accounts.Keys;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// MA-6 (OD-0114): the session sends the same request again on the next allowed key when a service answers 402 (out of credits) or 429 (rate limited),
// inside the same turn, with a "Continued on <name>" notice. Real HTTP against a scripted server on 127.0.0.1; no real key, no real network.
[Collection(OpenAICompatibleSessionCollection.Name)]
public sealed class OpenAICompatibleSessionKeyRotationTests
{
    [Theory]
    [InlineData("402")]
    [InlineData("429")]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T128")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_refusal_for_credits_or_rate_sends_the_same_request_on_the_next_key_and_says_so_once(string refusal)
    {
        await using var rig = new KeyRotationRig([refusal, KeyRotationRig.Ok]);
        Assert.Null(await rig.RunTurn());
        Assert.Equal(["Bearer sk-a", "Bearer sk-b"], rig.Requests.Select(r => r.Bearer).ToList());
        Assert.Equal(rig.Requests[0].Body, rig.Requests[1].Body);
        Assert.Equal(["Continued on Key B"], rig.Notices().ToList());
        Assert.Contains(rig.Events, e => e is ProviderEvent.MessageCompleted { Text: "hi" });
        Assert.Contains(rig.Events, e => e is ProviderEvent.TurnCompleted { Status: Marrow.Core.Models.TurnStatus.Completed });
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T129")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Both_refusals_in_a_row_walk_through_every_allowed_key_in_order()
    {
        await using var rig = new KeyRotationRig(["402", "429", KeyRotationRig.Ok]);
        Assert.Null(await rig.RunTurn());
        Assert.Equal(["Bearer sk-a", "Bearer sk-b", "Bearer sk-c"], rig.Requests.Select(r => r.Bearer).ToList());
        Assert.Equal(["Continued on Key B", "Continued on Key C"], rig.Notices().ToList());
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T130")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_key_that_is_off_is_not_used_for_the_request_or_the_continuation()
    {
        await using var rig = new KeyRotationRig(["429", KeyRotationRig.Ok]);
        rig.Store.Update("a", k => k with { Allowed = false });
        Assert.Null(await rig.RunTurn());
        Assert.Equal(["Bearer sk-b", "Bearer sk-c"], rig.Requests.Select(r => r.Bearer).ToList());
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T131")]
    [Trait("Rule", "R-TEST-4")]
    public async Task With_every_key_refused_the_turn_fails_with_an_all_keys_out_notice_and_the_pool_is_all_out()
    {
        await using var rig = new KeyRotationRig(["402", "402", "429:90"]);
        var error = await rig.RunTurn();
        Assert.Equal(3, rig.Requests.Count);
        Assert.Contains("All DeepSeek API keys are out.", rig.Notices());
        Assert.True(rig.Pool.State().AllOut);
        Assert.Equal(rig.Clock.GetUtcNow().AddSeconds(90), rig.Pool.State().ReturnsAt);
        Assert.Null(error);
        Assert.Contains(rig.Events, e => e is ProviderEvent.TurnCompleted { Status: Marrow.Core.Models.TurnStatus.Failed });
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T132")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_rate_limit_that_names_a_retry_after_keeps_that_key_out_for_that_long()
    {
        await using var rig = new KeyRotationRig(["429:7", KeyRotationRig.Ok]);
        Assert.Null(await rig.RunTurn());
        rig.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal("b", rig.Pool.ForThread(Guid.NewGuid()).Current()!.Id);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("a", rig.Pool.ForThread(Guid.NewGuid()).Current()!.Id);
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T133")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Another_error_status_is_not_retried_on_another_key()
    {
        await using var rig = new KeyRotationRig(["500"]);
        await rig.RunTurn();
        Assert.Single(rig.Requests);
        Assert.Empty(rig.Notices());
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T134")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Without_a_rotation_a_402_is_the_same_error_as_before_and_nothing_is_retried()
    {
        await using var rig = new KeyRotationRig(["402"], rotate: false);
        await rig.RunTurn();
        Assert.Equal(["Bearer sk-single"], rig.Requests.Select(r => r.Bearer).ToList());
        Assert.Empty(rig.Notices());
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T135")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_finished_request_costs_the_key_it_used_when_a_price_is_given()
    {
        await using var rig = new KeyRotationRig(["402", KeyRotationRig.Ok]);
        rig.Session.KeySpendEstimator = usage => usage.Total * 0.001;
        Assert.Null(await rig.RunTurn());
        Assert.Equal(0.1, rig.Spend.SpendSince("b", DateTimeOffset.MinValue), 6);
        Assert.Equal(0, rig.Spend.SpendSince("a", DateTimeOffset.MinValue));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T136")]
    [Trait("Rule", "R-TEST-4")]
    public async Task With_no_price_given_no_spend_is_recorded()
    {
        await using var rig = new KeyRotationRig([KeyRotationRig.Ok]);
        Assert.Null(await rig.RunTurn());
        Assert.Equal(0, rig.Spend.SpendSince("a", DateTimeOffset.MinValue));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T137")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_thread_pinned_to_a_key_goes_out_on_it_and_a_refusal_does_not_move_it()
    {
        await using var rig = new KeyRotationRig(["402"]);
        rig.Pool.Pin(KeyRotationRig.Thread, "c");
        await rig.RunTurn();
        Assert.Equal(["Bearer sk-c"], rig.Requests.Select(r => r.Bearer).ToList());
        Assert.DoesNotContain(rig.Notices(), n => n.StartsWith("Continued", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T153")]
    [Trait("Rule", "R-TEST-4")]
    public async Task By_default_a_402_does_not_retry_on_another_key_and_the_notice_asks_then_a_yes_lets_the_next_send_go_out_on_it()
    {
        await using var rig = new KeyRotationRig(["402", KeyRotationRig.Ok], autoMove: false);
        Assert.Null(await rig.RunTurn());
        Assert.Equal(["Bearer sk-a"], rig.Requests.Select(r => r.Bearer).ToList());
        var notice = Assert.Single(rig.Notices());
        Assert.Contains("Key A", notice, StringComparison.Ordinal);
        Assert.Contains("Key B", notice, StringComparison.Ordinal);

        rig.Session.KeyRotation!.AllowMoveOnce();
        await rig.SendAgain();
        Assert.Equal(["Bearer sk-a", "Bearer sk-b"], rig.Requests.Select(r => r.Bearer).ToList());
        Assert.Contains("Continued on Key B", rig.Notices());
    }

    [Fact]
    [Trait("Feature", "MA-6")]
    [Trait("Spec", "MA-T154")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_pinned_key_at_its_cap_sends_nothing_and_the_notice_offers_to_unpin_or_raise_the_cap()
    {
        await using var rig = new KeyRotationRig([KeyRotationRig.Ok]);
        rig.Store.Update("c", k => k with { MonthlyCapUsd = 1 });
        rig.Spend.Add("c", 1, rig.Clock.GetUtcNow());
        rig.Pool.Pin(KeyRotationRig.Thread, "c");
        await rig.RunTurn();
        Assert.Empty(rig.Requests);
        Assert.Contains("Key C reached its monthly cap. Unpin this thread or raise the cap.", rig.Notices());
        Assert.Contains(rig.Events, e => e is ProviderEvent.TurnCompleted { Status: Marrow.Core.Models.TurnStatus.Failed });
    }
}
