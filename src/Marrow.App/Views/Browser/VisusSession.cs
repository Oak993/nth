using Marrow.App.Runtime;
using Marrow.Core.Web;

// PORT-ONLY: web surfaces W8a, a thread's Visus tabs (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.1 "tab strip ... New tab
// (+), the tab overflow menu", 5.2 address and search, 2.3 profiles ("The model never inherits browse sessions"), 3.11
// persistence, 8.1, 8.3; the canvas's "Ctrl+T opens a new tab · Ctrl+Shift+N a private one"; ED-NEW-web-w8a-2). No Swift
// counterpart. The tabs, which one is chosen, the closed tabs that can come back, what typing an address does, and the
// browser commands. The pane draws it; nothing here touches Avalonia, so the headless tests drive it directly too.

namespace Marrow.App.Views.Browser;

/// <summary>A thread's Visus tabs and what they do.</summary>
public sealed class VisusSession
{
    /// <summary>The most closed tabs Ctrl+Shift+T can bring back.</summary>
    public const int ClosedLimit = 25;

    private readonly List<VisusTab> tabs = [];
    private readonly Stack<(VisusTabState State, int Index)> closed = new();
    private readonly ThreadRuntime? runtime;
    internal ThreadRuntime? Runtime => runtime;
    private VisusTab? selected;
    private bool visible;
    private bool restoring;

    public VisusSession(ThreadRuntime? runtime = null)
    {
        this.runtime = runtime;
        if (runtime?.VisusTabs is { Tabs.Count: > 0 } saved) Restore(saved);
    }

    public IReadOnlyList<VisusTab> Tabs => tabs;

    public VisusTab? Selected => selected;

    /// <summary>The window the tabs' web views belong to (the live-view cap and focus are per window; 2.3).</summary>
    public string WindowId { get; set; } = "main";

    /// <summary>The thread's folder, for agent tabs' own profile (2.3).</summary>
    public string? ThreadFolder { get; set; }

    /// <summary>The chat's thread id as the web tools name it (W11): the pane shows this chat's agent cards.</summary>
    public string? AgentThread { get; set; }

    /// <summary>Whether the pane is on screen; only then is the chosen tab's view shown.</summary>
    public bool IsVisible
    {
        get => visible;
        set
        {
            if (visible == value) return;
            visible = value;
            UpdateShown();
        }
    }

    /// <summary>Tabs were added, closed, moved or chosen.</summary>
    public event Action? Changed;

    /// <summary>The address field should take the keyboard (a new tab, Ctrl+L).</summary>
    public event Action? FocusAddressRequested;
    public event Action? FindRequested;
    public event Action? ZoomRequested;
    public event Action? SavePdfRequested;

    /// <summary>The last tab closed: the pane closes (a browser window closes with its last tab).</summary>
    public event Action? Emptied;

    /// <summary>A quiet notice for the pane to show under the address field (a refusal, a blocked link).</summary>
    public event Action<VisusTab?, string>? Notice;

    /// <summary>
    /// An app link wants opening (8.3): the pane shows the prompt and answers it. A handler that cannot ask leaves it; with no
    /// handler, <see cref="RequestLaunch"/> answers Cancel.
    /// </summary>
    public event Func<VisusExternalLaunch, bool>? LaunchRequested;

    /// <summary>Keys the pane takes from inside a page besides the browser keys (an open prompt's Esc, 10.1).</summary>
    public Func<Marrow.Core.Models.KeyChord, bool>? ClaimsPageKey { get; set; }

    /// <summary>A key <see cref="ClaimsPageKey"/> took was pressed in a tab's page.</summary>
    public event Action<VisusTab, Marrow.Core.Models.KeyChord>? PageKey;

    internal void RaisePageKey(VisusTab tab, Marrow.Core.Models.KeyChord chord) => PageKey?.Invoke(tab, chord);

    /// <summary>
    /// PORT-ONLY (web surfaces W11, ED-NEW-web-w11-1; DESIGN 5.10 "User takes over"): the user acted on a tab from Visus's own
    /// chrome (typed an address, chose a suggestion, Back, Forward, Reload); a tab the model drives pauses for it.
    /// </summary>
    public event Action<VisusTab>? UserActed;

    /// <summary>The user acted on <paramref name="tab"/> from Visus's chrome (the toolbar, the address field, the menus).</summary>
    internal void Acted(VisusTab? tab)
    {
        if (tab is not null) UserActed?.Invoke(tab);
    }

