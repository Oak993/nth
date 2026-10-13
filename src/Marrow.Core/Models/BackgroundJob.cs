// PORT-ONLY: web surfaces W5, the data model of the Background tasks pane (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.10;
// ED-NEW-web-w5-1). No Swift counterpart. One job is one immutable snapshot of something running (or lately finished) in the
// background: a Hydra head, a helper chat, a Tactus stream, a Visus agent tab, a terminal or dev server, a download, a workflow.
// The model knows no pixel and no AppModel, so the always-on assistant's mission board reads the same jobs
// (E:/Droppy/work/design/assistant/DESIGN.md 2.3 "Background tasks and the mission board share one data model, BackgroundJobs").

using Marrow.Core.Localisation;

namespace Marrow.Core.Models;

/// <summary>What a job is (the six sources of 3.10, plus workflows, which publish through the same feed).</summary>
public enum BackgroundJobKind
{
    HydraHead,
    Helper,
    Tactus,
    BrowserSession,
    Terminal,
    Download,
    Workflow,
}

/// <summary>Where a job is. Pausing and Stopping are the in-between states the assistant's table names (a stop or a pause asked for, not yet ended).</summary>
public enum BackgroundJobState
{
    Running,
    Pausing,
    Paused,
    Stopping,
    Done,
    Failed,
    Stopped,
}

/// <summary>Which jobs the pane lists (3.10 "Filter segmented: This thread / All threads").</summary>
public enum BackgroundJobScope
{
    ThisThread,
    AllThreads,
}

public static class BackgroundJobStates
{
    /// <summary>Done, Failed or Stopped: the job is over and sits in the Done group.</summary>
    public static bool IsFinished(this BackgroundJobState state) => state is BackgroundJobState.Done or BackgroundJobState.Failed or BackgroundJobState.Stopped;

    /// <summary>Running, Pausing, Paused or Stopping: the job is still there to act on.</summary>
    public static bool IsLive(this BackgroundJobState state) => !state.IsFinished();
}

/// <summary>
/// One job at one moment. Sources hand these to <see cref="BackgroundJobs"/> on every tick; nothing here is mutated in place.
/// The live fields (<see cref="Tokens"/>, <see cref="ToolUses"/>, <see cref="Activity"/>, <see cref="Progress"/>, <see cref="Subject"/>,
/// <see cref="FinishedAt"/>) change as the job runs; every other field is its <see cref="Shape"/>: a change in it changes the list
/// the pane draws, a change only in the live fields changes text.
/// </summary>
public sealed record BackgroundJob
{
    /// <summary>The job's identity, unique across sources ("hydra:" + the head's thread id). A job keeps its id for its whole life.</summary>
    public required string Id { get; init; }

    public required BackgroundJobKind Kind { get; init; }

    /// <summary>The card's title (the head's task, the script's name, the file being downloaded).</summary>
    public required string Title { get; init; }

    /// <summary>The kind's name on the card's second line ("Hydra head", "Tactus", "Dev server", "Download").</summary>
    public required string KindLabel { get; init; }

    /// <summary>What follows the kind after a middle dot ("Otto", "Excel", "localhost:5173", "3.1 of 5.0 GB"); live for a download.</summary>
    public string? Subject { get; init; }

    /// <summary>The thread the job belongs to; null for an app-wide job (a download), which every thread's list shows.</summary>
    public Guid? ThreadId { get; init; }

    public BackgroundJobState State { get; init; } = BackgroundJobState.Running;

    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When the job ended; a source that cannot tell leaves it null and <see cref="BackgroundJobs"/> stamps the first moment it saw the job over.</summary>
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>Tokens spent so far, for a job that spends them.</summary>
    public long? Tokens { get; init; }

    /// <summary>Tool uses so far, for a job that makes them.</summary>
    public long? ToolUses { get; init; }

    /// <summary>What the job is doing now ("Reading", "Running a command", "Typing in Visus"); null when it does not say.</summary>
    public string? Activity { get; init; }

    /// <summary>0 to 1 for a job that knows how far along it is (a download); null otherwise.</summary>
    public double? Progress { get; init; }

    /// <summary>The address a running dev server answers on; the card then offers "Open preview".</summary>
    public string? DevServerUrl { get; init; }

    /// <summary>The thread whose transcript "View transcript" opens (a head's own chat); null when the job has none.</summary>
    public Guid? TranscriptThreadId { get; init; }

    /// <summary>The job can be paused (and resumed): Tactus, downloads, browser sessions, workflows.</summary>
    public bool CanPause { get; init; }

    /// <summary>The job can be stopped.</summary>
    public bool CanStop { get; init; } = true;

    /// <summary>Groups jobs (the mission a head belongs to); unused by the pane, kept for the assistant's board.</summary>
    public string? GroupId { get; init; }

    public bool IsFinished => State.IsFinished();

    public bool IsLive => State.IsLive();

    public bool IsPaused => State is BackgroundJobState.Paused or BackgroundJobState.Pausing;

    public bool HasTranscript => TranscriptThreadId is not null;

    public bool IsDevServer => DevServerUrl is not null;

    /// <summary>The second line of the card: the kind, then the subject.</summary>
    public string KindText => string.IsNullOrEmpty(Subject) ? KindLabel : UiText.Format("jobs.kind_text", KindLabel, Subject);

    /// <summary>How long the job has run at <paramref name="now"/> (frozen at its end), never negative.</summary>
    public TimeSpan Elapsed(DateTimeOffset now)
    {
        var span = (FinishedAt ?? now) - StartedAt;
        return span < TimeSpan.Zero ? TimeSpan.Zero : span;
    }

    /// <summary>How long ago the job ended, or zero for one that has not.</summary>
    public TimeSpan Age(DateTimeOffset now)
    {
        if (FinishedAt is not { } finished) return TimeSpan.Zero;
        var span = now - finished;
        return span < TimeSpan.Zero ? TimeSpan.Zero : span;
    }

    /// <summary>Whether <paramref name="other"/> would be drawn by the same card: everything but the live fields matches.</summary>
    public bool SameShape(BackgroundJob? other) =>
        other is not null
        && Id == other.Id && Kind == other.Kind && Title == other.Title && KindLabel == other.KindLabel
        && ThreadId == other.ThreadId && State == other.State && StartedAt == other.StartedAt
        && CanPause == other.CanPause && CanStop == other.CanStop
        && HasTranscript == other.HasTranscript && DevServerUrl == other.DevServerUrl
        && (Tokens is not null) == (other.Tokens is not null) && (ToolUses is not null) == (other.ToolUses is not null)
        && (Progress is not null) == (other.Progress is not null) && (Activity is not null) == (other.Activity is not null)
        && (FinishedAt is not null) == (other.FinishedAt is not null);
}
