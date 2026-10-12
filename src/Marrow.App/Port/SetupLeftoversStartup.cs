// PORT-ONLY: no Swift twin. nth installer PLAN.md 5, "Leftovers", unit U9 (ED-NEW-inst-u9-1).
using Marrow.Core.Support;
using Velopack;

namespace Marrow.App.Port;

/// <summary>
/// nth's part of the setup's leftover sweeps. A crash or power loss in nth Setup leaves a folder under
/// <c>%LOCALAPPDATA%\nth Setup</c> (a staging folder, the setup's native libraries) and the uninstaller can leave its copy in
/// <c>%TEMP%\nth Uninstall</c>; nth removes them when it starts, and its before-uninstall hook does the same in case Velopack's
/// uninstaller is run directly. Both skip everything while an nth Setup window (or the uninstaller's) is open
/// (<see cref="SetupSweeper.SweepUnlessSetupRuns"/>) and leave any folder whose files are in use. Only a Velopack install
/// sweeps: a development run or a test host has no setup of its own. Nothing here throws: a janitor must never stop nth
/// from starting or an uninstall from finishing.
/// </summary>
internal static class SetupLeftoversStartup
{
    /// <summary>At nth's start: sweeps on the pool, once the first window is up. The task ends when the sweep has; it never faults.</summary>
    internal static Task SweepAtStart(string baseDirectory, string? localAppData = null, string? tempPath = null, ISetupRunningProbe? probe = null)
    {
        if (!Program.InstalledByVelopack(baseDirectory)) return Task.CompletedTask;
        return Task.Run(() => Sweep(localAppData, tempPath, probe));
    }

    /// <summary>Adds the sweep to Velopack's before-uninstall hook (Velopack adds callbacks to one multicast delegate, so the skills hook still runs).</summary>
    internal static void RegisterUninstall(Action<VelopackHook> register, string? localAppData = null, string? tempPath = null, ISetupRunningProbe? probe = null)
    {
        ArgumentNullException.ThrowIfNull(register);
        register(_ => Sweep(localAppData, tempPath, probe));
    }

    private static SweepResult Sweep(string? localAppData, string? tempPath, ISetupRunningProbe? probe)
    {
        try
        {
            return SetupSweeper.SweepUnlessSetupRuns(
                localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                tempPath ?? Path.GetTempPath(),
                probe ?? new NamedLockSetupProbe());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return SweepResult.Nothing;
        }
    }
}