    /// <summary>
    /// PORT-ONLY (web surfaces W11; DESIGN 2.3): "Let the model use this tab" shares one browse or preview tab with the chat's
    /// model (sharing another ends the first); false stops sharing. A private or agent tab is never shared this way.
    /// </summary>
    public void ShareWithModel(VisusTab tab, bool share)
    {
        if (!tabs.Contains(tab) || tab.Kind is VisusTabKind.Private or VisusTabKind.Agent) return;
        if (share)
            foreach (var other in tabs.Where(t => !ReferenceEquals(t, tab))) other.SetSharedWithModel(false);
        tab.SetSharedWithModel(share);
        Changed?.Invoke();
    }

    /// <summary>
    /// Opens a new tab after the chosen one (or at <paramref name="at"/>). With an address it opens it; without, the address field
    /// takes the keyboard (Ctrl+T, the + button).
    /// </summary>
    public VisusTab NewTab(VisusTabKind kind = VisusTabKind.Browse, string? url = null, bool select = true, int? at = null)
    {
        var tab = new VisusTab(this, kind);
        var index = Math.Clamp(at ?? (selected is null ? tabs.Count : tabs.IndexOf(selected) + 1), 0, tabs.Count);
        tabs.Insert(index, tab);
        if (select || selected is null) selected = tab;
        if (url is not null && WebPolicy.IsTopLevelAllowed(url) && !WebPolicy.IsBlank(url)) tab.Open(url);
        UpdateShown();
        Save();
        Changed?.Invoke();
        if (select && (url is null || WebPolicy.IsBlank(url))) FocusAddressRequested?.Invoke();
        return tab;
    }

    /// <summary>Chooses a tab.</summary>
    public void Select(VisusTab tab)
    {
        if (!tabs.Contains(tab) || ReferenceEquals(selected, tab)) return;
        selected = tab;
        UpdateShown();
        Save();
        Changed?.Invoke();
    }

    /// <summary>Closes a tab; its neighbour on the right (else the left) is chosen. A web tab can come back with Ctrl+Shift+T.</summary>
    public void Close(VisusTab tab)
    {
        var index = tabs.IndexOf(tab);
        if (index < 0) return;
        if (VisusTabRules.MayReopen(tab.Kind) && !tab.IsBlank)
        {
            closed.Push((tab.State, index));
            while (closed.Count > ClosedLimit) TrimOldestClosed();
        }
        RemoveAt(index);
        if (tabs.Count == 0)
        {
            selected = null;
            Save();
            Changed?.Invoke();
            Emptied?.Invoke();
            return;
        }
        UpdateShown();
        Save();
        Changed?.Invoke();
    }

    /// <summary>Closes every tab but <paramref name="keep"/>.</summary>
    public void CloseOthers(VisusTab keep)
    {
        if (!tabs.Contains(keep)) return;
        foreach (var tab in tabs.Where(tab => !ReferenceEquals(tab, keep)).ToList())
        {
            if (VisusTabRules.MayReopen(tab.Kind) && !tab.IsBlank) closed.Push((tab.State, tabs.IndexOf(tab)));
            RemoveAt(tabs.IndexOf(tab));
        }
        while (closed.Count > ClosedLimit) TrimOldestClosed();
        selected = keep;
        UpdateShown();
        Save();
        Changed?.Invoke();
    }

