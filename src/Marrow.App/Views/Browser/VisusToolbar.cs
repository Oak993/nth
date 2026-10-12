using Avalonia;
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Web;
using Marrow.UI.Chrome;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W8a, Visus's toolbar (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.1 "Toolbar row: Back, Forward
// (long-press or right-click for history), Reload/Stop, the address field (with a site chip at its start: lock, Not secure,
// Preview, Agent), the zoom chip (shown only when zoom is not 100%), and the menu", 5.2 address and search, 8.1 "Visus shows
// the source as host and port in ASCII", 10 "the trust strip is announced"; the canvas's .btb .hb .url .schip .zchip;
// ED-NEW-web-w8a-2). No Swift counterpart. The address field shows the page's address without its scheme until it is focused,
// then the whole address, selected. Typing opens local suggestions under it (a popup that never takes the keyboard): Up and
// Down move through them, Enter does what the first row says, Alt+Enter opens it in a new tab, Esc closes them, then puts
// the address back, then (nothing edited) is the pane's again.

namespace Marrow.App.Views.Browser;

/// <summary>An item a later unit adds to the ⋮ menu (W8b, W8c, W9).</summary>
public sealed record VisusMenuEntry(string Title, Action Run, string? Shortcut = null, bool IsEnabled = true);

/// <summary>DESIGN 5.1: Back, Forward, Reload, the address field and the ⋮ menu.</summary>
public sealed class VisusToolbar : Border
{
    private readonly VisusSession session;
    private readonly VisusPopover moreMenu;
    private readonly VisusPopover serversMenu;
    private readonly Button picker;
    private readonly Button servers;
    private readonly VisusPopover downloadsMenu;
    private readonly Avalonia.Controls.Shapes.Arc downloadsRing;
    private VisusTab? watched;

    public VisusToolbar(VisusSession session)
    {
        this.session = session;
        Height = VisusTokens.ToolbarHeight;
        Padding = new Thickness(6, 0);
        BorderThickness = new Thickness(0, 0, 0, 0.5);
        // + ED-NEW-web-w11-1: each press is the user's own action on the tab (a tab the model drives pauses for it, DESIGN 5.10).
        Back = new VisusToolbarButton(PaneGlyphKind.Back, "Back", "Alt+Left", _ => session.Perform(VisusCommand.Back));
        Forward = new VisusToolbarButton(PaneGlyphKind.Forward, "Forward", "Alt+Right", _ => session.Perform(VisusCommand.Forward));
        Reload = new VisusToolbarButton(PaneGlyphKind.Reload, "Reload", "F5", modifiers =>
        {
            if (session.Selected is not { } tab) return;
            session.Acted(tab);
            if (tab.IsLoading) tab.Stop();
            else tab.Reload(hard: modifiers.HasFlag(KeyModifiers.Shift));
        });
        Back.HistoryRequested += at => ShowHistory(Back, back: true, at);
        Forward.HistoryRequested += at => ShowHistory(Forward, back: false, at);
        More = new VisusToolbarButton(PaneGlyphKind.More, "Visus menu", null, _ => SetMoreOpen(!moreMenu!.IsOpen));
        moreMenu = new VisusPopover(More, () => new PopoverMenu(BuildMenu));
        // W8b (5.4; the canvas's data-act="dls", an .opt2 button): the downloads of this run, always in the toolbar unless it is
        // narrower than 420 DIP; a ring shows the progress of the transfers still running.
        Downloads = new VisusToolbarButton(PaneGlyphKind.Download, "Downloads", null, _ => SetDownloadsOpen(!downloadsMenu!.IsOpen));
        downloadsMenu = new VisusPopover(Downloads, () => new PopoverMenu(BuildDownloads));
        downloadsRing = new Avalonia.Controls.Shapes.Arc
        {
            Width = VisusTokens.ToolbarButton - 4,
            Height = VisusTokens.ToolbarButton - 4,
            StartAngle = -90,
            SweepAngle = 0,
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAccessibilityView(downloadsRing, AccessibilityView.Raw);
        var downloadsSlot = new Panel { Children = { Downloads, downloadsRing }, VerticalAlignment = VerticalAlignment.Center };
        Address = new VisusAddressField(session) { Margin = new Thickness(4, 0) };
        if (!MenuProviders.Contains(ProjectPreviewHost.Menu)) MenuProviders.Add(ProjectPreviewHost.Menu);
        picker = DevicePresetBar.Action("Pick element", () => session.Perform(VisusCommand.PickElement), new PreviewGlyph("Picker"));
        servers = DevicePresetBar.Action("Dev servers", () => serversMenu!.SetOpen(!serversMenu.IsOpen));
        serversMenu = new VisusPopover(servers, () => new PopoverMenu(BuildServers));
        var row = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
        foreach (var button in new[] { Back, Forward, Reload })
        {
            DockPanel.SetDock(button, Dock.Left);
            row.Children.Add(button);
        }
        DockPanel.SetDock(More, Dock.Right);
        row.Children.Add(More);
        DockPanel.SetDock(servers, Dock.Right); row.Children.Add(servers);
        DockPanel.SetDock(picker, Dock.Right); row.Children.Add(picker);
        DockPanel.SetDock(downloadsSlot, Dock.Right);
        row.Children.Add(downloadsSlot);
        row.Children.Add(Address);
        Child = new Panel { Children = { row, moreMenu.Popup, serversMenu.Popup, downloadsMenu.Popup } };
        AutomationProperties.SetName(this, "Visus toolbar");
        SetValue(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.ToolBar);
        ActualThemeVariantChanged += (_, _) => ApplyLook();
        SizeChanged += (_, _) => ApplyDownloads();
        Sync();
    }

    public VisusToolbarButton Back { get; }

    public VisusToolbarButton Forward { get; }

    public VisusToolbarButton Reload { get; }

    public VisusToolbarButton More { get; }

    /// <summary>The canvas's container rule: at this width or less the optional Downloads button gives its room to the address.</summary>
    public const double NarrowWidth = 420;

    /// <summary>The downloads popover's line while nothing was saved in this run.</summary>
    public const string NoDownloads = "No downloads yet. Files you save from Visus show here.";

    /// <summary>The downloads of this run (5.4), in the toolbar unless it is narrow.</summary>
    public VisusToolbarButton Downloads { get; }

    /// <summary>The ring's share of the live transfers' bytes (null when none runs with a known size).</summary>
    public double? DownloadsProgress => downloadsRing.IsVisible ? downloadsRing.SweepAngle / 360 : null;

    public bool IsDownloadsOpen => downloadsMenu.IsOpen;

    public VisusAddressField Address { get; }

    /// <summary>The site chip at the address field's start (prompts hang from it).</summary>
    public VisusTrustStrip Trust => Address.Trust;

    public bool IsMenuOpen => moreMenu.IsOpen;

    /// <summary>The ⋮ menu's items from later units, asked each time the menu opens.</summary>
    public static List<Func<VisusSession, VisusTab?, IEnumerable<VisusMenuEntry>>> MenuProviders { get; } = [];

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        session.Changed += Sync;
        VisusHost.Downloads.Changed += ApplyDownloads;
        Sync();
        ApplyDownloads();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        session.Changed -= Sync;
        if (watched is not null) watched.Changed -= Apply;
        watched = null;
        moreMenu.SetOpen(false);
        serversMenu.SetOpen(false);
        VisusHost.Downloads.Changed -= ApplyDownloads;
        downloadsMenu.SetOpen(false);
        base.OnDetachedFromVisualTree(e);
    }

