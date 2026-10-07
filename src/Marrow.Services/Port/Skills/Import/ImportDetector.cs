// PORT-ONLY: first-release feature unit SK-18 (OD-0118 addendum). Marrow finds, by itself, the apps on this PC that keep skills (the scanner's places, no picked folder) when the Skills page opens and when those folders change,
// and says so in one quiet line. It only reports: nothing is imported, and nothing in the other apps is written (the watch only listens). A dismissed line stays dismissed for the skills it was dismissed for; a skill found later brings it back.
using System.Text;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Import;

namespace Marrow.Services.Port.Skills.Import;

/// <summary>A folder to listen to. <see cref="Names"/> limits a shallow target to the entries of that name (the folder a skills place will appear in).</summary>
public sealed record WatchTarget(string Folder, bool Deep, IReadOnlyList<string>? Names);

public interface IFolderWatch
{
    /// <summary>Starts listening; <paramref name="changed"/> may be called from any thread. Dispose ends it.</summary>
    IDisposable Watch(IReadOnlyList<WatchTarget> targets, Action changed);
}

public sealed record Detection(int Count, IReadOnlyList<ForeignApp> Apps, IReadOnlyList<string> Keys)
{
    public bool Visible => Count > 0;
}

public static class ForeignWatchTargets
{
    /// <summary>For each place the scanner reads: the place itself when it exists (deep), else the nearest folder that exists below the home, listening for the next folder on the way (shallow).</summary>
    public static IReadOnlyList<WatchTarget> For(string home)
    {
        var deep = new List<string>();
        var shallow = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var root in ForeignSkillScanner.WatchRoots(home))
        {
            var current = root;
            var next = "";
            while (!Directory.Exists(current) && !string.Equals(current, home, StringComparison.Ordinal))
            {
                next = Path.GetFileName(current);
                current = Path.GetDirectoryName(current) ?? home;
            }
            if (string.Equals(current, root, StringComparison.Ordinal))
            {
                if (!deep.Contains(root)) deep.Add(root);
                continue;
            }
            // an ancestor that exists below a place already listened to deeply is covered
            if (!shallow.TryGetValue(current, out var names)) shallow[current] = names = [];
            if (!names.Contains(next)) names.Add(next);
        }
        var targets = new List<WatchTarget>();
        foreach (var folder in deep) targets.Add(new WatchTarget(folder, true, null));
        foreach (var (folder, names) in shallow) targets.Add(new WatchTarget(folder, false, names));
        return targets;
    }
}

