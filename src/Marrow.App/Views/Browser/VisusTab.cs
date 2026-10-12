using System.Text.Json;
using Marrow.Core.Web;
using Marrow.Services.Web;

// PORT-ONLY: web surfaces W8a, one Visus tab (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.1 "tab strip (favicon, title,
// close on hover; private tabs show a mask glyph and a darker tint)", "Back, Forward, Reload (Stop while loading) ... the site
// chip", 5.3 "Back and Forward with long-press history menus. Reload, hard reload (Shift)", 8.1 "NavigationStarting and
// FrameNavigationStarting enforce the scheme rules of 5.2. NewWindowRequested is always handled: user-initiated opens become a
// new Visus tab ...; others are blocked with a quiet 'Pop-up blocked' chip", 8.3; 2.3 "Tabs not shown for 10 minutes are
// discarded to a snapshot plus URL and restored on click"; ED-NEW-web-w8a-2). No Swift counterpart. A tab owns at most one web
// view, made the first time it has a page to show, and keeps its own address, title and favicon so a discarded view still
// reads as the page it was.

namespace Marrow.App.Views.Browser;

/// <summary>A history entry of a tab (the long-press menus of Back and Forward).</summary>
public sealed record VisusHistoryItem(int EntryId, string Url, string Title);

/// <summary>An app link a page or the address field wants opened (8.3), waiting for the prompt's answer.</summary>
public sealed class VisusExternalLaunch
{
    private readonly Action<bool> answer;
    private bool answered;

    internal VisusExternalLaunch(VisusTab tab, string uri, string source, string? origin, ExternalLaunchDecision decision, Action<bool> answer)
    {
        Tab = tab;
        Uri = uri;
        Source = source;
        Origin = origin;
        Scheme = WebPolicy.SchemeOf(uri) ?? "";
        CanRemember = decision == ExternalLaunchDecision.AskRememberable;
        this.answer = answer;
    }

    public VisusTab Tab { get; }

    public string Uri { get; }

    /// <summary>Who asks, as the trust strip names it ("example.com"; "Visus" for the address field).</summary>
    public string Source { get; }

    /// <summary>The asking site, for "Remember" (null when the address field asked).</summary>
    public string? Origin { get; }

    public string Scheme { get; }

    /// <summary>The prompt offers "Remember for this site" (mailto: and tel: in a browse or preview tab).</summary>
    public bool CanRemember { get; }

    public bool IsAnswered => answered;

    /// <summary>Open (true) or Cancel (false); <paramref name="remember"/> keeps an Open for this site. Only the first answer counts.</summary>
    public void Answer(bool open, bool remember = false)
    {
        if (answered) return;
        answered = true;
        if (open && remember && CanRemember) VisusHost.SchemeChoices.Remember(Tab.Kind, Origin, Scheme);
        answer(open);
    }
}

/// <summary>DESIGN 5.1: one tab of the Visus pane.</summary>
public sealed class VisusTab
{
    private readonly VisusSession session;
    private WebSurfaceView? view;
    private string url;
    private string title;
    private string? blockedPopup;
    private bool loading;
    private bool shown;
    private int faviconRead;

    internal VisusTab(VisusSession session, VisusTabKind kind, string? id = null, string? url = null, string? title = null, double zoom = 1)
    {
        this.session = session;
        Kind = kind;
        Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N")[..12] : id;
        this.url = url is not null && WebPolicy.IsTopLevelAllowed(url) ? url : WebPolicy.Blank;
        this.title = title ?? "";
        Zoom = zoom;
    }

    public string Id { get; }

    public VisusTabKind Kind { get; }

    /// <summary>The web view, once the tab has had a page to show (a new tab has none).</summary>
    public WebSurfaceView? View => view;

    /// <summary>The page's address (the committed one, or the one being opened).</summary>
    public string Url => view?.Url is { Length: > 0 } live && WebPolicy.IsTopLevelAllowed(live) ? live : url;

    /// <summary>A new tab: no page yet.</summary>
    public bool IsBlank => WebPolicy.IsBlank(Url);

