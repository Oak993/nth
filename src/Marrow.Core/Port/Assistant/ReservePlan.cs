// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.3, 11.4, 11.5; unit AS-13; ED-NEW-dextra-as13-8). No Swift twin.
// The plan the owner sees before a big task starts, and the report he gets after it: one line ("About 12% of Codex A, 8% of
// Claude B and up to 6% of Claude 20x. Your 30% reserve on Claude 20x stays."), the reading of his rule, one bar per account
// (used, planned, floor), and the four reasons the owner must tap Start. And the after-report: what each account used against
// the plan, the use outside nth on its own, and the state of each reserve. Pure data and words; ReserveGuard builds them.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

/// <summary>One account's bar in a plan: where its binding window stands, what the task is planned to use, and the floor.</summary>
public sealed record PlanAccountLine(string AccountKey, string Name, ReserveWindowKind Kind, string WindowTitle, double Used, double PlannedP50, double PlannedP90, double? Floor, bool Rough)
{
    /// <summary>Points under the floor after the task's p90, or null when no reserve applies. Negative: the plan would cross it.</summary>
    public double? Room => Floor is { } floor && floor < 100 ? floor - (Used + PlannedP90) : null;

    /// <summary>The window's name for the bar's label: "weekly", "5-hour window".</summary>
    public string WindowLabel => Kind switch
    {
        ReserveWindowKind.FiveHour => "5-hour window",
        ReserveWindowKind.Weekly => "weekly",
        ReserveWindowKind.Day => "today",
        ReserveWindowKind.Month => "this month",
        _ => "window",
    };
}

/// <summary>A reserve the plan keeps, in words, for the plan's last sentence.</summary>
public sealed record PlanReserve(string AccountName, double KeepPercent, ReserveLifetime Lifetime);

/// <summary>The plan (11.4).</summary>
public sealed record ReservePlan(IReadOnlyList<PlanAccountLine> Accounts)
{
    /// <summary>How the owner's rule was read ("keep 30% of what's left on each account"), stated so a wrong reading is visible (11.3).</summary>
    public string Reading { get; init; } = "";

    public IReadOnlyList<PlanReserve> Reserves { get; init; } = [];

    /// <summary>"Hydra team of 4", or empty.</summary>
    public string Team { get; init; } = "";

    public TimeSpan? Duration { get; init; }

    public int Agents { get; init; }

    /// <summary>API spend expected, in dollars.</summary>
    public decimal ApiDollars { get; init; }

    /// <summary>The "Always ask before starting" setting.</summary>
    public bool AlwaysAsk { get; init; }

    /// <summary>Where the agents run (AS-15): the swarm gate reads a cloud or API fan-out from it. Empty: not known, so no fan-out is claimed.</summary>
    public IReadOnlyList<SwarmLane> Lanes { get; init; } = [];

    /// <summary>The tokens the plan expects to spend, p50 and p90, over every step (AS-15: the swarm gate's cost).</summary>
    public long TokensP50 { get; init; }

    public long TokensP90 { get; init; }

    /// <summary>What the swarm gate shows (13.2): agents, time, tokens or dollars and who pays; null when the gate does not apply.</summary>
    public SwarmFacts? Swarm => SwarmGate.Facts(this);

    /// <summary>The one-line plan (11.4).</summary>
    public string Line => ReservePlanWords.Line(this);

    /// <summary>The line under it: the reading, the team, the time and whether Start needs the owner.</summary>
    public string Details => ReservePlanWords.Details(this);

    /// <summary>Why the owner must tap Start; empty when the plan line shows and the mission starts (11.4).</summary>
    public IReadOnlyList<string> StartNeeds => ReservePlanWords.StartNeeds(this);

    public bool NeedsStartTap => StartNeeds.Count > 0;

    /// <summary>True when any account's plan reaches its floor (the p90 is checked against every floor).</summary>
    public bool CrossesAFloor => Accounts.Any(a => a.Room is < 0);
}

/// <summary>What one account used on a task against its plan (the after-report's table).</summary>
public sealed record ReportRow(string AccountName, double PlannedPercent, double UsedPercent, string WindowLabel);

/// <summary>Use outside nth found while a task ran, listed apart from the task's own (11.5).</summary>
public sealed record ReportOutside(string AccountName, string WindowLabel, double Points, DateTimeOffset Since);

/// <summary>A reserve's state at the end: intact, or by how much it was dipped into, and whether the owner agreed.</summary>
public sealed record ReportReserve(string AccountName, string Rule, double DippedBy, bool UsedOnce)
{
    public bool Intact => DippedBy <= 0;
}

/// <summary>The after-report (11.5 "After the mission").</summary>
public sealed record ReserveAfterReport(string MissionTitle, IReadOnlyList<ReportRow> Rows, IReadOnlyList<ReportOutside> Outside, IReadOnlyList<ReportReserve> Reserves)
{
    /// <summary>"Every reserve intact." or the dip, in words.</summary>
    public string ReserveLine => ReservePlanWords.ReserveLine(this);

