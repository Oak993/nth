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
// reads as the page it was. W8b: a tab keeps the questions its page asks (permissions, pointer lock, downloads, sign-ins,
// dialogs) until they are answered in the pane, answers them safely when its document goes or it closes, contains a page's
// fullscreen and maps a browse page's notifications to nth's toast (5.4, 5.5, 8.4; ED-NEW-web-w8b-1, -2, -4).

namespace Marrow.App.Views.Browser;

/// <summary>A history entry of a tab (the long-press menus of Back and Forward).</summary>
public sealed record VisusHistoryItem(int EntryId, string Url, string Title);

/// <summary>An app link a page or the address field wants opened (8.3), waiting for the prompt's answer.</summary>
public sealed class VisusExternalLaunch : VisusAsk
{
    private readonly Action<bool> answer;

    internal VisusExternalLaunch(VisusTab tab, string uri, string source, string? origin, ExternalLaunchDecision decision, Action<bool> answer)
        : base(tab, source)
    {
        Uri = uri;
        Origin = origin;
        Scheme = WebPolicy.SchemeOf(uri) ?? "";
        CanRemember = decision == ExternalLaunchDecision.AskRememberable;
        this.answer = answer;
    }

    public override VisusAskKind Kind => VisusAskKind.App;

    /// <summary>W8a's rule: an app link whose tab is no longer on screen is cancelled (a click opens it again).</summary>
    public override bool CancelsWhenHidden => true;

    /// <summary>The link was the user's; it does not end with the page (the tab switch or the pane rule ends it).</summary>
    public override bool EndsWithDocument => false;

    public string Uri { get; }

    /// <summary>The asking site, for "Remember" (null when the address field asked).</summary>
    public string? Origin { get; }

    public string Scheme { get; }

    /// <summary>The prompt offers "Remember for this site" (mailto: and tel: in a browse or preview tab).</summary>
    public bool CanRemember { get; }

    /// <summary>Open (true) or Cancel (false); <paramref name="remember"/> keeps an Open for this site. Only the first answer counts.</summary>
    public void Answer(bool open, bool remember = false)
    {
        if (!Claim()) return;
        if (open && remember && CanRemember) VisusHost.SchemeChoices.Remember(Tab.Kind, Origin, Scheme);
        answer(open);
        Done();
    }

    protected override void Safe() => Answer(false);
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
    private IWebEngineView? zoomEngine;
    private bool applyingZoom;
    private bool released;
    private readonly List<VisusAsk> asks = [];
    private readonly Dictionary<VisusAsk, IDisposable> holds = [];
    private WebFailureKind? failure;
    private readonly HashSet<(string Origin, SitePermissionKind Kind)> tabBlocks = [];
    private int documentDialogs;
    private bool dialogsStopped;
    private bool fullScreen;

