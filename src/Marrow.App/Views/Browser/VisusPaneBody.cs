using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Views.Panes;
using Marrow.Core.Localisation;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.UI.Chrome;
using Marrow.UI.Panes;
using Marrow.UI.Port;
using Marrow.UI.Web;
using Marrow.App.Views.Artifacts;

// PORT-ONLY: web surfaces W8a, the Visus pane (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.1 pane anatomy: "Header row: tab
// strip ... Toolbar row ... Content: the page, inside a 6 DIP bezel", 3.7 the keyboard scope rule (the browser keys are
// Visus's while the keyboard is in the pane), 3.12 "in group mode the tab strip moves into the body", 2.4 airspace (anything
// over the page is a top-level popup), 8.3, 10.1 F6 and prompts; the canvas's .privv; ED-NEW-web-w8a-2). No Swift
// counterpart. Every tab that has a page keeps its WebViewHost in the tree and only the chosen one shows; a tab with no page
// shows the start surface (or the private one). Closing the last tab closes the pane. W8b: the pane shows the chosen tab's
// oldest question as its card (an app link, a permission, a download, a sign-in, a page dialog, an agent tab's file input), one
// at a time, hanging from the trust strip; F6 reaches it and gives the keyboard back (10.1); a page's fullscreen expands the
// pane to the workspace and Esc or Ctrl+Shift+\ ends it (8.4); ED-NEW-web-w8b-4.

namespace Marrow.App.Views.Browser;

/// <summary>DESIGN 5.1: the Visus pane (registered for <see cref="PaneKind.Browser"/>).</summary>
public static class VisusPane
{
    /// <summary>Makes Visus drawable in the workspace's grid (the app's start-up calls it; tests call it or replace the factory).</summary>
    public static void Register() => PaneContents.Register(PaneKind.Browser, (_, runtime) => Create(runtime));

    /// <summary>The content of a thread's Visus pane: the tab strip in the header, the toolbar and the pages in the body.</summary>
    public static PaneContent Create(ThreadRuntime runtime)
    {
        return Create(VisusHost.SessionFor(runtime), runtime);
    }

    /// <summary>The pane over <paramref name="session"/> (tests pass a session of their own and no runtime).</summary>
    internal static PaneContent Create(VisusSession session, ThreadRuntime? runtime)
    {
        var fresh = session.Tabs.Count == 0;
        if (fresh) session.NewTab();
        var body = new VisusPaneBody(session, runtime, focusAddress: fresh);
        var content = new PaneContent(body, PaneKinds.Title(PaneKind.Browser)) { HeaderContent = body.Strip };
        content.HeaderContentPlacementChanged += body.SetStripInHeader;
        return content;
    }
}

/// <summary>The Visus pane's body: the strip row (group mode), the toolbar, and the pages.</summary>
public sealed class VisusPaneBody : Grid
{
    /// <summary>The strip's own row in the body when the header has no room for it (group mode).</summary>
    public const double StripRowHeight = 34;

    private static readonly KeyChord EscapeChord = new(0x01, ChordModifiers.None);

    private readonly ThreadRuntime? runtime;
    private readonly Border stripRow;
    private readonly Border web;
    private readonly Panel pages = new() { ClipToBounds = true };
    private readonly Dictionary<VisusTab, WebViewHost> hosts = [];
    private readonly Dictionary<VisusTab, ProjectPreviewBody> previews = [];
    private readonly HashSet<VisusTab> watchedTabs = [];
    private readonly VisusNotice notice = new();
    private readonly VisusPopover noticePopover;
    private readonly TextBlock announcer;
    private VisusPopover? cardPopover;
    private IDisposable? cardEntry;
    private VisusTab? fullScreenTab;
    private bool expandedForFullScreen;
    private PaneWorkspace? workspace;
    // W11 (ED-NEW-web-w11-1): the web tools' card of this chat (one card at a time: a tab's own question or this one), and
    // the asks whose tab was brought forward once
    private VisusAgentCard? agentCard;
    private VisusPopover? agentPopover;
    private IDisposable? agentEntry;
    private readonly HashSet<VisusAgentAsk> broughtForward = [];
    private bool agentSyncing;
    private Control? blankSurface;
    private VisusTab? blankFor;
    private bool focusAddressPending;
    private bool attached;
    private TopLevel? top;
    private readonly VisusFindController find;
    private readonly VisusPopover findPopover;
    private readonly VisusPopover zoomPopover;
    private TextBlock? zoomLabel;
    private bool pdfBusy;
    private long exportEpoch;
    public VisusFindBar FindBar { get; }
    public ITabulaFiles Files { get; set; } = TabulaSystem.Instance;

