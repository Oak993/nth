using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

/// <summary>
/// A seeded, event-driven model of one account under a reserve (DESIGN.md 16, AS-13's simulation): up to 16 agents each asking
/// the guard before every turn, a plan-limit poll every 60 s whose reading is quantised to whole points, the owner's own use
/// arriving in bursts the guard cannot see until the next poll, and a calibrated rate that is a little off. It has no clock and
/// no threads: time is a number, so 10,000 runs take well under a second each hundred. The world's assumptions are the fields
/// of <see cref="SimOptions"/>; the ED entry ED-NEW-dextra-as13-5 states them in words.
/// </summary>
internal static class ReserveSimulation
{
    internal sealed record SimOptions
    {
        public int MaxAgents { get; init; } = 16;

        /// <summary>The run's length in seconds.</summary>
        public double Seconds { get; init; } = 2400;

        /// <summary>Readings are whole points: floored (the worst case: the truth can be a hair under the next point) or rounded.</summary>
        public bool FloorReadings { get; init; } = true;

        /// <summary>A turn never costs more than this many times its true p90 (a turn is bounded by its provider's turn limits).</summary>
        public double TurnTail { get; init; } = 1.5;

        /// <summary>The owner's bursts: mean seconds between them, and the most points one adds.</summary>
        public double BurstEverySeconds { get; init; } = 150;

        public double BurstMaxPoints { get; init; } = 1.0;

        /// <summary>The calibrated rate is off by up to this fraction either way.</summary>
        public double RateError { get; init; } = 0.2;

        /// <summary>The guard's p90 of a turn is off by this much (low, high) of the true p90.</summary>
        public (double Low, double High) P90Error { get; init; } = (-0.05, 0.15);

        public double PollSeconds { get; init; } = 60;

        public ReserveSafety Safety { get; init; } = ReserveSafety.Production;
    }

    /// <summary>
    /// <see cref="Crossed"/>: nth's spend carried the true usage over the floor even with the owner's use that nth could not yet
    /// have seen taken out. <see cref="CrossedStrict"/>: the true usage was over the floor at a spend of nth's, whatever the cause.
    /// </summary>
    internal sealed record SimResult(int Seed, bool Crossed, double MaxOver, bool CrossedStrict, double MaxOverStrict, int Agents, int Turns, int Holds, double Floor, double EndUsed);

    private enum Kind { Poll, Burst, Ask, Spend, End }

    private readonly record struct Ev(double At, long Order, Kind Kind, int Agent, int Index);

