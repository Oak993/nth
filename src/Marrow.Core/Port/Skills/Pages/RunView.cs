// PORT-ONLY: first-release feature unit SK-15 (DL-17, OD-0099 "Download all A"). What the Download all dialog shows in each phase: a value the view draws, never a state it keeps.
namespace Marrow.Core.Port.Skills.Pages;

/// <summary>
/// The phases of the dialog (the design's confirm, dl, paused, done and partial). Nothing downloads before Continue; Cancel in the confirmation leaves the dialog and starts nothing.
/// </summary>
public enum RunPhase
{
    None,
    Confirm,
    Downloading,
    Paused,
    Done,
    Partial,
}

public enum RunLineState
{
    AlreadyHere,
    NeedsTerms,
    NoSize,
    CantFetch,

    /// <summary>ED-NEW-dl-13: a skill its set leaves out; never fetched (<see cref="PlanLineKind.SkippedSkill"/>).</summary>
    SkippedSkill,
    Waiting,
    Downloading,
    Checking,
    Downloaded,
    Failed,
}

/// <summary>One line of the "Included" list. <see cref="Right"/> is the words at its right end; <see cref="Fraction"/> is 0 to 1 while it downloads.</summary>
public sealed record RunLine(string Id, string Title, RunLineState State, double Fraction, string Right);

/// <summary>
/// The dialog in one phase. The confirmation carries the statement (the large-download warning above 100 MB, the plain size below), the facts and whether Continue is on; a running
/// dialog carries the overall progress; the end phases carry the counts. <see cref="Lines"/> is the whole included list in the page's order.
/// </summary>
public sealed record RunView(
    RunPhase Phase,
    string Title,
    string Sub,
    string? Statement,
    bool Large,
    string Foot,
    string? ProgressText,
    double Overall,
    IReadOnlyList<RunLine> Lines,
    string IncludedHeading,
    string DownloadFact,
    string DiskFact,
    string? FreeLabel,
    string? FreeFact,
    string HostsFact,
    string? RoomNote,
    bool CanContinue,
    int Failed)
{
    public static RunView Closed { get; } = new(RunPhase.None, "", "", null, false, "", null, 0, [], "", "", "", null, null, "", null, false, 0);

    public bool IsOpen => Phase != RunPhase.None;

    /// <summary>The run is going or paused: Cancel stops the rest, Pause or Resume swaps.</summary>
    public bool IsRunning => Phase is RunPhase.Downloading or RunPhase.Paused;
}