    public VisusPaneBody(VisusSession session, ThreadRuntime? runtime = null, bool focusAddress = false)
    {
        Session = session;
        this.runtime = runtime;
        focusAddressPending = focusAddress;
        RowDefinitions = new RowDefinitions("Auto,Auto,*");
        Strip = new VisusTabStrip(session);
        stripRow = new Border { Height = StripRowHeight, Padding = new Thickness(6, 3, 6, 3), Child = Strip };
        Toolbar = new VisusToolbar(session);
        find = new VisusFindController(session);
        find.Suspend();
        FindBar = new VisusFindBar(find, CloseFind);
        findPopover = new VisusPopover(Toolbar, () => FindBar, lightDismiss: false, dismissed: CloseFind, placement: PlacementMode.BottomEdgeAlignedRight);
        var findCard = PopoverHost.RectangularCard(FindBar, Toolbar);
        findPopover.Popup.Child = findCard;
        findPopover.Popup.VerticalOffset = 10 - findCard.Margin.Top;
        findPopover.Popup.HorizontalOffset = findCard.Margin.Right - 24;
        PopoverHost.SetSiteCard(findPopover.Popup, findCard);
        find.Changed += () => { if (!find.IsOpen) findPopover.SetOpen(false); };
        // The chip is hidden at 100 %, so the stepper hangs from the address field (always shown), at its right edge where the chip sits.
        zoomPopover = new VisusPopover(Toolbar.Address, BuildZoom, placement: PlacementMode.BottomEdgeAlignedRight);
        web = new Border
        {
            Margin = new Thickness(VisusTokens.Bezel, 0, VisusTokens.Bezel, VisusTokens.Bezel),
            CornerRadius = new CornerRadius(0, 0, 6, 6),
            ClipToBounds = true,
            Child = pages,
        };
        SetRow(stripRow, 0);
        SetRow(Toolbar, 1);
        SetRow(web, 2);
        Children.Add(stripRow);
        Children.Add(Toolbar);
        Children.Add(web);
        Children.Add(findPopover.Popup);
        Children.Add(zoomPopover.Popup);
        noticePopover = new VisusPopover(Toolbar.Address, () => notice, lightDismiss: false, takesFocus: false);
        notice.Expired += () => noticePopover.SetOpen(false);
        Children.Add(noticePopover.Popup);
        // 10.1: a card is read in full as it appears, with no focus taken: an assertive live region in the main window carries
        // the card's announcement, which ends with "Press F6 to answer." (the card's own window never activates by itself).
        announcer = new TextBlock
        {
            Width = 1,
            Height = 1,
            FontSize = 1,
            MaxLines = 1,
            ClipToBounds = true,
            IsHitTestVisible = false,
            Focusable = false,
            Foreground = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Text = "",
        };
        AutomationProperties.SetLiveSetting(announcer, AutomationLiveSetting.Assertive);
        SetRow(announcer, 1);
        Children.Add(announcer);
        Toolbar.Address.Went += FocusPage;
        // The browser keys are Visus's wherever the keyboard is in the pane: the body, and the strip, which sits in the header.
        Func<KeyEventArgs, bool> claims = e => VisusKeys.CommandFor(e) is not null;
        ShortcutScopeHost.SetClaimsKey(this, claims);
        ShortcutScopeHost.SetClaimsKey(Strip, claims);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Strip.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AutomationProperties.SetName(this, "Visus");
        ActualThemeVariantChanged += (_, _) => ApplyLook();
        LayoutUpdated += (_, _) => UpdateVisible();
        Sync();
    }

    public VisusSession Session { get; }

    public VisusTabStrip Strip { get; }

    public VisusToolbar Toolbar { get; }

    /// <summary>Whether the strip is in the pane's header (false: its own row in the body, group mode).</summary>
    public bool IsStripInHeader => stripRow.Child is null;

    /// <summary>The page hosts, by tab.</summary>
    public IReadOnlyDictionary<VisusTab, WebViewHost> Hosts => hosts;
    public ProjectPreviewBody? Preview => Session.Selected is { } tab && previews.TryGetValue(tab, out var preview) ? preview : null;
    public void RefreshPreview() => Sync();

    /// <summary>The start or private surface a tab with no page shows (null while a page shows).</summary>
    public Control? BlankSurface => blankSurface is { IsVisible: true } ? blankSurface : null;

    /// <summary>The card showing now (any kind of question), or null.</summary>
    public VisusPromptCard? Card { get; private set; }

    /// <summary>The app-link prompt showing now (W8a's name for <see cref="Card"/> when it is one).</summary>
    public VisusPrompt? Prompt => Card as VisusPrompt;

    /// <summary>The questions of the chosen tab waiting behind the card.</summary>
    public int WaitingPrompts => Math.Max(0, (Session.Selected?.Asks.Count ?? 0) - (Card is null ? 0 : 1));

    /// <summary>The tab whose page fullscreen expanded the pane (null when none).</summary>
    public VisusTab? FullScreenTab => fullScreenTab;

    /// <summary>What the pane's live region said last (a card's announcement, 10.1), or "".</summary>
    public string Announcement => announcer.Text ?? "";

    /// <summary>The web tools' card showing now (W11): the model waits for the user's answer.</summary>
    public VisusAgentCard? AgentCard => agentCard;

    /// <summary>The popup the agent card is in (tests read its placement).</summary>
    internal Popup? AgentCardPopup => agentPopover?.Popup;

    /// <summary>Where the pointer is, for a card that must not open under it (a test scripts it, as DragHandleView's own seam).</summary>
    internal static Ambient<ICursorPlatform> Pointer { get; } = new(() => new WindowsCursorPlatform());