    /// <summary>Ctrl+Shift+T: the last closed web tab comes back where it was. False when there is none.</summary>
    public bool ReopenClosed()
    {
        if (closed.Count == 0) return false;
        var (state, index) = closed.Pop();
        var tab = new VisusTab(this, state.Kind, url: state.Url, title: state.Title, zoom: state.Zoom, preview: state.Preview);
        tabs.Insert(Math.Clamp(index, 0, tabs.Count), tab);
        selected = tab;
        tab.Reopen();
        UpdateShown();
        Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Whether a closed tab can come back.</summary>
    public bool CanReopenClosed => closed.Count > 0;

    /// <summary>Moves a tab to <paramref name="index"/> (dragging it along the strip).</summary>
    public void Move(VisusTab tab, int index)
    {
        var from = tabs.IndexOf(tab);
        if (from < 0) return;
        index = Math.Clamp(index, 0, tabs.Count - 1);
        if (from == index) return;
        tabs.RemoveAt(from);
        tabs.Insert(index, tab);
        Save();
        Changed?.Invoke();
    }

    /// <summary>A copy of a tab next to it (the tab's menu).</summary>
    public VisusTab? Duplicate(VisusTab tab)
    {
        if (!tabs.Contains(tab) || tab.IsBlank) return null;
        return NewTab(tab.Kind, tab.Url, select: true, at: tabs.IndexOf(tab) + 1);
    }

    /// <summary>
    /// DESIGN 5.2: what the address field does with what was typed into the chosen tab: go there, search, ask to open an app,
    /// or say why not. A dev host typed into a normal tab opens as a preview, and a public address typed into a preview opens as
    /// a normal tab (2.3); a tab that has no page yet becomes that tab, otherwise the new one opens beside it.
    /// </summary>
    public AddressResult? Go(string? input)
    {
        var current = selected;
        var result = VisusAddress.Resolve(input, VisusHost.SearchEngine(), VisusHost.IsRegisteredScheme);
        switch (result)
        {
            case null:
                return null;
            case { Action: AddressAction.Refused, Refusal: { } why }:
                Notify(current, why);
                return result;
            case { Action: AddressAction.External, Url: { } link }:
                OpenTypedApp(current, link);
                return result;
            case { Url: { } address }:
                Acted(current);
                Navigate(current, address);
                return result;
        }
        return result;
    }

    /// <summary>Opens a suggestion: a page goes like a typed address, an open tab is chosen.</summary>
    public void Choose(VisusSuggestion suggestion)
    {
        if (suggestion.Kind == VisusSuggestionKind.SwitchToTab && tabs.FirstOrDefault(tab => tab.Id == suggestion.TabId) is { } open)
        {
            Select(open);
            return;
        }
        if (suggestion.Kind == VisusSuggestionKind.OpenApp)
        {
            OpenTypedApp(selected, suggestion.Url);
            return;
        }
        if (!WebPolicy.IsTopLevelAllowed(suggestion.Url)) return;
        Acted(selected);
        Navigate(selected, suggestion.Url);
    }

    /// <summary>A browser key or menu command (3.7), for <paramref name="from"/> (the tab whose page had the key) or the chosen tab.</summary>
    public bool Perform(VisusCommand command, VisusTab? from = null)
    {
        var tab = from ?? selected;
        switch (command)
        {
            case VisusCommand.PickElement:
                if (tab?.Kind != VisusTabKind.Preview || tab.Preview?.Picker is not { } picker) return false;
                _ = picker.SetEnabledAsync(!picker.Enabled);
                return true;
            case VisusCommand.Find:
            case VisusCommand.ShowZoom:
            case VisusCommand.SavePdf:
                if (tab is null || tab.IsBlank || !tabs.Contains(tab)) return false;
                Select(tab);
                if (command == VisusCommand.Find) FindRequested?.Invoke();
                else if (command == VisusCommand.ShowZoom) ZoomRequested?.Invoke();
                else SavePdfRequested?.Invoke();
                return true;
            case VisusCommand.ZoomIn:
            case VisusCommand.ZoomOut:
            case VisusCommand.ResetZoom:
                if (tab is null || tab.IsBlank) return false;
                tab.SetZoom(command == VisusCommand.ResetZoom ? 1 : VisusSiteZoom.Step(tab.Zoom, command == VisusCommand.ZoomIn));
                return true;
            case VisusCommand.Print:
                if (tab is null || tab.IsBlank) return false;
                try
                {
                    if (tab.ReadyEngine() is { IsAttached: true } printEngine) printEngine.ShowPrintUI();
                    else Notify(tab, "Could not print this page");
                }
                catch (Exception) { Notify(tab, "Could not print this page"); }
                return true;
            case VisusCommand.NewTab:
                NewTab(VisusTabKind.Browse);
                return true;
            case VisusCommand.NewPrivateTab:
                NewTab(VisusTabKind.Private);
                return true;
            case VisusCommand.CloseTab:
                if (tab is null) return false;
                Close(tab);
                return true;
            case VisusCommand.ReopenClosedTab:
                return ReopenClosed();
            case VisusCommand.FocusAddress:
                if (tab is not null && !ReferenceEquals(tab, selected)) Select(tab);
                FocusAddressRequested?.Invoke();
                return true;
            case VisusCommand.Reload:
                Acted(tab);
                tab?.Reload();
                return tab is not null;
            case VisusCommand.HardReload:
                Acted(tab);
                tab?.Reload(hard: true);
                return tab is not null;
            case VisusCommand.Back:
                Acted(tab);
                tab?.GoBack();
                return tab is not null;
            case VisusCommand.Forward:
                Acted(tab);
                tab?.GoForward();
                return tab is not null;
            case VisusCommand.NextTab:
            case VisusCommand.PreviousTab:
                if (selected is null || tabs.Count < 2) return selected is not null;
                var at = tabs.IndexOf(selected) + (command == VisusCommand.NextTab ? 1 : -1);
                Select(tabs[(at + tabs.Count) % tabs.Count]);
                return true;
            case VisusCommand.SelectLastTab:
                if (tabs.Count == 0) return false;
                Select(tabs[^1]);
                return true;
            default:
                var number = command - VisusCommand.SelectTab1;
                if (number < 0 || number >= tabs.Count) return tabs.Count > 0;
                Select(tabs[number]);
                return true;
        }
    }

    /// <summary>Opens an address a page opened (a link to a new tab, an allowed pop-up), beside its tab, in the kind the rules give.</summary>
    internal VisusTab OpenFromPage(VisusTab opener, string url)
    {
        var at = tabs.IndexOf(opener);
        return NewTab(VisusTabRules.KindForOpened(opener.Kind, url), url, select: true, at: at < 0 ? null : at + 1);
    }

    internal void Notify(VisusTab? tab, string text) => Notice?.Invoke(tab, text);

    /// <summary>Hands an app link to the pane's prompt; false when nobody can ask (the caller answers Cancel).</summary>
    internal bool RequestLaunch(VisusExternalLaunch launch)
    {
        if (LaunchRequested is not { } handlers) return false;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<VisusExternalLaunch, bool>>())
            if (handler(launch)) return true;
        return false;
    }