public sealed class ImportDetector : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(800);

    private readonly object gate = new();
    private readonly Func<ForeignScan> scan;
    private readonly ImportedSkillsLibrary library;
    private readonly string home;
    private readonly IFolderWatch watch;
    private readonly string? stateFile;
    private readonly TimeProvider clock;
    private readonly HashSet<string> dismissed = new(StringComparer.Ordinal);
    private ForeignScan known = new([], []);
    private IDisposable? listening;
    private ITimer? timer;
    private bool started;
    private string announced = "";
    private bool disposed;

    public ImportDetector(Func<ForeignScan> scan, ImportedSkillsLibrary library, string home, IFolderWatch? watch = null, string? stateFile = null, TimeProvider? clock = null)
    {
        this.scan = scan;
        this.library = library;
        this.home = home;
        this.watch = watch ?? new SystemFolderWatch();
        this.stateFile = stateFile;
        this.clock = clock ?? TimeProvider.System;
        Load();
    }

    public event Action? Changed;

    public bool Watching { get { lock (gate) return listening is not null; } }

    /// <summary>What was found and is not yet imported nor dismissed, as of the last scan.</summary>
    public Detection Current
    {
        get
        {
            ForeignScan seen;
            HashSet<string> quiet;
            lock (gate)
            {
                seen = known;
                quiet = [.. dismissed];
            }
            var imported = library.List().Select(i => i.App + "|" + i.SourcePath).ToHashSet(StringComparer.Ordinal);
            var open = seen.Skills.Where(s => !imported.Contains(s.Key) && !quiet.Contains(s.Key)).ToArray();
            return new Detection(open.Length, [.. open.Select(s => s.App).Distinct().Order()], [.. open.Select(s => s.Key)]);
        }
    }

    public void Refresh()
    {
        ForeignScan next;
        try { next = scan(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return; // what was known stays
        }
        var refreshed = library.Refresh();
        lock (gate) known = next;
        Announce();
        if (refreshed) Changed?.Invoke();
    }

    public Task RefreshAsync() => Task.Run(Refresh);

    /// <summary>Starts listening to the places the scanner reads. Safe to call twice.</summary>
    public void Start()
    {
        lock (gate)
        {
            if (started || disposed) return;
            started = true;
            Listen();
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            started = false;
            listening?.Dispose();
            listening = null;
            timer?.Dispose();
            timer = null;
        }
    }

    public void Dismiss()
    {
        var keys = Current.Keys;
        lock (gate) foreach (var key in keys) dismissed.Add(key);
        Save();
        Announce();
    }

    public void Dispose()
    {
        Stop();
        lock (gate) disposed = true;
    }

    private void Listen()
    {
        listening?.Dispose();
        try { listening = watch.Watch(ForeignWatchTargets.For(home), OnChange); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException) { listening = null; }
    }

    // Each change starts the wait again; one scan follows once things have been quiet for a moment, and the watch is made again so a folder that has just appeared is listened to.
    private void OnChange()
    {
        lock (gate)
        {
            if (!started) return;
            if (timer is null) timer = clock.CreateTimer(_ => Later(), null, Settle, Timeout.InfiniteTimeSpan);
            else timer.Change(Settle, Timeout.InfiniteTimeSpan);
        }
    }

    private void Later()
    {
        lock (gate)
        {
            if (!started) return;
            Listen();
        }
        Refresh();
    }

    private void Announce()
    {
        var now = Current;
        var signature = string.Join("\n", now.Keys);
        lock (gate)
        {
            if (signature == announced) return;
            announced = signature;
        }
        Changed?.Invoke();
    }

    private void Load()
    {
        if (stateFile is null || !File.Exists(stateFile)) return;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(stateFile));
            if (document.RootElement.ValueKind != JsonValueKind.Array) return;
            foreach (var item in document.RootElement.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } key) dismissed.Add(key);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { dismissed.Clear(); }
    }

    private void Save()
    {
        if (stateFile is null) return;
        try
        {
            string[] keys;
            lock (gate) keys = [.. dismissed.Order(StringComparer.Ordinal)];
            Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
            FileOps.WriteAtomic(stateFile, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(keys)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the line then comes back next time; nothing else depends on it */ }
    }
}

/// <summary>The real listener: a FileSystemWatcher per target. It reads and listens; it never writes.</summary>
public sealed class SystemFolderWatch : IFolderWatch
{
    public IDisposable Watch(IReadOnlyList<WatchTarget> targets, Action changed)
    {
        var watchers = new List<FileSystemWatcher>();
        foreach (var target in targets)
        {
            if (!Directory.Exists(target.Folder)) continue;
            try
            {
                var watcher = new FileSystemWatcher(target.Folder)
                {
                    IncludeSubdirectories = target.Deep,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                var names = target.Names;
                bool Wanted(string? name) => names is null || (name is not null && names.Contains(name, StringComparer.OrdinalIgnoreCase));
                watcher.Created += (_, e) => { if (Wanted(e.Name)) changed(); };
                watcher.Deleted += (_, e) => { if (Wanted(e.Name)) changed(); };
                watcher.Renamed += (_, e) => { if (Wanted(e.Name) || Wanted(e.OldName)) changed(); };
                if (names is null) watcher.Changed += (_, _) => changed();
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException) { /* that folder is not listened to */ }
        }
        return new Ending(watchers);
    }

    private sealed class Ending(List<FileSystemWatcher> watchers) : IDisposable
    {
        public void Dispose()
        {
            foreach (var watcher in watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
        }
    }
}
