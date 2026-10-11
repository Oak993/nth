// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-2). The logic of the "Import plugins" sheet, in the pattern of "Import skills" (ImportController, SK-18): no Avalonia; the view draws what
// it holds and sends it the user's presses. A plugin comes in through the very path "+ Add > From a folder" takes (the target: check the folder, then add the checked plugin to nth's own plugins), so
// nth keeps its own pinned copy. A plugin whose name nth already has is skipped or replaced (Skip first); two own plugins never share a name.
using Marrow.Core.Port.Skills.Import;
using Marrow.Services.Port.Skills.Import;

namespace Marrow.Services.Port.Skills.PluginImport;

/// <summary>Where imported plugins go: the app's own plugins ("+ Add"). Tests give their own.</summary>
public interface IPluginImportTarget
{
    /// <summary>nth's own plugins now (id and name), read fresh.</summary>
    Task<IReadOnlyList<(string Id, string Name)>> ExistingAsync();

    /// <summary>Adds the plugin in <paramref name="folder"/>, named <paramref name="name"/>; with <paramref name="replacingId"/>, that own plugin goes first. Null when it was added, else why not.</summary>
    Task<string?> AddAsync(string folder, string name, string? replacingId, CancellationToken cancel);
}

public sealed record PluginImportRow(string Key, string Name, string Description, string Source, bool Ticked, bool Duplicate);

public sealed record PluginImportGroup(ForeignPluginApp App, string Title, IReadOnlyList<PluginImportRow> Rows, bool AllTicked);

public sealed record PluginImportOutcome(int Imported, int Replaced, int Skipped, IReadOnlyList<(string Name, string Reason)> Failed)
{
    public string Summary => ImportWords.Done(Imported, Replaced, Skipped, Failed.Count);
}