    /// <summary>The page's title, else its address as the field shows it, else "New tab" (or "Private tab").</summary>
    public string Title
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(title)) return title.Trim();
            if (!IsBlank) return VisusAddress.Display(Url);
            return Kind == VisusTabKind.Private ? "Private tab" : "New tab";
        }
    }

    /// <summary>The page's own title only (empty while it has none).</summary>
    public string PageTitle => title;

    /// <summary>The favicon as PNG bytes, read from the tab's profile by the browser; null shows the tab's glyph.</summary>
    public byte[]? Favicon { get; private set; }

    public bool IsLoading => loading;

    public bool CanGoBack => view?.Engine?.CanGoBack ?? false;

    public bool CanGoForward => view?.Engine?.CanGoForward ?? false;

    /// <summary>A pop-up the page tried to open without a click (8.1), shown as the quiet "Pop-up blocked" chip; null when none.</summary>
    public string? BlockedPopup => blockedPopup;

    /// <summary>The page zoom (W8c sets it per site; carried here so a saved tab comes back at its zoom).</summary>
    public double Zoom { get; private set; }

    /// <summary>DESIGN 5.1: the site chip.</summary>
    public SiteChip Chip => WebPolicy.ChipFor(Kind, Url);

    /// <summary>DESIGN 8.2: the source the trust strip names ("example.com", "localhost:5173", "New tab").</summary>
    public string Source => WebPolicy.SourceName(Url);

    /// <summary>The tab is on screen (the pane is open and this is its tab).</summary>
    public bool IsShown => shown;

    /// <summary>Raised after anything the strip, the toolbar or the trust strip shows changed.</summary>
    public event Action? Changed;

    /// <summary>The saveable form (3.11).</summary>
    public VisusTabState State => new(Id, Kind, Url, title, Zoom);

    /// <summary>Opens <paramref name="address"/> (an allowed page address; the address field resolved it) in this tab.</summary>
    public void Open(string address) => Open(address, restoring: false);

    /// <summary>A reopened tab (Ctrl+Shift+T) loads its page again and keeps its title until the page names itself.</summary>
    internal void Reopen()
    {
        if (view is null && !WebPolicy.IsBlank(url)) Open(url, restoring: true);
    }

    private void Open(string address, bool restoring)
    {
        if (!WebPolicy.IsTopLevelAllowed(address)) return;
        url = address;
        if (!restoring)
        {
            title = ""; // a saved tab keeps its title until its page names itself
            Favicon = null;
        }
        blockedPopup = null;
        if (view is null)
        {
            view = VisusHost.Surface.Open(SurfaceKind(Kind), session.WindowId, address, session.ThreadFolder);
            Hook(view);
            if (Zoom != 1 && view.Engine is { } engine) engine.Zoom = Zoom;
            view.SetShown(shown);
        }
        else
        {
            view.Navigate(address);
        }
        loading = !WebPolicy.IsBlank(address);
        Raise();
    }

    public void GoBack()
    {
        if (Engine() is { CanGoBack: true } engine) engine.GoBack();
    }

    public void GoForward()
    {
        if (Engine() is { CanGoForward: true } engine) engine.GoForward();
    }

    /// <summary>Reload; <paramref name="hard"/> bypasses the cache (Shift+Reload, 5.3).</summary>
    public void Reload(bool hard = false)
    {
        if (IsBlank) return;
        if (Engine() is not { } engine)
        {
            Open(url);
            return;
        }
        engine.Reload(hard);
        loading = true;
        Raise();
    }

    public void Stop()
    {
        Engine()?.Stop();
        if (!loading) return;
        loading = false;
        Raise();
    }

    /// <summary>Opens the pop-up the chip holds as a new tab (the user's click is the gesture now) and clears the chip.</summary>
    public void OpenBlockedPopup()
    {
        if (blockedPopup is not { } address) return;
        blockedPopup = null;
        Raise();
        session.OpenFromPage(this, address);
    }

    public void DismissBlockedPopup()
    {
        if (blockedPopup is null) return;
        blockedPopup = null;
        Raise();
    }

    /// <summary>
    /// DESIGN 5.3: the entries behind (<paramref name="back"/>) or ahead of the current page, nearest first (the long-press
    /// menus), read with the DevTools Protocol's <c>Page.getNavigationHistory</c>.
    /// </summary>
    public async Task<IReadOnlyList<VisusHistoryItem>> HistoryAsync(bool back, CancellationToken cancellationToken = default)
    {
        if (Engine() is not { IsAttached: true } engine) return [];
        string json;
        try
        {
            json = await engine.CallCdpAsync("Page.getNavigationHistory", "{}", null, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return [];
        }
        return ParseHistory(json, back);
    }

    /// <summary>Goes to one entry of <see cref="HistoryAsync"/>.</summary>
    public async Task GoToHistoryAsync(VisusHistoryItem item, CancellationToken cancellationToken = default)
    {
        if (Engine() is not { IsAttached: true } engine) return;
        try
        {
            await engine.CallCdpAsync("Page.navigateToHistoryEntry", "{\"entryId\":" + item.EntryId + "}", null, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
        }
    }

    internal static IReadOnlyList<VisusHistoryItem> ParseHistory(string json, bool back)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var current = root.GetProperty("currentIndex").GetInt32();
            var entries = root.GetProperty("entries").EnumerateArray()
                .Select(entry => new VisusHistoryItem(
                    entry.GetProperty("id").GetInt32(),
                    entry.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                    entry.TryGetProperty("title", out var t) ? t.GetString() ?? "" : ""))
                .ToList();
            var picked = back
                ? entries.Take(Math.Clamp(current, 0, entries.Count)).Reverse()
                : entries.Skip(Math.Clamp(current + 1, 0, entries.Count));
            return picked.Where(item => WebPolicy.IsTopLevelAllowed(item.Url)).Take(12).ToList();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }

    internal void SetShown(bool value)
    {
        if (shown == value) return;
        shown = value;
        view?.SetShown(value);
        if (value && view is null && !WebPolicy.IsBlank(url)) Open(url, restoring: true); // a saved tab goes live on first sight (3.11)
        Raise();
    }

    internal void SetZoom(double zoom)
    {
        Zoom = zoom;
        if (view?.Engine is { } engine) engine.Zoom = zoom;
    }

    internal void Release()
    {
        if (view is not { } released) return;
        Unhook(released);
        view = null;
        VisusHost.Surface.Close(released);
    }

    private IWebEngineView? Engine()
    {
        if (view is null) return null;
        if (view.Engine is null || view.Liveness == WebLiveness.Suspended) view.Activate(); // a discarded or frozen tab comes back on use (2.3)
        return view.Engine;
    }

    private static WebSurfaceKind SurfaceKind(VisusTabKind kind) => kind switch
    {
        VisusTabKind.Private => WebSurfaceKind.VisusPrivateTab,
        VisusTabKind.Preview => WebSurfaceKind.PreviewTab,
        VisusTabKind.Agent => WebSurfaceKind.AgentTab,
        _ => WebSurfaceKind.VisusTab,
    };

    private void Hook(WebSurfaceView v)
    {
        v.ClaimsChord = chord => VisusKeys.CommandFor(chord) is not null || session.ClaimsPageKey?.Invoke(chord) == true;
        v.ClaimedChordPressed += OnClaimedChord;
        v.NavigationStarting += OnNavigationStarting;
        v.FrameNavigationStarting += OnFrameNavigationStarting;
        v.NavigationCompleted += OnNavigationCompleted;
        v.TitleChanged += OnTitleChanged;
        v.FaviconChanged += OnFaviconChanged;
        v.HistoryChanged += OnHistoryChanged;
        v.NewWindowRequested += OnNewWindow;
        v.ExternalUriRequested += OnExternalUri;
        v.Changed += Raise;
    }

    private void Unhook(WebSurfaceView v)
    {
        v.ClaimsChord = null;
        v.ClaimedChordPressed -= OnClaimedChord;
        v.NavigationStarting -= OnNavigationStarting;
        v.FrameNavigationStarting -= OnFrameNavigationStarting;
        v.NavigationCompleted -= OnNavigationCompleted;
        v.TitleChanged -= OnTitleChanged;
        v.FaviconChanged -= OnFaviconChanged;
        v.HistoryChanged -= OnHistoryChanged;
        v.NewWindowRequested -= OnNewWindow;
        v.ExternalUriRequested -= OnExternalUri;
        v.Changed -= Raise;
    }

    private void OnClaimedChord(Marrow.Core.Models.KeyChord chord)
    {
        if (VisusKeys.CommandFor(chord) is { } command) session.Perform(command, this);
        else session.RaisePageKey(this, chord);
    }

    // DESIGN 5.2, 8.1: a page may only be an https, http or blank page. An app's scheme is left to the engine's launch request,
    // which the 8.3 rules gate; anything else (file:, data:, javascript:, internal pages) is cancelled where it starts.
    private void OnNavigationStarting(object? sender, WebNavigationStarting e)
    {
        switch (WebPolicy.CheckTopLevel(e.Url))
        {
            case TopLevelVerdict.Cancel:
                e.Cancel = true;
                if (e.IsUserInitiated) session.Notify(this, BlockedNotice(e.Url));
                if (loading)
                {
                    loading = false;
                    Raise();
                }
                return;
            case TopLevelVerdict.External:
                return;
        }
        blockedPopup = null;
        loading = true;
        Raise();
    }

    private void OnFrameNavigationStarting(object? sender, WebNavigationStarting e)
    {
        if (!WebPolicy.IsFrameAllowed(e.Url) && WebPolicy.CheckTopLevel(e.Url) != TopLevelVerdict.External) e.Cancel = true;
    }

    private void OnNavigationCompleted(object? sender, WebNavigationCompleted e)
    {
        loading = false;
        if (view?.Url is { Length: > 0 } now && WebPolicy.IsTopLevelAllowed(now)) url = now;
        if (view?.Engine?.Title is { Length: > 0 } pageTitle) title = pageTitle;
        if (e.IsSuccess) VisusHost.History.Record(Kind, Url, title, VisusHost.Time.GetUtcNow());
        Raise();
        session.TabStateChanged();
    }

    private void OnTitleChanged(object? sender, EventArgs e)
    {
        var next = view?.Engine?.Title ?? "";
        if (next == title) return;
        title = next;
        VisusHost.History.SetTitle(Kind, Url, title);
        Raise();
        session.TabStateChanged();
    }

    private void OnFaviconChanged(object? sender, EventArgs e) => _ = ReadFaviconAsync();

    private async Task ReadFaviconAsync()
    {
        var read = ++faviconRead;
        if (view is not { } v) return;
        byte[]? bytes;
        try
        {
            bytes = await v.GetFaviconAsync();
        }
        catch (Exception)
        {
            bytes = null;
        }
        if (read != faviconRead || ReferenceEquals(Favicon, bytes)) return;
        Favicon = bytes;
        Raise();
    }

    private void OnHistoryChanged(object? sender, EventArgs e) => Raise();

    // DESIGN 8.1: a page's new window is never a real window. A click opens a new Visus tab of the kind the rules give (a private
    // tab's links stay private); anything else is blocked, and the quiet chip offers it.
    private void OnNewWindow(object? sender, WebNewWindowRequest e)
    {
        var target = string.IsNullOrEmpty(e.Url) ? WebPolicy.Blank : e.Url;
        if (!WebPolicy.IsTopLevelAllowed(target))
        {
            e.Respond(WebNewWindowDecision.Block);
            return;
        }
        if (!e.IsUserInitiated)
        {
            e.Respond(WebNewWindowDecision.Block);
            if (WebPolicy.IsWeb(target))
            {
                blockedPopup = target;
                Raise();
            }
            return;
        }
        e.Respond(WebNewWindowDecision.HandledByNth);
        session.OpenFromPage(this, target);
    }

    // DESIGN 8.3: the surface already denied sandboxed, agent and gesture-less launches; a browse, preview or private tab asks.
    private void OnExternalUri(object? sender, WebExternalUriRequest e)
    {
        if (e.Decision.IsCompleted) return;
        var decision = ExternalProtocols.Decide(VisusTabRules.Profile(Kind), e.Uri, e.IsUserInitiated);
        if (decision == ExternalLaunchDecision.Deny)
        {
            e.Respond(false);
            return;
        }
        var origin = WebPolicy.OriginOf(e.InitiatingOrigin) ?? WebPolicy.OriginOf(Url);
        if (VisusHost.SchemeChoices.IsAllowed(Kind, origin, WebPolicy.SchemeOf(e.Uri)))
        {
            e.Respond(true);
            return;
        }
        var source = WebPolicy.SourceName(origin ?? Url);
        var launch = new VisusExternalLaunch(this, e.Uri, source, origin, decision, open => e.Respond(open));
        e.Defer(); // before anything else: the engine gives an undeferred request its default (deny) when the handler returns
        if (!session.RequestLaunch(launch)) launch.Answer(false); // no pane on screen to ask in: the safe answer
    }

    private static string BlockedNotice(string address) => WebPolicy.SchemeOf(address) switch
    {
        "file" => VisusAddress.LocalFileRefusal,
        "javascript" or "vbscript" => VisusAddress.ScriptRefusal,
        "data" => VisusAddress.DataRefusal,
        { } scheme when ExternalProtocols.BlockList.Contains(scheme) => $"Visus does not open {scheme}: links.",
        _ => VisusAddress.InternalRefusal,
    };

    private void Raise() => Changed?.Invoke();
}
