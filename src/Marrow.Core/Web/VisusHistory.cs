// PORT-ONLY: web surfaces W8a (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.2 "Suggestions come from history and open tabs
// only, no remote suggestion calls unless the user enables them", 8.1 "'Clear browsing data' per profile (cookies, cache,
// history, site permissions) ... private tabs leave nothing", 3.11 "browser tabs (URL, title, scroll, zoom, device preset)";
// ED-NEW-web-w8a-1). No Swift counterpart. nth's own record of visits (WebView2 keeps none it can be asked for), the address
// field's suggestions made from it and the open tabs, and the per-thread tab list that is saved with the thread.
using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Port;

namespace Marrow.Core.Web;

/// <summary>One page in the history: its address, title, last visit and how often it was visited.</summary>
public sealed record VisusHistoryEntry(string Url, string Title, DateTimeOffset LastVisit, int Visits, WebProfileKind Profile);

/// <summary>
/// The visits of browse and preview tabs (never private or agent tabs), newest first, at most <see cref="Capacity"/>.
/// Every member runs on the UI thread.
/// </summary>
public sealed class VisusHistory
{
    /// <summary>The most entries kept; the oldest go first.</summary>
    public const int Capacity = 2000;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly List<VisusHistoryEntry> entries = [];

    /// <summary>Newest first.</summary>
    public IReadOnlyList<VisusHistoryEntry> Entries => entries;

    /// <summary>Raised after every change (the app saves the file then).</summary>
    public event Action? Changed;

    /// <summary>
    /// A finished visit of <paramref name="url"/> in a tab of <paramref name="kind"/>. Private and agent tabs, and anything that is
    /// not an http or https page, leave nothing (8.1).
    /// </summary>
    public void Record(VisusTabKind kind, string? url, string? title, DateTimeOffset at)
    {
        if (!VisusTabRules.RecordsHistory(kind) || !WebPolicy.IsWeb(url)) return;
        var profile = VisusTabRules.Profile(kind);
        var index = entries.FindIndex(entry => entry.Profile == profile && string.Equals(entry.Url, url, StringComparison.Ordinal));
        var visits = 1;
        var kept = title;
        if (index >= 0)
        {
            visits = entries[index].Visits + 1;
            if (string.IsNullOrWhiteSpace(kept)) kept = entries[index].Title;
            entries.RemoveAt(index);
        }
        entries.Insert(0, new VisusHistoryEntry(url!, kept?.Trim() ?? "", at, visits, profile));
        if (entries.Count > Capacity) entries.RemoveRange(Capacity, entries.Count - Capacity);
        Changed?.Invoke();
    }

    /// <summary>The page's title arrived after the visit was recorded.</summary>
    public void SetTitle(VisusTabKind kind, string? url, string? title)
    {
        if (!VisusTabRules.RecordsHistory(kind) || url is null || string.IsNullOrWhiteSpace(title)) return;
        var profile = VisusTabRules.Profile(kind);
        var index = entries.FindIndex(entry => entry.Profile == profile && string.Equals(entry.Url, url, StringComparison.Ordinal));
        if (index < 0 || entries[index].Title == title.Trim()) return;
        entries[index] = entries[index] with { Title = title.Trim() };
        Changed?.Invoke();
    }

    /// <summary>"Clear browsing data" for one profile (8.1).</summary>
    public void Clear(WebProfileKind profile)
    {
        if (entries.RemoveAll(entry => entry.Profile == profile) > 0) Changed?.Invoke();
    }

    public string ToJson() => JsonSerializer.Serialize(entries, Json);

    /// <summary>The saved history; a damaged file reads as empty (the visits are a convenience, never worth a crash).</summary>
    public static VisusHistory FromJson(string? json)
    {
        var history = new VisusHistory();
        if (string.IsNullOrWhiteSpace(json)) return history;
        try
        {
            var read = JsonSerializer.Deserialize<List<VisusHistoryEntry>>(json, Json) ?? [];
            history.entries.AddRange(read
                .Where(entry => entry is not null && WebPolicy.IsWeb(entry.Url) && entry.Profile is WebProfileKind.Browse or WebProfileKind.Preview)
                .OrderByDescending(entry => entry.LastVisit)
                .Take(Capacity)
                .Select(entry => entry with { Title = entry.Title ?? "", Visits = Math.Max(1, entry.Visits) }));
        }
        catch (JsonException)
        {
        }
        return history;
    }
}

/// <summary>What a suggestion row does.</summary>
public enum VisusSuggestionKind
{
    /// <summary>Go to the typed address.</summary>
    Go,

    /// <summary>Search for what was typed.</summary>
    Search,

    /// <summary>Open the typed app link (through the 8.3 prompt).</summary>
    OpenApp,

    /// <summary>Switch to an open tab that shows this page.</summary>
    SwitchToTab,

    /// <summary>A page from the history.</summary>
    History,
}

