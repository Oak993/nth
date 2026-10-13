using Marrow.Core.Support;

namespace Marrow.Setup.Support;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 and 3a, unit U4, ED-NEW-inst-u4-2 and -3). What a <c>--silent</c> setup does before it runs
/// Velopack, with no window to ask anything in: take the install hold, then ask a running nth to quit when idle.
/// <list type="bullet">
/// <item>Another setup holds the root: exit <see cref="InstallerGate.InProgressExitCode"/> (4); nothing was asked, run or changed.</item>
/// <item>nth is idle and quits, or none runs: go ahead, holding the root until Velopack has finished (dispose this).</item>
/// <item>nth is busy, never answered, or will not: exit <see cref="InstallerGate.NotClosedExitCode"/> (3). A silent setup never stops anyone's work
/// and never ends a program, because there is nobody to say yes; the script can run it again.</item>
/// <item>Cancelled through <see cref="IRunningNth.CancelAsync"/> (a console Ctrl+C, a shutdown): exit <see cref="InstallerGate.CancelledExitCode"/> (2).</item>
/// </list>
/// The hold is taken first, so an nth started during the check waits for the install (see <see cref="InstallHold"/>) and the check is not stale
/// by the time Velopack runs.
/// </summary>
public sealed class SilentPreflight : IDisposable
{
    private SilentPreflight(int? exitCode, InstallHold.Hold? hold)
    {
        ExitCode = exitCode;
        Hold = hold;
    }

    /// <summary>The exit code to leave with, or null to go ahead and run Velopack.</summary>
    public int? ExitCode { get; }

    /// <summary>The hold on the root when going ahead; dispose this object when Velopack's <c>Setup.exe</c> has exited.</summary>
    public InstallHold.Hold? Hold { get; }

    public static async Task<SilentPreflight> RunAsync(string installRoot, IRunningNth runningNth, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runningNth);
        var hold = InstallHold.TryAcquire(installRoot);
        if (hold is null) return new SilentPreflight(InstallerGate.InProgressExitCode, null);
        try
        {
            var status = await runningNth.AskAsync(cancellationToken).ConfigureAwait(false);
            int? exit = status.State switch
            {
                RunningNthState.Clear or RunningNthState.Closed => null,
                RunningNthState.Cancelled => InstallerGate.CancelledExitCode,
                _ => InstallerGate.NotClosedExitCode,
            };
            if (exit is null) return new SilentPreflight(null, hold);
            hold.Dispose();
            return new SilentPreflight(exit, null);
        }
        catch
        {
            hold.Dispose();
            throw;
        }
    }

    public void Dispose() => Hold?.Dispose();
}