    private void Sync()
    {
        if (!ReferenceEquals(watched, session.Selected))
        {
            if (watched is not null) watched.Changed -= Apply;
            watched = session.Selected;
            if (watched is not null) watched.Changed += Apply;
        }
        Apply();
    }

    private void Apply()
    {
        var tab = session.Selected;
        picker.IsVisible = tab?.Kind == VisusTabKind.Preview;
        servers.IsVisible = session.Runtime?.App is not null;
        Back.IsEnabled = tab?.CanGoBack == true;
        Forward.IsEnabled = tab?.CanGoForward == true;
        Reload.IsEnabled = tab is { IsBlank: false } || tab?.IsLoading == true;
        var loading = tab?.IsLoading == true;
        Reload.SetGlyph(loading ? PaneGlyphKind.Close : PaneGlyphKind.Reload, loading ? "Stop" : "Reload", loading ? "Esc" : "F5");
        ApplyLook();
    }

    private void ApplyLook()
    {
        BorderBrush = PaneTokens.Line.Brush(this);
        Background = session.Selected?.Kind == VisusTabKind.Private ? VisusTokens.PrivateToolbar.Brush(this) : Brushes.Transparent;
    }

    private async void ShowHistory(Control button, bool back, Point at)
    {
        if (session.Selected is not { View: not null } tab) return;
        IReadOnlyList<VisusHistoryItem> items;
        try
        {
            items = await tab.HistoryAsync(back);
        }
        catch (Exception)
        {
            return; // the page went away; there is no list to show
        }
        if (items.Count == 0 || !ReferenceEquals(tab, session.Selected)) return;
        var entries = items.Select(item => new ContextMenuEntry(
            string.IsNullOrWhiteSpace(item.Title) ? VisusAddress.Display(item.Url) : item.Title,
            true, false, false, [], () => { session.Acted(tab); _ = tab.GoToHistoryAsync(item); })).ToList();
        NativeContextMenus.Presenter.Current.Present(button, at, entries);
    }

    private void SetMoreOpen(bool open) => moreMenu.SetOpen(open);
    private Control BuildServers()
    {
        var stack = new StackPanel(); stack.Children.Add(new PopoverSectionHeader("Dev servers"));
        var all = session.Runtime?.App?.Terminals.AllSessions.Where(s => s.IsRunning).ToArray() ?? [];
        foreach (var terminal in all)
            foreach (var address in terminal.PreviewServers)
            {
                var chosen = address;
                stack.Children.Add(new PopoverItem(WebPolicy.SourceName(chosen), () => { serversMenu.SetOpen(false); if (session.Runtime is { } runtime) ProjectPreviewHost.Open(runtime, chosen); }, detail: terminal.Title));
            }
        if (stack.Children.Count == 1) stack.Children.Add(new PopoverItem("No dev servers", () => { }, isEnabled: false));
        return stack;
    }

    private void SetDownloadsOpen(bool open) => downloadsMenu.SetOpen(open);

    private void ApplyDownloads()
    {
        var downloads = VisusHost.Downloads;
        Downloads.IsVisible = !(Bounds.Width is > 0 and <= NarrowWidth);
        var live = downloads.LiveCount;
        var progress = downloads.LiveProgress;
        downloadsRing.IsVisible = Downloads.IsVisible && live > 0 && progress is not null;
        downloadsRing.SweepAngle = (progress ?? 0) * 360;
        downloadsRing.Stroke = Marrow.UI.Chrome.Chrome.Accent.Brush(this);
        var name = live == 0 ? "Downloads" : progress is { } share
            ? $"Downloads, {live} in progress, {Math.Round(share * 100)} %"
            : $"Downloads, {live} in progress";
        AutomationProperties.SetName(Downloads, name);
        ToolTip.SetTip(Downloads, name);
        if (!Downloads.IsVisible && downloadsMenu.IsOpen) SetDownloadsOpen(false);
    }

