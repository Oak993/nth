// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.3 "Totals", 13.2 "Caps"; unit AS-16; ED-NEW-dextra-as16-14). No Swift twin.
// What each assistant used today against its slice, and what all of them used together: the figures the org chart's usage meter (AS-17) and
// Dextra's own view read. Read from the day's ledger the cost guard keeps; nothing here limits anything (the Astur's slice is a standing cap the
// guard enforces at the turn boundary).
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>An assistant's use of the day, against its slice.</summary>
public sealed record TeamUsageLine(string AssistantId, string Name, decimal Dollars, double WorstWindowPercent, AsturBudget? Slice)
{
    /// <summary>How much of its slice it has used: 0 to 1 and past it; null when it has no slice (Dextra).</summary>
    public double? Fraction { get; init; }
}

/// <summary>Everyone's use of the day, and the combined figure Dextra sees.</summary>
public sealed record TeamUsage(IReadOnlyList<TeamUsageLine> Lines, decimal TotalDollars, double TotalWorstWindowPercent)
{
    /// <summary>
    /// Today's figures from the ledger, one line for each assistant that is not retired. <c>WorstWindowPercent</c> is the most the assistant used of
    /// any one account window (points are percent of the window); <c>Fraction</c> is the larger of that over its percent slice and its dollars over
    /// its dollar slice. The total is the same figure for everyone together.
    /// </summary>
    public static TeamUsage Of(CostLedger ledger, IEnumerable<AssistantProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(profiles);
        var day = ledger.Today;
        var each = new CostCap("usage", CostCapScope.AssistantDay) { Percent = 100 };
        var lines = new List<TeamUsageLine>();
        foreach (var profile in profiles.Where(p => !p.Retired))
        {
            var use = ledger.UseOf(each, day, profile.Id);
            var worst = use.Points.Count == 0 ? 0 : use.Points.Values.Max();
            double? fraction = null;
            if (profile.Budget is { } slice)
            {
                fraction = worst / slice.DailyPercent;
                if (slice.DailyDollars is { } cap && cap > 0) fraction = Math.Max(fraction.Value, (double)(use.Dollars / cap));
            }
            lines.Add(new TeamUsageLine(profile.Id, profile.Name, use.Dollars, worst, profile.Budget) { Fraction = fraction });
        }
        var total = ledger.UseOf(each, day);
        return new TeamUsage(lines, total.Dollars, total.Points.Count == 0 ? 0 : total.Points.Values.Max());
    }
}
