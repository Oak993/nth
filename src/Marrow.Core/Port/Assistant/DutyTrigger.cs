// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.2; unit AS-10; ED-NEW-dextra-as10-1, -6, -7). No Swift twin.
// What starts a duty: a time, a change in a folder the owner granted, a web page, an app event, or the owner (Run now).
// Pure data; AssistantScheduler (Marrow.Services) arms them, FolderTrigger and WebWatcher watch for them.
namespace Marrow.Core.Port.Assistant;

public enum DutyTriggerKind { Time, Folder, Page, AppEvent, Manual }

/// <summary>What a folder trigger reacts to (7.2: new, changed or removed files).</summary>
[Flags]
public enum FolderEvents : byte
{
    None = 0,
    New = 1,
    Changed = 2,
    Removed = 4,
    All = New | Changed | Removed,
}

/// <summary>When a page watch fires (7.2): the region changed, a price is below an amount, or a text is there.</summary>
public enum PageCondition { Changed, PriceBelow, ContainsText }

/// <summary>The app events a duty can wait for (7.2, canvas 5): a chat finished, a head failed, a usage window reset, an account above a share, a mission finished.</summary>
public enum AppEventKind { ChatFinished, HeadFailed, WindowReset, AccountAbove, MissionFinished }

public abstract record DutyTrigger
{
    private protected DutyTrigger()
    {
    }

    public abstract DutyTriggerKind Kind { get; }

    /// <summary>Null when the trigger can run; else why not.</summary>
    public abstract string? Problem();
}

/// <summary>Time, in a stored zone (an id <see cref="TimeZoneInfo"/> finds), so a duty keeps its wall-clock meaning across clock changes.</summary>
public sealed record TimeTrigger(TimeSchedule Schedule, string TimeZoneId) : DutyTrigger
{
    public override DutyTriggerKind Kind => DutyTriggerKind.Time;

    public override string? Problem() => DutyZones.Find(TimeZoneId) is null ? "nth does not know that time zone." : Schedule.Problem();
}

/// <summary>
/// New, changed or removed files in a folder, matching a glob. <see cref="Path"/> must be a folder the owner granted: the scheduler
/// refuses to watch any other.
/// </summary>
public sealed record FolderChangeTrigger(string Path) : DutyTrigger
{
    /// <summary>A glob over file names: * and ? only. "*" is every file.</summary>
    public string Pattern { get; init; } = "*";

    public FolderEvents Events { get; init; } = FolderEvents.New;

    public bool Subfolders { get; init; }

    public override DutyTriggerKind Kind => DutyTriggerKind.Folder;

    public override string? Problem()
    {
        if (string.IsNullOrWhiteSpace(Path) || !System.IO.Path.IsPathFullyQualified(Path)) return "Give the folder's full path.";
        if (Events == FolderEvents.None) return "Pick at least one of new, changed or removed files.";
        return Pattern.Length is 0 or > 200 ? "Give a file pattern like *.pdf, or *." : null;
    }
}

/// <summary>
/// A page read on a schedule with plain HTTP (no sign-in, no cookies, no scripts). <see cref="StartMarker"/> and
/// <see cref="EndMarker"/> cut out the region of the page's text that counts (either may be empty: the whole page).
/// </summary>
public sealed record PageWatchTrigger(string Url, int EveryMinutes) : DutyTrigger
{
    public PageCondition Condition { get; init; } = PageCondition.Changed;

    /// <summary>ContainsText: the text to find.</summary>
    public string? Text { get; init; }

    /// <summary>PriceBelow: the amount the first price in the region must be under.</summary>
    public decimal? PriceBelow { get; init; }

    public string? StartMarker { get; init; }

    public string? EndMarker { get; init; }

    public override DutyTriggerKind Kind => DutyTriggerKind.Page;

    public override string? Problem()
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            return "Give the page's http or https address, without a name or password in it.";
        if (EveryMinutes < TimeSchedule.MinimumEveryMinutes) return $"Look no more often than every {TimeSchedule.MinimumEveryMinutes} minutes.";
        if (Condition == PageCondition.ContainsText && string.IsNullOrWhiteSpace(Text)) return "Say which text to look for.";
        return Condition == PageCondition.PriceBelow && PriceBelow is null or <= 0 ? "Say the price it must be below." : null;
    }
}

/// <summary>An app event (5.1): <see cref="Account"/> and <see cref="Window"/> narrow a usage event, <see cref="AbovePercent"/> is the share for AccountAbove.</summary>
public sealed record AppEventTrigger(AppEventKind Event) : DutyTrigger
{
    public string? Account { get; init; }

    public string? Window { get; init; }

    public double? AbovePercent { get; init; }

    public override DutyTriggerKind Kind => DutyTriggerKind.AppEvent;

    public override string? Problem() =>
        Event == AppEventKind.AccountAbove && AbovePercent is null or <= 0 or > 100 ? "Say the share, 1 to 100 percent." : null;
}

/// <summary>No trigger: the owner starts it with Run now. Always available (7.2), whatever the trigger.</summary>
public sealed record ManualTrigger : DutyTrigger
{
    public override DutyTriggerKind Kind => DutyTriggerKind.Manual;

    public override string? Problem() => null;
}

/// <summary>The time zones a duty is stored with.</summary>
public static class DutyZones
{
    /// <summary>The zone for an id (a Windows or an IANA id), or null.</summary>
    public static TimeZoneInfo? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    public static string LocalId => TimeZoneInfo.Local.Id;
}
