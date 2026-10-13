using Marrow.Core.Port.Assistant;
using Xunit.Abstractions;
using static Marrow.Core.Tests.Assistant.ReserveSimulation;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-13 (DESIGN.md rev 5, 11.5, 16 "AS-13"; ED-NEW-dextra-as13-5): the simulation. 10,000 seeded runs of up to 16 agents on
// one account whose plan-limit reading is a whole number and polled once a minute, with the owner's own use arriving in bursts the
// guard cannot see until the next poll, cross a floor in 0 runs. The world is ReserveSimulation.SimOptions; its assumptions are
// written in ED-NEW-dextra-as13-5. Event-driven and seeded: no clock, no threads, no Sleep.
public sealed class ReserveSimulationTests(ITestOutputHelper output)
{
    private const int Runs = 10_000;

    // The production world once for the class: the runs are seeded, so every test reads the same ten thousand.
    private static readonly Lazy<List<SimResult>> Production = new(() => All(new SimOptions()));

    private static List<SimResult> All(SimOptions options, int runs = Runs, int firstSeed = 1)
    {
        Assert.True(runs <= Runs, "a simulation is bounded");
        Assert.True(options.Seconds <= 7_200, "a run is bounded");
        return [.. Enumerable.Range(firstSeed, runs).Select(seed => Run(seed, options))];
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-5")]
    public void Ten_thousand_runs_of_up_to_16_agents_never_carry_nth_over_a_floor()
    {
        var results = Production.Value;
        var crossed = results.Where(r => r.Crossed).Take(5).Select(r => $"seed {r.Seed} over by {r.MaxOver:0.00} with {r.Agents} agents").ToList();
        Assert.Empty(crossed);

        // The runs exercise the guard: every size of team from 1 to 16, many turns sent, many asks held.
        Assert.Equal(Runs, results.Count);
        Assert.Equal(1, results.Min(r => r.Agents));
        Assert.Equal(16, results.Max(r => r.Agents));
        Assert.True(results.Average(r => r.Turns) > 30, "turns were sent");
        Assert.True(results.Average(r => r.Holds) > 100, "turns were held");
        output.WriteLine($"runs {results.Count}, mean agents {results.Average(r => r.Agents):0.0}, mean turns {results.Average(r => r.Turns):0.0}, mean holds {results.Average(r => r.Holds):0.0}, "
            + $"runs with a turn {results.Count(r => r.Turns > 0)}, ending within 2 of the floor {results.Count(r => r.EndUsed > r.Floor - 2)}");
        Assert.True(results.Count(r => r.Turns > 0) > 8_000, "most runs sent turns; the rest began within a turn of their floor");
        Assert.True(results.Count(r => r.EndUsed > r.Floor - 2) > 1_000, "the runs end close to their floors");
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-5")]
    public void The_bare_formula_of_11_5_alone_does_cross_so_the_two_allowances_are_needed_and_the_simulation_can_fail()
    {
        var bare = All(new SimOptions { Safety = ReserveSafety.Formula }, 4_000);
        var quantumOnly = All(new SimOptions { Safety = new ReserveSafety { NewTurnTail = 1.0 } }, 4_000);
        var tailOnly = All(new SimOptions { Safety = new ReserveSafety { QuantumAllowance = 0 } }, 4_000);
        output.WriteLine($"of 4000 runs: bare formula crosses in {bare.Count(r => r.Crossed)}, quantum only in {quantumOnly.Count(r => r.Crossed)}, tail only in {tailOnly.Count(r => r.Crossed)}");
        Assert.True(bare.Count(r => r.Crossed) >= 10, "the formula without the quantum and the tail allowances crosses in this world");
        Assert.Equal(0, quantumOnly.Count(r => r.Crossed)); // the quantum is what a whole-point reading needs
        Assert.True(tailOnly.Count(r => r.Crossed) >= 3, "a tail allowance cannot make up for a hidden point");
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-5")]
    public void The_guard_holds_in_a_wider_world_too()
    {
        // Rates off by 40%, a p90 30% too low, turns up to three times their p90, bigger and more frequent bursts.
        var wide = new SimOptions
        {
            RateError = 0.4,
            P90Error = (-0.3, 0.1),
            TurnTail = 3.0,
            BurstMaxPoints = 3.0,
            BurstEverySeconds = 60,
            Seconds = 3_600,
        };
        var results = All(wide, 3_000, firstSeed: 20_001);
        Assert.Equal(0, results.Count(r => r.Crossed));
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-5")]
    public void A_crossing_the_owners_own_use_explains_is_not_nth_s_and_is_counted_apart()
    {
        var results = Production.Value;
        var strict = results.Count(r => r.CrossedStrict);
        Assert.True(strict > 0, "the owner's bursts do carry usage over a floor now and then, between two polls");
        Assert.True(strict < 300, $"{strict} runs with the owner's own unseen use over a floor");
        Assert.True(results.Where(r => r.CrossedStrict).All(r => !r.Crossed), "each is explained by use nth could not see");
        // With no bursts there is nothing to explain: no run is over a floor for any reason.
        var quiet = All(new SimOptions { BurstMaxPoints = 0.2, BurstEverySeconds = 1e9 }, 4_000);
        Assert.Equal(0, quiet.Count(r => r.CrossedStrict));
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-5")]
    public void A_run_repeats_exactly_from_its_seed()
    {
        var a = Run(4242, new SimOptions());
        var b = Run(4242, new SimOptions());
        Assert.Equal(a, b);
        Assert.NotEqual(a, Run(4243, new SimOptions()));
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-5")]
    public void After_a_reading_shows_a_burst_no_turn_starts_that_would_cross_however_small()
    {
        var t0 = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
        var rate = new CalibratedRate(50_000, 40_000, 0.9, 8, true);
        var account = new ReserveAccount("claude:1", "Claude 20x", "claude", (_, _) => rate);
        var rule = new ReserveRule("r", ReserveScope.Every, ReserveLifetime.Standing) { KeepPercent = 30 };
        Func<ReserveWindow, ReserveFloor?> floors = _ => new ReserveFloor(70, rule, 70, false);
        account.Observe([new ReserveWindow("w", ReserveWindowKind.FiveHour, 55, t0, t0.AddHours(3))]);
        Assert.True(account.Check(floors, "m", 40_000, t0).Go);

        // The owner used 14 points of the window in the app between two polls.
        var seen = account.Observe([new ReserveWindow("w", ReserveWindowKind.FiveHour, 69, t0.AddSeconds(60), t0.AddHours(3))]);
        Assert.Equal(14, Assert.Single(seen).OutsidePoints, 6);
        Assert.False(account.Check(floors, "m", 1, t0.AddSeconds(61)).Go);       // a one-token turn would sit at 69 + 1 + 1 > 70
        Assert.False(account.Check(floors, "m", 0, t0.AddSeconds(61)).Go);       // and nothing new at all
    }
}