    /// <summary>A tab's address or title changed: the saved form follows.</summary>
    internal void TabStateChanged() => Save();

    /// <summary>The saveable form (3.11): every tab but private ones, and the chosen tab.</summary>
    public VisusTabsState Snapshot() => new VisusTabsState
    {
        Tabs = new Marrow.Core.Port.ValueList<VisusTabState>(tabs.Where(tab => VisusTabRules.IsPersisted(tab.Kind)).Select(tab => tab.State)),
        SelectedId = selected is { } chosen && VisusTabRules.IsPersisted(chosen.Kind) ? chosen.Id : null,
    }.Normalized();

    /// <summary>Closes every tab and its view for good (the thread is gone).</summary>
    public void Dispose()
    {
        foreach (var tab in tabs.ToList()) tab.Release();
        tabs.Clear();
        closed.Clear();
        selected = null;
    }

    private void Navigate(VisusTab? current, string address)
    {
        var kind = VisusTabRules.KindForTyped(current?.Kind ?? VisusTabKind.Browse, address);
        if (current is null)
        {
            NewTab(kind, address);
            return;
        }
        if (kind == current.Kind)
        {
            current.Open(address);
            return;
        }
        var index = tabs.IndexOf(current);
        if (current.IsBlank)
        {
            // The tab had no page: it becomes the tab the address needs, in its place.
            RemoveAt(index);
            NewTab(kind, address, select: true, at: index);
            return;
        }
        NewTab(kind, address, select: true, at: index + 1);
    }

    private void OpenTypedApp(VisusTab? current, string link)
    {
        var tab = current ?? NewTab(VisusTabKind.Browse);
        var decision = ExternalProtocols.Decide(VisusTabRules.Profile(tab.Kind), link, isUserInitiated: true);
        if (decision == ExternalLaunchDecision.Deny)
        {
            Notify(tab, "This tab cannot open other apps.");
            return;
        }
        // Typed by the user into nth's own field: nothing to remember for a site.
        var launch = new VisusExternalLaunch(tab, link, "Visus", null, ExternalLaunchDecision.Ask, open =>
        {
            if (open && !VisusHost.OpenExternal(link)) Notify(tab, "No app on this PC opens " + WebPolicy.SchemeOf(link) + ": links.");
        });
        if (!RequestLaunch(launch)) launch.Answer(false);
    }

    private void RemoveAt(int index)
    {
        var tab = tabs[index];
        tabs.RemoveAt(index);
        tab.SetShown(false);
        tab.Release();
        if (ReferenceEquals(selected, tab)) selected = tabs.Count == 0 ? null : tabs[Math.Min(index, tabs.Count - 1)];
    }

    private void TrimOldestClosed()
    {
        var kept = closed.Reverse().Skip(1).ToList();
        closed.Clear();
        foreach (var item in kept) closed.Push(item);
    }

    private void UpdateShown()
    {
        foreach (var tab in tabs) tab.SetShown(visible && ReferenceEquals(tab, selected));
    }

    private void Save()
    {
        if (restoring || runtime is null) return;
        runtime.VisusTabs = Snapshot();
    }

    private void Restore(VisusTabsState saved)
    {
        restoring = true;
        try
        {
            foreach (var state in saved.Normalized().Tabs)
            {
                tabs.Add(new VisusTab(this, state.Kind, state.Id, state.Url, state.Title, state.Zoom, state.Preview));
            }
            selected = tabs.FirstOrDefault(tab => tab.Id == saved.SelectedId) ?? tabs.FirstOrDefault();
        }
        finally
        {
            restoring = false;
        }
    }
}
