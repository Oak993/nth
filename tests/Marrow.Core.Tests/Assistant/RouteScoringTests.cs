using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-14 (DESIGN.md rev 5, 11.6; ED-NEW-dextra-as14-1): the router's rules as functions of their inputs. An option is an
// account with a model; it is left out for a reason, or scored: the room under its tightest floor after the step's projection (a
// window that resets within 12 hours counts double), plus 20 points for a perfect fit of model to work. The uncalibrated and the
// unread come last, the owner's pill order breaks a tie, and a model the owner pinned is the only one ever chosen.
[Trait("Decision", "ED-NEW-dextra-as14-1")]
[Trait("Feature", "AS-14")]
public sealed class RouteScoringTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    // A window projected to `projected` with the floor at `floor`; resets in `hours` (default: three days, far from expiring).
    private static RouteWindow Window(double floor, double projected, double hours = 72, ReserveWindowKind kind = ReserveWindowKind.Weekly) =>
        new(kind + "-" + hours, kind, projected - 2, floor, projected, Now.AddHours(hours));

    private static RouteOption Option(string key, RouteWindow[] windows, string model = "opus", ModelTier tier = ModelTier.Strong, int rank = 0,
        bool usable = true, string? refusal = null, bool rough = false) =>
        new(key, key, ProviderKind.Claude, model, tier) { Windows = windows, Rank = rank, Usable = usable, Refusal = refusal, Rough = rough };

    private static RouteDecision Decide(RouteStep step, params RouteOption[] options) => RouteScoring.Decide(options, step, Now);

    private static readonly RouteStep Edit = new(StepWork.Edit);

    private static string[] Order(RouteDecision decision) => [.. decision.Ranked.Select(r => r.Option.AccountKey)];

    // ---- headroom ----

    [Fact]
    public void The_option_with_the_most_room_under_its_floor_wins_and_its_score_is_room_plus_fit()
    {
        var decision = Decide(Edit, Option("tight", [Window(70, 55)]), Option("roomy", [Window(70, 40)]));
        Assert.Equal(["roomy", "tight"], Order(decision));
        var best = decision.Best!;
        Assert.Equal(30, best.Headroom, 9);
        Assert.Equal(1, best.Fit, 9);
        Assert.Equal(50, best.Score, 9);
        Assert.False(best.Expiring);
        Assert.Empty(decision.Excluded);
    }

    [Fact]
    public void A_window_that_resets_within_12_hours_counts_double_so_allowance_that_would_be_lost_is_spent_first()
    {
        // 20 points of room that vanish in 6 hours (40) beat 30 points that stay for three days.
        var decision = Decide(Edit, Option("later", [Window(70, 40)]), Option("soon", [Window(70, 50, hours: 6)]));
        Assert.Equal(["soon", "later"], Order(decision));
        Assert.Equal(40, decision.Best!.Headroom, 9);
        Assert.True(decision.Best.Expiring);
        Assert.False(decision.Ranked[1].Expiring);
    }

    [Theory]
    [InlineData(12.0, 2.0)]
    [InlineData(11.99, 2.0)]
    [InlineData(12.01, 1.0)]
    [InlineData(0.01, 2.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(-3.0, 1.0)]
    public void Only_a_window_that_is_still_to_reset_within_12_hours_counts_double(double hours, double weight)
    {
        var window = Window(70, 50, hours);
        Assert.Equal(20 * weight, window.Weighted(Now), 9);
        Assert.Equal(weight == 2.0, window.Expiring(Now));
    }

    [Fact]
    public void The_tighter_window_binds_after_the_doubling()
    {
        // 5-hour: 20 room x2 = 40; weekly: 30 room x1 = 30. The weekly window binds.
        var a = Option("a", [Window(70, 50, hours: 3, kind: ReserveWindowKind.FiveHour), Window(70, 40)]);
        Assert.Equal(30, Decide(Edit, a).Best!.Headroom, 9);
        // The weekly window about to reset doubles too: 20 x2 = 40 and 15 x2 = 30; the 5-hour window binds at 30.
        var b = Option("b", [Window(70, 55, hours: 3, kind: ReserveWindowKind.FiveHour), Window(70, 50, hours: 8)]);
        Assert.Equal(30, Decide(Edit, b).Best!.Headroom, 9);
        // Without the soon-to-reset window an account is limited by its weekly room alone.
        var c = Option("c", [Window(70, 50, hours: 3, kind: ReserveWindowKind.FiveHour), Window(70, 60, hours: 72)]);
        Assert.Equal(10, Decide(Edit, c).Best!.Headroom, 9);
    }

    [Fact]
    public void A_window_of_another_kind_is_not_counted_and_an_option_with_no_counted_window_has_no_headroom()
    {
        var other = Option("other", [Window(70, 50, kind: ReserveWindowKind.Other), Window(70, 40)]);
        Assert.Equal(30, Decide(Edit, other).Best!.Headroom, 9);
        var onlyOther = Option("only-other", [Window(70, 50, kind: ReserveWindowKind.Other)]);
        Assert.Equal(0, Decide(Edit, onlyOther).Best!.Headroom, 9);
    }

    [Fact]
    public void A_window_with_no_reserve_scores_its_room_to_the_end_and_is_never_crossed_by_a_projection_over_100()
    {
        var open = Option("open", [Window(100, 40)]);
        Assert.Equal(60, Decide(Edit, open).Best!.Headroom, 9);
        var full = Option("full", [Window(100, 101.5)]);
        var decision = Decide(Edit, full);
        Assert.Equal(["full"], Order(decision));
        Assert.Equal(0, decision.Best!.Headroom, 9);
    }

    // ---- eligibility ----

    [Fact]
    public void A_projection_that_crosses_the_floor_leaves_the_option_out_and_one_exactly_at_it_stays()
    {
        var decision = Decide(Edit, Option("over", [Window(70, 70.5)]), Option("at", [Window(70, 70)]), Option("under", [Window(70, 60)]));
        Assert.Equal(["under", "at"], Order(decision));
        var left = Assert.Single(decision.Excluded);
        Assert.Equal(("over", RouteWhy.NoRoom), (left.Option.AccountKey, left.Why));
        // The tighter window binds: one window over its floor leaves the whole option out.
        var one = Decide(Edit, Option("two", [Window(70, 40), Window(80, 81, hours: 5, kind: ReserveWindowKind.FiveHour)]));
        Assert.Empty(one.Ranked);
        Assert.Equal(RouteWhy.NoRoom, Assert.Single(one.Excluded).Why);
    }

    [Fact]
    public void Each_reason_to_leave_an_option_out_is_named_and_the_first_that_applies_is_the_one_given()
    {
        var windows = new[] { Window(70, 40) };
        var decision = Decide(Edit,
            Option("out", windows, usable: false, refusal: "No."),
            Option("unsealable", windows, refusal: "Gemini cannot run mission work yet."),
            Option("light", windows, model: "haiku", tier: ModelTier.Light),
            Option("full", [Window(70, 90)], usable: true),
            Option("fine", windows));
        Assert.Equal(["fine"], Order(decision));
        var why = decision.Excluded.ToDictionary(e => e.Option.AccountKey, e => e.Why);
        Assert.Equal(RouteWhy.Unusable, why["out"]);
        Assert.Equal(RouteWhy.Unsealable, why["unsealable"]);
        Assert.Equal(RouteWhy.ModelCannotDoStep, why["light"]);
        Assert.Equal(RouteWhy.NoRoom, why["full"]);
        Assert.Equal(4, decision.Excluded.Count);
    }

    [Fact]
    public void No_options_gives_an_empty_decision_with_no_best()
    {
        var decision = Decide(Edit);
        Assert.Empty(decision.Ranked);
        Assert.Empty(decision.Excluded);
        Assert.Null(decision.Best);
    }

    // ---- model fit ----

    [Theory]
    [InlineData(StepWork.Search, ModelTier.Light, true, 1.0)]
    [InlineData(StepWork.Search, ModelTier.Standard, true, 0.5)]
    [InlineData(StepWork.Search, ModelTier.Strong, true, 0.0)]
    [InlineData(StepWork.Read, ModelTier.Light, true, 1.0)]
    [InlineData(StepWork.Read, ModelTier.Strong, true, 0.0)]
    [InlineData(StepWork.Reason, ModelTier.Light, false, 0.0)]
    [InlineData(StepWork.Reason, ModelTier.Standard, true, 0.5)]
    [InlineData(StepWork.Reason, ModelTier.Strong, true, 1.0)]
    [InlineData(StepWork.Edit, ModelTier.Light, false, 0.0)]
    [InlineData(StepWork.Edit, ModelTier.Standard, true, 0.5)]
    [InlineData(StepWork.Edit, ModelTier.Strong, true, 1.0)]
    [InlineData(StepWork.Review, ModelTier.Light, false, 0.0)]
    [InlineData(StepWork.Review, ModelTier.Standard, true, 1.0)]
    [InlineData(StepWork.Review, ModelTier.Strong, true, 0.5)]
    public void Search_and_reading_fit_cheap_models_and_reasoning_and_edits_strong_ones(StepWork work, ModelTier tier, bool canDo, double fit)
    {
        Assert.Equal(canDo, RouteScoring.CanDo(work, tier));
        Assert.Equal(fit, RouteScoring.Fit(work, tier), 9);
    }

    [Fact]
    public void A_perfect_fit_is_worth_20_points_of_headroom()
    {
        var windows = new[] { Window(70, 50) };       // 20 room
        var strong = Option("strong", windows, tier: ModelTier.Strong, rank: 1);
        var standard = Option("standard", [Window(70, 39)], model: "sonnet", tier: ModelTier.Standard);   // 31 room + 10 for half a fit
        Assert.Equal(["standard", "strong"], Order(Decide(Edit, strong, standard)));
        Assert.Equal(41, Decide(Edit, strong, standard).Best!.Score, 9);
        // 30 room + 10 = 40 against 20 + 20 = 40: a tie, and the pill order (the standard model's account comes first) decides.
        var tied = Option("tied", [Window(70, 40)], model: "sonnet", tier: ModelTier.Standard);
        var decision = Decide(Edit, strong, tied);
        Assert.Equal(["tied", "strong"], Order(decision));
        Assert.Equal(decision.Ranked[0].Score, decision.Ranked[1].Score, 9);
    }

    [Fact]
    public void A_search_goes_to_the_cheap_model_unless_a_strong_one_has_20_points_more_room()
    {
        var search = new RouteStep(StepWork.Search);
        var cheap = Option("cheap", [Window(70, 60)], model: "haiku", tier: ModelTier.Light);       // 10 room + 20
        var strong = Option("strong", [Window(70, 41)], tier: ModelTier.Strong);                    // 29 room + 0
        Assert.Equal(["cheap", "strong"], Order(Decide(search, cheap, strong)));
        var strongRoomier = Option("strong", [Window(70, 39)], tier: ModelTier.Strong);              // 31 room + 0
        Assert.Equal(["strong", "cheap"], Order(Decide(search, cheap, strongRoomier)));
    }

    // ---- the owner's pill order, and last place ----

    [Fact]
    public void The_pill_order_breaks_a_tie_and_only_a_tie()
    {
        var first = Option("first", [Window(70, 50)], rank: 0);
        var second = Option("second", [Window(70, 50)], rank: 1);
        Assert.Equal(["first", "second"], Order(Decide(Edit, first, second)));
        Assert.Equal(["first", "second"], Order(Decide(Edit, second, first)));
        // Half a point more room outweighs a better place in the pill.
        var roomier = Option("roomier", [Window(70, 49.5)], rank: 5);
        Assert.Equal(["roomier", "first", "second"], Order(Decide(Edit, first, second, roomier)));
    }

    [Fact]
    public void Scores_that_differ_only_by_rounding_noise_are_a_tie_and_the_order_never_depends_on_the_input_order()
    {
        var a = Option("a", [Window(70, 50)], rank: 2);
        var b = Option("b", [Window(70, 50 + 1e-12)], rank: 1);
        var c = Option("c", [Window(70, 50)], rank: 1);
        var expected = new[] { "b", "c", "a" };      // equal scores: rank 1 before rank 2, then by key
        var all = new[] { a, b, c };
        int[][] permutations = [[0, 1, 2], [2, 1, 0], [1, 2, 0], [0, 2, 1]];
        foreach (var permutation in permutations)
            Assert.Equal(expected, Order(Decide(Edit, [.. permutation.Select(i => all[i])])));
    }

    [Fact]
    public void An_account_not_calibrated_or_not_read_is_used_last_however_much_room_it_seems_to_have()
    {
        var proven = Option("proven", [Window(70, 65)]);                                // 5 room
        var rough = Option("rough", [Window(70, 10)], rough: true);                     // 60 room, but a conservative default
        var unread = Option("unread", []);
        var decision = Decide(Edit, unread, rough, proven);
        Assert.Equal(["proven", "rough", "unread"], Order(decision));
        // Used when nothing else can be.
        Assert.Equal(["rough"], Order(Decide(Edit, rough)));
        Assert.Equal(["unread"], Order(Decide(Edit, unread)));
        Assert.True(unread.Unread);
        Assert.False(proven.Unread);
    }

    [Fact]
    public void A_model_to_avoid_comes_after_every_other_model_and_is_still_chosen_when_it_is_all_there_is()
    {
        var review = new RouteStep(StepWork.Review) { AvoidModel = "SONNET" };
        var same = Option("same", [Window(70, 30)], model: "sonnet", tier: ModelTier.Standard);
        var other = Option("other", [Window(70, 60)], model: "opus", tier: ModelTier.Strong);
        Assert.Equal(["other", "same"], Order(Decide(review, same, other)));
        Assert.Equal(["same"], Order(Decide(review, same)));
    }

    // ---- pinned models ----

    [Fact]
    public void A_pinned_model_is_the_only_one_ever_chosen_and_its_accounts_are_ranked_among_themselves()
    {
        var step = new RouteStep(StepWork.Edit) { Pinned = new(["Opus"]) };
        var pinnedFull = Option("pinned-full", [Window(70, 80)], model: "opus");
        var pinnedTight = Option("pinned-tight", [Window(70, 60)], model: "opus");
        var other = Option("other", [Window(70, 10)], model: "sonnet", tier: ModelTier.Standard);
        var decision = Decide(step, pinnedFull, other, pinnedTight);
        Assert.Equal(["pinned-tight"], Order(decision));
        var why = decision.Excluded.ToDictionary(e => e.Option.AccountKey, e => e.Why);
        Assert.Equal(RouteWhy.NoRoom, why["pinned-full"]);
        Assert.Equal(RouteWhy.NotPinned, why["other"]);
    }

    [Fact]
    public void When_every_account_of_a_pinned_model_is_at_its_reserve_nothing_is_chosen_and_no_other_model_takes_its_place()
    {
        var step = new RouteStep(StepWork.Edit) { Pinned = new(["opus"]) };
        var decision = Decide(step, Option("a", [Window(70, 80)], model: "opus"), Option("b", [Window(70, 10)], model: "sonnet", tier: ModelTier.Standard));
        Assert.Empty(decision.Ranked);
        Assert.Null(decision.Best);
        Assert.Equal(2, decision.Excluded.Count);
    }

    [Fact]
    public void Several_pinned_models_are_all_allowed_and_a_pin_overrides_what_a_tier_can_do()
    {
        var step = new RouteStep(StepWork.Edit) { Pinned = new(["haiku", "opus"]) };
        var light = Option("light", [Window(70, 30)], model: "haiku", tier: ModelTier.Light);
        var strong = Option("strong", [Window(70, 60)], model: "opus");
        var sonnet = Option("sonnet", [Window(70, 10)], model: "sonnet", tier: ModelTier.Standard);
        var decision = Decide(step, sonnet, strong, light);
        Assert.Equal(["light", "strong"], Order(decision));
        Assert.Equal(RouteWhy.NotPinned, Assert.Single(decision.Excluded).Why);
    }

    [Fact]
    public void An_option_with_no_model_is_never_pinned()
    {
        var step = new RouteStep(StepWork.Edit) { Pinned = new(["opus"]) };
        var none = new RouteOption("none", "none", ProviderKind.Claude, null, ModelTier.Standard) { Windows = [Window(70, 30)] };
        Assert.Empty(Decide(step, none).Ranked);
        Assert.Equal(["none"], Order(Decide(Edit, none)));
    }
}
