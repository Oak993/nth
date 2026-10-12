// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.2, 11.3; unit AS-13; ED-NEW-dextra-as13-1). No Swift twin.
// A ReserveRule as the owner states it ("keep 30% of what's left", "always keep 30% on Claude 20x", "keep $5 a day on the
// OpenRouter key"), the windows a rule can protect, and the readings of those windows. Pure data and the words around it:
// ReserveMath turns a rule into a floor, ReserveAccount tracks one account's windows, ReserveGuard (Marrow.App) acts.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

/// <summary>"Of what is left now" (a task's words) or "of the whole window" (a standing rule's), 11.3.</summary>
public enum ReserveBasis { WhatIsLeft, WholeWindow }

/// <summary>Which usage windows a rule protects (11.3 "5-hour, weekly, or both, where the tighter one binds"). API keys have day and month.</summary>
[Flags]
public enum ReserveWindows
{
    None = 0,
    FiveHour = 1,
    Weekly = 2,
    Day = 4,
    Month = 8,
    /// <summary>The two subscription windows: the tighter one binds.</summary>
    Both = FiveHour | Weekly,
}

/// <summary>"This task" or "standing" (11.3).</summary>
public enum ReserveLifetime { Task, Standing }

/// <summary>What a rule covers: every account, named accounts, a provider, or an assistant (11.3).</summary>
public enum ReserveScopeKind { EveryAccount, Accounts, Provider, Assistant }

/// <summary>The kind of a usage window. Easing and the rules' window choice both go by it.</summary>
public enum ReserveWindowKind { FiveHour, Weekly, Day, Month, Other }

/// <summary>Maps a provider's reported window to a kind. The reported length wins; the title is only a fallback.</summary>
public static class ReserveWindowKinds
{
    public const long FiveHourMinutes = 300;
    public const long WeekMinutes = 7 * 24 * 60;

    public static ReserveWindowKind Of(long? durationMinutes, string? title)
    {
        if (durationMinutes is { } minutes)
        {
            if (minutes == FiveHourMinutes) return ReserveWindowKind.FiveHour;
            if (minutes == WeekMinutes) return ReserveWindowKind.Weekly;
            if (minutes == 24 * 60) return ReserveWindowKind.Day;
            if (minutes is >= 28 * 24 * 60 and <= 31 * 24 * 60) return ReserveWindowKind.Month;
            return ReserveWindowKind.Other;
        }
        var text = title?.ToLowerInvariant() ?? "";
        if (text.Contains("5-hour", StringComparison.Ordinal) || text.Contains("5 hour", StringComparison.Ordinal)) return ReserveWindowKind.FiveHour;
        if (text.Contains("week", StringComparison.Ordinal)) return ReserveWindowKind.Weekly;
        if (text.Contains("month", StringComparison.Ordinal)) return ReserveWindowKind.Month;
        if (text.Contains("day", StringComparison.Ordinal) || text.Contains("daily", StringComparison.Ordinal)) return ReserveWindowKind.Day;
        return ReserveWindowKind.Other;
    }

    public static ReserveWindows Flag(ReserveWindowKind kind) => kind switch
    {
        ReserveWindowKind.FiveHour => ReserveWindows.FiveHour,
        ReserveWindowKind.Weekly => ReserveWindows.Weekly,
        ReserveWindowKind.Day => ReserveWindows.Day,
        ReserveWindowKind.Month => ReserveWindows.Month,
        _ => ReserveWindows.None,
    };

    /// <summary>"5-hour window", "weekly window", "day", "month".</summary>
    public static string Words(ReserveWindowKind kind) => kind switch
    {
        ReserveWindowKind.FiveHour => "5-hour window",
        ReserveWindowKind.Weekly => "weekly window",
        ReserveWindowKind.Day => "daily budget",
        ReserveWindowKind.Month => "monthly budget",
        _ => "window",
    };
}

/// <summary>The account a rule is asked about: its key, the name the owner gave it, its provider and the assistant the mission belongs to.</summary>
public sealed record ReserveSubject(string AccountKey, string AccountName, string ProviderId, string? AssistantId = null);

/// <summary>What a rule covers (11.3). Accounts are matched by the names the owner gave them, without case.</summary>
public sealed record ReserveScope(ReserveScopeKind Kind)
{
    public ValueList<string> AccountNames { get; init; } = ValueList<string>.Empty;

    public string? ProviderId { get; init; }

    public string? AssistantId { get; init; }

    public static ReserveScope Every { get; } = new(ReserveScopeKind.EveryAccount);

    public static ReserveScope Named(params string[] names) => new(ReserveScopeKind.Accounts) { AccountNames = new ValueList<string>(names) };

    public static ReserveScope Provider(string providerId) => new(ReserveScopeKind.Provider) { ProviderId = providerId };

    public static ReserveScope Assistant(string assistantId) => new(ReserveScopeKind.Assistant) { AssistantId = assistantId };

    public bool Covers(ReserveSubject subject) => Kind switch
    {
        ReserveScopeKind.EveryAccount => true,
        ReserveScopeKind.Accounts => AccountNames.Any(n => string.Equals(n.Trim(), subject.AccountName.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(n.Trim(), subject.AccountKey, StringComparison.Ordinal)),
        ReserveScopeKind.Provider => string.Equals(ProviderId, subject.ProviderId, StringComparison.OrdinalIgnoreCase),
        ReserveScopeKind.Assistant => subject.AssistantId is not null && string.Equals(AssistantId, subject.AssistantId, StringComparison.Ordinal),
        _ => false,
    };
}

/// <summary>
/// One reserve rule (11.3): a scope, an amount to keep, a basis, the windows it protects and a lifetime. An amount is
/// percent of a subscription window, or dollars of an API key's day or month (<see cref="KeepDollars"/>).
/// </summary>
public sealed record ReserveRule(string Id, ReserveScope Scope, ReserveLifetime Lifetime)
{
    /// <summary>Percent to keep, 0 to 100.</summary>
    public double KeepPercent { get; init; }

