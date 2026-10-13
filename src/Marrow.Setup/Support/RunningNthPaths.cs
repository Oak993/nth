namespace Marrow.Setup.Support;

/// <summary>PORT-ONLY (nth installer PLAN.md 3a, unit U4, ED-NEW-inst-u4-1): which files count as "inside the root".</summary>
public static class RunningNthPaths
{
    /// <summary>
    /// True when <paramref name="path"/> is a file or folder below <paramref name="root"/> (the folder itself is not below it).
    /// Case-insensitive, separators and <c>..</c> resolved, a trailing separator on the root ignored, a drive or share root
    /// accepted. A path that is empty or not a valid path is not inside anything. Only processes inside the root matter to the
    /// setup (a portable or development copy of nth elsewhere is not in Velopack's way).
    /// </summary>
    public static bool IsInside(string? root, string? path)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path)) return false;
        string folder;
        string file;
        try
        {
            folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            file = Path.GetFullPath(path);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
        var prefix = folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;
        return file.Length > prefix.Length && file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
