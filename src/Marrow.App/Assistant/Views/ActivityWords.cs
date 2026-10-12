// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5 "Activity view"; unit AS-21). No Swift counterpart.
// ED-NEW-dextra-as21-6: the words and times the Activity view shows. Every string the view says is here or in the view's
// own constructors, so a test can read them.
using System.Globalization;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant.Views;

/// <summary>How the Activity view says things. Times are local to the owner's PC; a row's day is the day file it was written to.</summary>
public static class ActivityWords
{
    public const string Title = "Activity log";
    public const string Checking = "Checking the log's chain…";
    public const string AnyAssistant = "All assistants";
    public const string AnyMission = "All missions";
    public const string AnyClass = "All classes";
    public const string AnyDay = "Any day";
    public const string NoMission = "No mission";
    public const string NoGrant = "No grant";
    public const string NoAgent = "No agent";
    public const string SearchPrompt = "Search the log";
    public const string ClearFilters = "Clear filters";
    public const string ShowLine = "Show line";
    public const string ShowDay = "Show day";
    public const string Refresh = "Refresh";
    public const string Export = "Export";
    public const string ExportJson = "JSON, every field";
    public const string ExportCsv = "CSV, for a spreadsheet";
    public const string LogMode = "Log";
    public const string LineageMode = "Lineage";
    public const string EmptyLog = "Nothing has been logged yet. Every action an assistant or an agent takes is written here.";
    public const string NothingMatches = "No line matches. Clear a filter to see more.";
    public const string NothingSelected = "Select a line to see its inputs, its grant and where it sits in the chain.";

    public static string Count(long count, string singular, string plural) =>
        count.ToString("N0", CultureInfo.InvariantCulture) + " " + (count == 1 ? singular : plural);

    public static string Lines(long count) => Count(count, "line", "lines");

    /// <summary>A local clock time, e.g. <c>08:00:30</c>.</summary>
    public static string Clock(DateTimeOffset time, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(time, zone).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>A local date and time with the zone's offset, e.g. <c>2026-10-12 08:00:30 +03:00</c>.</summary>
    public static string Stamp(DateTimeOffset time, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(time, zone).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

    /// <summary>A day header: Today, Yesterday, or <c>Mon 12 Oct 2026</c>.</summary>
    public static string DayTitle(DateOnly day, DateOnly today) =>
        day == today ? "Today" : day == today.AddDays(-1) ? "Yesterday" : day.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture);

    public static string DayChoice(ActivityDayFacet facet, DateOnly today) =>
        DayTitle(facet.Day, today) + " · " + Lines(facet.Count);

    public static string Money(decimal usd) => "$" + usd.ToString(usd < 0.01m && usd > 0 ? "0.0000" : "0.00", CultureInfo.InvariantCulture);

    public static string Tokens(long tokens) => tokens.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>What the action was, short: the tool, then its target.</summary>
    public static string What(ActivityRow row) => row.Kind switch
    {
        ActivityRowKind.Unreadable => "Unreadable line",
        ActivityRowKind.Record => row.Note ?? row.Event ?? "Log record",
        _ => string.IsNullOrEmpty(row.Target) ? row.Tool : row.Tool + " · " + ActivityText.OneLine(row.Target, 160),
    };

    public static string TrustWords(ActivityTrust trust) => trust switch
    {
        ActivityTrust.Verified => "Verified",
        ActivityTrust.NotVerified => "Not verified",
        _ => "Not checked",
    };

    /// <summary>The whole row as one sentence, for a screen reader.</summary>
    public static string Sentence(ActivityRow row, ActivityLabels labels, TimeZoneInfo zone)
    {
        if (row.Kind == ActivityRowKind.Unreadable) return $"Line {row.At.Line} of {row.At.File}, unreadable. {TrustWords(row.Trust)}.";
        var parts = new List<string>
        {
            Clock(row.Time, zone),
            labels.ForAssistant(row.Assistant),
            row.Kind == ActivityRowKind.Record ? What(row) : What(row) + ", " + row.Class,
        };
        if (row.Mission is { Length: > 0 } mission) parts.Add("mission " + labels.ForMission(mission));
        if (row.AllowedBy is { Length: > 0 } by) parts.Add("allowed by " + by);
        if (row.Result is { Length: > 0 } result) parts.Add(ActivityText.OneLine(result, 120));
        parts.Add(TrustWords(row.Trust));
        return string.Join(", ", parts);
    }

    /// <summary>The status strip's headline for a check (12.5: "Verified" or the first line that fails).</summary>
    public static string Headline(AuditVerification verification) => verification.Summary;

    /// <summary>The line under the headline: how many lines were checked, and what the log repaired by itself.</summary>
    public static string Detail(AuditVerification verification, ActivitySnapshot snapshot)
    {
        var parts = new List<string>();
        if (verification.Status == AuditVerificationStatus.Verified) parts.Add(Lines(verification.LinesVerified) + " checked, each sealed by the one before it");
        else if (verification.Status == AuditVerificationStatus.Failed) parts.Add(Lines(verification.LinesVerified) + " verified before the failure; the lines from it on are marked");
        if (verification.Recovered.Count > 0)
            parts.Add(Count(verification.Recovered.Count, "line a crash cut off was repaired", "lines a crash cut off were repaired"));
        if (snapshot.NotListed > 0) parts.Add(Lines(snapshot.NotListed) + " older than the newest " + snapshot.Rows.Count.ToString("N0", CultureInfo.InvariantCulture) + " are not listed (exports still cover them)");
        return string.Join(" · ", parts);
    }
}
