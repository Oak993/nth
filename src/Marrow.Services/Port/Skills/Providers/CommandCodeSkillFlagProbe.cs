// PORT-ONLY: SK-11e / WU-10 (r2), skills-PLAN.md 2.2: --skill goes on the argv only when `--help` lists it. Runs once per executable
// identity (path, size, modified time), never on the turn path; a failing or timed-out probe means "not listed".
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Marrow.Services.Port.Skills.Providers;

public sealed partial class CommandCodeSkillFlagProbe(Func<string, CancellationToken, Task<string?>> runHelp)
{
    private readonly ConcurrentDictionary<(string Path, long Size, long Modified), Task<bool>> cache = new();
    private readonly ConcurrentDictionary<(string Path, long Size, long Modified), bool> done = new();

    [GeneratedRegex(@"(?<![\w-])--skill(?![\w-])")]
    private static partial Regex Flag();

    private static (string, long, long)? Identity(string executable)
    {
        try
        {
            var info = new FileInfo(executable);
            return info.Exists ? (info.FullName, info.Length, info.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Off the chat path: runs `--help` once per executable identity and remembers the answer.</summary>
    public async Task<bool> Probe(string executable, CancellationToken cancel = default)
    {
        if (Identity(executable) is not { } id) return false;
        var key = (id.Item1, id.Item2, id.Item3);
        var task = cache.GetOrAdd(key, _ => Run(executable, key, cancel));
        return await task.ConfigureAwait(false);
    }

    private async Task<bool> Run(string executable, (string, long, long) key, CancellationToken cancel)
    {
        bool listed;
        try { listed = await runHelp(executable, cancel).ConfigureAwait(false) is { } text && Flag().IsMatch(text); }
        catch (Exception e) when (e is not OutOfMemoryException) { listed = false; }
        done[key] = listed;
        return listed;
    }

    /// <summary>The turn path: only what a finished probe stored for this exact executable identity. Never runs a process.</summary>
    public bool Listed(string executable) => Identity(executable) is { } id && done.TryGetValue((id.Item1, id.Item2, id.Item3), out var listed) && listed;
}
