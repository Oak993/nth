namespace Marrow.Testing;

/// <summary>
/// The canary of nth installer PLAN.md 8a ("Tests", unit U10): no test touches the real Desktop or Start menu. A test
/// assembly calls <see cref="Begin"/> when it loads (a module initializer) and has one test, run after the others, that
/// asks <see cref="Differences"/> and fails if anything appeared, changed or went. It only lists names, write times and sizes of
/// <c>nth*.lnk</c> in the user's and the public desktop, in the Start menu's Programs folders, and in their <c>nth</c> folders
/// (1.0.0 kept its shortcut there); it opens no file, so a OneDrive placeholder is never downloaded, and it writes nothing.
/// </summary>
public static class RealShellCanary
{
    private static readonly Lock Gate = new();
    private static IReadOnlyList<string>? before;

    /// <summary>Takes the baseline once; later calls keep the first one.</summary>
    public static void Begin()
    {
        lock (Gate) before ??= List();
    }

    /// <summary>Whether <see cref="Begin"/> ran (a canary test with no baseline would prove nothing).</summary>
    public static bool HasBaseline
    {
        get
        {
            lock (Gate) return before is not null;
        }
    }

    /// <summary>What differs from the baseline now, as lines starting "appeared", "went" or "changed" (empty when nothing does).</summary>
    public static IReadOnlyList<string> Differences()
    {
        IReadOnlyList<string> start;
        lock (Gate) start = before ?? throw new InvalidOperationException("The canary has no baseline: Begin was never called.");
        return Compare(start, List());
    }

    /// <summary>Compares two listings; each entry is <c>path|write ticks|length</c>.</summary>
    public static IReadOnlyList<string> Compare(IReadOnlyList<string> start, IReadOnlyList<string> now)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(now);
        var was = start.ToDictionary(PathOf, StringComparer.OrdinalIgnoreCase);
        var current = now.ToDictionary(PathOf, StringComparer.OrdinalIgnoreCase);
        var differences = new List<string>();
        foreach (var (path, entry) in current)
        {
            if (!was.TryGetValue(path, out var old)) differences.Add("appeared: " + entry);
            else if (!string.Equals(old, entry, StringComparison.OrdinalIgnoreCase)) differences.Add($"changed: {old} -> {entry}");
        }
        foreach (var (path, entry) in was)
            if (!current.ContainsKey(path)) differences.Add("went: " + entry);
        differences.Sort(StringComparer.Ordinal); // appeared, changed, then went
        return differences;
    }

    /// <summary>The listing now: read only.</summary>
    public static IReadOnlyList<string> List()
    {
        var entries = new List<string>();
        foreach (var folder in Folders())
        {
            foreach (var (directory, pattern) in new[] { (folder, "nth*.lnk"), (System.IO.Path.Combine(folder, "nth"), "*.lnk") })
            {
                try
                {
                    if (!Directory.Exists(directory)) continue;
                    foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
                    {
                        var info = new FileInfo(file);
                        entries.Add($"{file}|{info.LastWriteTimeUtc.Ticks}|{info.Length}");
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    entries.Add($"{directory}|unreadable|{error.GetType().Name}"); // the same each time, so it is never a difference
                }
            }
        }
        entries.Sort(StringComparer.OrdinalIgnoreCase);
        return entries;
    }

    private static IEnumerable<string> Folders() =>
        new[]
        {
            Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory,
            Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms,
        }
        .Select(Environment.GetFolderPath)
        .Where(path => path.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string PathOf(string entry) => entry[..entry.IndexOf('|', StringComparison.Ordinal)];
}
