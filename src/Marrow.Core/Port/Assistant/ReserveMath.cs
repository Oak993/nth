// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.3, 11.5; unit AS-13; ED-NEW-dextra-as13-3). No Swift twin.
// The arithmetic of a reserve, as functions of their inputs and nothing else: the floor of a rule, a floor easing near a
// reset, the estimate of an account's percent between polls, the margin, the outside use a new reading shows, and the
// plan line's start rules. No clock, no store: the caller passes the time in, so the tests and the simulation are exact.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

public static class ReserveMath
{
    /// <summary>"Never less than 1 point" (11.5).</summary>
    public const double MinMargin = 1.0;

    /// <summary>A reading higher than the estimate by more than this many points is outside use (11.5, "more than 2 points").</summary>
    public const double OutsideTolerance = 2.0;

    /// <summary>Actual spend more than this over the estimate re-plans (11.5, "more than 25% over").</summary>
    public const double OverEstimate = 0.25;

    /// <summary>A standing rule's 5-hour floor eases over the last 45 minutes before the reset (11.5).</summary>
    public static readonly TimeSpan FiveHourEase = TimeSpan.FromMinutes(45);

    /// <summary>A standing rule's weekly floor eases over the last 3 hours (11.5).</summary>
    public static readonly TimeSpan WeeklyEase = TimeSpan.FromHours(3);

    /// <summary>The over-5%-of-a-window rule: the owner taps Start (11.4).</summary>
    public const double StartTapPercent = 5.0;

    /// <summary>More than this many agents planned: the owner taps Start (11.4).</summary>
    public const int StartTapAgents = 8;

    /// <summary>API spend above this many dollars expected: the owner taps Start (11.4).</summary>
    public const decimal StartTapDollars = 2m;

    // ---- floors ----

    /// <summary>
    /// The floor of one rule: the highest percent used a task may reach. "Of what is left" measures from the percent used
    /// when the task began (22% used, keep 30% of what is left: 22 + 78 x 0.7 = 76.6); "of the whole window" is 100 minus
    /// what to keep. A dollar rule is a share of the API key's budget for the window.
    /// </summary>
    public static double FloorOf(ReserveRule rule, double usedAtStart, decimal? limitDollars = null)
    {
        var r = rule.Normalized();
        var start = double.IsNaN(usedAtStart) ? 100 : Math.Clamp(usedAtStart, 0, 100);
        if (r.KeepDollars is { } dollars)
        {
            // A dollar rule needs the key's budget; without it nothing can be said, so nothing is kept (the caller skips it).
            if (limitDollars is not { } limit || limit <= 0) return 100;
            return Math.Clamp(100.0 - (double)(dollars / limit) * 100.0, 0, 100);
        }
        return r.Basis == ReserveBasis.WhatIsLeft
            ? start + (100 - start) * (1 - r.KeepPercent / 100.0)
            : 100 - r.KeepPercent;
    }

    /// <summary>How long a standing floor eases for a window kind, or null for a window that does not ease.</summary>
    public static TimeSpan? EaseSpan(ReserveWindowKind kind) => kind switch
    {
        ReserveWindowKind.FiveHour => FiveHourEase,
        ReserveWindowKind.Weekly => WeeklyEase,
        _ => null,
    };

    /// <summary>
    /// A floor eased toward the reset (11.5 "Floors near a reset"): unchanged until the last <see cref="EaseSpan"/> before
    /// the reset, then rising in a straight line to 100 (no floor) at the reset. A window that has no reset time, or does not
    /// ease, keeps its floor.
    /// </summary>
    public static double Eased(double floor, ReserveWindowKind kind, DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (EaseSpan(kind) is not { } span || resetsAt is not { } reset) return floor;
        var remaining = reset - now;
        if (remaining >= span) return floor;
        if (remaining <= TimeSpan.Zero) return 100;
        var progress = 1 - remaining / span;
        return floor + (100 - floor) * progress;
    }