    /// <summary>Dollars to keep of an API key's day or month budget; null for a percent rule.</summary>
    public decimal? KeepDollars { get; init; }

    public ReserveBasis Basis { get; init; } = ReserveBasis.WhatIsLeft;

    public ReserveWindows Windows { get; init; } = ReserveWindows.Both;

    /// <summary>
    /// The rule as the engine reads it (ED-NEW-dextra-as13-1): a standing rule always keeps its share of the whole window
    /// (11.3), whatever a hand-edited file says, because "what is left" has no moment to measure from; an amount that is
    /// not a number keeps everything (the safe reading); a percent is held to 0..100.
    /// </summary>
    public ReserveRule Normalized() => this with
    {
        KeepPercent = double.IsNaN(KeepPercent) ? 100 : Math.Clamp(KeepPercent, 0, 100),
        KeepDollars = KeepDollars is { } dollars ? Math.Max(0, dollars) : null,
        Basis = Lifetime == ReserveLifetime.Standing ? ReserveBasis.WholeWindow : Basis,
    };

    public bool AppliesTo(ReserveWindowKind kind) => (Windows & ReserveWindowKinds.Flag(kind)) != ReserveWindows.None;
}

/// <summary>
/// One window of one account as the last poll (or the API key's own spend) read it. <see cref="Percent"/> is used, 0..100;
/// <see cref="LimitDollars"/> is an API key's budget for the window, which turns a dollar rule into a percent.
/// </summary>
public sealed record ReserveWindow(string Id, ReserveWindowKind Kind, double Percent, DateTimeOffset ObservedAt, DateTimeOffset? ResetsAt)
{
    public string Title { get; init; } = "";

    public decimal? LimitDollars { get; init; }
}

/// <summary>A rule and the percent its window had used when the task began, which "of what is left" measures from.</summary>
public sealed record ReserveFloorInput(ReserveRule Rule, double UsedAtStart);

/// <summary>The floor that binds one window: the highest percent used a task may reach (11.3 "The floor").</summary>
public sealed record ReserveFloor(double Percent, ReserveRule Rule, double Unadjusted, bool Eased);

/// <summary>The words of rules (the Settings > Reserves list, the card's "why" line, the plan's reading).</summary>
public static class ReserveWords
{
    /// <summary>"Always keep 30% on Claude 20x", "Keep 30% of what's left", "Keep $5 a day on the OpenRouter key".</summary>
    public static string Title(ReserveRule rule)
    {
        var r = rule.Normalized();
        var amount = Amount(r);
        var where = Where(r.Scope);
        if (r.KeepDollars is not null)
            return $"Keep {amount}{PerWord(r.Windows)}{(where.Length > 0 ? " on " + where : "")}";
        var lead = r.Lifetime == ReserveLifetime.Standing ? "Always keep" : "Keep";
        var basis = r.Basis == ReserveBasis.WhatIsLeft ? " of what's left" : "";
        return $"{lead} {amount}{basis}{(where.Length > 0 ? " on " + where : "")}";
    }

    /// <summary>"Standing · whole 5-hour and weekly windows · all assistants".</summary>
    public static string Detail(ReserveRule rule)
    {
        var r = rule.Normalized();
        var lifetime = r.Lifetime == ReserveLifetime.Standing ? "Standing" : "This task";
        var what = r.KeepDollars is not null
            ? "API key, dollars"
            : (r.Basis == ReserveBasis.WholeWindow ? "whole " : "what's left in ") + WindowsWords(r.Windows);
        var who = r.Scope.Kind == ReserveScopeKind.Assistant ? r.Scope.AssistantId + " only" : "all assistants";
        return $"{lifetime} \U000000B7 {what} \U000000B7 {who}";
    }

    public static string Amount(ReserveRule rule) => rule.KeepDollars is { } dollars
        ? "$" + dollars.ToString("0.##", CultureInfo.InvariantCulture)
        : Math.Round(rule.KeepPercent, 1).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    public static string WindowsWords(ReserveWindows windows)
    {
        var parts = new List<string>();
        if (windows.HasFlag(ReserveWindows.FiveHour)) parts.Add("5-hour");
        if (windows.HasFlag(ReserveWindows.Weekly)) parts.Add("weekly");
        if (windows.HasFlag(ReserveWindows.Day)) parts.Add("daily");
        if (windows.HasFlag(ReserveWindows.Month)) parts.Add("monthly");
        return parts.Count switch
        {
            0 => "no window",
            1 => parts[0] + " window",
            _ => string.Join(" and ", parts) + " windows",
        };
    }

    private static string PerWord(ReserveWindows windows) =>
        windows.HasFlag(ReserveWindows.Month) && !windows.HasFlag(ReserveWindows.Day) ? " a month" : " a day";

    private static string Where(ReserveScope scope) => scope.Kind switch
    {
        ReserveScopeKind.Accounts => string.Join(" and ", scope.AccountNames),
        ReserveScopeKind.Provider => scope.ProviderId ?? "",
        ReserveScopeKind.Assistant => scope.AssistantId ?? "",
        _ => "",
    };
}