    /// <summary>The downloads popover's rows: each file with where it is, its live controls, then the folder and Clear.</summary>
    internal IReadOnlyList<VisusMenuEntry?> DownloadEntries()
    {
        var downloads = VisusHost.Downloads;
        var rows = new List<VisusMenuEntry?>();
        foreach (var item in downloads.Items.Take(8))
        {
            var download = item;
            // The row shows the file in its folder; nothing here ever opens it (5.4 "Files are never opened automatically").
            rows.Add(new(download.FileName, () => VisusHost.ShowInFolder(download.Path), download.Status, download.State == VisusDownloadState.Done));
            if (download.State == VisusDownloadState.Running) rows.Add(new("Pause " + download.FileName, download.Pause));
            if (download.State == VisusDownloadState.Paused || download.State == VisusDownloadState.Failed && download.CanResume)
                rows.Add(new("Resume " + download.FileName, download.Resume));
            if (download.IsLive) rows.Add(new("Cancel " + download.FileName, download.Cancel));
        }
        if (rows.Count > 0) rows.Add(null);
        var folder = VisusHost.DownloadsFolder();
        rows.Add(new("Open the Downloads folder", () =>
        {
            if (folder is not null) Marrow.UI.Port.ShellWorkspace.OpenWithDefaultApp(folder);
        }, IsEnabled: folder is not null));
        rows.Add(new("Clear finished", downloads.ClearFinished, IsEnabled: downloads.Items.Any(item => !item.IsLive)));
        return rows;
    }

    private Control BuildDownloads()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(new PopoverSectionHeader("Downloads"));
        if (VisusHost.Downloads.Items.Count == 0) stack.Children.Add(new PopoverNote(NoDownloads));
        foreach (var row in DownloadEntries())
        {
            if (row is null)
            {
                stack.Children.Add(new PopoverDivider());
                continue;
            }
            var run = row.Run;
            stack.Children.Add(new PopoverItem(row.Title, () => { SetDownloadsOpen(false); run(); }, detail: row.Shortcut, isEnabled: row.IsEnabled));
        }
        return stack;
    }

    /// <summary>The ⋮ menu's rows (null is a divider): the tab commands, what later units add, the page's link, and the update restart.</summary>
    internal IReadOnlyList<VisusMenuEntry?> MenuEntries()
    {
        var tab = session.Selected;
        var rows = new List<VisusMenuEntry?>
        {
            new("New tab", () => session.NewTab(), "Ctrl+T"),
            new("New private tab", () => session.NewTab(VisusTabKind.Private), "Ctrl+Shift+N"),
            new("Reopen closed tab", () => session.ReopenClosed(), "Ctrl+Shift+T", session.CanReopenClosed),
        };
        var added = MenuProviders.SelectMany(provider => provider(session, tab)).ToList();
        if (added.Count > 0)
        {
            rows.Add(null);
            rows.AddRange(added);
        }
        rows.Add(null);
        var web = tab is not null && WebPolicy.IsWeb(tab.Url);
        rows.Add(new("Find in page", () => session.Perform(VisusCommand.Find), "Ctrl+F", web));
        rows.Add(new("Zoom " + Math.Round((tab?.Zoom ?? 1) * 100).ToString(CultureInfo.InvariantCulture) + " %", () => session.Perform(VisusCommand.ShowZoom), IsEnabled: web));
        rows.Add(new("Print", () => session.Perform(VisusCommand.Print), "Ctrl+P", web));
        rows.Add(new("Save as PDF", () => session.Perform(VisusCommand.SavePdf), IsEnabled: web));
        rows.Add(null);
        rows.Add(new("Copy link", () => VisusPaneBody.CopyText(this, tab!.Url), IsEnabled: web));
        // A private tab's page is not handed to the default browser, which would keep it in its history (5.1 "Nothing you do
        // here is kept").
        rows.Add(new("Open in default browser", () => VisusHost.OpenInDefaultBrowser(tab!.Url), IsEnabled: web && tab!.Kind != VisusTabKind.Private));
        if (VisusHost.IsUpdateReady)
        {
            rows.Add(null);
            rows.Add(new("Restart Visus to update", VisusHost.Restart));
        }
        return rows;
    }

    private Control BuildMenu()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var row in MenuEntries())
        {
            if (row is null)
            {
                stack.Children.Add(new PopoverDivider());
                continue;
            }
            var run = row.Run;
            stack.Children.Add(new PopoverItem(row.Title, () => { SetMoreOpen(false); run(); }, detail: row.Shortcut, isEnabled: row.IsEnabled));
        }
        return stack;
    }
}

/// <summary>A toolbar button (canvas .btb .hb: 28 square, a 15 glyph); Back and Forward also open their history.</summary>
public sealed class VisusToolbarButton : Button
{
    /// <summary>How long a press is held before Back or Forward shows its history instead.</summary>
    public static readonly TimeSpan LongPress = TimeSpan.FromMilliseconds(500);

    private readonly Action<KeyModifiers> invoke;
    private readonly Border face;
    private PaneGlyph glyph;
    private KeyModifiers pressModifiers;
    private DispatcherTimer? hold;
    private bool suppressClick;
    private bool hovered;

