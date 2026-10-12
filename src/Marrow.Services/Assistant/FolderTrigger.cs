// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.2 "Folder change"; canvas 5; unit AS-10; ED-NEW-dextra-as10-6). No Swift twin.
// Watches one folder the owner granted for new, changed or removed files that match a glob. A FileSystemWatcher says when to
// look; the look is a scan that compares the folder with what was seen before, so an event is never trusted for what changed.
// A burst of events waits 2 s for the files to settle (at most 30 s from the first), and a periodic rescan every 10 minutes
// catches what a watcher misses (network drives). The snapshot is kept between runs, so a file that arrived while nth was
// closed is found at the next start. A folder that is gone is a problem to report, never "every file was removed".
using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>What a scan found, filtered to the events the duty wants. Names are relative to the folder.</summary>
public sealed record FolderChange(IReadOnlyList<string> New, IReadOnlyList<string> Changed, IReadOnlyList<string> Removed)
{
    public bool Any => New.Count + Changed.Count + Removed.Count > 0;

    public int Total => New.Count + Changed.Count + Removed.Count;
}

/// <summary>Says when to look. The real one is a FileSystemWatcher; a test fakes it.</summary>
public interface IFolderEventSource : IDisposable
{
    /// <summary>Starts watching; <paramref name="poke"/> is called (on any thread) whenever something may have changed.</summary>
    void Start(string path, bool subfolders, Action poke);
}

public sealed class FolderTriggerOptions
{
    public required string Path { get; init; }

    public string Pattern { get; init; } = "*";

    public FolderEvents Events { get; init; } = FolderEvents.New;