    /// <summary>The outside use, one sentence each, or empty.</summary>
    public IReadOnlyList<string> OutsideLines => ReservePlanWords.OutsideLines(this);
}

/// <summary>The words of the plan and the report. One place, so the card, the toast and the tests read the same.</summary>
public static class ReservePlanWords
{
    // U+00B7 MIDDLE DOT, the separator of a detail line.
    private const string Dot = " \U000000B7 ";

    public static string Line(ReservePlan plan)
    {
        if (plan.Accounts.Count == 0) return "Nothing is planned to use.";
        // The account with the least room goes last: "... and up to 6% of Claude 20x".
        var ordered = plan.Accounts.OrderByDescending(a => a.Room ?? double.MaxValue).ToList();
        var parts = new List<string>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var a = ordered[i];
            var name = a.Name + (a.Rough ? " (estimate rough)" : "");
            parts.Add(a.Room is not null && i == ordered.Count - 1 ? $"up to {Whole(a.PlannedP90)} of {name}"
                : i == 0 ? $"about {Whole(a.PlannedP50)} of {name}"
                : $"{Whole(a.PlannedP50)} of {name}");
        }
        var text = parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
        return char.ToUpperInvariant(text[0]) + text[1..] + "." + Reserve(plan.Reserves);
    }

    private static string Reserve(IReadOnlyList<PlanReserve> reserves)
    {
        if (reserves.Count == 0) return "";
        if (reserves.Count == 1) return $" Your {ReserveMath.Percent(reserves[0].KeepPercent)} reserve on {reserves[0].AccountName} stays.";
        return " Your reserves stay.";
    }

    public static string Details(ReservePlan plan)
    {
        var parts = new List<string>();
        if (plan.Reading.Length > 0) parts.Add(plan.Reading);
        if (plan.Team.Length > 0) parts.Add(plan.Team);
        if (plan.Duration is { } time) parts.Add("about " + Minutes(time));
        var needs = StartNeeds(plan);
        parts.Add(needs.Count == 0 ? "starts on its own" : $"start needs you ({string.Join("; ", needs)})");
        return string.Join(Dot, parts);
    }

    /// <summary>The four reasons of 11.4 (and the setting), in the order the design lists them.</summary>
    public static IReadOnlyList<string> StartNeeds(ReservePlan plan)
    {
        var reasons = new List<string>();
        if (plan.Accounts.Any(a => a.PlannedP90 > ReserveMath.StartTapPercent)) reasons.Add("over 5% of a window");
        if (plan.Accounts.Any(a => a.Floor is { } floor && floor < 100 && floor - a.Used <= 2 * a.PlannedP90)) reasons.Add("a floor is close");
        if (plan.Agents > ReserveMath.StartTapAgents) reasons.Add($"{plan.Agents} agents planned");
        if (SwarmGate.FanOutReason(plan.Lanes) is { } fanOut) reasons.Add(fanOut);
        if (plan.ApiDollars > ReserveMath.StartTapDollars) reasons.Add($"about ${plan.ApiDollars.ToString("0.##", CultureInfo.InvariantCulture)} of API spend");
        if (plan.AlwaysAsk) reasons.Add("you asked to be asked");
        return reasons;
    }

    public static string Minutes(TimeSpan span)
    {
        var minutes = (int)Math.Max(1, Math.Round(span.TotalMinutes));
        return minutes < 90 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60} min";
    }

    /// <summary>"Every reserve intact." or "Reserve on Claude 20x dipped by 0.6% this once, as you said."</summary>
    public static string ReserveLine(ReserveAfterReport report)
    {
        var dipped = report.Reserves.Where(r => !r.Intact).ToList();
        if (dipped.Count == 0) return "Every reserve intact.";
        return string.Join(" ", dipped.Select(r => r.UsedOnce
            ? $"Reserve on {r.AccountName} dipped by {ReserveMath.Percent(r.DippedBy)} this once, as you said."
            : $"Reserve on {r.AccountName} dipped by {ReserveMath.Percent(r.DippedBy)}; use outside nth took it."));
    }

    public static IReadOnlyList<string> OutsideLines(ReserveAfterReport report) =>
        [.. report.Outside.Select(o => $"{ReserveMath.Percent(o.Points)} of {o.AccountName}'s {o.WindowLabel} was used outside nth since {o.Since.ToLocalTime():HH:mm}.")];

    // A planned amount is rounded up to a whole point: the owner is never told less than the plan may use.
    private static string Whole(double percent) => Math.Max(0, Math.Ceiling(percent - 1e-9)).ToString("0", CultureInfo.InvariantCulture) + "%";
}
