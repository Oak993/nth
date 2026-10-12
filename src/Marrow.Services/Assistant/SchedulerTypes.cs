// PORT-ONLY: Dextra (DESIGN.md rev 5, 7; unit AS-10; ED-NEW-dextra-as10-11). No Swift twin.
// What the scheduler asks of the app and tells it. The scheduler lives in Marrow.Services and knows nothing of missions: it hands
// a fire to an IDutyLauncher (Marrow.App starts a mission with Origin Duty), hears when the mission ends, and is told the app
// events a duty may wait for.
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>One run the scheduler wants to begin.</summary>
public sealed record DutyFire(Duty Duty, DutyCause Cause, bool Dry, string? Detail, bool DesktopLocked)
{
    /// <summary>Material from an untrusted source the run starts from: a page's diff, a folder's file names (12.4).</summary>
    public ContentOrigin? Untrusted { get; init; }
}

/// <summary>What starting a run came to: its mission, or why it did not start.</summary>
public sealed record DutyLaunch(Guid? MissionId, string? Refused = null)
{
    public static DutyLaunch Started(Guid mission) => new(mission);

    public static DutyLaunch Refusal(string why) => new(null, why);
}

public interface IDutyLauncher
{
    /// <summary>Starts a run (a mission). May take as long as it needs; the scheduler waits without holding a lock.</summary>
    Task<DutyLaunch> Launch(DutyFire fire);
}

/// <summary>An app event a duty may wait for (7.2), already reduced to facts by the app.</summary>
public sealed record DutyEvent(AppEventKind Kind, string Summary)
{
    public string? Account { get; init; }

    public string? Window { get; init; }

    /// <summary>AccountAbove: the share the window is at now.</summary>
    public double? Percent { get; init; }

    /// <summary>The mission the event is about; a mission's own events never start a duty (it would feed itself).</summary>
    public Guid? Mission { get; init; }
}

public enum DutyFeedKind { Fired, Skipped, Quiet, WatcherChanged, Problem }

/// <summary>A line for the Assistant's live feed (5.1): a duty fired, was skipped, ended quietly, or a watcher found something.</summary>
public sealed record DutyFeedLine(DutyFeedKind Kind, string Text, string DutyId, string AssistantId, Guid? Mission = null);

/// <summary>A duty as the duty list shows it (canvas 5).</summary>
public sealed record DutyView(Duty Duty, DateTimeOffset? NextRun, bool Running, bool Held, string? Status, bool ReadOnly);

public sealed class AssistantSchedulerOptions
{
    public required IDutyStore Store { get; init; }

    public required IDutyLauncher Launcher { get; init; }

    /// <summary>The ids of the assistants whose duties are loaded.</summary>
    public required Func<IReadOnlyList<string>> AssistantIds { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Whether duties may fire (not under Stop all, 12.6). When it turns true again, call Reevaluate.</summary>
    public Func<bool> MayFire { get; init; } = () => true;

    /// <summary>Whether the owner granted nth this folder (7.2). Null: none, so no folder is watched.</summary>
    public Func<string, bool>? FolderGranted { get; init; }

    public WebWatcher? Web { get; init; }

    public Func<bool> DesktopLocked { get; init; } = () => false;

    public Action<DutyFeedLine>? Feed { get; init; }

    public Action<string>? Faulted { get; init; }

    /// <summary>A test's source of folder events (the product uses FileSystemWatcher).</summary>
    public IFolderEventSource? FolderSource { get; init; }

    /// <summary>The longest the scheduler sleeps between looks at the clock, so a sleeping PC or a changed clock is noticed.</summary>
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>A run this far past its time counts as late (a catch-up), not on time.</summary>
    public TimeSpan Late { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>The longest a run is believed to be going; after it the duty may run again.</summary>
    public TimeSpan MaxRun { get; init; } = TimeSpan.FromHours(24);

    /// <summary>The fewest seconds between two runs a duty's app event starts.</summary>
    public TimeSpan EventCooldown { get; init; } = TimeSpan.FromSeconds(30);
}
