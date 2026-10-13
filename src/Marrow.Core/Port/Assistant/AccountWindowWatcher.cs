// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.1 table row MultiAccountRouting; unit AS-2; ED-NEW-dextra-as2-7). No Swift twin.
// Turns the account usage snapshots the app already polls into feed events: a window's percent changed by a point or
// more, a limit was reached, a window reset. Pure: the readings and the time come in, drafts go out. Used on the main
// thread only (the snapshot handler runs there).
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

/// <summary>One usage window of one account, as the last poll read it.</summary>
public sealed record AccountWindowReading(
    string AccountKey,
    string AccountName,
    string WindowId,
    string WindowTitle,
    double Percent,
    DateTimeOffset? ResetsAt);

/// <summary>Compares each poll with the one before and says what changed.</summary>
public sealed class AccountWindowWatcher : IDisposable
{
    /// <summary>"Account window percent changed (at least 1 point)" (5.1).</summary>
    public const double MinChange = 1.0;

    /// <summary>A window counts as reset only when its reset time moved on by more than this.</summary>
    public static readonly TimeSpan ResetSlack = TimeSpan.FromMinutes(1);

    /// <summary>A limit with no known reset time is not reported again for this long.</summary>
    public static readonly TimeSpan UnknownResetQuiet = TimeSpan.FromMinutes(30);

    private bool disposed;
    private readonly Dictionary<(string Account, string Window), AccountWindowReading> last = [];
    private readonly Dictionary<(string Account, DateTimeOffset? Reset), DateTimeOffset> limitedUntil = [];

    /// <summary>The events for one poll of one account. The first reading of a window only sets the baseline.</summary>
    public IReadOnlyList<FeedDraft> Observe(IEnumerable<AccountWindowReading> readings, DateTimeOffset now)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var drafts = new List<FeedDraft>();
        foreach (var reading in readings)
        {
            if (!double.IsFinite(reading.Percent)) continue;
            var key = (reading.AccountKey, reading.WindowId);
            var hadPrevious = last.TryGetValue(key, out var previous);
            last[key] = reading;

            if (reading.Percent >= 100 && (!hadPrevious || previous!.Percent < 100))
            {
                if (NoteLimit(reading.AccountKey, reading.AccountName, reading.ResetsAt, now) is { } limit) drafts.Add(limit);
                continue;
            }
            if (!hadPrevious) continue;

            if (IsReset(previous!, reading, now))
            {
                ForgetResetLimit(previous!, now);
                drafts.Add(new FeedDraft(FeedEventKind.AccountWindowReset, FeedSeverity.Info, now,
                    $"{reading.AccountName}: {reading.WindowTitle} reset, from {Percent(previous!.Percent)} to {Percent(reading.Percent)}"));
            }
            else if (Math.Abs(reading.Percent - previous!.Percent) >= MinChange)
            {
                drafts.Add(new FeedDraft(FeedEventKind.AccountWindow, FeedSeverity.Quiet, now,
                    $"{reading.AccountName}: {reading.WindowTitle} at {Percent(reading.Percent)}, {(reading.Percent > previous.Percent ? "up" : "down")} {Percent(Math.Abs(reading.Percent - previous.Percent))}"));
            }
        }
        return drafts;
    }

    /// <summary>
    /// A provider said an account hit its limit. Returns the event, or null when this limit was reported already (the
    /// snapshot poll and the provider's own report both see it).
    /// </summary>
    public FeedDraft? NoteLimit(string accountKey, string accountName, DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        foreach (var key in limitedUntil.Keys.Where(key => key.Account == accountKey && limitedUntil[key] <= now).ToArray())
            limitedUntil.Remove(key);
        if (limitedUntil.Keys.Any(key => key.Account == accountKey
            && (resetsAt is null || (key.Reset is { } reset && (resetsAt.Value - reset).Duration() <= ResetSlack)))) return null;
        limitedUntil[(accountKey, resetsAt)] = resetsAt ?? now + UnknownResetQuiet;
        var tail = resetsAt is { } reset ? $", resets in {Relative(reset - now)}" : "";
        return new FeedDraft(FeedEventKind.AccountLimitReached, FeedSeverity.Info, now, $"{accountName}: usage limit reached{tail}");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        last.Clear();
        limitedUntil.Clear();
    }

    // Remove only this window's old reset episode. A different window may still own a live limit,
    // even with the same reset time and a lagging snapshot; an unidentified provider limit stays until its quiet period expires.
    private void ForgetResetLimit(AccountWindowReading previous, DateTimeOffset now)
    {
        var others = last.Values.Where(reading => reading.AccountKey == previous.AccountKey
            && reading.WindowId != previous.WindowId
            && (reading.ResetsAt is null || reading.ResetsAt > now)).ToArray();
        foreach (var key in limitedUntil.Keys.Where(key => key.Account == previous.AccountKey).ToArray())
        {
            if (key.Reset is { } reset)
            {
                if (previous.ResetsAt is not { } before || (reset - before).Duration() > ResetSlack) continue;
                if (others.Any(reading => reading.ResetsAt is not { } other || (reset - other).Duration() <= ResetSlack)) continue;
            }
            else continue; // Unknown provider episodes have no window provenance; retain until expiry.
            limitedUntil.Remove(key);
        }
    }

    // A window reset when its percent fell and its reset time moved on (or had already passed). A rolling window whose
    // percent only decays is not a reset.
    private static bool IsReset(AccountWindowReading previous, AccountWindowReading current, DateTimeOffset now)
    {
        if (previous.Percent - current.Percent < MinChange) return false;
        if (previous.ResetsAt is not { } before) return false;
        return before <= now || (current.ResetsAt is { } after && after > before + ResetSlack);
    }

    private static string Percent(double value) => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(value):0}%");

    /// <summary>A wait in words: "under a minute", "45 min", "2 h 5 min", "3 days".</summary>
    public static string Relative(TimeSpan span)
    {
        if (span <= TimeSpan.Zero) return "under a minute";
        if (span < TimeSpan.FromMinutes(1)) return "under a minute";
        if (span < TimeSpan.FromMinutes(90)) return string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(span.TotalMinutes)} min");
        if (span < TimeSpan.FromHours(48))
        {
            var minutes = (int)Math.Round(span.TotalMinutes);
            return string.Create(CultureInfo.InvariantCulture, $"{minutes / 60} h {minutes % 60} min");
        }
        return string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(span.TotalDays)} days");
    }
}
