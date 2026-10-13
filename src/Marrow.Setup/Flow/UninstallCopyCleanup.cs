using System.ComponentModel;
using System.Globalization;
using Marrow.Core.Support;

namespace Marrow.Setup.Flow;

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 8, "The temp copy deletes itself"; ED-NEW-inst-picks-1, which settles the
/// open pick of ED-NEW-inst-fix-2 as option (b)). The uninstaller runs as a copy in <c>%TEMP%\nth Uninstall</c> so that it can
/// remove nth's folder, and a running program cannot delete its own file. As the copy ends it starts one short-lived helper:
/// Windows' own <c>cmd.exe</c>, hidden, which waits for the copy's process to end and then removes exactly that folder, and
/// ends. The helper is the only one: it leaves nothing behind (no <c>RunOnce</c> entry, no scheduled task, no second exe), and
/// it is bounded (<see cref="Rounds"/> rounds of about a second). Nothing is started unless every fact below holds, and the
/// folder is named in full, never by a wildcard.
/// <list type="bullet">
/// <item>The running program is the copy: it lies directly in <c>%TEMP%\nth Uninstall</c>, the folder under the temp folder
/// that <see cref="SetupFolders.UninstallTemp"/> names.</item>
/// <item>nth made the folder: it carries the setup's ownership marker with exactly its text, neither it nor any ancestor up to
/// nth's own top folder is a reparse point, and it holds only plain files (no folder, no link), at most
/// <see cref="MaxEntries"/>.</item>
/// <item>The folder's path and the system folder's can be written between quotes on a command line (no <c>%</c>, <c>"</c> or
/// control character), and <c>cmd.exe</c>, <c>tasklist.exe</c>, <c>find.exe</c> and <c>ping.exe</c> exist there; the helper
/// calls each by its full path, so no search path or current folder decides what runs.</item>
/// </list>
/// A helper that cannot start (or a refusal above) leaves the folder to the restart removal and the sweeps (unit U9), as
/// before. The helper's own last check is the one the file system makes: a folder replaced by a link after the checks is
/// removed as the link alone, since <c>rd /s</c> never follows one.
/// </summary>
public static class UninstallCopyCleanup
{
    /// <summary>How many rounds the helper makes (about one second each): it waits for the process to end, then removes the folder, trying again if a scanner holds a file.</summary>
    public const int Rounds = 90;

    /// <summary>The most entries the folder may hold (the copy, its native libraries, logs, the marker and a claim).</summary>
    public const int MaxEntries = 64;

    /// <summary>What to start: the shell, its command line and its working folder (never the folder to remove).</summary>
    public sealed record Helper(string Folder, string FileName, string Arguments, string WorkingDirectory);

    /// <summary>
    /// The helper for the copy running as <paramref name="processPath"/> with id <paramref name="processId"/>, or null when any
    /// fact in the type's summary does not hold. <paramref name="systemDirectory"/> is Windows' system folder.
    /// </summary>
    public static Helper? Plan(string? processPath, string tempPath, int processId, string systemDirectory)
    {
        if (processId <= 0 || !IsFull(processPath) || !IsFull(tempPath) || !IsFull(systemDirectory)) return null;
        try
        {
            var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(tempPath));
            var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(SetupFolders.UninstallTemp(tempPath)));
            var system = Path.TrimEndingDirectorySeparator(Path.GetFullPath(systemDirectory));
            if (!string.Equals(Path.GetDirectoryName(folder), temp, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(folder), SetupFolders.UninstallTempFolderName, StringComparison.Ordinal)) return null;
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(processPath!)), folder, StringComparison.OrdinalIgnoreCase)) return null;
            if (!Quotable(folder) || !Quotable(system)) return null;
            foreach (var tool in Tools)
                if (!File.Exists(Path.Combine(system, tool))) return null;
            if (!NthMade(folder)) return null;
            return new Helper(folder, Path.Combine(system, "cmd.exe"), Script(system, folder, processId), system);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts the helper through <paramref name="start"/> (file, command line, working folder; false when it could not start).
    /// Returns whether a helper is running; false leaves the folder to the restart removal and the sweeps.
    /// </summary>
    public static bool Begin(string? processPath, string tempPath, int processId, string systemDirectory, Func<string, string, string, bool> start)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (Plan(processPath, tempPath, processId, systemDirectory) is not { } helper) return false;
        try
        {
            return start(helper.FileName, helper.Arguments, helper.WorkingDirectory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// The helper's command line: no command extensions that a registry switch could turn off or on (<c>/d</c> skips AutoRun,
    /// <c>/v:off</c> keeps <c>!</c> literal), one loop of <see cref="Rounds"/> rounds. A round asks <c>tasklist</c> for the
    /// process id and looks for it in the answer: while it is there the round waits about a second (<c>ping</c> to the loopback,
    /// the one wait that works with no console); once it is gone the round removes the folder, ends the shell when the folder is
    /// gone, and otherwise waits and tries again. Every program is called by its full path.
    /// </summary>
    internal static string Script(string system, string folder, int processId)
    {
        var id = processId.ToString(CultureInfo.InvariantCulture);
        string Tool(string name) => Quote(Path.Combine(system, name));
        var wait = Tool("ping.exe") + " -n 2 127.0.0.1 >nul";
        var gone = Quote(folder);
        var round = Tool("tasklist.exe") + " /nh /fi \"PID eq " + id + "\" 2>nul | " + Tool("find.exe") + " \"" + id + "\" >nul"
            + " & if errorlevel 1 (rd /s /q " + gone + " 2>nul & (if not exist " + gone + " exit) & " + wait + ") else (" + wait + ")";
        return "/d /v:off /s /c \"for /l %n in (1," + "1," + Rounds.ToString(CultureInfo.InvariantCulture) + ") do @(" + round + ")\"";
    }

    private static readonly string[] Tools = ["cmd.exe", "tasklist.exe", "find.exe", "ping.exe"];

    private static string Quote(string path) => "\"" + path + "\"";

    private static bool IsFull(string? path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);

    /// <summary>Whether the path can sit between quotes on a command line as it is: <c>%</c> would be expanded, <c>"</c> would end the quote.</summary>
    private static bool Quotable(string path)
    {
        foreach (var c in path)
            if (c is '%' or '"' || char.IsControl(c)) return false;
        return true;
    }

    /// <summary>The folder is the one nth made: marked, a plain folder (no link above it up to nth's own top), holding plain files only.</summary>
    private static bool NthMade(string folder)
    {
        SetupFolders.RequireNoLinks(folder);
        var attributes = File.GetAttributes(folder);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0) return false;
        if (!SetupFolders.HasOwnMarker(folder)) return false;
        var count = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
        {
            if (++count > MaxEntries) return false;
            if ((File.GetAttributes(entry) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return false;
        }
        return true;
    }
}
