using Marrow.Core.Support;

namespace Marrow.Setup.Support;

/// <summary>Where the running-nth handshake stands (PLAN.md 3a). The screens (unit U2) draw from this and never from the pipe.</summary>
public enum RunningNthState
{
    /// <summary>Nothing has been asked yet.</summary>
    NotAsked,

    /// <summary>Looking for nth in the install root and asking it. Takes a moment; show nothing new.</summary>
    Asking,

    /// <summary>No nth runs inside the root, and nothing was asked of anything: go ahead.</summary>
    Clear,

    /// <summary>
    /// nth answered "busy": <see cref="RunningNthStatus.Work"/> lists what it is doing. Nothing has changed; nth was not asked to quit.
    /// The canvas's "nth is still working": <see cref="IRunningNth.WaitForWorkToFinishAsync"/> is "Update when they finish",
    /// <see cref="IRunningNth.StopWorkAndQuitAsync"/> is "Stop them and update".
    /// </summary>
    Busy,

    /// <summary>The setup waits for the work to finish and nth will quit by itself the moment it is idle ("Update waits for nth"). The list shrinks as work ends.</summary>
    Waiting,

    /// <summary>
    /// nth agreed and is saving and quitting. <see cref="RunningNthStatus.FinishingMerge"/> says it is first finishing a team merge, which it
    /// waits for (up to four minutes) the way closing its window does.
    /// </summary>
    Closing,

    /// <summary>
    /// nth is still there 15 s after it agreed, or never answered (<see cref="RunningNthStatus.Reason"/> says which): "nth isn't responding. Close it
    /// anyway?" Nothing is forced. <see cref="IRunningNth.EndItAsync"/> is the person's yes, <see cref="IRunningNth.KeepWaitingAsync"/> the no.
    /// </summary>
    NotResponding,

    /// <summary>The person said yes and nth is being ended.</summary>
    Ending,

    /// <summary>An nth that was running is gone: go ahead.</summary>
    Closed,

    /// <summary>nth heard the request and will not do it, or cannot be reached or ended (<see cref="RunningNthStatus.Reason"/>). Stop here and say so.</summary>
    Refused,

    /// <summary>The person gave up (Close, Stop waiting, Cancel). Anything nth was told is called off; nothing was changed or closed.</summary>
    Cancelled,
}

/// <summary>
/// One reading of the handshake. <see cref="Work"/> is what nth said it is doing (names are cleaned for display, longest running first,
/// at most 20; <see cref="WorkCount"/> is how many in all), kept from the last "busy" for <see cref="RunningNthState.Busy"/>,
/// <see cref="RunningNthState.Waiting"/> and what follows.
/// </summary>
public sealed record RunningNthStatus(
    RunningNthState State,
    IReadOnlyList<NthWorkItem> Work,
    int WorkCount,
    bool FinishingMerge = false,
    string? Reason = null)
{
    /// <summary>The first reading, before anything was asked.</summary>
    public static RunningNthStatus Initial { get; } = new(RunningNthState.NotAsked, [], 0);

    /// <summary>True when the install may go ahead: nothing ran in the root, or what ran has gone.</summary>
    public bool CanProceed => State is RunningNthState.Clear or RunningNthState.Closed;

    /// <summary>True when the handshake has nothing more to do until it is driven again.</summary>
    public bool IsSettled => State is RunningNthState.Clear or RunningNthState.Closed or RunningNthState.Refused or RunningNthState.Cancelled
        or RunningNthState.Busy or RunningNthState.NotResponding or RunningNthState.NotAsked;
}

