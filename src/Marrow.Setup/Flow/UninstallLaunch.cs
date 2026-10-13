using Marrow.Core.Support;

namespace Marrow.Setup.Flow;

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 8, "Uninstall flow"; VELOPACK_CHECK.md check 7; unit U6, ED-NEW-inst-u6-1).
/// How uninstall mode starts. Settings › Apps › nth › Uninstall runs <c>"&lt;root&gt;\nth Uninstall.exe" --uninstall</c>, a
/// program inside the folder Velopack is about to empty, and Velopack's <c>Update.exe</c> stops every process running from
/// the root before it removes anything. So a run from inside the root never shows its window: before its first frame it
/// copies itself to <c>%TEMP%\nth Uninstall\</c>, starts the copy with the root named (<c>--installto</c>) and
/// <see cref="CopyFlag"/>, and exits; the copy shows the one window and does the whole uninstall. (A window cannot move
/// between processes without a visible swap, so the copy is made at start, not on confirm; Cancel then only leaves the copy
/// for its own exit to remove.) A run from anywhere else (a downloaded nth Setup started with <c>--uninstall</c>) runs in place.
/// </summary>
public static class UninstallLaunch
{
    /// <summary>The copy's own mark: it waits for the first process to let go of the setup lock, and never copies itself again.</summary>
    public const string CopyFlag = "--uninstall-copy";

    /// <summary>The copy's file name in <c>%TEMP%\nth Uninstall\</c> (the same as the root's, so Task Manager reads the same).</summary>
    public const string CopyFileName = InstallLayout.RootUninstaller;

    /// <summary>How long the copy waits for the first process to exit and free the setup lock.</summary>
    public static readonly TimeSpan HandOffPatience = TimeSpan.FromSeconds(10);

    /// <summary><see cref="CopyFlag"/> before any <c>--</c>: this process is the temporary copy.</summary>
    public static bool IsCopy(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        foreach (var argument in arguments)
        {
            if (argument == "--") return false;
            if (string.Equals(argument, CopyFlag, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// The root to remove: <c>--installto</c> when given (the copy is always started with it); else the folder this program
    /// runs from when it holds <c>Update.exe</c> (the root's own <c>nth Uninstall.exe</c>); else the uninstall entry's
    /// <c>InstallLocation</c> when that folder exists; else <c>%LOCALAPPDATA%\NthApp</c> (1.0.0's root). Unlike the install
    /// route, a root is never passed over for an unreadable version: whatever Velopack can still remove is removed.
    /// </summary>
    public static string FindRoot(IReadOnlyList<string> arguments, string? processPath, IUninstallKeys keys, string localAppData, string launchDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(keys);
        if (InstallerGate.ReadInstallTo(arguments, launchDirectory) is { } installTo) return Path.TrimEndingDirectorySeparator(Path.GetFullPath(installTo));
        if (processPath is not null && Path.GetDirectoryName(processPath) is { } own && File.Exists(Path.Combine(own, InstallLayout.UpdateExe)))
            return Path.TrimEndingDirectorySeparator(own);
        var location = keys.InstallLocation(InstallLayout.AppId)?.Trim().Trim('"');
        if (!string.IsNullOrEmpty(location) && Path.IsPathFullyQualified(location) && Directory.Exists(location))
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(location));
        return Path.Combine(localAppData, InstallLayout.AppId);
    }

    /// <summary>A run from inside the root (and not already the copy) hands off to a temporary copy.</summary>
    public static bool NeedsCopy(IReadOnlyList<string> arguments, string? processPath, string root) =>
        !IsCopy(arguments) && processPath is not null && InstallLayout.IsInside(root, processPath);

    /// <summary>The copy's arguments: uninstall mode, the root, the copy's mark, and <c>--silent</c> when this run is silent. Nothing else is carried.</summary>
    public static IReadOnlyList<string> CopyArguments(string root, bool silent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        List<string> arguments = ["--uninstall", "--installto", root, CopyFlag];
        if (silent) arguments.Add("--silent");
        return arguments;
    }

    /// <summary>
    /// Writes this program to <c>%TEMP%\nth Uninstall\nth Uninstall.exe</c> (through a temporary file and one replacing move,
    /// so a copy is never half-written); when that name is held by a copy still running, a name of its own beside it.
    /// Returns the copy's path. The caller holds the folder's claim (unit U9) from before this call until the copy runs, so no
    /// sweep takes the copy in between.
    /// </summary>
    public static string WriteCopy(string processPath, string tempPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processPath);
        var folder = Directory.CreateDirectory(SetupFolders.UninstallTemp(tempPath)).FullName;
        var target = Path.Combine(folder, CopyFileName);
        var temporary = Path.Combine(folder, CopyFileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.Copy(processPath, temporary, overwrite: false);
            try
            {
                File.Move(temporary, target, overwrite: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // the usual name is a copy that still runs (or is held open): this run takes a name of its own
                target = Path.Combine(folder, Path.GetFileNameWithoutExtension(CopyFileName) + " " + Guid.NewGuid().ToString("N")[..8] + ".exe");
                File.Move(temporary, target, overwrite: false);
            }
            return target;
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } // moved already, or nothing to undo
        }
    }

    /// <summary>
    /// Claims <c>%TEMP%\nth Uninstall</c>, writes the copy and starts it with <see cref="CopyArguments"/> in its own folder (never
    /// in the root, which Velopack removes). <paramref name="start"/> starts a program (path, arguments, working folder) and
    /// returns false when it could not. Returns the copy's path when it started, else null.
    /// </summary>
    public static string? HandOff(string processPath, string root, string tempPath, bool silent, Func<string, IReadOnlyList<string>, string, bool> start)
    {
        ArgumentNullException.ThrowIfNull(start);
        try
        {
            using var claim = FolderClaim.Take(SetupFolders.UninstallTemp(tempPath));
            var copy = WriteCopy(processPath, tempPath);
            return start(copy, CopyArguments(root, silent), Path.GetDirectoryName(copy)!) ? copy : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The copy's last step when the cleanup helper (<see cref="UninstallCopyCleanup"/>, ED-NEW-inst-picks-1) could not start
    /// (PLAN.md 8: "the temp copy deletes itself"). A running program cannot delete its own file, so the copy and its folder are
    /// given to Windows for removal at the next restart (<c>MoveFileEx</c> with <c>MOVEFILE_DELAY_UNTIL_REBOOT</c>, through
    /// <paramref name="deleteAtRestart"/>). Windows allows that only to an administrator; otherwise the copy stays until the next
    /// nth Setup run or nth start sweeps <c>%TEMP%\nth Uninstall</c> (unit U9), or Windows cleans its temporary files. Only a
    /// program that runs from inside <c>%TEMP%\nth Uninstall</c> asks. Returns whether Windows took the file.
    /// </summary>
    public static bool ScheduleCopyRemoval(string? processPath, string tempPath, Func<string, bool> deleteAtRestart)
    {
        ArgumentNullException.ThrowIfNull(deleteAtRestart);
        var folder = SetupFolders.UninstallTemp(tempPath);
        if (processPath is null || !InstallLayout.IsInside(folder, processPath) || InstallLayout.IsInside(processPath, folder)) return false;
        var taken = deleteAtRestart(processPath);
        if (taken) _ = deleteAtRestart(folder); // removed at restart only if it is empty by then
        return taken;
    }
}