    public VisusToolbarButton(PaneGlyphKind kind, string name, string? shortcut, Action<KeyModifiers> invoke)
    {
        this.invoke = invoke;
        glyph = new PaneGlyph(kind, VisusTokens.ToolbarGlyph);
        face = new Border
        {
            Width = VisusTokens.ToolbarButton,
            Height = VisusTokens.ToolbarButton,
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Child = glyph,
        };
        Content = face;
        Padding = default;
        Background = Brushes.Transparent;
        PlainControls.KeyboardFocus(this, 8);
        VerticalAlignment = VerticalAlignment.Center;
        SetGlyph(kind, name, shortcut);
        PointerEntered += (_, _) => { hovered = true; ApplyLook(); };
        PointerExited += (_, _) => { hovered = false; ApplyLook(); };
        AddHandler(PointerPressedEvent, OnPress, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, _) => StopHold(), RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerCaptureLost += (_, _) => StopHold();
        ActualThemeVariantChanged += (_, _) => ApplyLook();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsEffectivelyEnabledProperty) ApplyLook();
        };
    }

    /// <summary>Right-click or a long press asked for the history menu, at this point in the button.</summary>
    public event Action<Point>? HistoryRequested;

    public PaneGlyphKind Kind => glyph.Kind;

    internal void SetGlyph(PaneGlyphKind kind, string name, string? shortcut)
    {
        if (glyph.Kind != kind)
        {
            glyph = new PaneGlyph(kind, VisusTokens.ToolbarGlyph);
            face.Child = glyph;
        }
        ToolTip.SetTip(this, shortcut is null ? name : name + "  " + shortcut);
        AutomationProperties.SetName(this, name);
        if (shortcut is null) ClearValue(AutomationProperties.AcceleratorKeyProperty);
        else AutomationProperties.SetAcceleratorKey(this, shortcut);
        ApplyLook();
    }

    protected override void OnClick()
    {
        if (suppressClick)
        {
            suppressClick = false;
            return;
        }
        invoke(pressModifiers);
        pressModifiers = KeyModifiers.None;
        base.OnClick();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        pressModifiers = e.KeyModifiers;
        base.OnKeyDown(e);
    }

    private void OnPress(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        pressModifiers = e.KeyModifiers;
        suppressClick = false;
        if (point.Properties.IsRightButtonPressed && HistoryRequested is not null)
        {
            HistoryRequested(e.GetPosition(this));
            e.Handled = true;
            return;
        }
        if (!point.Properties.IsLeftButtonPressed || HistoryRequested is null || !IsEffectivelyEnabled) return;
        var at = e.GetPosition(this);
        StopHold();
        hold = new DispatcherTimer(LongPress, DispatcherPriority.Input, (_, _) =>
        {
            StopHold();
            suppressClick = true;
            HistoryRequested?.Invoke(at);
        });
        hold.Start();
    }

    private void StopHold()
    {
        hold?.Stop();
        hold = null;
    }

    private void ApplyLook()
    {
        var on = hovered && IsEffectivelyEnabled;
        face.Background = on ? PaneTokens.HoverFill.Brush(this) : Brushes.Transparent;
        glyph.Foreground = (on ? PaneTokens.Text1 : PaneTokens.Text2).Brush(this);
        glyph.Opacity = IsEffectivelyEnabled ? 1 : 0.35;
    }
}

/// <summary>
/// DESIGN 8.1, 10: the site chip at the address field's start. It says whose page this is and what kind of tab it is (the
/// lock, Not secure, Preview, Private, Agent), shows the host while the address is edited, and is what prompts point at.
/// </summary>
public sealed class VisusTrustStrip : Button
{
    private readonly Border face;
    private readonly PaneGlyph glyph = new(PaneGlyphKind.Search, 13);
    private readonly TextBlock label = new() { FontSize = VisusTokens.ChipFontSize, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
    private readonly VisusPopover info;
    private SiteChip chip;
    private VisusTab? tab;
    private bool editing;

    public VisusTrustStrip()
    {
        face = new Border
        {
            Height = VisusTokens.ChipHeight,
            CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { glyph, label } },
        };
        glyph.VerticalAlignment = VerticalAlignment.Center;
        Content = face;
        Padding = default;
        Background = Brushes.Transparent;
        PlainControls.KeyboardFocus(this, 10);
        VerticalAlignment = VerticalAlignment.Center;
        info = new VisusPopover(this, () => new PopoverMenu(BuildInfo));
        Click += (_, _) => SetInfoOpen(!info.IsOpen && tab is { IsBlank: false });
        ActualThemeVariantChanged += (_, _) => ApplyLook();
        Update(null, false);
    }

    /// <summary>The chip shown now.</summary>
    public SiteChip Chip => chip;

    /// <summary>The chip's words now ("Preview", or the host while the address is edited).</summary>
    public string Label => label.Text ?? "";

    /// <summary>The glyph at the chip's start.</summary>
    public PaneGlyphKind Glyph => glyph.Kind;

    public bool IsInfoOpen => info.IsOpen;

    /// <summary>The popup the strip owns (the address field puts it in the tree).</summary>
    internal Popup InfoPopup => info.Popup;

