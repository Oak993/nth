// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-settings; PLAN unit U5). Settings › Ignis "Savings history" as plain data, read
// from the savings ledger (ISavingsLedger, built by unit U1) for one local day "today": the all-time tile, one entry per
// month since the first saving (the monthly chart and its month picker), each month's tile, where its savings came from,
// and its days newest first with what Ignis did. Pure: no control, no clock; the page hands it the ledger and the day.
using Marrow.Core.Port.Ignis;

namespace Marrow.App.Port.Ignis.Settings;

/// <summary>One thing Ignis did on a day ("214 quick reuses", "kept warm 3 times"), with the source whose dot it carries.</summary>
public sealed record IgnisDidItem(IgnisSource Source, string Text);

/// <summary>One row of "Day by day": the label ("Today", "Tue 6 Oct"), what Ignis did (empty: "A quiet day") and the money saved.</summary>
public sealed record IgnisDayRow(DateOnly Day, string Label, IReadOnlyList<IgnisDidItem> Did, decimal Saved)
{
    public string Amount => IgnisWords.Money(Saved);
}

/// <summary>One month of savings: the days counted (from the first saving, up to today), the total and its sources.</summary>
public sealed record IgnisMonthSavings(DateOnly Month, bool IsCurrent, IReadOnlyList<IgnisDayTotals> Days, decimal Total, IReadOnlyDictionary<IgnisSource, decimal> BySource)
{
    /// <summary>The month's helper cache rebuilds (ED-NEW-ignis-runtime): a cost, never inside <see cref="Total"/>.</summary>
    public decimal Missed => Days.Sum(day => day.Missed);

    /// <summary>"Helpers rebuilt their quick memory: $0.31"; null when there were none.</summary>
    public string? MissedLine => Missed > 0m ? IgnisWords.HelpersRebuilt(Missed) : null;

    /// <summary>The legend's sources: the design's four, plus "Longer quick memory" in a month that has some.</summary>
    public IReadOnlyList<IgnisSource> Shown =>
        BySource.TryGetValue(IgnisSource.LongerMemory, out var longer) && longer > 0m ? [.. IgnisSavingsHistory.Sources, IgnisSource.LongerMemory] : IgnisSavingsHistory.Sources;

    public int DayCount => Days.Count;

    /// <summary>The month tile's label: "October so far" or "September".</summary>
    public string TileLabel => IgnisWords.MonthTile(Month, IsCurrent);

    /// <summary>"6 days · about $9.73 a day".</summary>
    public string TileDetail => IgnisWords.MonthTileDetail(DayCount, Total);

    /// <summary>The days newest first, as "Day by day" lists them.</summary>
    public IReadOnlyList<IgnisDayRow> DayRows(DateOnly today) =>
        Days.Reverse().Select(day => new IgnisDayRow(day.Day, IgnisWords.DayLabel(day.Day, today), IgnisSavingsHistory.DidOn(day), day.Saved)).ToList();
}

public sealed class IgnisSavingsHistory
{
    /// <summary>The sources in the design's legend order (cache, warm, rebuild, help).</summary>
    public static IReadOnlyList<IgnisSource> Sources { get; } = [IgnisSource.Reuse, IgnisSource.KeptWarm, IgnisSource.TidyUp, IgnisSource.SmartHelpers];

    /// <summary>Every source a month adds up (ED-NEW-ignis-runtime: "Longer quick memory" after the design's four).</summary>
    public static IReadOnlyList<IgnisSource> AllSources { get; } = [.. Sources, IgnisSource.LongerMemory];

    /// <summary>At most this many months stand in the chart at once; the picker reaches the older ones.</summary>
    public const int ChartMonths = 12;

    private IgnisSavingsHistory(DateOnly today, DateOnly since, decimal allTime, IReadOnlyList<IgnisMonthSavings> months)
    {
        Today = today;
        Since = since;
        AllTime = allTime;
        Months = months;
    }

    public DateOnly Today { get; }

    /// <summary>The day of the first saving; today when the ledger is empty.</summary>
    public DateOnly Since { get; }

    public decimal AllTime { get; }

    /// <summary>Every month from the first saving's to today's, oldest first (never empty: today's month is always there).</summary>
    public IReadOnlyList<IgnisMonthSavings> Months { get; }

    public string AllTimeAmount => IgnisWords.Money(AllTime);

    /// <summary>"Since 3 February 2026, across all chats".</summary>
    public string SinceText => IgnisWords.SinceAcrossAllChats(Since);

    /// <summary>The months the chart shows when <paramref name="selected"/> is picked: the last <see cref="ChartMonths"/>, moved back so the picked one is always among them.</summary>
    public (int Start, int Count) ChartWindow(int selected)
    {
        var count = Math.Min(ChartMonths, Months.Count);
        var start = Math.Min(Months.Count - count, Math.Max(0, selected));
        return (start, count);
    }

    /// <summary>Reads the ledger for local day <paramref name="today"/>. A null ledger (Ignis not running) reads as an empty one.</summary>
    public static IgnisSavingsHistory From(ISavingsLedger? ledger, DateOnly today)
    {
        var since = ledger?.Since is { } first && first <= today ? first : today;
        var days = ledger?.Days(since, today) ?? [IgnisDayTotals.Empty(today)];
        var months = new List<IgnisMonthSavings>();
        for (var month = new DateOnly(since.Year, since.Month, 1); month <= today; month = month.AddMonths(1))
        {
            var inMonth = days.Where(day => day.Day.Year == month.Year && day.Day.Month == month.Month).OrderBy(day => day.Day).ToList();
            var bySource = AllSources.ToDictionary(source => source, source => inMonth.Sum(day => day.SavedFrom(source)));
            months.Add(new IgnisMonthSavings(month, month.Year == today.Year && month.Month == today.Month, inMonth, inMonth.Sum(day => day.Saved), bySource));
        }
        return new IgnisSavingsHistory(today, since, ledger?.AllTimeSaved ?? 0m, months);
    }

    /// <summary>What Ignis did on a day, in legend order; empty for a quiet day.</summary>
    public static IReadOnlyList<IgnisDidItem> DidOn(IgnisDayTotals day)
    {
        var did = new List<IgnisDidItem>();
        if (day.Reuses > 0) did.Add(new(IgnisSource.Reuse, IgnisWords.QuickReuses(day.Reuses)));
        if (day.Pings > 0) did.Add(new(IgnisSource.KeptWarm, IgnisWords.KeptWarmTimes(day.Pings)));
        if (day.TidyUps > 0) did.Add(new(IgnisSource.TidyUp, IgnisWords.TidyUps(day.TidyUps)));
        if (day.HelperJobs > 0) did.Add(new(IgnisSource.SmartHelpers, IgnisWords.HelperJobs(day.HelperJobs)));
        return did;
    }
}
