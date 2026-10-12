// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5 "Activity view"; unit AS-21). No Swift counterpart.
// ED-NEW-dextra-as21-3: the filter of the Activity view.
using System.Globalization;

namespace Marrow.Services.Assistant;

/// <summary>
/// What the Activity view shows (12.5: "Filter by assistant, mission, class or date"; 3.3: a mission's lineage is "the
/// Activity log filtered to this mission"). A null field does not filter. A row passes when it passes every field set:
/// ids and classes compare exactly, the days are inclusive local days (a row's day is the day file it was written to), and
/// the search text must have each of its words (split at spaces) somewhere in the row, in any case.
/// </summary>
public sealed record ActivityFilter
{
    /// <summary>The value of <see cref="Mission"/> or <see cref="Grant"/> that means "rows that name none".</summary>
    public const string None = "";

    public string? Assistant { get; init; }

    /// <summary>A mission id, or <see cref="None"/> for the actions that belong to no mission.</summary>
    public string? Mission { get; init; }

    public string? Agent { get; init; }

    public string? Class { get; init; }

    /// <summary>A grant or decision id (the row's <see cref="ActivityRow.AllowedBy"/>), or <see cref="None"/> for actions no grant covered.</summary>
    public string? Grant { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public string Text { get; init; } = "";

    /// <summary>True when nothing is set, so every row passes.</summary>
    public bool IsEmpty => Assistant is null && Mission is null && Agent is null && Class is null && Grant is null
        && From is null && To is null && string.IsNullOrWhiteSpace(Text);

    /// <summary>How many fields filter (the text counts once).</summary>
    public int ActiveCount =>
        (Assistant is null ? 0 : 1) + (Mission is null ? 0 : 1) + (Agent is null ? 0 : 1) + (Class is null ? 0 : 1)
        + (Grant is null ? 0 : 1) + (From is null && To is null ? 0 : 1) + (string.IsNullOrWhiteSpace(Text) ? 0 : 1);

    /// <summary>A filter for one mission: the lineage the mission card opens.</summary>
    public static ActivityFilter ForMission(string mission) => new() { Mission = mission };

    public bool Matches(ActivityRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (Assistant is not null && !string.Equals(row.Assistant, Assistant, StringComparison.Ordinal)) return false;
        if (Mission is not null && !string.Equals(row.Mission ?? None, Mission, StringComparison.Ordinal)) return false;
        if (Agent is not null && !string.Equals(row.Agent ?? None, Agent, StringComparison.Ordinal)) return false;
        if (Class is not null && !string.Equals(row.Class, Class, StringComparison.Ordinal)) return false;
        if (Grant is not null && !string.Equals(row.AllowedBy ?? None, Grant, StringComparison.Ordinal)) return false;
        if (From is { } from && row.Day < from) return false;
        if (To is { } to && row.Day > to) return false;
        return string.IsNullOrWhiteSpace(Text) || HasEveryWord(row, Words(Text));
    }

    /// <summary>The words of a search text, split at white space.</summary>
    public static string[] Words(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static bool HasEveryWord(ActivityRow row, string[] words)
    {
        foreach (var word in words)
            if (!Has(row, word)) return false;
        return true;
    }

    private static bool Has(ActivityRow row, string word)
    {
        if (In(row.Assistant, word) || In(row.Mission, word) || In(row.Agent, word) || In(row.Tool, word) || In(row.Class, word)
            || In(row.Target, word) || In(row.Result, word) || In(row.AllowedBy, word) || In(row.Event, word) || In(row.Note, word)) return true;
        foreach (var input in row.Inputs)
            if (In(input.Name, word) || In(input.Display, word)) return true;
        return false;
    }

    private static bool In(string? field, string word) => field is not null && field.Contains(word, StringComparison.OrdinalIgnoreCase);

    /// <summary>The filter in words, for the export header and the status line, e.g. "mission m-1, class Run, 2026-10-01 to 2026-10-12".</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Assistant is not null) parts.Add("assistant " + Assistant);
        if (Mission is not null) parts.Add(Mission.Length == 0 ? "no mission" : "mission " + Mission);
        if (Agent is not null) parts.Add(Agent.Length == 0 ? "no agent" : "agent " + Agent);
        if (Class is not null) parts.Add("class " + Class);
        if (Grant is not null) parts.Add(Grant.Length == 0 ? "no grant" : "grant " + Grant);
        if (From is { } from && To is { } to) parts.Add(from == to ? Iso(from) : Iso(from) + " to " + Iso(to));
        else if (From is { } onlyFrom) parts.Add("from " + Iso(onlyFrom));
        else if (To is { } onlyTo) parts.Add("up to " + Iso(onlyTo));
        if (!string.IsNullOrWhiteSpace(Text)) parts.Add("text \"" + Text.Trim() + "\"");
        return string.Join(", ", parts);
    }

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
