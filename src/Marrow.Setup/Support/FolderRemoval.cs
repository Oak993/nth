namespace Marrow.Setup.Support;

/// <summary>What removing a folder tree did: how many entries went, and every path that stayed (open, denied, or a link's target).</summary>
public sealed record FolderRemovalResult(bool Existed, int Removed, IReadOnlyList<string> Kept)
{
    public static FolderRemovalResult Absent { get; } = new(false, 0, []);

    /// <summary>The folder is gone (or was never there).</summary>
    public bool Gone => Kept.Count == 0;
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8, the uninstall flow; unit U6, ED-NEW-inst-u6-1). Deletes a folder tree the way an
/// uninstaller must: it never follows a link (a symbolic link or junction inside is removed as a link, so nothing outside the
/// folder is ever reached through it), it clears the read-only attribute it meets, it keeps going past a file that is open or
/// denied and reports it, and it never throws for a file-system failure. A folder that is itself a link is not followed: the
/// link is removed and its target is left (the target is not the folder nth made).
/// </summary>
public static class FolderRemoval
{
    public static FolderRemovalResult Remove(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException($"'{folder}' is not a full path.", nameof(folder));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (Path.GetPathRoot(full) is { } drive && string.Equals(Path.TrimEndingDirectorySeparator(drive), full, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{folder}' is a drive's root.", nameof(folder));
        FileSystemInfo top;
        try
        {
            top = new DirectoryInfo(full);
            if (!top.Exists) return File.Exists(full) ? new FolderRemovalResult(true, 0, [full]) : FolderRemovalResult.Absent;
        }
        catch (Exception error) when (IsFileSystemFailure(error))
        {
            return new FolderRemovalResult(true, 0, [full]);
        }
        var tally = new Tally();
        RemoveEntry(top, tally);
        return new FolderRemovalResult(true, tally.Removed, tally.Kept);
    }

    private static void RemoveEntry(FileSystemInfo entry, Tally tally)
    {
        try
        {
            entry.Refresh();
            var attributes = entry.Attributes;
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                // a link: removed as a link, never followed
                Clear(entry, attributes);
                if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(entry.FullName, recursive: false);
                else File.Delete(entry.FullName);
                tally.Removed++;
                return;
            }
            if ((attributes & FileAttributes.Directory) != 0)
            {
                var directory = entry as DirectoryInfo ?? new DirectoryInfo(entry.FullName);
                foreach (var child in directory.EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = 0, IgnoreInaccessible = false, ReturnSpecialDirectories = false }).ToList())
                    RemoveEntry(child, tally);
                if (tally.KeptUnder(entry.FullName)) return; // something inside stayed, so the folder stays (and is not listed twice)
                Clear(entry, attributes);
                Directory.Delete(entry.FullName, recursive: false);
                tally.Removed++;
                return;
            }
            Clear(entry, attributes);
            File.Delete(entry.FullName);
            tally.Removed++;
        }
        catch (Exception error) when (IsFileSystemFailure(error))
        {
            if (!Exists(entry.FullName)) return; // went by itself (another remover, Velopack's own delayed delete)
            tally.Keep(entry.FullName);
        }
    }

    private static void Clear(FileSystemInfo entry, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReadOnly) != 0) entry.Attributes = attributes & ~FileAttributes.ReadOnly;
    }

    private static bool Exists(string path)
    {
        // both read the entry's own attributes, so a link whose target is gone still counts as there
        try { return File.Exists(path) || Directory.Exists(path); }
        catch (Exception error) when (IsFileSystemFailure(error)) { return true; }
    }

    internal static bool IsFileSystemFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException;

    private sealed class Tally
    {
        private readonly List<string> kept = [];

        public int Removed;

        public IReadOnlyList<string> Kept => kept;

        public void Keep(string path) => kept.Add(path);

        public bool KeptUnder(string folder) =>
            kept.Any(path => path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }
}
