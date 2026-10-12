using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.UI.Chrome;
using Marrow.UI.Panes;
using Marrow.UI.Port;
using Marrow.UI.Web;

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
    private readonly HashSet<VisusTab> watchedTabs = [];
    private readonly VisusNotice notice = new();
    private readonly VisusPopover noticePopover;
    private VisusPopover? cardPopover;
    private IDisposable? cardEntry;
    private VisusTab? fullScreenTab;
    private bool expandedForFullScreen;
    private PaneWorkspace? workspace;
    private Control? blankSurface;
    private VisusTab? blankFor;
    private bool focusAddressPending;
    private bool attached;
    private TopLevel? top;

    public VisusPaneBody(VisusSession session, ThreadRuntime? runtime = null, bool focusAddress = false)
    {
        Session = session;
        this.runtime = runtime;
        focusAddressPending = focusAddress;
        RowDefinitions = new RowDefinitions("Auto,Auto,*");
        Strip = new VisusTabStrip(session);
        stripRow = new Border { Height = StripRowHeight, Padding = new Thickness(6, 3, 6, 3), Child = Strip };
        Toolbar = new VisusToolbar(session);
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
        noticePopover = new VisusPopover(Toolbar.Address, () => notice, lightDismiss: false, takesFocus: false);
        notice.Expired += () => noticePopover.SetOpen(false);
        Children.Add(noticePopover.Popup);
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
        top = TopLevel.GetTopLevel(this);
        Session.WindowId = VisusHost.WindowIdOf(top);
        Session.Changed += Sync;
        Session.FocusAddressRequested += OnFocusAddress;
        Session.Emptied += OnEmptied;
        Session.Notice += OnNotice;
        Session.LaunchRequested += OnLaunch;
        Session.PageKey += OnPageKey;
        // Esc from inside a page is the pane's while a card is open, the page is in fullscreen or holds the pointer (8.4, 10.1).
        Session.ClaimsPageKey = chord => chord == EscapeChord && (Card is not null || Session.Selected is { IsFullScreen: true } or { IsPointerLocked: true });
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
        Session.Changed -= Sync;
        Session.FocusAddressRequested -= OnFocusAddress;
        Session.Emptied -= OnEmptied;
        Session.Notice -= OnNotice;
        Session.LaunchRequested -= OnLaunch;
        Session.PageKey -= OnPageKey;
        Session.ClaimsPageKey = null;
        if (top is WindowBase window)
        {
            window.Activated -= OnActivated;
            window.Deactivated -= OnDeactivated;
        }
        top = null;
        CloseCard(); // the questions wait in their tabs (an app link is cancelled as its tab leaves the screen)
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
        if (Session.IsVisible != visible) Session.IsVisible = visible;
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
            pages.Children.Remove(host);
            hosts.Remove(tab);
        }
        foreach (var tab in Session.Tabs)
        {
            if (tab.View is not { } view || hosts.ContainsKey(tab)) continue;
            var host = VisusHost.CreateHost(view);
            hosts[tab] = host;
            pages.Children.Insert(0, host);
        }
        var selected = Session.Selected;
        foreach (var (tab, host) in hosts) host.IsVisible = ReferenceEquals(tab, selected) && !tab.IsBlank;
        UpdateBlank(selected);
        UpdateCard();
        UpdateFullScreen();
        ApplyLook();
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
        if (Card is null && want is not null) ShowCard(want);
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
        if (top is { } window) cardEntry = VisusPrompts.Add(window, () => card.FocusFirst(window.FocusManager?.GetFocusedElement()));
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
    }

    private void OnFocusReturned(VisusPromptCard card) => FocusPage();

    private Task<string?> PickSavePathAsync(VisusDownloadAsk ask) => VisusHost.PickSavePath(top, ask.FileName, VisusHost.DownloadsFolder());

    private Task<IReadOnlyList<string>?> PickFilesAsync(VisusFileAsk ask) => VisusHost.PickOpenFiles(top, ask.Multiple);

    private void OnPageKey(VisusTab tab, KeyChord chord)
    {
        if (chord != EscapeChord) return;
        tab.View?.ReleasePointerLock(); // Esc always releases the pointer (8.4)
        if (Card is { } card)
        {
            card.Dismiss();
            return;
        }
        tab.ExitFullScreen();
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
    /// <summary>The start surface's hint (the canvas's words).</summary>
    public const string Hint = "Ctrl+T opens a new tab · Ctrl+Shift+N a private one";

    /// <summary>How many recent visits it lists.</summary>
    public const int RecentCount = 6;

    public VisusStartSurface(VisusSession session)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20) };
        var glyph = new PaneGlyph(PaneGlyphKind.Browser, 30) { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(glyph);
        stack.Children.Add(Line("New tab", 16, PaneTokens.Text1, FontWeight.SemiBold, new Thickness(0, 10, 0, 4)));
        stack.Children.Add(Line("Type an address or a search in the field above.", 12.5, PaneTokens.Text2));
        stack.Children.Add(Line(Hint, 11.5, PaneTokens.Text3, margin: new Thickness(0, 4, 0, 0)));
        Recent = VisusHost.History.Entries.Where(entry => entry.Profile == WebProfileKind.Browse || entry.Profile == WebProfileKind.Preview)
            .OrderByDescending(entry => entry.LastVisit).Take(RecentCount).ToList();
        if (Recent.Count > 0)
        {
            stack.Children.Add(Line("Recent", 11.5, PaneTokens.Text3, FontWeight.SemiBold, new Thickness(0, 18, 0, 4), HorizontalAlignment.Left));
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
                AutomationProperties.SetName(button, title.Text + ", " + host.Text);
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
    public const string Heading = "Private tab";

    public const string Kept = "Nothing you do here is kept: no history, cookies or site data.";

    public const string Unseen = "The model cannot see private tabs.";

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
        AutomationProperties.SetName(this, Heading + ". " + Kept + " " + Unseen);
        void Glyph() => glyph.Foreground = PaneTokens.Text2.Brush(this);
        ActualThemeVariantChanged += (_, _) => Glyph();
        Glyph();
    }
}
