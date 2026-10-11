// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-1). The logic of the "Import servers" sheet, in the pattern of "Import skills" (ImportController, SK-18): no Avalonia; the view draws
// what it holds and sends it the user's presses. The scan runs off the caller; the import runs on the caller (the UI thread, where the MCP store is called). Changed may come from either thread.
using Marrow.Core.Port.MCP.Import;
using Marrow.Services.Port.Skills.Import;

namespace Marrow.Services.Port.MCP.Import;

public enum McpImportPhase
{
    Closed,
    Scanning,
    Choosing,
    Done,
}

/// <summary>A row of the sheet: a server (tickable), or a file that could not be read (<see cref="Unreadable"/>, never tickable).</summary>
public sealed record McpImportRow(string Key, string Name, string Detail, string Source, bool Ticked, bool Duplicate, bool Unreadable = false);

public sealed record McpImportGroup(ForeignMcpApp App, string Title, IReadOnlyList<McpImportRow> Rows, bool AllTicked);

public sealed class McpImportController(Func<ForeignMcpScan> scan, IMcpImportTarget target)
{
    private readonly object gate = new();
    private readonly HashSet<string> ticks = new(StringComparer.Ordinal);
    private ForeignMcpScan found = ForeignMcpScan.Empty;
    private McpImportPhase phase = McpImportPhase.Closed;
    private DuplicateChoice choice = DuplicateChoice.Skip;
    private McpImportOutcome? outcome;
    private int revision;

    public event Action? Changed;

    public int Revision { get { lock (gate) return revision; } }

    public McpImportPhase Phase { get { lock (gate) return phase; } }

    /// <summary>What a duplicate gets; Skip until the user picks another.</summary>
    public DuplicateChoice Choice { get { lock (gate) return choice; } }

    public int TickedCount { get { lock (gate) return ticks.Count; } }

    public int ServerCount { get { lock (gate) return found.Servers.Count; } }

    public int ProblemCount { get { lock (gate) return found.Problems.Count; } }

    public McpImportOutcome? Outcome { get { lock (gate) return outcome; } }

    public string Summary { get { lock (gate) return outcome?.Summary ?? ""; } }

    /// <summary>Whether any ticked server is already in nth (then the sheet asks what to do with it).</summary>
    public bool TickedDuplicates
    {
        get
        {
            var customs = target.Customs;
            lock (gate) return found.Servers.Any(s => ticks.Contains(s.Key) && McpImport.DuplicateOf(s, customs) is not null);
        }
    }

    public IReadOnlyList<McpImportGroup> Groups
    {
        get
        {
            var customs = target.Customs; // read before the gate: the store is the UI thread's
            lock (gate)
            {
                var apps = found.Servers.Select(s => s.App).Concat(found.Problems.Select(p => p.App)).Distinct().Order();
                return [.. apps.Select(app =>
                {
                    var rows = found.Servers.Where(s => s.App == app)
                        .Select(s => new McpImportRow(s.Key, s.Name, s.Detail, s.Source, ticks.Contains(s.Key), McpImport.DuplicateOf(s, customs) is not null))
                        .Concat(found.Problems.Where(p => p.App == app).Select(p => new McpImportRow("problem|" + p.Path, Path.GetFileName(p.Path), p.Path, McpImportWords.NotRead, false, false, Unreadable: true)))
                        .ToArray();
                    var servers = rows.Where(r => !r.Unreadable).ToArray();
                    return new McpImportGroup(app, Title(app), rows, servers.Length > 0 && servers.All(r => r.Ticked));
                })];
            }
        }
    }

    public static string Title(ForeignMcpApp app) => app switch
    {
        ForeignMcpApp.ClaudeCode => McpImportWords.ClaudeCode,
        ForeignMcpApp.ClaudeDesktop => McpImportWords.ClaudeDesktop,
        ForeignMcpApp.Codex => McpImportWords.Codex,
        ForeignMcpApp.Cursor => McpImportWords.Cursor,
        ForeignMcpApp.VSCode => McpImportWords.VSCode,
        ForeignMcpApp.Windsurf => McpImportWords.Windsurf,
        ForeignMcpApp.Gemini => McpImportWords.Gemini,
        _ => app.ToString(),
    };

    /// <summary>Opens the sheet and scans. Only a closed sheet opens.</summary>
    public async Task OpenAsync()
    {
        lock (gate)
        {
            if (phase != McpImportPhase.Closed) return;
            ticks.Clear();
            outcome = null;
            choice = DuplicateChoice.Skip;
            found = ForeignMcpScan.Empty;
            phase = McpImportPhase.Scanning;
        }
        Raise();
        ForeignMcpScan scanned;
        // the scanner reports every file it cannot read; this is only the last guard, so a scan never takes the sheet down
        try { scanned = await Task.Run(scan); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { scanned = new ForeignMcpScan([], [new ForeignMcpProblem(ForeignMcpApp.ClaudeCode, "", e.Message)]); }
        lock (gate)
        {
            if (phase != McpImportPhase.Scanning) return; // closed meanwhile
            found = scanned;
            phase = McpImportPhase.Choosing;
        }
        Raise();
    }

    public void Toggle(string key)
    {
        lock (gate)
        {
            if (phase != McpImportPhase.Choosing || !found.Servers.Any(s => s.Key == key)) return;
            if (!ticks.Remove(key)) ticks.Add(key);
        }
        Raise();
    }

    public void ToggleGroup(ForeignMcpApp app)
    {
        lock (gate)
        {
            if (phase != McpImportPhase.Choosing) return;
            var keys = found.Servers.Where(s => s.App == app).Select(s => s.Key).ToArray();
            if (keys.Length == 0) return;
            if (keys.All(ticks.Contains)) ticks.ExceptWith(keys);
            else ticks.UnionWith(keys);
        }
        Raise();
    }

    public void SetChoice(DuplicateChoice value)
    {
        lock (gate)
        {
            if (choice == value) return;
            choice = value;
        }
        Raise();
    }

    public McpImportOutcome? ImportSelected() => Import(all: false);

    public McpImportOutcome? ImportAll() => Import(all: true);

    private McpImportOutcome? Import(bool all)
    {
        ForeignMcpServer[] picked;
        DuplicateChoice chosen;
        lock (gate)
        {
            if (phase != McpImportPhase.Choosing) return null;
            picked = [.. found.Servers.Where(s => all || ticks.Contains(s.Key))];
            chosen = choice;
        }
        if (picked.Length == 0) return null;
        var result = McpImport.Import(picked, chosen, target);
        lock (gate)
        {
            outcome = result;
            phase = McpImportPhase.Done;
        }
        Raise();
        return result;
    }

    public void Close()
    {
        lock (gate)
        {
            if (phase == McpImportPhase.Closed) return;
            phase = McpImportPhase.Closed;
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