    public bool Subfolders { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Quiet time before a scan (7.2: 2 s).</summary>
    public TimeSpan Settle { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The longest a stream of events can keep a scan waiting.</summary>
    public TimeSpan MaxSettle { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How often the folder is scanned whatever the watcher said.</summary>
    public TimeSpan Rescan { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>The most files one folder may hold for a watch; more is refused (the owner narrows the folder or the pattern).</summary>
    public int FileLimit { get; init; } = 20_000;

    /// <summary>The most names one change reports.</summary>
    public int NameLimit { get; init; } = 50;

    /// <summary>Whether the owner granted nth this folder. A folder that is not granted is never watched (7.2).</summary>
    public required Func<string, bool> Granted { get; init; }

    public IFolderEventSource? Source { get; init; }
}

public sealed class FolderTrigger : IDisposable
{
    private readonly object gate = new();
    private readonly FolderTriggerOptions o;
    private readonly Action<FolderChange> changed;
    private readonly Action<string?> report;
    private string? lastStatus;
    private readonly string root;
    private IFolderEventSource? source;
    private ITimer? settle;
    private ITimer? rescan;
    private DateTimeOffset? firstPoke;
    private Dictionary<string, string>? seen;
    private bool disposed;
    private bool scanning;
    private bool again;

    /// <param name="changed">A scan found changes the duty wants.</param>
    /// <param name="status">A problem with the folder (words for the owner), or null when it is fine again.</param>
    public FolderTrigger(FolderTriggerOptions options, Action<FolderChange> changed, Action<string?> status)
    {
        o = options ?? throw new ArgumentNullException(nameof(options));
        this.changed = changed ?? throw new ArgumentNullException(nameof(changed));
        report = status ?? throw new ArgumentNullException(nameof(status));
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(o.Path));
    }

    /// <summary>
    /// Starts watching. <paramref name="saved"/> is the snapshot the last run kept: with one for this folder the first scan reports
    /// what changed since; without, the first scan is the baseline and reports nothing. Returns why it cannot watch, or null.
    /// </summary>
    public string? Start(JsonObject? saved)
    {
        if (!Path.IsPathFullyQualified(o.Path)) return "Give the folder's full path.";
        if (!o.Granted(root)) return $"{root} is not a folder you granted nth, so it is not watched.";
        lock (gate)
        {
            if (disposed) return "The watch is stopped.";
            seen = Restore(saved);
        }
        if (!Directory.Exists(root)) return $"The folder {root} is not there.";
        try
        {
            source = o.Source ?? new FileSystemSource();
            source.Start(root, o.Subfolders, Poke);
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // The scan still runs on its timer; the watcher only makes it quicker.
            Status("Windows would not watch " + root + " live (" + error.Message + "); nth looks every " + Minutes(o.Rescan) + " minutes instead.");
            source?.Dispose();
            source = null;
        }
        rescan = o.Time.CreateTimer(_ => Scan(), null, o.Rescan, o.Rescan);
        Scan();
        return null;
    }

    // The owner hears of a problem when it begins and again when it is gone, not at every scan.
    private void Status(string? text)
    {
        lock (gate)
        {
            if (lastStatus == text) return;
            lastStatus = text;
        }
        report(text);
    }

    private static string Minutes(TimeSpan span) => Math.Max(1, (int)Math.Round(span.TotalMinutes)).ToString(CultureInfo.InvariantCulture);

    /// <summary>Something may have changed: a scan runs after the folder is quiet for the settle time.</summary>
    internal void Poke()
    {
        lock (gate)
        {
            if (disposed) return;
            var now = o.Time.GetUtcNow();
            firstPoke ??= now;
            var wait = o.Settle;
            var left = firstPoke.Value + o.MaxSettle - now;
            if (left < wait) wait = left < TimeSpan.Zero ? TimeSpan.Zero : left;
            if (settle is null) settle = o.Time.CreateTimer(_ => Settled(), null, wait, Timeout.InfiniteTimeSpan);
            else settle.Change(wait, Timeout.InfiniteTimeSpan);
        }
    }

    private void Settled()
    {
        lock (gate) firstPoke = null;
        Scan();
    }

    /// <summary>The snapshot to keep between runs.</summary>
    public JsonObject State()
    {
        lock (gate)
        {
            var files = new JsonObject();
            if (seen is not null) foreach (var (name, stamp) in seen) files[name] = stamp;
            return new JsonObject { ["version"] = 1, ["folder"] = root, ["files"] = files };
        }
    }

    /// <summary>Looks at the folder now and reports what changed since the last look.</summary>
    public void Scan()
    {
        lock (gate)
        {
            if (disposed) return;
            if (scanning)
            {
                again = true;
                return;
            }
            scanning = true;
        }
        try
        {
            do
            {
                lock (gate) again = false;
                ScanOnce();
            }
            while (again && !disposed);
        }
        finally
        {
            lock (gate) scanning = false;
        }
    }

    private void ScanOnce()
    {
        if (!Directory.Exists(root))
        {
            Status($"The folder {root} is not there.");
            return;
        }
        Dictionary<string, string>? now;
        try { now = Read(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Status($"nth cannot read {root}: {error.Message}");
            return;
        }
        if (now is null)
        {
            Status($"{root} holds more than {o.FileLimit.ToString(CultureInfo.InvariantCulture)} files that match. Narrow the pattern or pick a smaller folder.");
            return;
        }
        FolderChange? change = null;
        lock (gate)
        {
            if (disposed) return;
            if (seen is not null)
            {
                var added = now.Keys.Where(k => !seen.ContainsKey(k)).Order(StringComparer.OrdinalIgnoreCase).ToList();
                var modified = now.Where(p => seen.TryGetValue(p.Key, out var before) && before != p.Value).Select(p => p.Key).Order(StringComparer.OrdinalIgnoreCase).ToList();
                var removed = seen.Keys.Where(k => !now.ContainsKey(k)).Order(StringComparer.OrdinalIgnoreCase).ToList();
                var found = new FolderChange(
                    o.Events.HasFlag(FolderEvents.New) ? added.Take(o.NameLimit).ToList() : [],
                    o.Events.HasFlag(FolderEvents.Changed) ? modified.Take(o.NameLimit).ToList() : [],
                    o.Events.HasFlag(FolderEvents.Removed) ? removed.Take(o.NameLimit).ToList() : []);
                if (found.Any) change = found;
            }
            seen = now;
        }
        Status(null);
        if (change is not null) changed(change);
    }

    // The files that match, as relative name -> "size:last write". Null when there are more than the limit.
    private Dictionary<string, string>? Read()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = o.Subfolders,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            // A junction or a symbolic link inside the folder could lead out of it: it is neither followed nor listed.
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        };
        foreach (var info in new DirectoryInfo(root).EnumerateFiles("*", options))
        {
            if (!FolderGlob.IsMatch(o.Pattern, info.Name)) continue;
            if (result.Count >= o.FileLimit) return null;
            var relative = Path.GetRelativePath(root, info.FullName).Replace('\\', '/');
            result[relative] = info.Length.ToString(CultureInfo.InvariantCulture) + ":" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }
        return result;
    }

    private Dictionary<string, string>? Restore(JsonObject? saved)
    {
        if (saved is null || saved["version"]?.GetValue<int>() != 1) return null;
        if (!string.Equals(saved["folder"]?.GetValue<string>(), root, StringComparison.OrdinalIgnoreCase)) return null;
        if (saved["files"] is not JsonObject files || files.Count > o.FileLimit) return null;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, stamp) in files)
        {
            if (stamp?.GetValue<string>() is not { } text) return null;
            result[name] = text;
        }
        return result;
    }

    public void Dispose()
    {
        ITimer? first;
        ITimer? second;
        IFolderEventSource? watching;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            first = settle;
            second = rescan;
            watching = source;
            settle = null;
            rescan = null;
            source = null;
        }
        first?.Dispose();
        second?.Dispose();
        watching?.Dispose();
    }

    private sealed class FileSystemSource : IFolderEventSource
    {
        private FileSystemWatcher? watcher;

        public void Start(string path, bool subfolders, Action poke)
        {
            watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = subfolders,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Created += (_, _) => poke();
            watcher.Changed += (_, _) => poke();
            watcher.Deleted += (_, _) => poke();
            watcher.Renamed += (_, _) => poke();
            watcher.Error += (_, _) => poke();
            watcher.EnableRaisingEvents = true;
        }

        public void Dispose() => watcher?.Dispose();
    }
}

/// <summary>A glob over a file name: <c>*</c> is any run of characters, <c>?</c> is one. Case is ignored.</summary>
public static class FolderGlob
{
    public static bool IsMatch(string pattern, string name)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(name);
        int p = 0, n = 0, star = -1, mark = 0;
        while (n < name.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = n;
            }
            else if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(name[n])))
            {
                p++;
                n++;
            }
            else if (star >= 0)
            {
                p = star + 1;
                n = ++mark;
            }
            else
            {
                return false;
            }
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }
}