    internal VisusTab(VisusSession session, VisusTabKind kind, string? id = null, string? url = null, string? title = null, double zoom = 1)
    {
        this.session = session;
        Kind = kind;
        Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N")[..12] : id;
        this.url = url is not null && WebPolicy.IsTopLevelAllowed(url) ? url : WebPolicy.Blank;
        this.title = title ?? "";
        Zoom = double.IsFinite(zoom) && zoom >= VisusSiteZoom.Steps[0] && zoom <= VisusSiteZoom.Steps[^1] ? zoom : 1;
        if (kind == VisusTabKind.Private) VisusHost.SiteZoom.RetainPrivate();
        VisusHost.SiteZoom.Changed += OnSiteZoom;
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
    internal long DocumentVersion { get; private set; }

    /// <summary>DESIGN 5.1: the site chip.</summary>
    public SiteChip Chip => WebPolicy.ChipFor(Kind, Url);

    /// <summary>DESIGN 8.2: the source the trust strip names ("example.com", "localhost:5173", "New tab").</summary>
    public string Source => WebPolicy.SourceName(Url);

    /// <summary>The tab is on screen (the pane is open and this is its tab).</summary>
    public bool IsShown => shown;

    /// <summary>
    /// PORT-ONLY (web surfaces W11, ED-NEW-web-w11-1; DESIGN 2.3 "The user can share one tab with the model ('Let the model use
    /// this tab'), and then per-site approvals apply"): the model may use this browse or preview tab. Never a private tab; kept
    /// only while the session lives, so a restart shares nothing.
    /// </summary>
    public bool SharedWithModel { get; private set; }

    internal void SetSharedWithModel(bool value)
    {
        if (SharedWithModel == value) return;
        SharedWithModel = value;
        Raise();
    }

    /// <summary>The questions the tab waits on, oldest first (the pane shows the first while the tab is on screen).</summary>
    public IReadOnlyList<VisusAsk> Asks => asks;

    /// <summary>The page is in fullscreen: the pane is expanded to the workspace for it (8.4).</summary>
    public bool IsFullScreen => fullScreen;

    /// <summary>The page holds the pointer (8.4); Esc always releases it.</summary>
    public bool IsPointerLocked => view?.IsPointerLocked ?? false;

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
        DocumentVersion++;
        url = address;
        if (restoring && Zoom != 1 && !VisusHost.SiteZoom.Contains(ZoomProfile, address)) VisusHost.SiteZoom.Set(ZoomProfile, address, Zoom);
        Zoom = VisusHost.SiteZoom.Get(ZoomProfile, address);
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
            BindZoomEngine();
            view.SetShown(shown);
        }
        else
        {
            ApplyZoom();
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
        DocumentVersion++;
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
        if (!value)
        {
            AnswerAsks(ask => ask.CancelsWhenHidden); // an app link needs its click again (W8a)
            ExitFullScreen();
        }
        view?.SetShown(value);
        if (value && view is null && !WebPolicy.IsBlank(url)) Open(url, restoring: true); // a saved tab goes live on first sight (3.11)
        Raise();
    }

    internal void SetZoom(double zoom)
    {
        VisusSiteZoom.Validate(zoom);
        Zoom = zoom;
        ApplyZoom();
        VisusHost.SiteZoom.Set(ZoomProfile, url, zoom);
        Raise();
        session.TabStateChanged();
    }

    internal void Release()
    {
        if (this.released) return;
        this.released = true;
        VisusHost.SiteZoom.Changed -= OnSiteZoom;
        if (Kind == VisusTabKind.Private) VisusHost.SiteZoom.ReleasePrivate();
        if (zoomEngine is { } old) { old.Attached -= OnZoomAttached; old.ZoomChanged -= OnEngineZoom; }
        zoomEngine = null;
        AnswerAsks(_ => true); // a closed tab answers everything it waited on safely
        tabBlocks.Clear();
        fullScreen = false;
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
    internal IWebEngineView? ReadyEngine() => Engine();

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
        v.PermissionRequested += OnPermission;
        v.DownloadStarting += OnDownload;
        v.AuthenticationRequested += OnAuthentication;
        v.ScriptDialogOpening += OnScriptDialog;
        v.FullScreenChanged += OnFullScreenChanged;
        v.NotificationReceived += OnNotification;
        v.PointerLockRequested += OnPointerLockRequested;
        v.PointerLockChanged += OnPointerLockChanged;
        v.FileChooserIntercepted += OnFileChooser;
        v.EngineReleased += OnEngineReleased;
        v.Changed += OnViewChanged;
        BindZoomEngine();
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
        v.PermissionRequested -= OnPermission;
        v.DownloadStarting -= OnDownload;
        v.AuthenticationRequested -= OnAuthentication;
        v.ScriptDialogOpening -= OnScriptDialog;
        v.FullScreenChanged -= OnFullScreenChanged;
        v.NotificationReceived -= OnNotification;
        v.PointerLockRequested -= OnPointerLockRequested;
        v.PointerLockChanged -= OnPointerLockChanged;
        v.FileChooserIntercepted -= OnFileChooser;
        v.EngineReleased -= OnEngineReleased;
        v.Changed -= OnViewChanged;
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
        if (!e.IsRedirected) NewDocument();
        blockedPopup = null;
        url = e.Url;
        DocumentVersion++;
        Zoom = VisusHost.SiteZoom.Get(ZoomProfile, url);
        ApplyZoom();
        loading = true;
        Raise();
    }

