// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-1). The logic of the "Import servers" sheet, in the pattern of "Import skills" (ImportController, SK-18): no Avalonia; the view draws
// what it holds and sends it the user's presses. The scan runs off the caller; the import runs on the caller (the UI thread, where the MCP store is called) but hands the thread back between servers
// (ED-NEW-mcpimport-7), so the window never freezes however many servers come in. Changed may come from either thread.
using Marrow.Core.Models;
using Marrow.Core.Port.MCP.Import;
using Marrow.Services.Port.Skills.Import;

namespace Marrow.Services.Port.MCP.Import;

public enum McpImportPhase
{
    Closed,
    Scanning,
    Choosing,
    Importing,
    Done,
}

/// <summary>
/// A row of the sheet: a server (tickable), or a file that could not be read (<see cref="Unreadable"/>, never tickable). A server nth cannot take (the older SSE connection, an address that
/// needs a value nth cannot fill in) has <see cref="Blocked"/> say why and is not tickable either; a server that comes in with an empty value has <see cref="NeedsValue"/> say which.
/// <see cref="Origin"/> is the file (and project) it was found in, for the row's tip.
/// </summary>
public sealed record McpImportRow(string Key, string Name, string Detail, string Source, bool Ticked, bool Duplicate, bool Unreadable = false, string? Blocked = null, string? NeedsValue = null, string Origin = "")
{
    public bool CanTick => !Unreadable && Blocked is null;
}

public sealed record McpImportGroup(ForeignMcpApp App, string Title, IReadOnlyList<McpImportRow> Rows, bool AllTicked);

/// <param name="scan">The read-only scan; it and the filling-in of placeholders run off the caller.</param>
/// <param name="target">Where servers go.</param>
/// <param name="lookup">A variable's value (null: none); default this PC's environment.</param>
/// <param name="yieldFrame">Awaited between servers so the caller's thread can draw (the app lets the UI draw a frame); default none.</param>
public sealed class McpImportController(Func<ForeignMcpScan> scan, IMcpImportTarget target, Func<string, string?>? lookup = null, Func<Task>? yieldFrame = null)
{
    private readonly object gate = new();
    private readonly HashSet<string> ticks = new(StringComparer.Ordinal);
    private Dictionary<string, McpPrepared> prepared = new(StringComparer.Ordinal);
    private int done, total;
    private bool stopRequested;
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

    /// <summary>Servers found that nth cannot take (<see cref="McpImportRow.Blocked"/>).</summary>
    public int BlockedCount { get { lock (gate) return found.Servers.Count(s => Blocked(s) is not null); } }

    /// <summary>While importing: the server being done (0-based) and how many there are.</summary>
    public (int Done, int Total) Progress { get { lock (gate) return (done, total); } }

    public McpImportOutcome? Outcome { get { lock (gate) return outcome; } }

    public string Summary { get { lock (gate) return outcome?.Summary ?? ""; } }