    /// <summary>The notice showing now (null when none).</summary>
    public string? NoticeText => noticePopover.IsOpen ? notice.Text : null;

    /// <summary>Copies text to the clipboard of <paramref name="from"/>'s window.</summary>
    public static void CopyText(Control from, string text)
    {
        if (TopLevel.GetTopLevel(from)?.Clipboard is { } clipboard) _ = clipboard.SetTextAsync(text);
    }

    /// <summary>The header took the strip (true) or gave it back to the body's own row (false).</summary>
    internal void SetStripInHeader(bool inHeader)
    {
        if (inHeader)
        {
            stripRow.Child = null;
            stripRow.IsVisible = false;
        }
        else
        {
            if (Strip.GetVisualParent() is Panel panel) panel.Children.Remove(Strip);
            stripRow.Child = Strip;
            stripRow.IsVisible = true;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        find.Resume();
        top = TopLevel.GetTopLevel(this);
        Session.WindowId = VisusHost.WindowIdOf(top);
        Session.Changed += Sync;
        Session.FocusAddressRequested += OnFocusAddress;
        Session.FindRequested += OpenFind;
        Session.ZoomRequested += OpenZoom;
        Session.SavePdfRequested += OnSavePdf;
        Session.Emptied += OnEmptied;
        Session.Notice += OnNotice;
        Session.LaunchRequested += OnLaunch;
        Session.PageKey += OnPageKey;
        // Esc from inside a page is the pane's while a card is open, the page is in fullscreen or holds the pointer (8.4, 10.1).
        Session.ClaimsPageKey = chord => chord == EscapeChord && (Card is not null || agentCard is not null || find.IsOpen || Session.Selected is { IsFullScreen: true } or { IsPointerLocked: true });
        VisusAgentAsks.Shared.Changed += OnAsksChanged;
        if (top is WindowBase window)
        {
            window.Activated += OnActivated;
            window.Deactivated += OnDeactivated;
        }
        workspace = this.FindAncestorOfType<PaneWorkspace>();
        if (workspace is { PromptFirst: null } panes)
        {
            var window2 = top;
            panes.PromptFirst = () => VisusPrompts.FocusNewest(window2);
        }
        if (workspace is not null) workspace.StateChanged += OnWorkspaceState;
        Sync();
        UpdateVisible();
        if (focusAddressPending) OnFocusAddress();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false;
        exportEpoch++;
        Session.Changed -= Sync;
        Session.FocusAddressRequested -= OnFocusAddress;
        Session.FindRequested -= OpenFind;
        Session.ZoomRequested -= OpenZoom;
        Session.SavePdfRequested -= OnSavePdf;
        find.Suspend(); findPopover.SetOpen(false); zoomPopover.SetOpen(false);
        foreach (var tab in watchedTabs) tab.Changed -= Sync;
        watchedTabs.Clear();
        Session.Emptied -= OnEmptied;
        Session.Notice -= OnNotice;
        Session.LaunchRequested -= OnLaunch;
        Session.PageKey -= OnPageKey;
        Session.ClaimsPageKey = null;
        VisusAgentAsks.Shared.Changed -= OnAsksChanged;
        if (top is WindowBase window)
        {
            window.Activated -= OnActivated;
            window.Deactivated -= OnDeactivated;
        }
        top = null;
        CloseCard(); // the questions wait in their tabs (an app link is cancelled as its tab leaves the screen)
        CloseAgentCard(); // its ask waits for the pane to come back
        EndFullScreen();
        if (workspace is not null) workspace.StateChanged -= OnWorkspaceState;
        workspace = null;
        noticePopover.SetOpen(false);
        notice.Stop();
        Session.IsVisible = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateVisible()
    {
        var visible = attached && IsEffectivelyVisible;
        if (!visible) { find.Close(); findPopover.SetOpen(false); zoomPopover.SetOpen(false); }
        if (Session.IsVisible == visible) return;
        Session.IsVisible = visible;
        SyncAgentCard();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (VisusHost.IsStarted) VisusHost.Surface.SetWindowFocused(Session.WindowId, true);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (VisusHost.IsStarted) VisusHost.Surface.SetWindowFocused(Session.WindowId, false);
    }

    private void Sync()
    {
        foreach (var tab in watchedTabs.Where(tab => !Session.Tabs.Contains(tab)).ToList())
        {
            tab.Changed -= Sync;
            watchedTabs.Remove(tab);
        }
        foreach (var tab in Session.Tabs)
            if (watchedTabs.Add(tab)) tab.Changed += Sync;
        foreach (var (tab, host) in hosts.Where(pair => !Session.Tabs.Contains(pair.Key) || !ReferenceEquals(pair.Key.View, pair.Value.Surface)).ToList())
        {
            if (previews.Remove(tab, out var preview)) { preview.Dispose(); pages.Children.Remove(preview); }
            pages.Children.Remove(host);
            hosts.Remove(tab);
        }
        foreach (var tab in Session.Tabs)
        {
            if (tab.View is not { } view || hosts.ContainsKey(tab)) continue;
            var host = VisusHost.CreateHost(view);
            hosts[tab] = host;
            if (tab.Kind == VisusTabKind.Preview && tab.Preview is not null)
            {
                var preview = new ProjectPreviewBody(tab, host); previews[tab] = preview; pages.Children.Insert(0, preview);
            }
            else pages.Children.Insert(0, host);
        }
        var selected = Session.Selected;
        foreach (var (tab, host) in hosts) host.IsVisible = ReferenceEquals(tab, selected) && !tab.IsBlank;
        foreach (var (tab, preview) in previews) preview.IsVisible = ReferenceEquals(tab, selected) && !tab.IsBlank;
        web.Margin = selected?.Kind == VisusTabKind.Preview ? new Thickness(0) : new Thickness(VisusTokens.Bezel, 0, VisusTokens.Bezel, VisusTokens.Bezel);
        UpdateBlank(selected);
        UpdateCard();
        UpdateFullScreen();
        UpdateZoomLabel();
        ApplyLook();
        SyncAgentCard();
    }

    private void UpdateBlank(VisusTab? selected)
    {
        var blank = selected is null || selected.IsBlank;
        if (!blank)
        {
            if (blankSurface is not null) blankSurface.IsVisible = false;
            return;
        }
        if (blankSurface is null || !ReferenceEquals(blankFor, selected))
        {
            if (blankSurface is not null) pages.Children.Remove(blankSurface);
            blankFor = selected;
            blankSurface = selected?.Kind == VisusTabKind.Private ? new VisusPrivateSurface() : new VisusStartSurface(Session);
            pages.Children.Add(blankSurface);
        }
        blankSurface.IsVisible = true;
    }

    private void ApplyLook()
    {
        var page = Session.Selected is { IsBlank: false };
        web.Background = page
            ? VisusTokens.Page.Brush(this)
            : (Session.Selected?.Kind == VisusTabKind.Private ? VisusTokens.PrivateSurface : VisusTokens.StartSurface).Brush(this);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || VisusKeys.CommandFor(e) is not { } command) return;
        if (Session.Perform(command)) e.Handled = true;
    }

    private void OnFocusAddress()
    {
        if (!attached)
        {
            focusAddressPending = true;
            return;
        }
        focusAddressPending = false;
        Dispatcher.UIThread.Post(() =>
        {
            if (attached) Toolbar.Address.FocusAndSelectAll();
        }, DispatcherPriority.Input);
    }

    /// <summary>After Enter in the address field the keyboard goes to the page.</summary>
    private void FocusPage()
    {
        if (Session.Selected is not { } tab || !hosts.TryGetValue(tab, out var host)) return;
        if (host.LiveContent is InputElement live) live.Focus(NavigationMethod.Tab);
        else host.Focus(NavigationMethod.Tab);
    }

    private void OnEmptied()
    {
        if (runtime is not null) runtime.Workspace = runtime.Workspace.Close(PaneKind.Browser);
    }

    private void OnNotice(VisusTab? tab, string text)
    {
        if (tab is not null && !ReferenceEquals(tab, Session.Selected)) return;
        notice.Show(text);
        noticePopover.SetOpen(true);
    }

    // An app link is asked only while its tab is on screen (W8a); it then waits in its tab with the other questions.
    private bool OnLaunch(VisusExternalLaunch launch)
    {
        if (!attached || top is null || !Session.IsVisible || !ReferenceEquals(launch.Tab, Session.Selected)) return false;
        launch.Tab.AddAsk(launch);
        return true;
    }

    // The card on screen is the chosen tab's oldest question while the pane is on screen; any other card goes (its question,
    // unanswered, waits in its tab and comes back when the tab does).
    private void UpdateCard()
    {
        var selected = Session.Selected;
        var want = attached && top is not null && Session.IsVisible && selected is not null && Session.Tabs.Contains(selected)
            ? selected.Asks.FirstOrDefault(ask => !ask.IsAnswered)
            : null;
        if (Card is { } card && (card.Ask.IsAnswered || !ReferenceEquals(card.Ask, want))) CloseCard();
        if (Card is null && agentCard is null && want is not null) ShowCard(want); // one card at a time (W11)
    }

    private void ShowCard(VisusAsk ask)
    {
        VisusPromptCard card = ask switch
        {
            VisusExternalLaunch launch => new VisusPrompt(launch, ExternalProtocols.AppName(launch.Scheme, VisusHost.SystemAppName)),
            VisusPermissionAsk permission => new VisusPermissionPrompt(permission, ask.Tab.PageTitle),
            VisusDownloadAsk download => new VisusDownloadPrompt(download, PickSavePathAsync),
            VisusSignInAsk signIn => new VisusSignInCard(signIn),
            VisusDialogAsk dialog => new VisusDialogCard(dialog),
            VisusFileAsk file => new VisusFileCard(file, PickFilesAsync),
            _ => throw new InvalidOperationException("A Visus question with no card: " + ask.GetType().Name),
        };
        Card = card;
        card.Done += OnCardDone;
        card.FocusReturned += OnFocusReturned;
        cardPopover = new VisusPopover(Toolbar.Trust, () => card, lightDismiss: false, takesFocus: false, dismissed: card.Dismiss);
        Children.Add(cardPopover.Popup);
        cardPopover.SetOpen(true);
        Announce(card.Announcement);
        if (top is { } window) cardEntry = VisusPrompts.Add(window, () => card.FocusFirst(window.FocusManager?.GetFocusedElement()));
    }

    private void Announce(string words)
    {
        var old = announcer.Text ?? "";
        // The same words twice (a second card of one kind) still have to be heard: the region is cleared first.
        if (old == words) announcer.Text = "";
        announcer.Text = words;
        ControlAutomationPeer.CreatePeerForElement(announcer)?.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, old, words);
    }