    internal void Update(VisusTab? shown, bool isEditing)
    {
        tab = shown;
        editing = isEditing;
        chip = shown?.Chip ?? default;
        var kind = chip.Kind switch
        {
            SiteChipKind.Secure => PaneGlyphKind.Lock,
            SiteChipKind.NotSecure => PaneGlyphKind.Warning,
            SiteChipKind.Preview => PaneGlyphKind.Browser,
            SiteChipKind.Private => PaneGlyphKind.Mask,
            SiteChipKind.Agent => PaneGlyphKind.Shield,
            _ => PaneGlyphKind.Search,
        };
        if (glyph.Kind != kind) glyph.Kind = kind;
        var words = editing && shown is { IsBlank: false } ? shown.Source : chip.Label;
        label.Text = words;
        label.IsVisible = words.Length > 0;
        var source = shown is null || shown.IsBlank ? "new tab" : shown.Source;
        AutomationProperties.SetName(this, shown is null || shown.IsBlank ? "Visus · new tab" : $"Visus · {source}, {chip.Spoken}");
        ToolTip.SetTip(this, shown is null || shown.IsBlank ? null : "Site information");
        IsHitTestVisible = shown is { IsBlank: false };
        Focusable = shown is { IsBlank: false };
        if (info.IsOpen && (shown is null || shown.IsBlank)) SetInfoOpen(false);
        ApplyLook();
    }

    private void ApplyLook()
    {
        (SwiftColor? Fill, SwiftColor? Rim) look = chip.Kind switch
        {
            SiteChipKind.Preview => (VisusTokens.PreviewFill, VisusTokens.PreviewRim),
            SiteChipKind.Agent => (VisusTokens.AgentFill, VisusTokens.AgentRim),
            SiteChipKind.Private => (VisusTokens.PrivateFill, null),
            SiteChipKind.NotSecure => (VisusTokens.ChipFill, null),
            _ => (null, null),
        };
        var (fill, rim) = look;
        var pill = label.IsVisible;
        face.Padding = pill ? new Thickness(7, 0) : new Thickness(0);
        face.Background = pill && fill is { } f ? f.Brush(this) : Brushes.Transparent;
        face.BorderBrush = pill && rim is { } r ? r.Brush(this) : null;
        face.BorderThickness = pill && rim is not null ? new Thickness(0.5) : default;
        var text = chip.Kind == SiteChipKind.NotSecure || chip.NotSecure && !editing ? VisusTokens.NotSecureText : PaneTokens.Text1;
        label.Foreground = text.Brush(this);
        glyph.Foreground = (pill ? text : PaneTokens.Text3).Brush(this);
    }

    private void SetInfoOpen(bool open) => info.SetOpen(open);

    private Control BuildInfo()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        if (tab is null) return stack;
        stack.Children.Add(new PopoverSectionHeader(tab.Source));
        var note = chip.Kind switch
        {
            SiteChipKind.Secure => "The connection to this site is encrypted.",
            SiteChipKind.NotSecure => "This page is not encrypted. Do not type passwords or card numbers here.",
            SiteChipKind.Preview => "A project preview: a server on this PC or your network.",
            SiteChipKind.Private => "A private tab: nothing you do here is kept, and the model cannot see it.",
            SiteChipKind.Agent => "The model's tab: the model reads and drives this page.",
            _ => "",
        };
        if (chip.NotSecure && chip.Kind is SiteChipKind.Private or SiteChipKind.Agent or SiteChipKind.Preview)
            note += " This page is not encrypted.";
        if (note.Length > 0) stack.Children.Add(new PopoverNote(note));
        var shown = tab;
        // W8b (5.5): what this site may do, as nth keeps it (browse and preview), with Reset.
        var kept = KeptPermissions();
        if (kept.Count > 0)
        {
            stack.Children.Add(new PopoverDivider());
            stack.Children.Add(new PopoverSectionHeader("Permissions"));
            stack.Children.Add(new PopoverNote(PermissionsLine(kept)));
            stack.Children.Add(new PopoverItem("Reset permissions", () => { SetInfoOpen(false); ResetPermissions(); }));
        }
        stack.Children.Add(new PopoverDivider());
        stack.Children.Add(new PopoverItem("Copy link", () => { SetInfoOpen(false); VisusPaneBody.CopyText(this, shown.Url); }));
        return stack;
    }

    /// <summary>The answers nth keeps for the shown site (none for private and agent tabs).</summary>
    internal IReadOnlyList<SitePermission> KeptPermissions()
    {
        if (tab is null || tab.IsBlank) return [];
        var profile = VisusTabRules.Profile(tab.Kind);
        if (!PermissionStore.Keeps(profile) || PermissionStore.Clean(WebPolicy.OriginOf(tab.Url)) is not { } origin) return [];
        return VisusHost.Permissions.For(profile, origin);
    }

    /// <summary>"Camera: allowed · Notifications: blocked".</summary>
    internal static string PermissionsLine(IReadOnlyList<SitePermission> kept) =>
        string.Join(" · ", kept.Select(entry => SitePermissions.Noun(entry.Kind) + ": " + SitePermissions.StateWord(entry.State).ToLowerInvariant()));

    /// <summary>Reset permissions: the site asks again next time.</summary>
    internal void ResetPermissions()
    {
        if (tab is null) return;
        var profile = VisusTabRules.Profile(tab.Kind);
        if (PermissionStore.Clean(WebPolicy.OriginOf(tab.Url)) is { } origin) VisusHost.Permissions.Forget(profile, origin);
    }
}

/// <summary>DESIGN 5.2: the address field, its chips and its suggestions.</summary>
public sealed class VisusAddressField : Border
{
    /// <summary>The height of one suggestion row.</summary>
    public const double SuggestionHeight = 30;

    /// <summary>The address field's placeholder (the canvas).</summary>
    public const string Placeholder = "Search or enter an address";

