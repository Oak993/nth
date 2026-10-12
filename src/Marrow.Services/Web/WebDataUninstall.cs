using Marrow.Core.Support;

namespace Marrow.Services.Web;

/// <summary>How the web data removal ended (W23).</summary>
/// <param name="Removed">The folder is gone (or never was).</param>
/// <param name="Left">What could not be deleted when the budget ran out; empty when <paramref name="Removed"/>.</param>
/// <param name="OutOfTime">The budget ran out before everything was deleted.</param>
/// <param name="Attempts">How many passes it made.</param>
/// <param name="Refused">The path was not a <c>nth\WebView2</c> folder, so nothing was touched.</param>
public sealed record WebDataUninstallReport(bool Removed, IReadOnlyList<string> Left, bool OutOfTime, int Attempts, bool Refused = false);

/// <summary>
/// DESIGN 2.3, 13.4 (W23): the web part of Velopack's before-uninstall hook (<c>OnBeforeUninstallFastCallback</c>; Velopack gives a
/// hook 30 seconds in all, shared with the skills and install hooks). It deletes <c>%LOCALAPPDATA%\nth\WebView2</c>, the one user
/// data folder every Visus profile lives in, so an uninstalled nth leaves no cookies, logins or cache behind.
/// ENGINEERING DECISIONS (ED-NEW-web-w23-3): (1) it deletes only a folder named <c>WebView2</c> directly under a folder named
/// <c>nth</c>, and nothing else, so a wrong path can never reach another folder; (2) it never follows a link: a junction or
/// symbolic link in the tree is removed itself and its target stays; (3) the browser process group of nth's web views may take
/// a moment to exit after nth does, and holds files until it does, so it makes up to <see cref="DefaultBudget"/> of passes with a
/// growing pause (a held file costs only its own ancestors, as in the skills hook); (4) read-only files (Chromium marks some) are
/// made writable first; (5) it never throws into Velopack and ends inside its budget whatever is still held.
/// </summary>
public static class WebDataUninstall
{
    /// <summary>Five seconds: the skills hook may use 20 of Velopack's 30 and the install hook needs the rest.</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan FirstPause = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LongestPause = TimeSpan.FromSeconds(1);

    /// <param name="folder">The web data folder (<see cref="WebDataRoot.Resolve"/>).</param>
    /// <param name="budget">How long the passes may take in all.</param>
    /// <param name="clock">The clock the budget is read from.</param>
    /// <param name="wait">Pauses between passes (<see cref="Thread.Sleep(TimeSpan)"/> by default).</param>
    public static WebDataUninstallReport BeforeUninstall(string folder, TimeSpan? budget = null, TimeProvider? clock = null, Action<TimeSpan>? wait = null)
    {
        if (!IsWebDataFolder(folder)) return new(false, [], false, 0, Refused: true);
        var time = clock ?? TimeProvider.System;
        var pause = wait ?? (span => Thread.Sleep(span));
        var deadline = time.GetUtcNow() + (budget ?? DefaultBudget);
        var attempts = 0;
        var next = FirstPause;
        while (true)
        {
            attempts++;
            var left = Pass(folder);
            if (left.Count == 0) return new(true, [], false, attempts);
            if (time.GetUtcNow() + next > deadline) return new(false, left, true, attempts);
            pause(next);
            next = TimeSpan.FromTicks(Math.Min(LongestPause.Ticks, next.Ticks * 2));
        }
    }

    /// <summary>True for a fully qualified path whose last two names are <c>nth</c> and <c>WebView2</c>.</summary>
    public static bool IsWebDataFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) return false;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var parent = Path.GetDirectoryName(full);
        return string.Equals(Path.GetFileName(full), WebDataRoot.FolderName, StringComparison.OrdinalIgnoreCase)
            && parent is not null
            && string.Equals(Path.GetFileName(parent), AppInfo.DataFolderName, StringComparison.OrdinalIgnoreCase);
    }

    // One pass: the paths still there afterwards (empty when the folder is gone).
    private static List<string> Pass(string folder)
    {
        var left = new List<string>();
        if (!Path.Exists(folder)) return left;
        if (IsLink(folder))
        {
            if (!TryRun(() => Directory.Delete(folder))) left.Add(folder);
            return left;
        }

        // The quick way first: nearly always everything goes at once.
        MakeWritable(folder);
        if (TryRun(() => Directory.Delete(folder, recursive: true)) && !Path.Exists(folder)) return left;

        // Something is held: delete what can be, bottom up, and report what cannot.
        RemoveChildren(folder, left);
        if (left.Count > 0) left.Add(folder);
        else if (Path.Exists(folder) && !TryRun(() => Directory.Delete(folder))) left.Add(folder);
        return left;
    }

    private static void RemoveChildren(string folder, List<string> left)
    {
        string[] children = [];
        if (!TryRun(() => children = Directory.GetFileSystemEntries(folder))) { left.Add(folder); return; }
        foreach (var child in children)
        {
            if (IsPlainDirectory(child))
            {
                var before = left.Count;
                RemoveChildren(child, left);
                if (left.Count == before && Path.Exists(child) && !TryRun(() => Directory.Delete(child))) left.Add(child);
            }
            else if (IsLink(child) && IsDirectory(child))
            {
                if (!TryRun(() => Directory.Delete(child))) left.Add(child);
            }
            else if (!TryRun(() => { Writable(child); File.Delete(child); })) left.Add(child);
        }
    }

    private static void MakeWritable(string folder)
    {
        string[] entries = [];
        if (!TryRun(() => entries = Directory.GetFileSystemEntries(folder))) return;
        foreach (var entry in entries)
        {
            if (IsPlainDirectory(entry)) MakeWritable(entry);
            else if (!IsLink(entry)) TryRun(() => Writable(entry));
        }
    }

    private static void Writable(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }

    private static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool IsDirectory(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.Directory) != 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool IsPlainDirectory(string path)
    {
        try { return (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool TryRun(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