    private void CloseCard()
    {
        if (Card is not { } card) return;
        // The keyboard was in the card (F6): it goes back where it came from before the card goes.
        if (card.IsKeyboardFocusWithin) card.ReturnFocus();
        card.Done -= OnCardDone;
        card.FocusReturned -= OnFocusReturned;
        cardEntry?.Dispose();
        cardEntry = null;
        if (cardPopover is { } popover)
        {
            popover.SetOpen(false);
            Children.Remove(popover.Popup);
        }
        cardPopover = null;
        Card = null;
    }

    private void OnCardDone(VisusPromptCard card)
    {
        if (!ReferenceEquals(card, Card)) return;
        CloseCard();
        UpdateCard();
        SyncAgentCard(); // a model's card waiting behind this one shows now (W11)
    }

    private void OnFocusReturned(VisusPromptCard card) => FocusPage();

    private Task<string?> PickSavePathAsync(VisusDownloadAsk ask) => VisusHost.PickSavePath(top, ask.FileName, VisusHost.DownloadsFolder());

    private Task<IReadOnlyList<string>?> PickFilesAsync(VisusFileAsk ask) => VisusHost.PickOpenFiles(top, ask.Multiple);

    private void OnPageKey(VisusTab tab, KeyChord chord)
    {
        if (chord != EscapeChord) return;
        tab.View?.ReleasePointerLock(); // Esc always releases the pointer (8.4)
        if (find.IsOpen) { CloseFind(); return; }
        if (Card is { } card)
        {
            card.Dismiss();
            return;
        }
        if (agentCard is { } asking)
        {
            asking.Refuse();
            return;
        }
        tab.ExitFullScreen();
    }