    private void OnFrameNavigationStarting(object? sender, WebNavigationStarting e)
    {
        if (!WebPolicy.IsFrameAllowed(e.Url) && WebPolicy.CheckTopLevel(e.Url) != TopLevelVerdict.External) e.Cancel = true;
    }

    private string ZoomProfile => Kind switch
    {
        VisusTabKind.Private => WebProfiles.PrivateProfile,
        VisusTabKind.Preview => WebProfiles.PreviewProfile,
        VisusTabKind.Agent => WebProfiles.AgentProfileName(session.ThreadFolder ?? throw new InvalidOperationException("An agent tab needs its thread folder.")),
        _ => WebProfiles.BrowseProfile,
    };
    private void OnSiteZoom(string profile, string origin)
    {
        if (released || profile != ZoomProfile || VisusSiteZoom.Origin(url) != origin) return;
        Zoom = VisusHost.SiteZoom.Get(profile, url);
        ApplyZoom(); Raise(); session.TabStateChanged();
    }
    private void BindZoomEngine()
    {
        if (ReferenceEquals(zoomEngine, view?.Engine)) return;
        if (zoomEngine is { } old) { old.Attached -= OnZoomAttached; old.ZoomChanged -= OnEngineZoom; }
        zoomEngine = view?.Engine;
        if (zoomEngine is { } current) { current.Attached += OnZoomAttached; current.ZoomChanged += OnEngineZoom; ApplyZoom(); }
    }
    private void OnZoomAttached(object? sender, EventArgs e) => ApplyZoom();
    private void OnEngineZoom(object? sender, EventArgs e) { if (!applyingZoom && zoomEngine is { } current && double.IsFinite(current.Zoom) && current.Zoom >= 0.25 && current.Zoom <= 5) SetZoom(current.Zoom); }
    private void ApplyZoom()
    {
        if (zoomEngine is not { } engine) return;
        applyingZoom = true;
        try { engine.Zoom = Zoom; }
        finally { applyingZoom = false; }
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

    // ---- W8b: the questions this tab waits on (5.4, 5.5, 8.4; ED-NEW-web-w8b-1, -2, -4) ----

    /// <summary>The notice while the pointer is locked (8.4), word for word.</summary>
    public const string PointerLockNotice = "Press Esc to release your pointer";

    /// <summary>A queued ask joins the tab's list; the pane shows the oldest while the tab is on screen.</summary>
    internal void AddAsk(VisusAsk ask)
    {
        if (ask.IsAnswered || asks.Contains(ask)) return;
        asks.Add(ask);
        ask.Answered += OnAskAnswered;
        // The page's side of the question lives in its engine: the view is neither suspended nor discarded while it waits.
        if (ask.Kind != VisusAskKind.App && view is { } page) holds[ask] = page.HoldForUser(blocksScript: ask.Kind == VisusAskKind.Dialog);
        Raise();
    }

    private void OnAskAnswered(VisusAsk ask)
    {
        ask.Answered -= OnAskAnswered;
        if (holds.Remove(ask, out var hold)) hold.Dispose();
        if (asks.Remove(ask)) Raise();
    }

    // 2.7: a crashed page's questions went with its renderer (a download, held by the browser, waits on); a browser that went
    // away or a missing runtime takes every question of the page with it. A hung page keeps them (it may be waiting on its reader).
    private void OnViewChanged()
    {
        BindZoomEngine();
        var now = view?.Failure?.Kind;
        if (now != failure)
        {
            failure = now;
            if (now == WebFailureKind.RendererCrashed) AnswerAsks(ask => ask.EndsWithDocument);
            else if (now is { } gone && gone != WebFailureKind.RendererHung) AnswerAsks(ask => ask.Kind != VisusAskKind.App);
        }
        Raise();
    }

    // The engine went (a crash, a missing runtime, a closed view): the page's questions died with it, so they are withdrawn
    // (nothing kept, nothing announced). An app link was the user's own and waits on.
    private void OnEngineReleased() => AnswerAsks(ask => ask.Kind != VisusAskKind.App);

    /// <summary>Withdraws every waiting ask that <paramref name="which"/> picks, oldest first (the safe answer, nothing kept).</summary>
    private void AnswerAsks(Func<VisusAsk, bool> which)
    {
        foreach (var ask in asks.Where(which).ToList()) ask.Withdraw();
    }

    // A new document: the old page's questions end (a download outlives it; a leave-page dialog belongs to the navigation that
    // raised it), and its dialog count and "no more dialogs" choice start again.
    private void NewDocument()
    {
        documentDialogs = 0;
        dialogsStopped = false;
        AnswerAsks(ask => ask.EndsWithDocument && ask is not VisusDialogAsk { DialogKind: WebScriptDialogKind.BeforeUnload });
    }

    internal static SitePermissionKind? PermissionKindOf(WebPermissionKind kind) => kind switch
    {
        WebPermissionKind.Camera => SitePermissionKind.Camera,
        WebPermissionKind.Microphone => SitePermissionKind.Microphone,
        WebPermissionKind.Location => SitePermissionKind.Location,
        WebPermissionKind.Notifications => SitePermissionKind.Notifications,
        WebPermissionKind.ClipboardRead => SitePermissionKind.ClipboardRead,
        WebPermissionKind.Midi => SitePermissionKind.Midi,
        WebPermissionKind.Sensors => SitePermissionKind.Sensors,
        WebPermissionKind.LocalFonts => SitePermissionKind.LocalFonts,
        WebPermissionKind.WindowManagement => SitePermissionKind.WindowManagement,
        WebPermissionKind.FileSystemAccess => SitePermissionKind.FileSystemAccess,
        WebPermissionKind.Autoplay => SitePermissionKind.Autoplay,
        WebPermissionKind.MultipleDownloads => SitePermissionKind.MultipleDownloads,
        _ => null,
    };

    // DESIGN 5.5: denied by default; a kept answer (browse and preview) is given at once; else nth's prompt asks. The engine is
    // never told to keep anything (SavesInProfile is off, ED-NEW-web-w8b-1): nth's store is the one memory, so Remove works.
    private void OnPermission(object? sender, WebPermissionRequest e)
    {
        if (e.Decision.IsCompleted) return;
        var profile = VisusTabRules.Profile(Kind);
        if (PermissionKindOf(e.Kind) is not { } kind || !SitePermissions.Asks(profile, kind))
        {
            e.Respond(WebPermissionDecision.Deny);
            return;
        }
        var origin = PermissionStore.Clean(e.Origin) ?? PermissionStore.Clean(WebPolicy.OriginOf(Url));
        if (origin is null)
        {
            e.Respond(WebPermissionDecision.Deny);
            return;
        }
        if (Kept(profile, origin, kind) is { } kept)
        {
            e.Respond(kept ? WebPermissionDecision.AllowOnce : WebPermissionDecision.Deny);
            return;
        }
        e.Defer();
        // One question per site and kind: a second request while the first waits gets the same answer.
        if (asks.OfType<VisusPermissionAsk>().FirstOrDefault(ask => ask.Origin == origin && ask.Permission == kind) is { } waiting)
        {
            waiting.Answered += _ => e.Respond(waiting.Result is SitePermissionAnswer.Block or null ? WebPermissionDecision.Deny : WebPermissionDecision.AllowOnce);
            return;
        }
        var source = WebPolicy.SourceName(origin);
        VisusPermissionAsk? ask = null;
        ask = new VisusPermissionAsk(this, source, origin, kind, SitePermissions.OffersAlways(profile), answer =>
        {
            e.Respond(answer switch
            {
                SitePermissionAnswer.AllowAlways => WebPermissionDecision.AllowAlways,
                SitePermissionAnswer.AllowOnce => WebPermissionDecision.AllowOnce,
                _ => WebPermissionDecision.Deny,
            });
            if (ask!.IsWithdrawn) return;
            Remember(profile, origin, kind, answer);
            session.Notify(this, ConfirmationFor(profile, source, kind, answer));
        });
        AddAsk(ask);
    }

    // 8.4: pointer lock asks like a permission; the page's gate waits for this answer.
    private void OnPointerLockRequested(object? sender, WebPointerLockRequest e)
    {
        if (e.IsAnswered) return;
        var profile = VisusTabRules.Profile(Kind);
        var origin = PermissionStore.Clean(e.Origin) ?? PermissionStore.Clean(WebPolicy.OriginOf(Url));
        if (origin is null || !SitePermissions.Asks(profile, SitePermissionKind.PointerLock))
        {
            e.Answer(false);
            return;
        }
        if (Kept(profile, origin, SitePermissionKind.PointerLock) is { } kept)
        {
            e.Answer(kept);
            return;
        }
        var source = WebPolicy.SourceName(origin);
        VisusPermissionAsk? ask = null;
        ask = new VisusPermissionAsk(this, source, origin, SitePermissionKind.PointerLock, SitePermissions.OffersAlways(profile), answer =>
        {
            e.Answer(answer != SitePermissionAnswer.Block);
            if (ask!.IsWithdrawn) return;
            Remember(profile, origin, SitePermissionKind.PointerLock, answer);
            // An allowed lock shows "Press Esc to release your pointer" as it takes hold; a refusal says so now.
            if (answer == SitePermissionAnswer.Block) session.Notify(this, ConfirmationFor(profile, source, SitePermissionKind.PointerLock, answer));
        });
        AddAsk(ask);
    }

    private void OnPointerLockChanged(bool locked)
    {
        if (locked) session.Notify(this, PointerLockNotice);
        Raise();
    }

    private bool? Kept(WebProfileKind profile, string origin, SitePermissionKind kind)
    {
        if (tabBlocks.Contains((origin, kind))) return false;
        if (!PermissionStore.Keeps(profile)) return null;
        return VisusHost.Permissions.StateOf(profile, origin, kind) switch
        {
            SitePermissionState.Allow => true,
            SitePermissionState.Block => false,
            _ => null,
        };
    }

    private void Remember(WebProfileKind profile, string origin, SitePermissionKind kind, SitePermissionAnswer answer)
    {
        if (PermissionStore.Keeps(profile)) VisusHost.Permissions.Record(profile, origin, kind, answer);
        else if (answer == SitePermissionAnswer.Block) tabBlocks.Add((origin, kind)); // a private tab's Block holds for the tab
    }

    private static string ConfirmationFor(WebProfileKind profile, string source, SitePermissionKind kind, SitePermissionAnswer answer) =>
        answer == SitePermissionAnswer.Block && !PermissionStore.Keeps(profile)
            ? SitePermissions.PrivateBlockConfirmation(kind)
            : SitePermissions.Confirmation(source, kind, answer);

    // DESIGN 5.4: every download asks, in the tab that started it (a model-started one too: only the prompt's buttons answer).
    private void OnDownload(object? sender, WebDownloadRequest e)
    {
        if (e.Decision.IsCompleted) return;
        e.Defer();
        var from = WebPolicy.OriginOf(e.SourceOrigin) ?? WebPolicy.OriginOf(e.Url) ?? WebPolicy.OriginOf(Url);
        var source = from is null ? Source : WebPolicy.SourceName(from);
        VisusDownloadAsk? ask = null;
        ask = new VisusDownloadAsk(this, source, e, (choice, path) => SaveDownload(e, ask!, choice, path));
        AddAsk(ask);
    }

    private void SaveDownload(WebDownloadRequest e, VisusDownloadAsk ask, DownloadChoice choice, string? chosen)
    {
        if (choice == DownloadChoice.Cancel)
        {
            e.Respond(WebDownloadDecision.Cancel);
            if (!ask.IsWithdrawn) session.Notify(this, DownloadGate.Confirmation(DownloadChoice.Cancel, ask.FileName));
            return;
        }
        var path = chosen;
        if (choice == DownloadChoice.Save)
        {
            var folder = VisusHost.DownloadsFolder();
            if (string.IsNullOrEmpty(folder))
            {
                e.Respond(WebDownloadDecision.Cancel);
                session.Notify(this, "Visus could not find your Downloads folder. Use Save as… to pick a place.");
                return;
            }
            path = DownloadGate.UniquePath(folder, ask.FileName, VisusHost.FileExists);
        }
        e.Respond(new WebDownloadDecision(true, path));
        _ = TrackDownloadAsync(e, ask, choice);
    }

    private async Task TrackDownloadAsync(WebDownloadRequest e, VisusDownloadAsk ask, DownloadChoice choice)
    {
        IWebDownloadOperation? operation;
        try
        {
            operation = await e.Started;
        }
        catch (Exception)
        {
            operation = null;
        }
        if (operation is null) return;
        var name = Path.GetFileName(operation.ResultFilePath);
        if (string.IsNullOrEmpty(name)) name = ask.FileName;
        var download = VisusHost.Downloads.Add(name, ask.Source, Kind, operation, VisusHost.Time);
        void Saved(VisusDownload item)
        {
            item.Finished -= Saved;
            session.Notify(this, DownloadGate.Confirmation(choice, item.FileName));
        }
        if (download.State == VisusDownloadState.Done) Saved(download);
        else download.Finished += Saved;
    }

    // 8.4: HTTP auth is nth's card (the surface already refused agent tabs and sandboxed views).
    private void OnAuthentication(object? sender, WebAuthRequest e)
    {
        if (e.Decision.IsCompleted) return;
        e.Defer();
        AddAsk(new VisusSignInAsk(this, WebPolicy.SourceName(WebPolicy.OriginOf(e.Uri) ?? Url), e));
    }

    // 8.4: a page's dialog is nth's card labelled with the site; from the page's second dialog the card offers to stop them.
    private void OnScriptDialog(object? sender, WebScriptDialogRequest e)
    {
        if (e.Decision.IsCompleted) return;
        if (dialogsStopped && e.Kind != WebScriptDialogKind.BeforeUnload)
        {
            e.Respond(WebScriptDialogDecision.Dismiss);
            return;
        }
        e.Defer();
        documentDialogs++;
        var ask = new VisusDialogAsk(this, WebPolicy.SourceName(WebPolicy.OriginOf(e.Uri) ?? Url), e,
            offersStop: documentDialogs >= 2 && e.Kind != WebScriptDialogKind.BeforeUnload);
        ask.Answered += answered =>
        {
            if (answered is VisusDialogAsk { StopRequested: true }) dialogsStopped = true;
        };
        AddAsk(ask);
    }

    // 8.4: fullscreen is contained: the pane expands to the workspace and the page fills the web rectangle (the pane does it).
    private void OnFullScreenChanged(object? sender, bool full)
    {
        if (fullScreen == full) return;
        fullScreen = full;
        Raise();
    }

    /// <summary>Ends the page's fullscreen (Esc, Ctrl+Shift+\, another tab chosen, the pane closed).</summary>
    public void ExitFullScreen()
    {
        if (!fullScreen) return;
        view?.ExitFullScreen();
    }

    // DESIGN 5.5: a browse page's notification is nth's own toast, naming the site (other profiles never raise it).
    private void OnNotification(object? sender, WebNotification e)
    {
        if (Kind != VisusTabKind.Browse) return;
        VisusHost.ShowNotification(WebPolicy.SourceName(WebPolicy.OriginOf(e.SenderOrigin) ?? Url), e.Title, e.Body, !e.IsSilent);
        e.Shown = true;
    }

    // 8.4, 5.10: an agent tab's page asked for a file. Only the user can give one, in the guarded file upload card.
    private void OnFileChooser(object? sender, WebFileChooser e)
    {
        if (Kind != VisusTabKind.Agent || view is not { } v) return;
        AddAsk(new VisusFileAsk(this, Source, e, v));
    }
}