    internal static SimResult Run(int seed, SimOptions o)
    {
        var rng = new Random(seed);
        double Uni(double a, double b) => a + rng.NextDouble() * (b - a);
        double LogUni(double a, double b) => Math.Exp(Uni(Math.Log(a), Math.Log(b)));
        double Gauss() => Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
        double Exp(double mean) => -mean * Math.Log(1 - rng.NextDouble());

        var t0 = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
        var agents = 1 + rng.Next(o.MaxAgents);
        var floor = Uni(55, 92);
        var used = floor - Uni(3, 30);
        var tokensPerPoint = LogUni(20_000, 400_000);
        var rateError = Uni(-o.RateError, o.RateError);
        var guardMedian = tokensPerPoint * (1 + rateError);
        var guardRate = new CalibratedRate(guardMedian, guardMedian * 0.8, 0.8, 10, true);
        var median = LogUni(0.05, 1.0);                       // a turn's median cost in points
        const double sigma = 0.5;
        var trueP90 = median * Math.Exp(1.2816 * sigma);
        var cap = o.TurnTail * trueP90;
        var guardP90Tokens = trueP90 * tokensPerPoint * (1 + Uni(o.P90Error.Low, o.P90Error.High));
        var quantFloor = o.FloorReadings;

        var account = new ReserveAccount("claude:1", "Claude", "claude", (_, _) => guardRate, o.Safety);
        var rule = new ReserveRule("r", ReserveScope.Every, ReserveLifetime.Task) { KeepPercent = 30 };
        var guardFloor = new ReserveFloor(floor, rule, floor, false);
        Func<ReserveWindow, ReserveFloor?> floorOf = _ => guardFloor;

        var queue = new PriorityQueue<Ev, (double, long)>();
        long order = 0;
        void Add(double at, Kind kind, int agent = -1, int index = 0)
        {
            if (at > o.Seconds) return;
            queue.Enqueue(new Ev(at, order, kind, agent, index), (at, order));
            order++;
        }
        DateTimeOffset At(double s) => t0.AddSeconds(s);
        double Quant(double value) => quantFloor ? Math.Floor(value) : Math.Round(value);

        // One reading at the start, so the guard knows the window before any agent asks.
        account.Observe([new ReserveWindow("w", ReserveWindowKind.FiveHour, Quant(used), At(0), At(0).AddHours(3))]);
        Add(o.PollSeconds, Kind.Poll);
        Add(Exp(o.BurstEverySeconds), Kind.Burst);
        for (var a = 0; a < agents; a++) Add(Uni(0, 20), Kind.Ask, a);

        var turnCost = new double[agents];
        var turnDuration = new double[agents];
        var turnId = new Guid[agents];
        var turnSteps = new int[agents];
        var turns = 0;
        var holds = 0;
        var crossed = false;
        var maxOver = 0.0;
        var crossedStrict = false;
        var maxOverStrict = 0.0;
        var outsideCum = 0.0;                          // all the owner's use so far
        var outsideAtPoll = 0.0;                       // ... as of the last poll
        var blindFrom = new double[agents];            // ... as of the poll before each running turn was admitted
        var running = new bool[agents];

        while (queue.Count > 0)
        {
            var ev = queue.Dequeue();
            switch (ev.Kind)
            {
                case Kind.Poll:
                    outsideAtPoll = outsideCum;
                    account.Observe([new ReserveWindow("w", ReserveWindowKind.FiveHour, Quant(used), At(ev.At), At(ev.At).AddHours(3))]);
                    Add(ev.At + o.PollSeconds, Kind.Poll);
                    break;
                case Kind.Burst:
                    var burst = Uni(0.2, o.BurstMaxPoints);
                    used += burst;
                    outsideCum += burst;
                    Add(ev.At + Exp(o.BurstEverySeconds), Kind.Burst);
                    break;
                case Kind.Ask:
                {
                    var check = account.Check(floorOf, "m", guardP90Tokens, At(ev.At));
                    if (!check.Go)
                    {
                        holds++;
                        Add(ev.At + 20, Kind.Ask, ev.Agent);
                        break;
                    }
                    turns++;
                    blindFrom[ev.Agent] = outsideAtPoll;
                    running[ev.Agent] = true;
                    var cost = Math.Min(Math.Exp(Math.Log(median) + sigma * Gauss()), cap);
                    turnCost[ev.Agent] = cost;
                    turnDuration[ev.Agent] = Uni(10, 120);
                    turnSteps[ev.Agent] = Math.Max(1, (int)Math.Ceiling(turnDuration[ev.Agent] / 10));
                    turnId[ev.Agent] = Guid.NewGuid();
                    account.TurnStarted(turnId[ev.Agent], "m", guardP90Tokens);
                    for (var step = 1; step <= turnSteps[ev.Agent]; step++)
                        Add(ev.At + turnDuration[ev.Agent] * step / turnSteps[ev.Agent], step == turnSteps[ev.Agent] ? Kind.End : Kind.Spend, ev.Agent, step);
                    break;
                }
                case Kind.Spend:
                case Kind.End:
                {
                    var points = turnCost[ev.Agent] / turnSteps[ev.Agent];
                    used += points;
                    account.Spend(At(ev.At), (long)Math.Round(points * tokensPerPoint), "m", turnId[ev.Agent]);
                    if (used > floor)
                    {
                        crossedStrict = true;
                        maxOverStrict = Math.Max(maxOverStrict, used - floor);
                        // Outside use that arrived after the poll before the oldest running turn was admitted could not have been seen.
                        var oldest = double.MaxValue;
                        for (var a = 0; a < agents; a++) if (running[a]) oldest = Math.Min(oldest, blindFrom[a]);
                        var blind = outsideCum - oldest;
                        if (used - blind > floor)
                        {
                            crossed = true;
                            maxOver = Math.Max(maxOver, used - blind - floor);
                        }
                    }
                    if (ev.Kind == Kind.End)
                    {
                        running[ev.Agent] = false;
                        account.TurnEnded(turnId[ev.Agent]);
                        Add(ev.At + Uni(1, 15), Kind.Ask, ev.Agent);
                    }
                    break;
                }
            }
        }
        return new SimResult(seed, crossed, maxOver, crossedStrict, maxOverStrict, agents, turns, holds, floor, used);
    }
}