    /// <summary>
    /// The floor that binds a window, or null when no rule applies. Rules are read through <see cref="ReserveRule.Normalized"/>;
    /// a standing rule's floor eases unless the owner holds reserves until the reset; a task rule is kept exactly. The
    /// lowest floor wins (the tighter rule binds), a task rule on a tie. <paramref name="allowance"/> is what the owner
    /// agreed to use of the reserve this once (the card's third option).
    /// </summary>
    public static ReserveFloor? EffectiveFloor(IEnumerable<ReserveFloorInput> rules, ReserveWindow window, DateTimeOffset now, bool holdReserves = false, double allowance = 0)
    {
        ReserveFloor? best = null;
        foreach (var input in rules)
        {
            var rule = input.Rule.Normalized();
            if (!rule.AppliesTo(window.Kind)) continue;
            if (rule.KeepDollars is not null && window.LimitDollars is not > 0) continue;
            var plain = FloorOf(rule, input.UsedAtStart, window.LimitDollars);
            var floor = rule.Lifetime == ReserveLifetime.Standing && !holdReserves ? Eased(plain, window.Kind, window.ResetsAt, now) : plain;
            if (best is null || floor < best.Percent || (floor == best.Percent && rule.Lifetime == ReserveLifetime.Task))
                best = new ReserveFloor(floor, rule, plain, floor > plain);
        }
        if (best is null) return null;
        return allowance > 0 ? best with { Percent = Math.Min(100, best.Percent + allowance) } : best;
    }

    // ---- the estimate and the margin ----

    /// <summary>
    /// The current percent between polls (11.5): the last reading plus the tokens the ledger saw since, converted with the
    /// calibrated rate and rounded up to a whole point. A new reading replaces it. A rate that is not a positive number gives
    /// 100 (fail closed).
    /// </summary>
    public static double Estimate(double lastReading, double tokensSince, double tokensPerPercent)
    {
        if (tokensSince <= 0) return lastReading;
        if (!(tokensPerPercent > 0) || !double.IsFinite(tokensPerPercent)) return 100;
        return EstimateFromPoints(lastReading, tokensSince / tokensPerPercent);
    }

    /// <summary>The same estimate when the tokens since the reading were already converted (several models, several rates).</summary>
    public static double EstimateFromPoints(double lastReading, double pointsSince) =>
        pointsSince <= 0 ? lastReading : double.IsFinite(pointsSince) ? Math.Ceiling(lastReading + pointsSince - 1e-9) : 100;

    /// <summary>
    /// The margin (11.5): the p90 of every turn already in flight on the account, across all missions (up to 16 agents), plus
    /// the seconds since the last reading times the account's recent burn rate per second; never less than 1 point.
    /// </summary>
    public static double Margin(IEnumerable<double> inFlightP90Points, TimeSpan sinceReading, double burnPercentPerSecond)
    {
        var inFlight = 0.0;
        foreach (var p90 in inFlightP90Points) inFlight += Math.Max(0, p90);
        var stale = Math.Max(0, sinceReading.TotalSeconds) * Math.Max(0, burnPercentPerSecond);
        var margin = inFlight + stale;
        return double.IsFinite(margin) ? Math.Max(MinMargin, margin) : 100;
    }

    /// <summary>The projection before a turn is sent: the estimate, this turn's p90 and the margin (11.5).</summary>
    public static double Projected(double estimate, double turnP90Points, double margin) => estimate + Math.Max(0, turnP90Points) + margin;

    /// <summary>A projection crosses a floor when it is over it. A floor of 100 or more is "no reserve" and is never crossed.</summary>
    public static bool Crosses(double projected, double floor) => floor < 100 && projected > floor;

    /// <summary>
    /// What a new reading shows about the owner's own use (11.5): the points by which it beats the estimate, when that is more
    /// than <see cref="OutsideTolerance"/>; else 0. The reading is the truth, so this only decides what is reported.
    /// </summary>
    public static double OutsideUse(double reading, double estimate) => reading - estimate > OutsideTolerance ? reading - estimate : 0;

    /// <summary>Whether actual spend is far enough over the estimate to re-plan (11.5).</summary>
    public static bool RunsOver(double actualTokens, double estimatedTokens) =>
        estimatedTokens > 0 && actualTokens > estimatedTokens * (1 + OverEstimate);

    /// <summary>
    /// How many whole points of the reserve a turn that would otherwise cross needs: the projection beyond the floor, rounded
    /// up, at least 1 ("Use 4% of the reserve this once").
    /// </summary>
    public static double AllowanceNeeded(double projected, double floor) => Math.Max(1, Math.Ceiling(projected - floor - 1e-9));

    // ---- words ----

    /// <summary>A percent as the owner reads it: "68.6%", "70%". Whole numbers show no decimal.</summary>
    public static string Percent(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
