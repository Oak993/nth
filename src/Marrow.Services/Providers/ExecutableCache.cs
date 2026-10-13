using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Providers;

/// <summary>
/// PORT-ONLY (ED-NEW-perf-ui-thread-2). Remembers where a provider's CLI was found, so the PATH x PATHEXT search (about a thousand file checks when
/// nothing matches early) is not run again on the UI thread by every caller of <see cref="ProviderRegistry.Executable"/> and Environment(for:).
/// The key names every input of the search: the provider, the custom path of Settings, and the search itself (PATH, PATHEXT and the home folder:
/// <see cref="LoginEnvironment.SearchSignature"/>), so a changed setting or PATH is a different entry. A hit is trusted for <see cref="Lifetime"/>
/// and only while the remembered file is still there (one check); a search that finds nothing is never remembered, so an install is seen at once.
/// The cap keeps the table small however often the PATH changes. Safe from any thread.
/// </summary>
internal sealed class ExecutableCache
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    internal const int Capacity = 64;

    private readonly record struct Key(ProviderKind Provider, string Custom, string Search);
    private readonly record struct Entry(string Path, ContinuousInstant Expires);

    private readonly object gate = new();
    private readonly Dictionary<Key, Entry> entries = [];

    internal int Count { get { lock (gate) return entries.Count; } }

    /// <summary>The remembered path when it is fresh and still exists; otherwise <paramref name="search"/>'s answer, remembered when it found one.
    /// <paramref name="refresh"/> skips the memory (a provider probe, which runs off the UI thread and wants the disk's answer) and replaces it.</summary>
    internal string? Resolve(ProviderKind provider, string custom, string searchSignature, Func<string?> search, bool refresh = false)
    {
        var key = new Key(provider, custom, searchSignature);
        var now = SwiftClock.Now;
        if (!refresh)
        {
            Entry hit;
            bool found;
            lock (gate) found = entries.TryGetValue(key, out hit) && now < hit.Expires;
            if (found)
            {
                if (File.Exists(hit.Path) || Directory.Exists(hit.Path)) return hit.Path;
            }
        }
        var path = search();
        lock (gate)
        {
            if (path is null) entries.Remove(key);
            else
            {
                if (entries.Count >= Capacity && !entries.ContainsKey(key)) Trim(now);
                entries[key] = new Entry(path, now + Lifetime);
            }
        }
        return path;
    }

    private void Trim(ContinuousInstant now)
    {
        foreach (var stale in entries.Where(pair => now >= pair.Value.Expires).Select(pair => pair.Key).ToList()) entries.Remove(stale);
        if (entries.Count >= Capacity) entries.Clear();
    }
}
