// PORT-ONLY: Dextra (DESIGN.md rev 5, 13.2 "Failed runs never retry endlessly", 7.1; unit AS-15; ED-NEW-dextra-as15-5). No Swift twin.
// A duty that fails three times in a row is paused with a decision card. This is the rule and its words, pure: DutyHealth is what is
// known of one duty's runs, DutyBreaker moves it on when a run ends. A run that finds nothing changed is a success; a success starts
// the count again; a paused duty counts nothing more until the owner lets it go on.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

/// <summary>How a duty's run ended.</summary>
public enum DutyOutcome { Succeeded, Failed }

/// <summary>What is known of one duty's recent runs: how many in a row failed, why, and whether the breaker paused it.</summary>
public sealed record DutyHealth(string DutyId)
{
    /// <summary>Failures in a row, ending with the latest run.</summary>
    public int ConsecutiveFailures { get; init; }

    /// <summary>The breaker paused the duty; it stays paused until the owner answers the card or turns it on.</summary>
    public bool Paused { get; init; }

    /// <summary>When it was paused.</summary>
    public DateTimeOffset? PausedAt { get; init; }

    /// <summary>Why the last runs failed, oldest first, one short line each (data, never instructions).</summary>
    public ValueList<string> Reasons { get; init; } = ValueList<string>.Empty;
}

/// <summary>The result of a run ending: the duty's health now, and whether this run is the one that paused it.</summary>
public sealed record DutyBreak(DutyHealth Health, bool Tripped);

public static class DutyBreaker
{
    /// <summary>"A duty that fails 3 times in a row is paused with a decision card" (13.2).</summary>
    public const int MaxConsecutiveFailures = 3;

    /// <summary>The longest line kept of a failure's reason.</summary>
    public const int ReasonChars = 160;

    /// <summary>
    /// A run ended. A success clears the count. A failure adds to it; the third in a row pauses the duty (<see cref="DutyBreak.Tripped"/>
    /// is true once, for that run). A duty that is already paused changes nothing: it should not have run, and a run that did (the
    /// owner's Run now) neither hides its failure nor pauses it twice.
    /// </summary>
    public static DutyBreak Record(DutyHealth health, DutyOutcome outcome, string? reason, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(health);
        if (health.Paused) return new DutyBreak(health, false);
        if (outcome == DutyOutcome.Succeeded)
            return new DutyBreak(health with { ConsecutiveFailures = 0, Reasons = ValueList<string>.Empty }, false);
        var failures = health.ConsecutiveFailures + 1;
        var reasons = health.Reasons.Concat([OneLine(reason)]).TakeLast(MaxConsecutiveFailures).ToList();
        var next = health with { ConsecutiveFailures = failures, Reasons = new ValueList<string>(reasons) };
        return failures >= MaxConsecutiveFailures
            ? new DutyBreak(next with { Paused = true, PausedAt = at }, true)
            : new DutyBreak(next, false);
    }

    /// <summary>The owner let the duty go on (or turned it on again): paused no more, and the count starts again.</summary>
    public static DutyHealth Resume(DutyHealth health) =>
        health with { Paused = false, PausedAt = null, ConsecutiveFailures = 0, Reasons = ValueList<string>.Empty };

    /// <summary>
    /// One short line of a reason: control characters and line breaks become spaces, runs of space become one, and it is cut at
    /// <see cref="ReasonChars"/>. A reason can come from a page or a file the duty read, so it is only ever shown as data.
    /// </summary>
    public static string OneLine(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "no reason given";
        var chars = reason.Select(c => char.IsControl(c) || char.IsWhiteSpace(c) ? ' ' : c);
        var text = string.Join(" ", new string([.. chars]).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (text.Length <= ReasonChars) return text;
        var cut = text[..(ReasonChars - 1)];
        if (char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
        return cut + "\U00002026";
    }
}

/// <summary>The words of the duty card (13.2, 7.1).</summary>
public static class DutyWords
{
    public const string TryAgain = "Try it again";
    public const string KeepPaused = "Keep it paused";

    public static string Title(string dutyName) => string.Create(CultureInfo.InvariantCulture, $"{dutyName} was paused after {DutyBreaker.MaxConsecutiveFailures} failures in a row");

    public const string Why = "A duty that fails three times in a row is paused, so it never tries again and again on its own.";

    public const string Note = "Its next run will not start until you answer.";
}