    // ---- W11: the web tools' cards (DESIGN 5.10; ED-NEW-web-w11-1) ----

    private void OnAsksChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) SyncAgentCard();
        else Dispatcher.UIThread.Post(SyncAgentCard);
    }

    // The oldest ask of this chat shows when its tab is the chosen one (an ask for a new tab hangs from whichever tab is); a
    // new ask brings its tab forward once, so the user sees the page it is about. An ask goes when it is answered or withdrawn.
    private void SyncAgentCard()
    {
        if (agentSyncing) return;
        agentSyncing = true;
        try
        {
            var asks = VisusAgentAsks.Shared.Pending.Where(Mine).ToList();
            broughtForward.RemoveWhere(ask => ask.IsDone || !asks.Contains(ask));
            if (agentCard is { } open && (open.Ask.IsDone || !asks.Contains(open.Ask) || !Shows(open.Ask))) open.Close();
            // one card at a time: the tab's own question (a permission, a download, a dialog) first
            if (agentCard is not null || Card is not null || !attached || top is null || !Session.IsVisible) return;
            foreach (var ask in asks)
            {
                if (broughtForward.Add(ask) && TabOf(ask) is { } tab && !ReferenceEquals(tab, Session.Selected)) Session.Select(tab);
                if (!Shows(ask)) continue;
                ShowAgentCard(ask);
                return;
            }
        }
        finally
        {
            agentSyncing = false;
        }
    }

    private bool Mine(VisusAgentAsk ask) =>
        Session.AgentThread is { } thread ? string.Equals(ask.Thread, thread, StringComparison.OrdinalIgnoreCase) : TabOf(ask) is not null;

    private VisusTab? TabOf(VisusAgentAsk ask) => ask.TabId is { } id ? Session.Tabs.FirstOrDefault(t => t.Id == id) : null;

    private bool Shows(VisusAgentAsk ask) => ask.TabId is null || (Session.Selected is { } selected && selected.Id == ask.TabId);

    private void ShowAgentCard(VisusAgentAsk ask)
    {
        var made = new VisusAgentCard(ask);
        agentCard = made;
        made.Done += OnAgentCardDone;
        var near = (ask.Request as Marrow.Services.Web.WebGuardAsk)?.Near;
        var popover = new VisusPopover(near is null ? Toolbar.Trust : pages, () => made, lightDismiss: false, takesFocus: false, dismissed: made.Refuse);
        if (near is { } point) PlaceNear(popover.Popup, point, Session.Selected?.Zoom ?? 1);
        agentPopover = popover;
        Children.Add(popover.Popup);
        popover.SetOpen(true);
        made.LayoutUpdated += AvoidPointer;
        made.FocusReturned += OnAgentFocusReturned;
        Announce(made.Announcement);
        if (top is { } window) agentEntry = VisusPrompts.Add(window, () => made.FocusFirst(window.FocusManager?.GetFocusedElement()));
    }

    // The canvas's guard card: above the element the step acts on (10 DIPs off its edge), below it when there is no room.
    internal static void PlaceNear(Popup popup, Marrow.Services.Web.WebPoint point, double zoom)
    {
        const double reach = 24; // half a typical control's height plus the gap
        popup.Placement = PlacementMode.AnchorAndGravity;
        popup.PlacementRect = new Rect(point.X * zoom - 1, point.Y * zoom - reach, 2, 2 * reach);
        popup.PlacementAnchor = PopupAnchor.Top;
        popup.PlacementGravity = PopupGravity.Top;
        popup.PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipY | PopupPositionerConstraintAdjustment.SlideX | PopupPositionerConstraintAdjustment.SlideY;
    }

    // "Never under the pointer": once the card has its place, it moves aside when the pointer is over it.
    private void AvoidPointer(object? sender, EventArgs e)
    {
        if (agentCard is not { } shown || agentPopover is not { } popover || shown.Bounds.Width <= 0 || TopLevel.GetTopLevel(shown) is not { } root) return;
        shown.LayoutUpdated -= AvoidPointer;
        var scale = root.RenderScaling;
        var from = shown.PointToScreen(default);
        var to = shown.PointToScreen(new Point(shown.Bounds.Width, shown.Bounds.Height));
        var shift = VisusAgentCard.PointerShift(new PixelRect(from, to), Pointer.Current.ScreenPosition, (int)Math.Ceiling(VisusAgentCard.PointerMargin * scale));
        if (shift != 0) popover.Popup.HorizontalOffset += shift / scale;
    }

    // The pane leaves the screen: the card goes, its ask stays (it shows again when the pane comes back).
    private void CloseAgentCard()
    {
        if (agentCard is not { } open) return;
        if (open.IsKeyboardFocusWithin) open.ReturnFocus(); // the keyboard goes back where it came from first (10.1)
        open.Close();
    }

    private void OnAgentFocusReturned(VisusAgentCard returned) => FocusPage();

    private void OnAgentCardDone(VisusAgentCard done)
    {
        if (!ReferenceEquals(done, agentCard)) return;
        done.Done -= OnAgentCardDone;
        done.FocusReturned -= OnAgentFocusReturned;
        done.LayoutUpdated -= AvoidPointer;
        agentEntry?.Dispose();
        agentEntry = null;
        if (agentPopover is { } popover)
        {
            popover.SetOpen(false);
            Children.Remove(popover.Popup);
        }
        agentPopover = null;
        agentCard = null;
        if (attached) Dispatcher.UIThread.Post(() =>
        {
            UpdateCard();
            SyncAgentCard();
        }, DispatcherPriority.Background);
    }

    private void OpenFind()
    {
        zoomPopover.SetOpen(false);
        if (!find.Open()) return;
        FindBar.Search();
        findPopover.SetOpen(true);
        Dispatcher.UIThread.Post(() => { if (find.IsOpen) FindBar.FocusQuery(); }, DispatcherPriority.Input);
    }
    private void CloseFind() { find.Close(); findPopover.SetOpen(false); if (attached) FocusPage(); }
    private void OpenZoom()
    {
        find.Close();
        // The stepper's tip points at the field's right end, where the chip sits when it is shown.
        var field = Toolbar.Address.Bounds.Size;
        var end = Math.Min(field.Width, ZoomTipReach);
        zoomPopover.Popup.PlacementRect = new Rect(field.Width - end, 0, end, field.Height);
        zoomPopover.SetOpen(true);
    }
    private const double ZoomTipReach = 56;
    private Control BuildZoom()
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(4) };
        zoomLabel = new TextBlock { FontSize = 13, FontWeight = FontWeight.Medium, TextAlignment = TextAlignment.Center, MinWidth = 48 };
        UpdateZoomLabel();
        stack.Children.Add(ZoomStep(new TextBlock { Text = "−", FontSize = 17 }, UiText.Get("visus.zoom.out"), VisusCommand.ZoomOut, 30));
        stack.Children.Add(ZoomStep(zoomLabel, UiText.Get("visus.zoom.reset"), VisusCommand.ResetZoom, 62));
        stack.Children.Add(ZoomStep(new TextBlock { Text = "+", FontSize = 17 }, UiText.Get("visus.zoom.in"), VisusCommand.ZoomIn, 30));
        return stack;
    }
    // A stepper button: a 28 DIP face with the toolbar's hover fill and the theme's ink, named for assistive tools.
    private Button ZoomStep(TextBlock text, string name, VisusCommand command, double width)
    {
        text.HorizontalAlignment = HorizontalAlignment.Center; text.VerticalAlignment = VerticalAlignment.Center;
        var face = new Border { Width = width, Height = 28, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Child = text };
        var button = PlainControls.Button(face, () => Session.Perform(command)).KeyboardFocus(8);
        button.Padding = default;
        button.OnHover(on => face.Background = on ? PaneTokens.HoverFill.Brush(button) : Brushes.Transparent);
        void Look() => text.Foreground = PaneTokens.Text1.Brush(button);
        button.ActualThemeVariantChanged += (_, _) => Look();
        Look();
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        return button;
    }
    // The stepper shows the zoom as it is, also after Ctrl+Plus or a same-site tab changed it while the stepper is open.
    private void UpdateZoomLabel()
    {
        if (zoomLabel is { } label) label.Text = UiText.Format("visus.zoom.percent", Math.Round((Session.Selected?.Zoom ?? 1) * 100).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    private void OnSavePdf() => _ = SavePdfAsync();
    public async Task SavePdfAsync()
    {
        if (pdfBusy || Session.Selected is not { IsBlank: false } tab || tab.ReadyEngine() is not { IsAttached: true } engine) return;
        var url = tab.Url;
        var document = tab.DocumentVersion;
        var epoch = exportEpoch;
        var selectionChanged = false;
        void OnSelection() { if (!ReferenceEquals(Session.Selected, tab)) selectionChanged = true; }
        pdfBusy = true;
        Session.Changed += OnSelection;
        try
        {
            var name = string.Concat(tab.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
            if (string.IsNullOrEmpty(name)) name = "page";
            var path = await Files.SaveAsync(this, new TabulaSaveRequest(UiText.Get("visus.print.save_pdf"), null, name + ".pdf", "PDF", "pdf"));
            if (path is null || selectionChanged || epoch != exportEpoch || document != tab.DocumentVersion || !ReferenceEquals(Session.Selected, tab) || tab.Url != url || !ReferenceEquals(tab.View?.Engine, engine) || !engine.IsAttached) return;
            if (!await engine.PrintToPdfAsync(path, CancellationToken.None)) Session.Notify(tab, UiText.Get("visus.print.save_pdf_failed"));
        }
        catch (Exception) { Session.Notify(tab, UiText.Get("visus.print.save_pdf_failed")); }
        finally { Session.Changed -= OnSelection; pdfBusy = false; }
    }

    // 8.4: a page's fullscreen is contained: the pane expands to the workspace (3.4) with the trust strip in view, and the
    // page fills the web rectangle, never the screen. Another tab chosen, the pane closed or restored ends it.
    private void UpdateFullScreen()
    {
        var selected = Session.Selected;
        var want = attached && Session.IsVisible && selected is { IsFullScreen: true } ? selected : null;
        if (ReferenceEquals(want, fullScreenTab)) return;
        if (fullScreenTab is { } old) old.ExitFullScreen();
        fullScreenTab = want;
        if (want is null)
        {
            RestoreExpanded();
            return;
        }
        if (workspace is null || workspace.State.Expanded == PaneKind.Browser) return;
        expandedForFullScreen = true;
        workspace.SetState(workspace.State.Expand(PaneKind.Browser));
    }

    private void RestoreExpanded()
    {
        if (!expandedForFullScreen) return;
        expandedForFullScreen = false;
        if (workspace is { } panes && panes.State.Expanded == PaneKind.Browser) panes.SetState(panes.State.RestoreExpanded());
    }

    private void EndFullScreen()
    {
        fullScreenTab?.ExitFullScreen();
        fullScreenTab = null;
        RestoreExpanded();
    }

    // Ctrl+Shift+\ (or anything else that restores the pane) while the page is in fullscreen ends the page's fullscreen.
    private void OnWorkspaceState(PaneWorkspaceState state)
    {
        if (fullScreenTab is not { } tab || state.Expanded == PaneKind.Browser) return;
        expandedForFullScreen = false;
        fullScreenTab = null;
        tab.ExitFullScreen();
    }
}


/// <summary>A popover the pane opens itself (glass card, the window's Esc closes it), built each time it opens.</summary>
public sealed class VisusPopover
{
    private readonly Control anchor;
    private readonly Func<Control> build;
    private readonly Action? dismissed;

    public VisusPopover(Control anchor, Func<Control> build, bool lightDismiss = true, bool takesFocus = true, Action? dismissed = null, PlacementMode placement = PlacementMode.Bottom)
    {
        this.anchor = anchor;
        this.build = build;
        this.dismissed = dismissed;
        Popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = placement,
            IsLightDismissEnabled = lightDismiss,
            TakesFocusFromNativeControl = takesFocus,
        };
        Popup.Closed += (_, _) =>
        {
            if (!IsOpen) return;
            IsOpen = false; // closed from outside (a click elsewhere, the window's Esc)
            Presentation.SetPresented(Popup, false, anchor, () => { });
            dismissed?.Invoke();
        };
    }

    public Popup Popup { get; }

    public bool IsOpen { get; private set; }

    public void SetOpen(bool open)
    {
        if (IsOpen == open) return;
        IsOpen = open;
        if (open && build() is var content)
        {
            // Once presented, the popup's child is the glass card (PopoverHost wraps the content in it): new content goes
            // into the card.
            if (Popup.Child is PopoverCard card)
            {
                if (!ReferenceEquals(card.Child, content)) card.Child = content;
            }
            else if (!ReferenceEquals(Popup.Child, content))
            {
                Popup.Child = content;
            }
        }
        Presentation.SetPresented(Popup, open, anchor, () =>
        {
            if (!IsOpen) return;
            SetOpen(false);
            dismissed?.Invoke();
        });
    }
}

/// <summary>A new tab's page: "New tab", how to search, and the latest visits.</summary>
public sealed class VisusStartSurface : Border
{
    /// <summary>The start surface's hint (the canvas's words; resource key visus.start.hint).</summary>
    public static string Hint => UiText.Get("visus.start.hint");

    /// <summary>How many recent visits it lists.</summary>
    public const int RecentCount = 6;

    public VisusStartSurface(VisusSession session)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20) };
        var glyph = new PaneGlyph(PaneGlyphKind.Browser, 30) { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(glyph);
        stack.Children.Add(Line(UiText.Get("visus.tabs.title_new"), 16, PaneTokens.Text1, FontWeight.SemiBold, new Thickness(0, 10, 0, 4)));
        stack.Children.Add(Line(UiText.Get("visus.start.type_address"), 12.5, PaneTokens.Text2));
        stack.Children.Add(Line(Hint, 11.5, PaneTokens.Text3, margin: new Thickness(0, 4, 0, 0)));
        Recent = VisusHost.History.Entries.Where(entry => entry.Profile == WebProfileKind.Browse || entry.Profile == WebProfileKind.Preview)
            .OrderByDescending(entry => entry.LastVisit).Take(RecentCount).ToList();
        if (Recent.Count > 0)
        {
            stack.Children.Add(Line(UiText.Get("visus.start.recent"), 11.5, PaneTokens.Text3, FontWeight.SemiBold, new Thickness(0, 18, 0, 4), HorizontalAlignment.Left));
            foreach (var entry in Recent)
            {
                var url = entry.Url;
                var row = new DockPanel { LastChildFill = true };
                var title = new TextBlock { Text = string.IsNullOrWhiteSpace(entry.Title) ? VisusAddress.Display(url) : entry.Title, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
                var host = new TextBlock { Text = WebPolicy.SourceName(url), FontSize = 11.5, Margin = new Thickness(10, 0, 0, 0) };
                DockPanel.SetDock(host, Dock.Right);
                row.Children.Add(host);
                row.Children.Add(title);
                var face = new Border { Padding = new Thickness(8, 5), CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Child = row };
                var button = PlainControls.Button(face, () => session.Go(url)).KeyboardFocus(7);
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                AutomationProperties.SetName(button, UiText.Format("visus.start.recent_name", title.Text, host.Text));
                void Look()
                {
                    title.Foreground = PaneTokens.Text1.Brush(button);
                    host.Foreground = PaneTokens.Text3.Brush(button);
                }
                button.OnHover(on => face.Background = on ? PaneTokens.HoverFill.Brush(button) : Brushes.Transparent);
                button.ActualThemeVariantChanged += (_, _) => Look();
                Look();
                stack.Children.Add(button);
            }
        }
        Child = stack;
        void Glyph() => glyph.Foreground = PaneTokens.Text3.Brush(this);
        ActualThemeVariantChanged += (_, _) => Glyph();
        Glyph();
    }

    /// <summary>The visits it lists, newest first.</summary>
    public IReadOnlyList<VisusHistoryEntry> Recent { get; }

    internal static TextBlock Line(string text, double size, SwiftColor color, FontWeight? weight = null, Thickness? margin = null, HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = align == HorizontalAlignment.Center ? TextAlignment.Center : TextAlignment.Left,
            HorizontalAlignment = align,
            Margin = margin ?? default,
        };
        block.Foreground = color.Brush(block);
        block.ActualThemeVariantChanged += (_, _) => block.Foreground = color.Brush(block);
        return block;
    }
}

