using Marrow.Core.Support;

namespace Marrow.Setup.Support;

/// <summary>What nth Setup did after Velopack's Setup.exe exited 0.</summary>
/// <param name="StartPresent"><c>Programs\nth.lnk</c> exists once the step is done (VELOPACK_CHECK.md check 14).</param>
/// <param name="OldStart">The old Start folder clean-up, run again in case the hook was killed before it.</param>
/// <param name="Start">The Start AppUserModelID re-stamp.</param>
/// <param name="Desktop">The alignment of the desktop shortcut nth made to Start (a repair; it never creates).</param>
/// <param name="UninstallKey">The uninstall entry re-point.</param>
/// <param name="ResultUpdated"><c>install-result.json</c> was rewritten to record the re-stamp.</param>
public sealed record AfterSetupExitReport(
    bool StartPresent,
    OldStartLinkResult OldStart,
    StartLinkResult Start,
    ShortcutChangeResult? Desktop,
    UninstallRepointResult? UninstallKey,
    bool ResultUpdated);

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 8, item 1, and 8a's table row "After Setup.exe exits 0"; unit U5,
/// ED-NEW-inst-u5-1). Velopack writes the uninstall key after the install hook, and may write Start after it, so nth Setup
/// finishes the job once Setup.exe has exited 0, before Close is allowed (milliseconds; the desktop scan keeps the hook's
/// cap): it moves 1.0.0's Start link if the hook did not, re-stamps Start's AppUserModelID if needed, aligns a desktop
/// shortcut nth made to Start (repair only), and re-points the uninstall entry to <c>&lt;root&gt;\nth Uninstall.exe</c> with
/// <c>NoModify</c>, <c>NoRepair</c> and the measured <c>EstimatedSize</c>. A re-stamp is recorded in
/// <c>install-result.json</c>. Each part catches its own shell or registry error; this never throws.
/// </summary>
public static class AfterSetupExit
{
    public static AfterSetupExitReport Run(IShellLinks shell, IUserRegistry registry, string root, string? resultPath,
        Func<string, long?>? measureFootprintKb = null, Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var measure = measureFootprintKb ?? UninstallEntry.MeasureFootprintKb;
        var exists = fileExists ?? File.Exists;
        var shortcut = new DesktopShortcut(shell, root);

        var oldStart = shortcut.MoveOldStartLink();
        var start = shortcut.RestampStart();
        var startPresent = Safe(() => shell.FileExists(shortcut.StartLinkPath));
        var desktop = Safe(() => shortcut.Repair(ShortcutScan.Hook));
        var key = Safe(() => UninstallEntry.Repoint(registry, root, measure(root), exists(InstallLayout.UninstallerPath(root))));

        var resultUpdated = false;
        if (start.Restamped && resultPath is not null && InstallResult.Read(resultPath) is { StartAppIdRestamped: false } result)
            resultUpdated = Safe(() =>
            {
                (result with { StartAppIdRestamped = true }).Write(resultPath);
                return true;
            });
        return new AfterSetupExitReport(startPresent, oldStart, start, desktop, key, resultUpdated);
    }

    private static T? Safe<T>(Func<T> step)
    {
        try { return step(); }
        catch (Exception error) when (DesktopShortcut.IsShellFailure(error) || error is System.Security.SecurityException)
        {
            return default;
        }
    }
}