/// <summary>One row under the address field: what it does, the address it goes to, its title and its detail.</summary>
public sealed record VisusSuggestion(VisusSuggestionKind Kind, string Url, string Title, string Detail, string? TabId = null);

/// <summary>An open tab the suggestions may offer ("Switch to tab").</summary>
public sealed record VisusOpenTab(string Id, string Url, string Title, VisusTabKind Kind);

/// <summary>DESIGN 5.2: the address field's suggestions, from what was typed, the open tabs and the history only.</summary>
public static class VisusSuggestions
{
    /// <summary>The most rows the list shows.</summary>
    public const int Limit = 6;

    /// <summary>
    /// The rows for <paramref name="input"/>: first what Enter does (go, search or open the app), then open tabs that match (not
    /// the current one, never a private tab's page from a normal tab), then matching history, best first. Nothing leaves the PC.
    /// </summary>
    public static IReadOnlyList<VisusSuggestion> For(
        string? input, SearchEngine engine, IReadOnlyList<VisusOpenTab> openTabs, IEnumerable<VisusHistoryEntry> history,
        string? currentTabId, VisusTabKind currentKind, DateTimeOffset now, Func<string, bool>? isRegisteredScheme = null)
    {
        var text = (input ?? "").Trim();
        var rows = new List<VisusSuggestion>();
        if (text.Length == 0) return rows;
        switch (VisusAddress.Resolve(text, engine, isRegisteredScheme))
        {
            case { Action: AddressAction.Navigate, Url: { } url }:
                rows.Add(new VisusSuggestion(VisusSuggestionKind.Go, url, VisusAddress.Display(url), "Go to this address"));
                break;
            case { Action: AddressAction.Search, Url: { } url, Query: { } query }:
                rows.Add(new VisusSuggestion(VisusSuggestionKind.Search, url, query, "Search " + SearchEngines.Name(engine)));
                break;
            case { Action: AddressAction.External, Url: { } url }:
                rows.Add(new VisusSuggestion(VisusSuggestionKind.OpenApp, url, url, "Open in another app"));
                break;
        }

        var tokens = text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<string>(rows.Select(row => row.Url), StringComparer.Ordinal);
        var tabs = openTabs
            .Where(tab => tab.Id != currentTabId && WebPolicy.IsWeb(tab.Url) && (tab.Kind != VisusTabKind.Private || currentKind == VisusTabKind.Private))
            .Select(tab => (tab, score: Score(tokens, text, tab.Url, tab.Title, 1, null, now)))
            .Where(pair => pair.score > 0)
            .OrderByDescending(pair => pair.score)
            .Take(2);
        foreach (var (tab, _) in tabs)
        {
            if (!seen.Add(tab.Url)) continue;
            rows.Add(new VisusSuggestion(VisusSuggestionKind.SwitchToTab, tab.Url, Title(tab.Title, tab.Url), "Switch to this tab", tab.Id));
        }

        var pages = history
            .Select(entry => (entry, score: Score(tokens, text, entry.Url, entry.Title, entry.Visits, entry.LastVisit, now)))
            .Where(pair => pair.score > 0)
            .OrderByDescending(pair => pair.score)
            .ThenByDescending(pair => pair.entry.LastVisit);
        foreach (var (entry, _) in pages)
        {
            if (rows.Count >= Limit) break;
            if (!seen.Add(entry.Url)) continue;
            rows.Add(new VisusSuggestion(VisusSuggestionKind.History, entry.Url, Title(entry.Title, entry.Url), VisusAddress.Display(entry.Url)));
        }
        return rows;
    }

    /// <summary>
    /// W10, "Show search suggestions": the search engine's own suggested searches (at most <see cref="SearchSuggestionRules.MaxShown"/>) go under the
    /// typed row, above the reader's tabs and history; the list stays within <see cref="Limit"/>. A suggestion already in the list is not repeated.
    /// With no typed search row (an address, an app link) there is nothing to add to.
    /// </summary>
    public static IReadOnlyList<VisusSuggestion> WithRemote(IReadOnlyList<VisusSuggestion> local, IReadOnlyList<string> remote, SearchEngine engine)
    {
        if (local.Count == 0 || remote.Count == 0 || local[0].Kind != VisusSuggestionKind.Search) return local;
        var seen = new HashSet<string>(local.Where(row => row.Kind == VisusSuggestionKind.Search).Select(row => row.Title), StringComparer.OrdinalIgnoreCase);
        var rows = new List<VisusSuggestion> { local[0] };
        foreach (var text in remote)
        {
            if (rows.Count - 1 >= SearchSuggestionRules.MaxShown) break;
            if (!seen.Add(text)) continue;
            rows.Add(new VisusSuggestion(VisusSuggestionKind.Search, SearchEngines.QueryUrl(engine, text), text, "Search " + SearchEngines.Name(engine)));
        }
        rows.AddRange(local.Skip(1));
        return [.. rows.Take(Limit)];
    }

    private static string Title(string title, string url) => string.IsNullOrWhiteSpace(title) ? VisusAddress.Display(url) : title.Trim();