    /// <summary>Whether any ticked server is already in nth (then the sheet asks what to do with it).</summary>
    public bool TickedDuplicates
    {
        get
        {
            var customs = target.Customs;
            lock (gate) return found.Servers.Any(s => ticks.Contains(s.Key) && McpImport.DuplicateOf(Resolved(s), customs) is not null);
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
                        .Select(s => Row(s, customs))
                        .Concat(found.Problems.Where(p => p.App == app).Select(p => new McpImportRow("problem|" + p.Path, Path.GetFileName(p.Path), p.Path, McpImportWords.NotRead, false, false, Unreadable: true)))
                        .ToArray();
                    var servers = rows.Where(r => r.CanTick).ToArray();
                    return new McpImportGroup(app, Title(app), rows, servers.Length > 0 && servers.All(r => r.Ticked));
                })];
            }
        }
    }

    private McpImportRow Row(ForeignMcpServer s, IReadOnlyList<MCPCustomServer> customs)
    {
        var prep = prepared.GetValueOrDefault(s.Key);
        var server = prep?.Server ?? s;
        return new McpImportRow(s.Key, s.Name, s.Detail, s.Source, ticks.Contains(s.Key), McpImport.DuplicateOf(server, customs) is not null, Blocked: prep?.Blocked,
            NeedsValue: prep is { NeedsValue.Count: > 0 } ? McpImportWords.NeedsValueTip(prep.NeedsValue) : null, Origin: McpImportWords.Origin(s.Path, s.ProjectPath));
    }

    private string? Blocked(ForeignMcpServer s) => prepared.TryGetValue(s.Key, out var prep) ? prep.Blocked : s.LegacySse ? McpImportWords.LegacySse : null;

    private ForeignMcpServer Resolved(ForeignMcpServer s) => prepared.TryGetValue(s.Key, out var prep) ? prep.Server : s;

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
            if (phase == McpImportPhase.Importing && stopRequested) { stopRequested = false; return; } // closed while importing, opened again: the import it was is still on show
            if (phase != McpImportPhase.Closed) return;
            ticks.Clear();
            outcome = null;
            choice = DuplicateChoice.Skip;
            found = ForeignMcpScan.Empty;
            prepared = new Dictionary<string, McpPrepared>(StringComparer.Ordinal);
            phase = McpImportPhase.Scanning;
        }
        Raise();
        ForeignMcpScan scanned;
        Dictionary<string, McpPrepared> ready = new(StringComparer.Ordinal);
        // the scanner reports every file it cannot read; this is only the last guard, so a scan never takes the sheet down. Placeholders are filled in here too, off the caller (they read the environment).
        try
        {
            (scanned, ready) = await Task.Run(() =>
            {
                var result = scan();
                var map = new Dictionary<string, McpPrepared>(StringComparer.Ordinal);
                foreach (var server in result.Servers) map.TryAdd(server.Key, McpImport.Prepare(server, lookup));
                return (result, map);
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { scanned = new ForeignMcpScan([], [new ForeignMcpProblem(ForeignMcpApp.ClaudeCode, "", e.Message)]); }
        lock (gate)
        {
            if (phase != McpImportPhase.Scanning) return; // closed meanwhile
            found = scanned;
            prepared = ready;
            phase = McpImportPhase.Choosing;
        }
        Raise();
    }

    public void Toggle(string key)
    {
        lock (gate)
        {
            if (phase != McpImportPhase.Choosing || !found.Servers.Any(s => s.Key == key && Blocked(s) is null)) return;
            if (!ticks.Remove(key)) ticks.Add(key);
        }
        Raise();
    }

    public void ToggleGroup(ForeignMcpApp app)
    {
        lock (gate)
        {
            if (phase != McpImportPhase.Choosing) return;
            var keys = found.Servers.Where(s => s.App == app && Blocked(s) is null).Select(s => s.Key).ToArray();
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
            if (phase != McpImportPhase.Choosing || choice == value) return;
            choice = value;
        }
        Raise();
    }

    public Task<McpImportOutcome?> ImportSelectedAsync() => ImportAsync(all: false);

    public Task<McpImportOutcome?> ImportAllAsync() => ImportAsync(all: true);

    private async Task<McpImportOutcome?> ImportAsync(bool all)
    {
        ForeignMcpServer[] picked;
        DuplicateChoice chosen;
        lock (gate)
        {
            if (phase != McpImportPhase.Choosing) return null;
            picked = [.. found.Servers.Where(s => Blocked(s) is null && (all || ticks.Contains(s.Key)))];
            if (picked.Length == 0) return null;
            chosen = choice;
            phase = McpImportPhase.Importing;
            done = 0;
            total = picked.Length;
            stopRequested = false;
        }
        Raise();
        var result = await McpImport.ImportAsync(picked, chosen, target, lookup, yieldFrame, i => { lock (gate) done = i; Raise(); }, () => { lock (gate) return stopRequested; });
        lock (gate)
        {
            outcome = result;
            phase = stopRequested ? McpImportPhase.Closed : McpImportPhase.Done; // closed meanwhile: what was done stays done, the sheet stays closed
            if (stopRequested) ticks.Clear();
            stopRequested = false;
        }
        Raise();
        return result;
    }

    /// <summary>Closes the sheet. An import under way finishes the server it is on and stops there; what it did stays.</summary>
    public void Close()
    {
        lock (gate)
        {
            if (phase == McpImportPhase.Closed) return;
            if (phase == McpImportPhase.Importing) stopRequested = true;
            else
            {
                phase = McpImportPhase.Closed;
                ticks.Clear();
            }
        }
        Raise();
    }

    private void Raise()
    {
        lock (gate) revision++;
        Changed?.Invoke();
    }
}