/// <summary>Every wait of the handshake. The defaults are the plan's; tests pass shorter ones or move a fake clock.</summary>
/// <param name="ExitGrace">How long nth has to exit after it agreed (and after a merge it was finishing ends): 15 s (PLAN.md 3a, step 3).</param>
/// <param name="ExitPoll">How often the setup looks whether nth is gone.</param>
/// <param name="StartupPatience">How long a running nth that has no pipe yet is given before "isn't responding" (it may be starting).</param>
/// <param name="AskRetry">The pause between asks while the pipe is not there yet.</param>
/// <param name="WorkPoll">How often "Update when they finish" asks again whether nth is idle.</param>
/// <param name="WorkFailureLimit">How many asks in a row may go unanswered, with nth still running, before "isn't responding".</param>
/// <param name="MergePoll">While nth finishes a merge before quitting, how often the setup asks whether it still does (so the 15 s do not run out under a merge).</param>
/// <param name="WithdrawTimeout">How long calling off a request may take; it is best effort.</param>
public sealed record RunningNthTimings(
    TimeSpan ExitGrace,
    TimeSpan ExitPoll,
    TimeSpan StartupPatience,
    TimeSpan AskRetry,
    TimeSpan WorkPoll,
    int WorkFailureLimit,
    TimeSpan MergePoll,
    TimeSpan WithdrawTimeout)
{
    public static RunningNthTimings Default { get; } = new(
        ExitGrace: TimeSpan.FromSeconds(15),
        ExitPoll: TimeSpan.FromMilliseconds(100),
        StartupPatience: TimeSpan.FromSeconds(5),
        AskRetry: TimeSpan.FromMilliseconds(500),
        WorkPoll: TimeSpan.FromSeconds(2),
        WorkFailureLimit: 3,
        MergePoll: TimeSpan.FromSeconds(1),
        WithdrawTimeout: TimeSpan.FromSeconds(1));
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3a, unit U4, ED-NEW-inst-u4-1). What the screens (unit U2) drive: ask a running nth to quit,
/// wait for its work, stop its work, and the person's yes or no to ending it. One object per attempt (Install, Retry, Update); it runs
/// one operation at a time and may be asked again after it settles. Every operation returns the status it settled on, and
/// <see cref="StatusChanged"/> reports each change on the way (it is raised on whatever thread runs the operation, so a window
/// posts it to its own).
/// <para>
/// The flow, by the plan's numbers: <see cref="AskAsync"/> (1) finds nth in the root and asks. Closed: <see cref="RunningNthState.Clear"/>.
/// Open and agrees: <see cref="RunningNthState.Closing"/> then <see cref="RunningNthState.Closed"/>. Busy (2): <see cref="RunningNthState.Busy"/>, then
/// <see cref="WaitForWorkToFinishAsync"/> or <see cref="StopWorkAndQuitAsync"/>. Agreed but still there after 15 s (3): <see cref="RunningNthState.NotResponding"/>,
/// then <see cref="EndItAsync"/> (only on yes) or <see cref="KeepWaitingAsync"/>. The same object serves Retry and a fresh install (4):
/// ask again. <see cref="CancelAsync"/> is Close and Stop waiting.
/// </para>
/// </summary>
public interface IRunningNth
{
    /// <summary>Where it stands now.</summary>
    RunningNthStatus Status { get; }

    /// <summary>Raised after each change of <see cref="Status"/>, with the new reading.</summary>
    event Action<RunningNthStatus>? StatusChanged;

    /// <summary>
    /// True when an nth program runs inside the install root. Nothing is asked of it and the status does not change: this is what the Update
    /// screen needs to say "nth is open: it saves your work and closes for a moment" before the person has pressed Update.
    /// </summary>
    Task<bool> IsNthOpenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks for nth in the install root and, if it runs, asks it to quit when idle; waits for it to be gone. Settles on Clear, Closed, Busy,
    /// NotResponding, Refused or Cancelled. Allowed from any settled state (Retry asks again).
    /// </summary>
    Task<RunningNthStatus> AskAsync(CancellationToken cancellationToken = default);

    /// <summary>"Update when they finish": from Busy. Asks again every couple of seconds until nth is idle, then it quits; waits for it to be gone.</summary>
    Task<RunningNthStatus> WaitForWorkToFinishAsync(CancellationToken cancellationToken = default);

    /// <summary>"Stop them and update": from Busy. nth stops what is running the way closing its window does, saves and quits; waits for it to be gone.</summary>
    Task<RunningNthStatus> StopWorkAndQuitAsync(CancellationToken cancellationToken = default);

    /// <summary>The person's yes to "Close it anyway?": from NotResponding. Ends every nth program inside the root, then reports Closed (or Refused if Windows would not let it).</summary>
    Task<RunningNthStatus> EndItAsync(CancellationToken cancellationToken = default);

    /// <summary>The person's no: from NotResponding. Nothing is forced; waits another 15 s (or asks again when nth never answered).</summary>
    Task<RunningNthStatus> KeepWaitingAsync(CancellationToken cancellationToken = default);

    /// <summary>Close, Stop waiting, Cancel: stops what is running, calls off a quit nth was told to do, and settles on Cancelled. Returns when that is done.</summary>
    Task CancelAsync();
}
