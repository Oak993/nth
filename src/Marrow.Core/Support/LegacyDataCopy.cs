// PORT-ONLY (OD-0135, no Swift twin): the product is named Nth, so its data folder moves from %LOCALAPPDATA%\Marrow Code
// to %LOCALAPPDATA%\Nth and its home state folder from .marrow / .marrow-dev to .nth / .nth-dev. Program.Main runs this once,
// before anything reads settings, logs, the library or the GPU program cache, so a user who ran the app under its old name
// opens Nth with everything they had.
namespace Marrow.Core.Support;

/// <summary>What one <see cref="LegacyDataCopy.CopyOnce"/> did.</summary>
public enum LegacyDataCopyOutcome
{
    /// <summary>The new folder already holds the marker of an earlier copy (or is a file): nothing was read or written.</summary>
    AlreadyThere,

    /// <summary>Neither folder exists: nothing was written (a fresh install).</summary>
    NothingToCopy,

    /// <summary>The old folder was copied into the new one and the marker written.</summary>
    Copied,

    /// <summary>
    /// The new folder existed without the marker (made by something other than the copy, such as a Command Code mod written
    /// into it): what it held was kept, everything else was copied in beside it, and the marker written.
    /// </summary>
    Merged,

    /// <summary>The copy could not be made (the new folder could not be created): nothing is left behind.</summary>
    Failed,
}

/// <summary>The outcome and the entries skipped: files that could not be read (one the old app holds open), and links.</summary>
public sealed record LegacyDataCopyResult(LegacyDataCopyOutcome Outcome, IReadOnlyList<string> Skipped);

/// <summary>
/// OD-0135: a one-time COPY of a folder kept under the product's old name into the folder of its new name. The old folder is
/// never moved, changed or deleted (an older copy of the app may still run from it). The marker file (<see cref="MarkerName"/>),
/// written last, records that the copy was made: once it is there nothing is copied again. The copy is made in a staging folder
/// beside the new one and renamed into place at the end, so an interrupted copy leaves no half-made new folder. A new folder
/// that exists WITHOUT the marker was made by something else before the first launch under the new name (a test or a tool
/// writing one file into it): nothing in it is overwritten, and everything it lacks is moved in from the staging copy. A file
/// that cannot be read is skipped and the rest is still copied; links (symbolic links, junctions) are skipped and never
/// followed, so nothing outside the old folder is read.
/// </summary>
public static class LegacyDataCopy
{
    /// <summary>The marker written into the new folder after a copy.</summary>
    public const string MarkerName = ".copied-from-legacy-name";

    /// <summary>The suffix of the staging folder the copy is made in before it is renamed into place.</summary>
    public const string StagingSuffix = ".copying";

    /// <summary>
    /// The data folder (%LOCALAPPDATA%\Marrow Code to %LOCALAPPDATA%\Nth) under <paramref name="localApplicationData"/>, and
    /// the state folder (.marrow to .nth, .marrow-dev to .nth-dev) under <paramref name="homeDirectory"/>.
    /// </summary>
    public static (LegacyDataCopyResult Data, LegacyDataCopyResult State) CopyProductFolders(
        string localApplicationData, string homeDirectory, bool isDevelopment) =>
        (CopyOnce(Path.Combine(localApplicationData, AppInfo.LegacyDataFolderName), Path.Combine(localApplicationData, AppInfo.DataFolderName)),
         CopyOnce(Path.Combine(homeDirectory, AppInfo.LegacyStateDirectoryNameFor(isDevelopment)),
             Path.Combine(homeDirectory, AppInfo.StateDirectoryNameFor(isDevelopment))));

    /// <summary>Copies <paramref name="legacyRoot"/> to <paramref name="root"/> unless <paramref name="root"/> holds the marker of an earlier copy.</summary>
    public static LegacyDataCopyResult CopyOnce(string legacyRoot, string root)
    {
        if (File.Exists(root) || File.Exists(Path.Combine(root, MarkerName))) return new(LegacyDataCopyOutcome.AlreadyThere, []);
        if (Directory.Exists(root) && IsLink(root)) return new(LegacyDataCopyOutcome.AlreadyThere, []); // never written through a link
        if (!Directory.Exists(legacyRoot) || IsLink(legacyRoot)) return new(LegacyDataCopyOutcome.NothingToCopy, []);
        var staging = Path.TrimEndingDirectorySeparator(root) + StagingSuffix;
        var skipped = new List<string>();
        try
        {
            // A staging folder left by an interrupted copy is this copy's own: start again from nothing.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);
            CopyDirectory(legacyRoot, staging, skipped);
            File.WriteAllText(Path.Combine(staging, MarkerName),
                "Copied once from " + legacyRoot + " at " + DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                + " (OD-0135: the product is named Nth). The old folder was left as it was.\n");
            if (!Directory.Exists(root))
            {
                Directory.Move(staging, root);
                return new(LegacyDataCopyOutcome.Copied, skipped);
            }
            MergeMissing(staging, root, skip: MarkerName);
            File.Move(Path.Combine(staging, MarkerName), Path.Combine(root, MarkerName)); // last: only a finished merge is marked
            Directory.Delete(staging, recursive: true); // what is left is what the new folder already had: its own copy wins
            return new(LegacyDataCopyOutcome.Merged, skipped);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            return new(LegacyDataCopyOutcome.Failed, skipped);
        }
    }

    // Moves every entry of the staging copy that the existing folder lacks into it; a folder both have is merged the same way,
    // and a file both have stays as the existing folder has it. Both sides are on one volume, so a move is a rename.
    private static void MergeMissing(string staging, string root, string? skip = null)
    {
        foreach (var directory in Directory.GetDirectories(staging))
        {
            var target = Path.Combine(root, Path.GetFileName(directory));
            if (File.Exists(target)) continue;
            if (Directory.Exists(target)) MergeMissing(directory, target);
            else Directory.Move(directory, target);
        }
        foreach (var file in Directory.GetFiles(staging))
        {
            var name = Path.GetFileName(file);
            var target = Path.Combine(root, name);
            if (name == skip || File.Exists(target) || Directory.Exists(target)) continue;
            File.Move(file, target);
        }
    }

    private static void CopyDirectory(string source, string destination, List<string> skipped)
    {
        string[] directories, files;
        try
        {
            directories = Directory.GetDirectories(source);
            files = Directory.GetFiles(source);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            skipped.Add(source); // a folder that cannot be listed: the rest is still copied
            return;
        }
        foreach (var directory in directories)
        {
            if (IsLink(directory)) { skipped.Add(directory); continue; }
            var target = Path.Combine(destination, Path.GetFileName(directory));
            try { Directory.CreateDirectory(target); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { skipped.Add(directory); continue; }
            CopyDirectory(directory, target, skipped);
        }
        foreach (var file in files)
        {
            if (IsLink(file)) { skipped.Add(file); continue; }
            try
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                skipped.Add(file); // held open without read sharing, or unreadable: the rest is still copied
            }
        }
    }

    private static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return true; }
    }
}