/// <summary>A private tab with no page (the canvas's .privv): the mask, "Private tab" and what it keeps.</summary>
public sealed class VisusPrivateSurface : Border
{
    /// <summary>Resource key visus.private.heading.</summary>
    public static string Heading => UiText.Get("visus.private.heading");

    /// <summary>Resource key visus.private.kept.</summary>
    public static string Kept => UiText.Get("visus.private.kept");

    /// <summary>Resource key visus.private.unseen.</summary>
    public static string Unseen => UiText.Get("visus.private.unseen");

    public VisusPrivateSurface()
    {
        var glyph = new PaneGlyph(PaneGlyphKind.Mask, 34) { HorizontalAlignment = HorizontalAlignment.Center };
        Child = new StackPanel
        {
            Orientation = Orientation.Vertical,
            MaxWidth = 380,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20),
            Children =
            {
                glyph,
                VisusStartSurface.Line(Heading, 16, PaneTokens.Text1, FontWeight.SemiBold, new Thickness(0, 10, 0, 4)),
                VisusStartSurface.Line(Kept, 12.5, PaneTokens.Text2),
                VisusStartSurface.Line(Unseen, 12.5, PaneTokens.Text2, margin: new Thickness(0, 2, 0, 0)),
                VisusStartSurface.Line(VisusStartSurface.Hint, 11.5, PaneTokens.Text3, margin: new Thickness(0, 10, 0, 0)),
            },
        };
        AutomationProperties.SetName(this, UiText.Format("visus.private.surface_name", Heading, Kept, Unseen));
        void Glyph() => glyph.Foreground = PaneTokens.Text2.Brush(this);
        ActualThemeVariantChanged += (_, _) => Glyph();
        Glyph();
    }
}