    private readonly VisusSession session;
    private readonly TextBox box;
    private readonly VisusPopover suggestionsPopup;
    private readonly Border suggestionsFrame;
    private readonly StackPanel suggestionRows = new() { Orientation = Orientation.Vertical };
    private readonly VisusPopover popupMenu;
    private readonly TextBlock zoomText = new() { FontSize = 11, FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), VerticalAlignment = VerticalAlignment.Center };
    private IReadOnlyList<VisusSuggestion> suggestions = [];
    private CancellationTokenSource? remoteSuggestions;
    private int highlight = -1;
    private bool editing;
    private bool programmatic;
    private bool selectAllOnRelease;
    private VisusTab? watched;

    public VisusAddressField(VisusSession session)
    {
        this.session = session;
        Height = VisusTokens.AddressHeight;
        CornerRadius = new CornerRadius(VisusTokens.AddressHeight / 2);
        Padding = new Thickness(9, 0, 4, 0);
        BorderThickness = new Thickness(0.5);
        Trust = new VisusTrustStrip { Margin = new Thickness(0, 0, 7, 0) };
        box = PlainControls.TextField(Placeholder, new Binding<string>(() => "", _ => { }));
        box.FontSize = VisusTokens.AddressFontSize;
        box.VerticalAlignment = VerticalAlignment.Center;
        box.VerticalContentAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(box, "Address and search");
        PopupChip = Chip(PaneGlyphKind.Shield, "Pop-up blocked", () => SetPopupMenuOpen(!popupMenu!.IsOpen));
        ZoomChip = new Button { Content = new Border { Height = VisusTokens.ZoomChipHeight, Padding = new Thickness(8, 0), CornerRadius = new CornerRadius(11), Child = zoomText }, Padding = default, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        PlainControls.KeyboardFocus(ZoomChip, 11);
        ZoomChip.Click += (_, _) => session.Selected?.SetZoom(1);
        ToolTip.SetTip(ZoomChip, "Reset zoom  Ctrl+0");
        popupMenu = new VisusPopover(PopupChip, () => new PopoverMenu(BuildPopupMenu));
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(Trust, Dock.Left);
        DockPanel.SetDock(ZoomChip, Dock.Right);
        DockPanel.SetDock(PopupChip, Dock.Right);
        row.Children.Add(Trust);
        row.Children.Add(ZoomChip);
        row.Children.Add(PopupChip);
        row.Children.Add(box);
        suggestionsFrame = new Border { ClipToBounds = true, Padding = new Thickness(4), Child = suggestionRows };
        // The field, not a click elsewhere, closes them (its focus leaving does); they never take the keyboard.
        suggestionsPopup = new VisusPopover(this, () => suggestionsFrame, lightDismiss: false, takesFocus: false, dismissed: () => highlight = -1);
        AutomationProperties.SetName(suggestionRows, "Suggestions");
        Child = new Panel { Children = { row, suggestionsPopup.Popup, popupMenu.Popup, Trust.InfoPopup } };
        box.GotFocus += OnGotFocus;
        box.LostFocus += OnLostFocus;
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty && !programmatic) OnTyped();
        };
        box.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        box.AddHandler(PointerReleasedEvent, (_, _) =>
        {
            if (!selectAllOnRelease) return;
            selectAllOnRelease = false;
            if (box.SelectionStart == box.SelectionEnd) box.SelectAll();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        ActualThemeVariantChanged += (_, _) => ApplyLook();
        Sync();
    }

    public VisusTrustStrip Trust { get; }

    public TextBox Box => box;

    /// <summary>The "Pop-up blocked" chip (shown while the chosen tab has a blocked pop-up).</summary>
    public Button PopupChip { get; }

    /// <summary>The zoom chip (shown only when the zoom is not 100%).</summary>
    public Button ZoomChip { get; }

    /// <summary>The text was typed since the field took the keyboard (the address is not what the page shows).</summary>
    public bool IsEditing => editing;

    public bool AreSuggestionsOpen => suggestionsPopup.IsOpen;

    public IReadOnlyList<VisusSuggestion> Suggestions => suggestions;

    /// <summary>The suggestion Up and Down chose; -1 is none (Enter does what the first row says).</summary>
    public int Highlight => highlight;

    public bool IsPopupMenuOpen => popupMenu.IsOpen;

    /// <summary>Enter went somewhere: the pane puts the keyboard in the page.</summary>
    public event Action? Went;

    /// <summary>Focuses the field with the whole address selected (Ctrl+L, Alt+D).</summary>
    public void FocusAndSelectAll()
    {
        box.Focus(NavigationMethod.Tab);
        box.SelectAll();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        session.Changed += Sync;
        Sync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        session.Changed -= Sync;
        if (watched is not null) watched.Changed -= Apply;
        watched = null;
        CloseSuggestions();
        popupMenu.SetOpen(false);
        base.OnDetachedFromVisualTree(e);
    }

    private void Sync()
    {
        if (!ReferenceEquals(watched, session.Selected))
        {
            if (watched is not null) watched.Changed -= Apply;
            watched = session.Selected;
            if (watched is not null) watched.Changed += Apply;
            editing = false; // another tab: its own address, never the last tab's draft
            CloseSuggestions();
        }
        Apply();
    }

    private void Apply()
    {
        var tab = session.Selected;
        if (!editing) SetText(box.IsFocused ? EditingText(tab) : DisplayText(tab));
        Trust.Update(tab, editing);
        PopupChip.IsVisible = tab?.BlockedPopup is not null;
        if (!PopupChip.IsVisible && popupMenu.IsOpen) SetPopupMenuOpen(false);
        var zoom = tab?.Zoom ?? 1;
        ZoomChip.IsVisible = Math.Abs(zoom - 1) > 0.001;
        zoomText.Text = Math.Round(zoom * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + " %";
        AutomationProperties.SetName(ZoomChip, "Zoom " + zoomText.Text + ", reset");
        ApplyLook();
    }

    private static string DisplayText(VisusTab? tab) => tab is null || tab.IsBlank ? "" : VisusAddress.Display(tab.Url);

    private static string EditingText(VisusTab? tab) => tab is null || tab.IsBlank ? "" : VisusAddress.Editing(tab.Url);

    private void SetText(string text)
    {
        if (string.Equals(box.Text, text, StringComparison.Ordinal)) return;
        programmatic = true;
        try
        {
            box.Text = text;
        }
        finally
        {
            programmatic = false;
        }
    }

    private void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (!editing) SetText(EditingText(session.Selected));
        box.SelectAll();
        selectAllOnRelease = e.NavigationMethod == NavigationMethod.Pointer;
        Trust.Update(session.Selected, editing);
        ApplyLook();
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        CloseSuggestions();
        selectAllOnRelease = false;
        if (!editing || string.IsNullOrWhiteSpace(box.Text))
        {
            editing = false;
            SetText(DisplayText(session.Selected));
        }
        Trust.Update(session.Selected, editing);
        ApplyLook();
    }

    private void OnTyped()
    {
        editing = true;
        highlight = -1;
        Trust.Update(session.Selected, editing);
        UpdateSuggestions();
    }

    private void UpdateSuggestions()
    {
        var text = box.Text ?? "";
        var tab = session.Selected;
        suggestions = string.IsNullOrWhiteSpace(text)
            ? []
            : VisusSuggestions.For(
                text, VisusHost.SearchEngine(),
                session.Tabs.Select(t => new VisusOpenTab(t.Id, t.Url, t.Title, t.Kind)).ToList(),
                tab is null || VisusTabRules.RecordsHistory(tab.Kind) ? VisusHost.History.Entries : [],
                tab?.Id, tab?.Kind ?? VisusTabKind.Browse, VisusHost.Time.GetUtcNow(), VisusHost.IsRegisteredScheme);
        if (highlight >= suggestions.Count) highlight = suggestions.Count - 1;
        BuildRows();
        if (suggestions.Count == 0) CloseSuggestions();
        else OpenSuggestions();
        AskRemoteSuggestions(text, tab);
    }

    // PORT-ONLY (web surfaces W10, DESIGN 5.2 and 9, ED-NEW-web-w10-7): with "Show search suggestions" on, a search phrase is also asked of the search
    // engine, a moment after the last key, and its suggestions join the list under the typed row. Never for a private or agent tab, never for an
    // address or app link, and the answer is dropped if the text moved on.
    private void AskRemoteSuggestions(string text, VisusTab? tab)
    {
        remoteSuggestions?.Cancel();
        remoteSuggestions = null;
        if (!VisusHost.SearchSuggestions() || tab is null || tab.Kind is VisusTabKind.Private or VisusTabKind.Agent) return;
        var engine = VisusHost.SearchEngine();
        if (!SearchSuggestionRules.MaySend(text, engine, VisusHost.IsRegisteredScheme)) return;
        var source = remoteSuggestions = new CancellationTokenSource();
        _ = AskRemoteSuggestionsAsync(text, engine, source.Token);
    }

    private async Task AskRemoteSuggestionsAsync(string text, SearchEngine engine, CancellationToken token)
    {
        try
        {
            await Task.Delay(VisusHost.SuggestionDelay, VisusHost.Time, token);
            var found = await VisusHost.SuggestionSource(engine, text, token);
            if (token.IsCancellationRequested || !string.Equals(box.Text, text, StringComparison.Ordinal) || !editing) return;
            var merged = VisusSuggestions.WithRemote(suggestions, found, engine);
            if (merged.Count == suggestions.Count && merged.SequenceEqual(suggestions)) return;
            suggestions = merged;
            BuildRows();
            OpenSuggestions();
        }
        catch (OperationCanceledException)
        {
            return; // a newer key, or the field closed: the newer ask (or none) owns the list
        }
    }

    private void BuildRows()
    {
        suggestionRows.Children.Clear();
        for (var i = 0; i < suggestions.Count; i++)
        {
            var at = i;
            suggestionRows.Children.Add(new VisusSuggestionRow(suggestions[i], i == highlight, () => ChooseRow(at)));
        }
        suggestionsFrame.Width = Math.Max(240, Bounds.Width);
        suggestionsFrame.Height = suggestions.Count * SuggestionHeight + suggestionsFrame.Padding.Top + suggestionsFrame.Padding.Bottom;
    }

    private void OpenSuggestions() => suggestionsPopup.SetOpen(true);

    private void CloseSuggestions()
    {
        highlight = -1;
        remoteSuggestions?.Cancel();
        remoteSuggestions = null;
        suggestionsPopup.SetOpen(false);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down or Key.Up when suggestionsPopup.IsOpen && suggestions.Count > 0:
                var step = e.Key == Key.Down ? 1 : -1;
                highlight = highlight + step < -1 ? suggestions.Count - 1 : highlight + step >= suggestions.Count ? -1 : highlight + step;
                BuildRows();
                e.Handled = true;
                return;
            case Key.Enter:
                Commit(e.KeyModifiers.HasFlag(KeyModifiers.Alt));
                e.Handled = true;
                return;
            case Key.Escape:
                if (suggestionsPopup.IsOpen)
                {
                    CloseSuggestions();
                    e.Handled = true;
                }
                else if (editing)
                {
                    editing = false;
                    SetText(EditingText(session.Selected));
                    box.SelectAll();
                    Trust.Update(session.Selected, editing);
                    e.Handled = true;
                }
                // nothing edited: Esc is not the field's (the pane's and the window's own Esc run)
                return;
        }
    }

    private void ChooseRow(int index)
    {
        highlight = index;
        Commit(alt: false);
    }

    /// <summary>Enter: the chosen suggestion, else what the first row says (the typed text resolved); Alt+Enter in a new tab.</summary>
    private void Commit(bool alt)
    {
        var text = box.Text ?? "";
        var chosen = highlight > 0 && highlight < suggestions.Count ? suggestions[highlight] : null;
        CloseSuggestions();
        if (string.IsNullOrWhiteSpace(text) && chosen is null) return;
        if (alt) session.NewTab();
        AddressResult? result = null;
        if (chosen is not null) session.Choose(chosen);
        else result = session.Go(text);
        if (result is { Action: AddressAction.Refused })
        {
            box.SelectAll(); // the notice says why; the text stays to be fixed
            return;
        }
        editing = false;
        Apply();
        Went?.Invoke();
    }

    private void SetPopupMenuOpen(bool open) => popupMenu.SetOpen(open && PopupChip.IsVisible);

    private Control BuildPopupMenu()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        var tab = session.Selected;
        if (tab?.BlockedPopup is not { } link) return stack;
        stack.Children.Add(new PopoverNote("Visus blocked a pop-up this page opened by itself."));
        stack.Children.Add(new PopoverItem("Open " + VisusAddress.Display(link), () => { SetPopupMenuOpen(false); tab.OpenBlockedPopup(); }));
        stack.Children.Add(new PopoverItem("Keep blocking", () => { SetPopupMenuOpen(false); tab.DismissBlockedPopup(); }));
        return stack;
    }

    private Button Chip(PaneGlyphKind kind, string name, Action invoke)
    {
        var glyph = new PaneGlyph(kind, 12) { VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock { Text = name, FontSize = VisusTokens.ChipFontSize, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
        var face = new Border
        {
            Height = VisusTokens.ChipHeight,
            Padding = new Thickness(7, 0),
            CornerRadius = new CornerRadius(10),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { glyph, text } },
        };
        var button = PlainControls.Button(face, invoke).KeyboardFocus(10);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Margin = new Thickness(4, 0, 0, 0);
        AutomationProperties.SetName(button, name);
        void Look()
        {
            face.Background = VisusTokens.ChipFill.Brush(button);
            glyph.Foreground = PaneTokens.Text1.Brush(button);
            text.Foreground = PaneTokens.Text1.Brush(button);
        }
        button.ActualThemeVariantChanged += (_, _) => Look();
        Look();
        return button;
    }

    private void ApplyLook()
    {
        var focused = box.IsFocused;
        Background = (focused ? VisusTokens.AddressFocusFill : VisusTokens.AddressFill).Brush(this);
        BorderBrush = (focused ? VisusTokens.AddressFocusRim : VisusTokens.AddressRim).Brush(this);
        box.Foreground = PaneTokens.Text1.Brush(this);
        if (ZoomChip.Content is Border zoomFace) zoomFace.Background = PaneTokens.HoverFill.Brush(this);
        zoomText.Foreground = PaneTokens.Text2.Brush(this);
    }
}