    // Every word typed must appear in the address or the title. A host that starts with what was typed ranks first, then an
    // address that does, then a title word that does; visits and recency break ties.
    private static double Score(string[] tokens, string text, string url, string title, int visits, DateTimeOffset? lastVisit, DateTimeOffset now)
    {
        var shown = VisusAddress.Display(url).ToLowerInvariant();
        var name = (title ?? "").ToLowerInvariant();
        foreach (var token in tokens)
            if (!shown.Contains(token, StringComparison.Ordinal) && !name.Contains(token, StringComparison.Ordinal)) return 0;
        var typed = text.ToLowerInvariant();
        var host = WebPolicy.SourceName(url).ToLowerInvariant();
        var score = 1.0;
        if (host.StartsWith(typed, StringComparison.Ordinal) || host.StartsWith("www." + typed, StringComparison.Ordinal)) score += 100;
        else if (shown.StartsWith(typed, StringComparison.Ordinal)) score += 80;
        else if (name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word => word.StartsWith(tokens[0], StringComparison.Ordinal))) score += 40;
        else score += 10;
        score += Math.Log(1 + Math.Max(1, visits)) * 10;
        if (lastVisit is { } at)
        {
            var age = now - at;
            if (age < TimeSpan.FromDays(1)) score += 20;
            else if (age < TimeSpan.FromDays(7)) score += 10;
        }
        return score;
    }
}

/// <summary>One saved tab of a thread's Visus pane (3.11): its kind, address, title and zoom.</summary>
public sealed record VisusTabState(string Id, VisusTabKind Kind, string Url, string Title, double Zoom = 1)
{
    public PreviewState? Preview { get; init; }
}

/// <summary>
/// DESIGN 3.11: a thread's Visus tabs as they are saved with the thread. Private tabs are never in it; addresses that are not
/// pages (anything but http, https and the blank page) are dropped on the way in.
/// </summary>
public sealed record VisusTabsState
{
    public static VisusTabsState Empty { get; } = new();

    public ValueList<VisusTabState> Tabs { get; init; } = ValueList<VisusTabState>.Empty;

    public string? SelectedId { get; init; }

    /// <summary>The saveable form: private tabs out, bad addresses out, ids unique, the selection one of the tabs (or null).</summary>
    public VisusTabsState Normalized()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var kept = Tabs
            .Where(tab => tab is not null && VisusTabRules.IsPersisted(tab.Kind) && !string.IsNullOrEmpty(tab.Id) && WebPolicy.IsTopLevelAllowed(tab.Url))
            .Where(tab => ids.Add(tab.Id))
            .Select(tab => tab with { Title = tab.Title ?? "", Zoom = double.IsFinite(tab.Zoom) && tab.Zoom > 0 ? tab.Zoom : 1, Preview = tab.Kind == VisusTabKind.Preview ? tab.Preview?.Normalized() : null })
            .ToList();
        var selected = kept.Any(tab => tab.Id == SelectedId) ? SelectedId : kept.FirstOrDefault()?.Id;
        return new VisusTabsState { Tabs = new ValueList<VisusTabState>(kept), SelectedId = selected };
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// The JSON the thread keeps for its Visus pane (ThreadRuntime.SetPaneState, web W3c), always of the normalized form:
    /// <c>{"selected":id,"tabs":[{"id","kind","url","title","zoom"}]}</c>.
    /// </summary>
    public string ToJson()
    {
        var saved = Normalized();
        var tabs = saved.Tabs.Select(tab => (SavedTab?)new SavedTab(tab.Id, tab.Kind.ToString(), tab.Url, tab.Title, tab.Zoom, tab.Preview)).ToList();
        return JsonSerializer.Serialize(new Saved(saved.SelectedId, tabs), Json);
    }

    /// <summary>
    /// The saved tabs back from <see cref="ToJson"/>, normalized. Text that does not parse reads as no tabs; a tab of a kind
    /// this build does not know (or a private one written by hand) is dropped, the others kept.
    /// </summary>
    public static VisusTabsState FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        try
        {
            if (JsonSerializer.Deserialize<Saved>(json, Json) is not { Tabs: { } read } saved) return Empty;
            var tabs = new List<VisusTabState>();
            foreach (var tab in read)
            {
                if (tab is { Id: { } id, Url: { } url } && Enum.TryParse<VisusTabKind>(tab.Kind, ignoreCase: false, out var kind) && Enum.IsDefined(kind))
                    tabs.Add(new VisusTabState(id, kind, url, tab.Title ?? "", tab.Zoom ?? 1) { Preview = tab.Preview });
            }
            return new VisusTabsState { Tabs = new ValueList<VisusTabState>(tabs), SelectedId = saved.Selected }.Normalized();
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private sealed record Saved(string? Selected, List<SavedTab?>? Tabs);

    private sealed record SavedTab(string? Id, string? Kind, string? Url, string? Title, double? Zoom,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] PreviewState? Preview = null);
}
