namespace Marrow.Setup.Support;

/// <summary>
/// An nth process: its id, its file and when it started. The start time stops a recycled process id from being taken for
/// nth (zero means "unknown": the id alone is used).
/// </summary>
public sealed record RunningNthProcess(int ProcessId, string ExecutablePath, long StartedAtTicks = 0);

/// <summary>How ending a process went.</summary>
public enum ProcessEndResult
{
    /// <summary>It was ended and is gone.</summary>
    Ended,

    /// <summary>It was already gone (or its id now belongs to another program): nothing was ended.</summary>
    Gone,

    /// <summary>Windows refused (an elevated nth, or another user's): nothing was ended.</summary>
    AccessDenied,

    /// <summary>It could not be ended or did not go away in time.</summary>
    Failed,
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3a and 10, unit U4, ED-NEW-inst-u4-1). The processes of nth inside an install root, the only
/// ones that matter to the setup (Velopack stops every process in the root; a portable or development copy elsewhere is not in
/// its way). Production: <c>Port/WindowsRunningNthProcesses.cs</c>. The calls can take a moment (a process list; ending a
/// process), so the handshake makes them off the window's thread.
/// </summary>
public interface IRunningNthProcesses
{
    /// <summary>The nth programs (<c>nth.exe</c>, and <c>Marrow.App.exe</c> of an install made before the rename) whose file lies inside <paramref name="installRoot"/>.</summary>
    IReadOnlyList<RunningNthProcess> FindInside(string installRoot);

    /// <summary>True while this very process (same id, same start) runs.</summary>
    bool IsRunning(RunningNthProcess process);

    /// <summary>
    /// <c>TerminateProcess</c>, and a short wait for it to go. Only ever called after the person said yes (PLAN.md 3a, step 3).
    /// Does nothing to an id that no longer is this process.
    /// </summary>
    ProcessEndResult End(RunningNthProcess process);
}