public sealed class PluginImportController(Func<IReadOnlyList<string>, ForeignPluginScan> scan, IPluginImportTarget target, Func<Task<string?>>? pickFolder = null)
{
    private readonly object gate = new();
    private readonly List<string> folders = [];
    private readonly HashSet<string> ticks = new(StringComparer.Ordinal);
    private ForeignPluginScan found = new([], []);
    private HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);
    private ImportPhase phase = ImportPhase.Closed;
    private DuplicateChoice choice = DuplicateChoice.Skip;
    private PluginImportOutcome? outcome;
    private int revision;

    public event Action? Changed;

    public int Revision { get { lock (gate) return revision; } }

    public ImportPhase Phase { get { lock (gate) return phase; } }

    public DuplicateChoice Choice { get { lock (gate) return choice; } }

    public int TickedCount { get { lock (gate) return ticks.Count; } }

    public int ProblemCount { get { lock (gate) return found.Problems.Count; } }

    public PluginImportOutcome? Outcome { get { lock (gate) return outcome; } }

    public string Summary { get { lock (gate) return outcome?.Summary ?? ""; } }

    public IReadOnlyList<PluginImportGroup> Groups
    {
        get
        {
            lock (gate)
            {
                return [.. found.Plugins.GroupBy(p => p.App).OrderBy(g => g.Key).Select(g =>
                {
                    var rows = g.Select(p => new PluginImportRow(p.Key, p.Name, p.Description, p.Source, ticks.Contains(p.Key), existing.Contains(p.Name))).ToArray();
                    return new PluginImportGroup(g.Key, Title(g.Key), rows, rows.Length > 0 && rows.All(r => r.Ticked));
                })];
            }
        }
    }

    public static string Title(ForeignPluginApp app) => app switch
    {
        ForeignPluginApp.ClaudeCode => PluginImportWords.ClaudeCode,
        ForeignPluginApp.Codex => PluginImportWords.Codex,
        _ => PluginImportWords.Folders,
    };

    public async Task OpenAsync()
    {
        lock (gate)
        {
            if (phase != ImportPhase.Closed) return;
            ticks.Clear();
            outcome = null;
            choice = DuplicateChoice.Skip;
            found = new([], []);
            phase = ImportPhase.Scanning;
        }
        Raise();
        await ScanAsync();
    }

    private async Task ScanAsync()
    {
        string[] chosen;
        lock (gate) chosen = [.. folders];
        ForeignPluginScan scanned;
        try { scanned = await Task.Run(() => scan(chosen)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { scanned = new ForeignPluginScan([], [new ScanProblem("", e.Message)]); }
        IReadOnlyList<(string Id, string Name)> own;
        try { own = await target.ExistingAsync(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException) { own = []; }
        lock (gate)
        {
            if (phase is not (ImportPhase.Scanning or ImportPhase.Choosing)) return;
            found = scanned;
            existing = new HashSet<string>(own.Select(o => o.Name), StringComparer.OrdinalIgnoreCase);
            ticks.IntersectWith(found.Plugins.Select(p => p.Key));
            phase = ImportPhase.Choosing;
        }
        Raise();
    }

    /// <summary>Asks for a folder and looks in it too.</summary>
    public async Task AddFolderAsync()
    {
        if (pickFolder is null || Phase != ImportPhase.Choosing) return;
        var folder = await pickFolder();
        if (string.IsNullOrWhiteSpace(folder)) return;
        lock (gate)
        {
            if (phase != ImportPhase.Choosing) return;
            if (!folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) folders.Add(folder);
            phase = ImportPhase.Scanning;
        }
        Raise();
        await ScanAsync();
    }

    public void Toggle(string key)
    {
        lock (gate)
        {
            if (phase != ImportPhase.Choosing || !found.Plugins.Any(p => p.Key == key)) return;
            if (!ticks.Remove(key)) ticks.Add(key);
        }
        Raise();
    }

    public void ToggleGroup(ForeignPluginApp app)
    {
        lock (gate)
        {
            if (phase != ImportPhase.Choosing) return;
            var keys = found.Plugins.Where(p => p.App == app).Select(p => p.Key).ToArray();
            if (keys.Length == 0) return;
            if (keys.All(ticks.Contains)) ticks.ExceptWith(keys);
            else ticks.UnionWith(keys);
        }
        Raise();
    }

    public void SetChoice(DuplicateChoice value)
    {
        if (value == DuplicateChoice.KeepBoth) return; // a plugin's name is its manifest's: two own plugins never share one
        lock (gate)
        {
            if (choice == value) return;
            choice = value;
        }
        Raise();
    }

    public Task<PluginImportOutcome?> ImportSelectedAsync() => ImportAsync(all: false);

    public Task<PluginImportOutcome?> ImportAllAsync() => ImportAsync(all: true);

    private async Task<PluginImportOutcome?> ImportAsync(bool all)
    {
        ForeignPlugin[] picked;
        DuplicateChoice chosen;
        lock (gate)
        {
            if (phase != ImportPhase.Choosing) return null;
            picked = [.. found.Plugins.Where(p => all || ticks.Contains(p.Key))];
            if (picked.Length == 0) return null;
            chosen = choice;
            phase = ImportPhase.Importing;
        }
        Raise();
        int imported = 0, replaced = 0, skipped = 0;
        var failed = new List<(string, string)>();
        foreach (var plugin in picked)
        {
            try
            {
                var own = await target.ExistingAsync();
                var same = own.FirstOrDefault(o => string.Equals(o.Name, plugin.Name, StringComparison.OrdinalIgnoreCase));
                if (same.Id is not null && chosen == DuplicateChoice.Skip) { skipped++; continue; }
                var reason = await target.AddAsync(plugin.Path, plugin.Name, same.Id, CancellationToken.None);
                if (reason is not null) { failed.Add((plugin.Name, reason)); continue; }
                if (same.Id is not null) replaced++; else imported++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or ArgumentException or OperationCanceledException or System.Text.Json.JsonException)
            {
                failed.Add((plugin.Name, e.Message));
            }
        }
        var result = new PluginImportOutcome(imported, replaced, skipped, failed);
        lock (gate)
        {
            outcome = result;
            if (phase == ImportPhase.Importing) phase = ImportPhase.Done; // closed meanwhile: the import finished, the sheet stays closed
        }
        Raise();
        return result;
    }

    public void Close()
    {
        lock (gate)
        {
            if (phase == ImportPhase.Closed) return;
            phase = ImportPhase.Closed;
            ticks.Clear();
        }
        Raise();
    }

    private void Raise()
    {
        lock (gate) revision++;
        Changed?.Invoke();
    }
}
