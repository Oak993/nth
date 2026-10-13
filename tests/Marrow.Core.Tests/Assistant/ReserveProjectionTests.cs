using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-14 (DESIGN.md rev 5, 11.5, 11.6; ED-NEW-dextra-as14-5): the router asks an account where every one of its windows stands for a
// turn that has not been sent, whether or not a floor applies. It is the projection Check makes, per window, so the router can never choose
// an account the gate would then refuse. The rate is 50,000 tokens to the point (40,000 at its p90) for every model, as in AS-13's tests.
[Trait("Decision", "ED-NEW-dextra-as14-5")]
[Trait("Feature", "AS-14")]
public sealed class ReserveProjectionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly CalibratedRate Rate = new(50_000, 40_000, 0.9, 8, true);

    private static ReserveAccount Account(ReserveSafety? safety = null) => new("claude:1", "Claude 20x", "claude", (_, _) => Rate, safety ?? ReserveSafety.Formula);

    private static ReserveWindow W(double percent, DateTimeOffset at, ReserveWindowKind kind = ReserveWindowKind.FiveHour) =>
        new(kind.ToString(), kind, percent, at, at.AddHours(kind == ReserveWindowKind.FiveHour ? 3 : 72));

    private static readonly ReserveRule AnyRule = new("r", ReserveScope.Every, ReserveLifetime.Standing) { KeepPercent = 30 };

    private static Func<ReserveWindow, ReserveFloor?> Floors(double fiveHour) => window =>
        window.Kind == ReserveWindowKind.FiveHour ? new ReserveFloor(fiveHour, AnyRule, fiveHour, false) : null;

    [Fact]
    public void Every_window_is_projected_the_way_the_check_projects_it_and_a_window_with_no_floor_is_projected_too()
    {
        var account = Account();
        account.Observe([W(60, T0), W(40, T0, ReserveWindowKind.Weekly)]);
        account.Spend(T0.AddSeconds(10), 100_000, "m");                       // 2 points on each window at the same rate
        var now = T0.AddSeconds(20);

        var projections = account.Projections("m", 200_000, now);              // a turn of p90 5 points
        Assert.Equal(["FiveHour", "Weekly"], projections.Select(p => p.Window.Id).Order(StringComparer.Ordinal));
        var five = projections.Single(p => p.Window.Kind == ReserveWindowKind.FiveHour);
        var week = projections.Single(p => p.Window.Kind == ReserveWindowKind.Weekly);
        Assert.Equal(62, five.Estimate);
        Assert.Equal(5, five.TurnP90, 9);
        Assert.Equal(ReserveMath.MinMargin, five.Margin, 9);
        Assert.Equal(0, five.Quantum);
        Assert.Equal(62 + 5 + 1, five.Projected, 9);
        Assert.Equal(42 + 5 + 1, week.Projected, 9);

        // Check, for the window a floor applies to, says the same numbers.
        var check = account.Check(Floors(70), "m", 200_000, now);
        var binding = Assert.IsType<ReserveWindowCheck>(check.Binding);
        Assert.Equal((five.Estimate, five.TurnP90, five.Margin, five.Quantum, five.Projected), (binding.Estimate, binding.TurnP90, binding.Margin, binding.Quantum, binding.Projected));
    }

    [Fact]
    public void The_allowances_the_gate_adds_are_in_the_projection_and_a_turn_of_nothing_counts_only_what_turns_in_flight_have_left()
    {
        var account = Account(ReserveSafety.Production);
        account.Observe([W(60, T0)]);                                          // a whole number: a point under the truth is possible
        var whole = Assert.Single(account.Projections("m", 200_000, T0));
        Assert.Equal(1, whole.Quantum);
        Assert.Equal(5 * 1.5, whole.TurnP90, 9);                               // the new turn counted at 1.5 times its p90
        Assert.Equal(60 + 7.5 + 1 + 1, whole.Projected, 9);

        account.Observe([W(60.5, T0.AddSeconds(60))]);
        Assert.Equal(0, Assert.Single(account.Projections("m", 200_000, T0.AddSeconds(60))).Quantum);

        // No new turn: the margin is what the turns in flight still have to spend (a p90 of 160,000 tokens: 4 points, none spent yet).
        account.TurnStarted(Guid.NewGuid(), "m", 160_000);
        var between = Assert.Single(account.Projections("m", 0, T0.AddSeconds(60)));
        Assert.Equal(0, between.TurnP90);
        Assert.Equal(4, between.Margin, 9);
    }

    [Fact]
    public void An_account_nobody_has_read_has_no_windows_to_project()
    {
        Assert.Empty(Account().Projections("m", 100_000, T0));
    }
}
