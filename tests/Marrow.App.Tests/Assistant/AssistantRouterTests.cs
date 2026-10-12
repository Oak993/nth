using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using static Marrow.App.Tests.Fixtures.RouterRig;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-14 (DESIGN.md rev 5, 3.4, 11.5, 11.6, 15.4; ED-NEW-dextra-as14-5 to -8): the router over the reserve guard and fake
// chats. For a step it lists every account with the models worth trying, asks the guard where each stands (the projection that
// gates a turn, against the floors that bind), and ranks them: room under the floor (allowance that resets within 12 hours counts
// double), model fit, the owner's pill order. Every account's rate in the rig is 100,000 tokens to the point; a new turn costs 2.19
// points at its p90 (131,623 tokens at the 90,000 pessimistic rate, x 1.5), the margin 1, and a whole-number reading adds a point.
[Trait("Decision", "ED-NEW-dextra-as14-5")]
[Trait("Feature", "AS-14")]
public sealed class AssistantRouterTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private RouterRig Rig(bool claudeB = false) => new(temp.Folder, claudeB);

    // ---- room under the floor ----

    [Fact]
    public void An_edit_goes_to_the_account_with_the_most_room_under_its_floor_on_the_strongest_model_that_fits()
    {
        using var rig = Rig();
        rig.Reserve.Always(30);                                  // Claude 20x: the floor is 70
        rig.Read(ReserveRig.Claude20, 40);                       // 40 + 2.19 + 1 + 1 = 44.2 projected: 25.8 of room
        rig.Read(ReserveRig.CodexA, 20);                         // no reserve: 100 - 24.2 = 75.8 of room
        var decision = rig.Router.Decide(rig.Request(StepWork.Edit));
        Assert.Equal(["codex:a/astra", "codex:a/sol", "claude:20x/opus", "claude:20x/sonnet"], Order(decision));
        Assert.Equal(151.6, decision.Best!.Headroom, 1);         // 75.8 x 2: the window resets in 3 hours
        var pick = rig.Router.Pick(rig.Request(StepWork.Edit))!;
        Assert.Equal((RouterRig.CodexId, "astra", ProviderKind.Codex), (pick.AccountId, pick.Model, pick.Provider));
        Assert.Equal(ReserveRig.CodexA, pick.Account);
        Assert.Equal(new PlanStep(ReserveRig.CodexA, "astra", 50_000, 80_000), pick.ToPlanStep(50_000, 80_000));
        // The light model cannot edit, so it is left out.
        Assert.Equal(RouteWhy.ModelCannotDoStep, Assert.Single(Left(decision, "claude:20x"), e => e.Option.Model == "haiku").Why);
        Assert.Equal(RouteWhy.ModelCannotDoStep, Assert.Single(Left(decision, "claude:20x")).Why);
    }

    [Fact]
    public void An_account_whose_floor_a_turn_would_cross_is_left_out_and_the_gate_would_have_held_it_too()
    {
        using var rig = Rig();
        rig.Reserve.Always(30);
        rig.Read(ReserveRig.Claude20, 68);                       // 68 + 2.19 + 1 + 1 = 72.2 > 70
        rig.Read(ReserveRig.CodexA, 20);
        var decision = rig.Router.Decide(rig.Request(StepWork.Edit));
        Assert.All(decision.Ranked, r => Assert.Equal("codex:a", r.Option.AccountKey));
        Assert.Equal([RouteWhy.ModelCannotDoStep, RouteWhy.NoRoom, RouteWhy.NoRoom], Left(decision, "claude:20x").Select(e => e.Why));

        // What the router leaves out, the gate holds; what it picks, the gate lets go.
        rig.Reserve.DefaultAccount = ReserveRig.CodexA;
        var good = rig.Missions.Started(rig.Missions.Request("On the pick", ProviderKind.Codex));
        Assert.Equal(MissionState.Working, good.Mission.State);
        Assert.Null(rig.Reserve.Card(good));
        rig.Reserve.DefaultAccount = ReserveRig.Claude20;
        var held = rig.Missions.Started(rig.Missions.Request("On the one left out"));
        Assert.NotNull(rig.Reserve.Card(held));
    }

    [Fact]
    public void Allowance_that_resets_within_12_hours_is_spent_first_even_when_the_other_account_has_more_room()
    {
        using var rig = Rig();
        // A weekly window has no calibration yet, so a turn costs 0.33 of a point at the conservative rate: 60 + 0.33 + 1 + 1 = 62.3.
        rig.Read(ReserveRig.Claude20, 10, weekly: 60, weeklyHours: 6);    // weekly: 37.7 of room, gone in 6 hours: counts 75.3
        rig.Read(ReserveRig.CodexA, 10, weekly: 50, weeklyHours: 72);     // weekly: 47.7 of room, kept for 3 days: counts 47.7
        var soon = rig.Router.Decide(rig.Request(StepWork.Edit));
        Assert.Equal(["claude:20x/opus", "claude:20x/sonnet", "codex:a/astra", "codex:a/sol"], Order(soon));
        Assert.True(soon.Best!.Expiring);
        Assert.Equal(75.3, soon.Best.Headroom, 1);
        Assert.Equal(47.7, soon.Ranked[2].Headroom, 1);

        // The same readings with the weekly window of both far from its reset: the account with more room wins.
        rig.Read(ReserveRig.Claude20, 10, weekly: 60, weeklyHours: 72);
        Assert.Equal(["codex:a/astra", "claude:20x/opus", "codex:a/sol", "claude:20x/sonnet"], Order(rig.Router.Decide(rig.Request(StepWork.Edit))));
    }

    // ---- search and reading go to cheap models ----

    [Fact]
    public void Reading_goes_to_the_light_model_where_an_edit_goes_to_the_strong_one_on_the_same_account()
    {
        using var rig = Rig();
        rig.Read(ReserveRig.Claude20, 30);
        rig.Accounts.RemoveAll(a => a.Id == RouterRig.CodexId);
        Assert.Equal("haiku", rig.Router.Pick(rig.Request(StepWork.Read))!.Model);
        Assert.Equal("haiku", rig.Router.Pick(rig.Request(StepWork.Search))!.Model);
        Assert.Equal("opus", rig.Router.Pick(rig.Request(StepWork.Edit))!.Model);
        Assert.Equal("opus", rig.Router.Pick(rig.Request(StepWork.Reason))!.Model);
        Assert.Equal("sonnet", rig.Router.Pick(rig.Request(StepWork.Review))!.Model);
    }

    [Fact]
    public void A_provider_with_a_long_catalogue_is_tried_with_one_model_of_each_tier_and_one_that_lists_none_with_its_own_default()
    {
        using var rig = Rig();
        rig.Read(ReserveRig.Claude20, 30);
        rig.ModelsOf[ProviderKind.Claude] = [.. Enumerable.Range(0, 40).Select(i => new RouteModel("m" + i, ModelTier.Standard)), new("big", ModelTier.Strong, IsDefault: true), new("tiny", ModelTier.Light)];
        rig.Accounts.RemoveAll(a => a.Id == RouterRig.CodexId);
        Assert.Equal(["claude:20x/big", "claude:20x/m0"], Order(rig.Router.Decide(rig.Request(StepWork.Edit))));
        Assert.Equal(["claude:20x/tiny", "claude:20x/m0", "claude:20x/big"], Order(rig.Router.Decide(rig.Request(StepWork.Read))));
        rig.ModelsOf[ProviderKind.Claude] = [];
        var none = rig.Router.Pick(rig.Request(StepWork.Edit))!;
        Assert.Null(none.Model);                                  // the chat starts on the provider's default
        Assert.Equal(RouterRig.ClaudeId, none.AccountId);
    }

    // ---- a model the owner pinned ----

    [Fact]
    public void A_pinned_model_is_the_only_one_ever_chosen_and_when_its_accounts_have_no_room_nothing_takes_its_place()
    {
        using var rig = Rig();
        rig.Reserve.Always(30);
        rig.Read(ReserveRig.Claude20, 68);                       // opus has no room here
        rig.Read(ReserveRig.CodexA, 10);                         // Codex has plenty, but lists no opus
        var pinned = rig.Router.Decide(rig.Request(StepWork.Edit, "opus"));
        Assert.Empty(pinned.Ranked);
        var only = Assert.Single(pinned.Excluded);
        Assert.Equal(("claude:20x", "opus", RouteWhy.NoRoom), (only.Option.AccountKey, only.Option.Model, only.Why));
        Assert.Null(rig.Router.Pick(rig.Request(StepWork.Edit, "opus")));

        // Room again: the pin is honoured on its account, whatever Codex offers; a pin is matched without case.
        rig.Read(ReserveRig.Claude20, 30);
        var back = rig.Router.Pick(rig.Request(StepWork.Edit, "OPUS"))!;
        Assert.Equal((ReserveRig.Claude20, "opus"), (back.Account, back.Model));
        // A model the owner pinned may be a light one even for an edit; it is his choice.
        Assert.Equal("haiku", rig.Router.Pick(rig.Request(StepWork.Edit, "haiku"))!.Model);
        // A pin to a model only Codex lists goes to Codex.
        Assert.Equal(ReserveRig.CodexA, rig.Router.Pick(rig.Request(StepWork.Edit, "sol"))!.Account);
        // A pin no account lists leaves nothing to choose.
        Assert.Null(rig.Router.Pick(rig.Request(StepWork.Edit, "gpt-9")));
    }

    // ---- the owner's pill order ----

    [Fact]
    public void The_owners_pill_order_breaks_a_tie_between_accounts_and_only_a_tie()
    {
        using var rig = Rig();
        rig.Read(ReserveRig.Claude20, 30);
        rig.Read(ReserveRig.CodexA, 30);
        Assert.Equal(["claude:20x/opus", "codex:a/astra", "claude:20x/sonnet", "codex:a/sol"], Order(rig.Router.Decide(rig.Request(StepWork.Edit))));
        rig.Set(RouterRig.ClaudeId, a => a with { Rank = 5 });
        Assert.Equal(["codex:a/astra", "claude:20x/opus", "codex:a/sol", "claude:20x/sonnet"], Order(rig.Router.Decide(rig.Request(StepWork.Edit))));
        // A reading that is not a whole number leaves a point less to be careful about (no quantum): that is no tie.
        rig.Read(ReserveRig.Claude20, 29.5);
        Assert.Equal("claude:20x/opus", Order(rig.Router.Decide(rig.Request(StepWork.Edit)))[0]);
    }

    // ---- last place ----

    [Fact]
    public void An_account_not_calibrated_and_one_nobody_has_read_are_used_last_and_still_used_when_nothing_else_can_be()
    {
        using var rig = Rig(claudeB: true);
        rig.Read(ReserveRig.Claude20, 60);                       // 100 - 64.2: little room, but its rate is known
        rig.Read(ClaudeB, 5);                                    // lots of room, but the rate is the conservative default
        // Codex A is not read at all.
        var order = Order(rig.Router.Decide(rig.Request(StepWork.Edit)));
        Assert.Equal(["claude:20x/opus", "claude:20x/sonnet", "claude:b/opus", "claude:b/sonnet", "codex:a/astra", "codex:a/sol"], order);

        rig.Accounts.RemoveAll(a => a.Id != RouterRig.CodexId);
        Assert.Equal(["codex:a/astra", "codex:a/sol"], Order(rig.Router.Decide(rig.Request(StepWork.Edit))));
        Assert.Equal(RouterRig.CodexId, rig.Router.Pick(rig.Request(StepWork.Edit))!.AccountId);
    }

    [Fact]
    public void An_account_that_cannot_be_used_or_sealed_is_left_out_and_with_no_account_left_nothing_is_picked()
    {
        using var rig = Rig();
        rig.Read(ReserveRig.Claude20, 30);
        rig.Read(ReserveRig.CodexA, 30);
        rig.Refused[ProviderKind.Codex] = "Codex cannot run mission work yet.";
        rig.Set(RouterRig.ClaudeId, a => a with { Usable = false });
        var decision = rig.Router.Decide(rig.Request(StepWork.Edit));
        Assert.Empty(decision.Ranked);
        Assert.All(Left(decision, "claude:20x"), e => Assert.Equal(RouteWhy.Unusable, e.Why));
        Assert.All(Left(decision, "codex:a"), e => Assert.Equal(RouteWhy.Unsealable, e.Why));
        Assert.Null(rig.Router.Pick(rig.Request(StepWork.Edit)));
        Assert.Null(rig.Router.PickLead("dextra"));
        rig.Accounts.Clear();
        Assert.Null(rig.Router.Pick(rig.Request(StepWork.Edit)));
        Assert.Empty(rig.Router.Decide(rig.Request(StepWork.Edit)).Excluded);
    }

    [Fact]
    public void Only_the_accounts_of_the_provider_or_the_account_asked_for_are_considered()
    {
        using var rig = Rig(claudeB: true);
        rig.Read(ReserveRig.Claude20, 30);
        rig.Read(ReserveRig.CodexA, 10);
        rig.Read(ClaudeB, 30);
        var onlyClaude = rig.Request(StepWork.Edit) with { Provider = ProviderKind.Claude };
        Assert.All(rig.Router.Decide(onlyClaude).Ranked, r => Assert.StartsWith("claude:", r.Option.AccountKey, StringComparison.Ordinal));
        var onlyB = rig.Request(StepWork.Edit) with { AccountId = RouterRig.ClaudeBId };
        Assert.All(rig.Router.Decide(onlyB).Ranked, r => Assert.Equal("claude:b", r.Option.AccountKey));
        Assert.Equal(RouterRig.ClaudeBId, rig.Router.Pick(onlyB)!.AccountId);
    }

    // ---- the rules the owner spoke for a task, before it exists ----

    [Fact]
    public void A_task_rule_binds_the_options_of_a_task_that_has_not_started_yet_and_standing_rules_bind_every_task()
    {
        using var rig = Rig();
        rig.Read(ReserveRig.Claude20, 40);
        rig.Read(ReserveRig.CodexA, 40);
        var taskRule = new ReserveRule("t", ReserveScope.Named("Codex A"), ReserveLifetime.Task) { KeepPercent = 50, Basis = ReserveBasis.WholeWindow };   // Codex A: the floor is 50
        var withRule = rig.Router.Decide(rig.Request(StepWork.Edit) with { TaskRules = [taskRule] });
        // 40 + 2.19 + 1 + 1 = 44.2 stays under 50: Codex A still has room, 5.8 of it.
        Assert.Equal(5.8, withRule.Ranked.First(r => r.Option.AccountKey == "codex:a").Headroom / 2, 1);
        rig.Read(ReserveRig.CodexA, 48);
        var crossing = rig.Router.Decide(rig.Request(StepWork.Edit) with { TaskRules = [taskRule] });
        Assert.DoesNotContain(crossing.Ranked, r => r.Option.AccountKey == "codex:a");
        Assert.Contains(crossing.Excluded, e => e.Option.AccountKey == "codex:a" && e.Why == RouteWhy.NoRoom);
        // Without that rule the same reading has room: nothing was registered with the guard by asking.
        Assert.Contains(rig.Router.Decide(rig.Request(StepWork.Edit)).Ranked, r => r.Option.AccountKey == "codex:a");
        rig.Reserve.Always(60, "Codex A");
        Assert.DoesNotContain(rig.Router.Decide(rig.Request(StepWork.Edit)).Ranked, r => r.Option.AccountKey == "codex:a");
    }
}