/// <summary>One row of the address suggestions: its glyph, its words and what it is.</summary>
public sealed class VisusSuggestionRow : Border
{
    public VisusSuggestionRow(VisusSuggestion suggestion, bool highlighted, Action choose)
    {
        Suggestion = suggestion;
        IsHighlighted = highlighted;
        Height = VisusAddressField.SuggestionHeight;
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(8, 0);
        var glyph = new PaneGlyph(suggestion.Kind switch
        {
            VisusSuggestionKind.Search => PaneGlyphKind.Search,
            VisusSuggestionKind.OpenApp => PaneGlyphKind.OpenExternal,
            VisusSuggestionKind.SwitchToTab => PaneGlyphKind.Browser,
            _ => PaneGlyphKind.Browser,
        }, 13) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var title = new TextBlock { Text = suggestion.Title, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var detail = new TextBlock { Text = suggestion.Detail, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), MaxWidth = 220 };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(glyph, Dock.Left);
        DockPanel.SetDock(detail, Dock.Right);
        row.Children.Add(glyph);
        row.Children.Add(detail);
        row.Children.Add(title);
        Child = row;
        var hovered = false;
        void Look()
        {
            Background = highlighted || hovered ? PaneTokens.HoverFill.Brush(this) : Brushes.Transparent;
            glyph.Foreground = PaneTokens.Text2.Brush(this);
            title.Foreground = PaneTokens.Text1.Brush(this);
            detail.Foreground = PaneTokens.Text3.Brush(this);
        }
        PointerEntered += (_, _) => { hovered = true; Look(); };
        PointerExited += (_, _) => { hovered = false; Look(); };
        // The press is taken so the field keeps the keyboard; the release chooses.
        PointerPressed += (_, e) => e.Handled = true;
        PointerReleased += (_, e) =>
        {
            e.Handled = true;
            choose();
        };
        ActualThemeVariantChanged += (_, _) => Look();
        Look();
        AutomationProperties.SetName(this, suggestion.Title + ", " + suggestion.Detail);
        SetValue(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.ListItem);
    }

    public VisusSuggestion Suggestion { get; }

    public bool IsHighlighted { get; }
}
