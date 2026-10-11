using System.Globalization;
using Marrow.Core.Port;

namespace Marrow.Services.Tests.Fixtures;

// Helper for Git/WorkingTreeWatchNoteBudgetTests.cs (R-TEST-3). Not port code.
// ED-NEW-lean-18: a frozen copy of WorkingTreeWatch.NoteBatch's per-path decisions as they stood before the lean pass
// (claude/release-integration 0a946c558, WorkingTreeWatch.cs:174-213, every path normalised before any test). The
// lean pass answers the common paths with ordinal prefix checks first; this oracle proves those answers equal the
// old ones, path by path and batch by batch.
public sealed class WorkingTreeNoteOracle(string root, string gitRoot)
{
    private const int ChangedLimit = 64;
    private readonly string cookiePrefix = gitRoot + "\\" + "droppy-code-sync-";

    public ulong Generation { get; private set; } = 1;

    public ulong CookieSeen { get; private set; }

    public HashSet<string>? Changed { get; private set; } = new(StringComparer.Ordinal);

    public void NoteBatch(IReadOnlyList<string> paths, bool everything)
    {
        var didChange = everything;
        var directories = new List<string>();
        ulong cookie = 0;
        foreach (var path in paths)
        {
            var normalized = SwiftPath.Normalize(path).Replace('/', '\\');
            if (normalized.StartsWith(cookiePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = normalized[cookiePrefix.Length..];
                if (ulong.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) cookie = Math.Max(cookie, number);
                continue;
            }
            if (SwiftPath.TailUnder(normalized, gitRoot) is { Length: > 0 }) continue;
            didChange = true;
            var relative = SwiftPath.TailUnder(normalized, root);
            if (relative is null || relative.Length == 0)
            {
                everything = true;
                continue;
            }
            var separator = relative.LastIndexOfAny(['\\', '/']);
            directories.Add((separator < 0 ? relative : relative[..separator]).Replace('\\', '/'));
        }
        if (!didChange && cookie == 0) return;
        if (didChange) Generation = unchecked(Generation + 1);
        CookieSeen = Math.Max(CookieSeen, cookie);
        if (everything) Changed = null;
        else if (Changed is not null)
        {
            foreach (var directory in directories) Changed.Add(Changed.Count >= ChangedLimit ? TopLevel(directory) : directory);
            if (Changed.Count > ChangedLimit) Changed = new(Changed.Select(TopLevel), StringComparer.Ordinal);
        }
    }

    private static string TopLevel(string directory)
    {
        var slash = directory.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? directory : directory[..slash];
    }
}
