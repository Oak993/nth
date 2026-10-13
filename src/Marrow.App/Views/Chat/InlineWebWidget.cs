// PORT-ONLY: web surfaces W19, a web widget in the thread (DESIGN.md 7.6 "Display modes", "Live versus snapshot in the thread";
// 8.2 trust frame; canvas .mapp, .mah, .mseg, .mweb, .inl-ph; ED-NEW-web-w19-4). No Swift counterpart.
//
// The card: a 38 DIP header (the server's cube, "shipyard · Release board", the trust strip "Sandboxed · from shipyard", and
// the Inline / Pane / Pop out switch), then the app's web rectangle inset 6 DIP, as tall as the app asks, at most 60 % of the
// thread. While the app is in the App pane or its own window, the card is a quiet 44 DIP line that says where it went, with
// Bring back. An html_widget is the same card with "Widget · from this answer" and no switch (it lives only in the thread).
//
// Live or snapshot: the thread's coordinator keeps every inline web rectangle on its snapshot while the thread scrolls (from
// the first moved frame), and 150 ms after it stops lets one go live: the one the user last touched, else the newest that
// lies fully inside the clear part of the thread (below the header glass, above the composer). The switch is invisible: the
// snapshot is the page's own last frame.
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.Web.McpApps;
using Marrow.App.Runtime;
using Marrow.App.Views.Artifacts;
using Marrow.App.Views.Composer;
using Marrow.Core.Localisation;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.UI.Chrome;
using Marrow.UI.Panes;
using Marrow.UI.Port;
using Marrow.UI.Web;

namespace Marrow.App.Views.Chat;

public sealed class InlineWebWidget : Decorator
{
    public const double HeaderHeight = 38;
    public const double PlaceholderHeight = 44;
    public const double FailureHeight = 88;

    /// <summary>Below this width the trust strip and the switch's words step aside (the canvas's 520).</summary>
    public const double CompactWidth = 520;

    public static string WidgetTrustText => UiText.Get("live.widget.trust");
    public static string TryAgainText => UiText.Get("live.widget.try_again");
    public static string LiveText => UiText.Get("live.widget.live");
    public static string SnapshotText => UiText.Get("live.widget.snapshot");
    public static string OpenLinkTitle => UiText.Get("live.widget.open_link");

    /// <summary>ED-NEW-web-sec-res-2: "Open link from the app", the offer of a link the user clicked in an MCP App.</summary>
    public static string AppOpenLinkTitle => UiText.Get("live.app.open_link");

    // The card's glass (canvas: rgba(255,255,255,.035) and a .5 rim of .1 on dark; the light look mirrors it in ink).
    internal static readonly SwiftColor CardFill = SwiftColor.Dynamic(new SwiftColor(0, 0, 0, 0.025), new SwiftColor(1, 1, 1, 0.035));
    internal static readonly SwiftColor CardRim = SwiftColor.Dynamic(new SwiftColor(0, 0, 0, 0.09), new SwiftColor(1, 1, 1, 0.10));
    internal static readonly SwiftColor SegmentFill = SwiftColor.Dynamic(new SwiftColor(0, 0, 0, 0.05), new SwiftColor(1, 1, 1, 0.06));
    internal static readonly SwiftColor SegmentPressed = SwiftColor.Dynamic(new SwiftColor(0, 0, 0, 0.10), new SwiftColor(1, 1, 1, 0.13));
    internal static readonly SwiftColor PlaceholderFill = SwiftColor.Dynamic(new SwiftColor(0, 0, 0, 0.03), new SwiftColor(1, 1, 1, 0.04));

    private readonly McpAppSession? app;
    private readonly InlineWidgetSession? widget;
    private readonly Decorator slot = new() { Name = "WebSlot" };
    private readonly TabulaNotice linkNotice = new() { IsVisible = false, Margin = new Thickness(6, 6, 6, 6) };
    private readonly Border card;
    private readonly Border body;
    private readonly Border placeholder;
    private readonly TextBlock title;
    private readonly StackPanel trust;
    private readonly TextBlock placeholderText;
    private readonly PaneGlyph placeholderGlyph;
    private readonly Border failure;
    private readonly TextBlock failureText;
    private readonly Dictionary<McpAppDisplayMode, Segment> segments = [];
    private readonly Border badge;
    private readonly TextBlock badgeText;
    private readonly Border badgeDot;
    private ScrollViewer? scroller;
    private InlineWebCoordinator? coordinator;
    private DateTimeOffset lastIntent;
    private string? offered;

    private sealed record Segment(Button Button, Border Face, PaneGlyph Glyph, TextBlock Label);

    /// <summary>The card of an MCP App in its tool row.</summary>
    public InlineWebWidget(McpAppSession session) : this(session, null)
    {
    }

    private InlineWebWidget(McpAppSession? app, InlineWidgetSession? widget)
    {
        this.app = app;
        this.widget = widget;
        var glyph = new PaneGlyph(PaneGlyphKind.App, 14);
        glyph.Foreground = PaneTokens.Text2.Brush(glyph);
        title = new TextBlock
        {
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.Foreground = PaneTokens.Text1.Brush(title);
        var shield = new PaneGlyph(PaneGlyphKind.Shield, 12);
        shield.Foreground = PaneTokens.Text3.Brush(shield);
        var trustText = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        trustText.Foreground = PaneTokens.Text3.Brush(trustText);
        trust = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { shield, trustText } };

        // The canvas's .live pill: a green dot and "Live" while the page itself shows, a grey dot and "Snapshot" while its last
        // frame stands in (the thread scrolls, or another widget is the live one). It is as wide as "Snapshot" either way, so
        // the trust strip beside it never moves while the thread scrolls.
        badgeDot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
        badgeText = new TextBlock { Text = LiveText, FontSize = 10.5, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
        badgeText.Foreground = PaneTokens.Text2.Brush(badgeText);
        var badgeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center, Children = { badgeDot, badgeText } };
        badge = new Border
        {
            Height = 20,
            Padding = new Thickness(7, 0, 7, 0),
            CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = BadgeWidth(),
            Child = badgeRow,
        };
        badge.Background = SegmentFill.Brush(badge);
        ShowLive(true);

        if (app is not null)
        {
            title.Text = app.Server + " · " + app.Title;
            trustText.Text = McpAppTrust.Sandboxed(app.Server);
        }
        else
        {
            title.Text = WidgetTrustText;
            trust.IsVisible = false;
        }

        var header = new Grid
        {
            Height = HeaderHeight,
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(12, 0, 6, 0),
            Children = { glyph, title, badge, trust },
        };
        Grid.SetColumn(title, 1);
        Grid.SetColumn(badge, 2);
        Grid.SetColumn(trust, 3);
        if (app is not null)
        {
            var switcher = Switcher();
            Grid.SetColumn(switcher, 5);
            header.Children.Add(switcher);
        }

        var hairline = new Border { Height = 0.5, VerticalAlignment = VerticalAlignment.Bottom };
        hairline.Background = PaneTokens.Line.Brush(hairline);
        var top = new Panel { Children = { header, hairline } };

        failureText = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 420 };
        failureText.Foreground = PaneTokens.Text2.Brush(failureText);
        var retry = TextButton(TryAgainText, () => app?.Mount.Reload());
        failure = new Border
        {
            Height = FailureHeight,
            IsVisible = false,
            Child = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { failureText, retry } },
        };

        body = new Border
        {
            Margin = new Thickness(6, 0, 6, 6), // the canvas's .mweb: under the header's hairline, 6 DIP in at the sides and foot
            CornerRadius = new CornerRadius(9),
            ClipToBounds = true,
            Child = new Panel { Children = { slot, failure } },
        };
        // ED-NEW-web-sec-artifacts-10: a link the user clicked in the widget is offered under the header, its whole address shown;
        // ED-NEW-web-sec-res-2: the same for a link the user clicked in an MCP App shown inline.
        linkNotice.Dismissed += () =>
        {
            widget?.DismissLink();
            app?.DismissLink();
        };
        var column = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), Children = { top, linkNotice, body } };
        Grid.SetRow(linkNotice, 1);
        Grid.SetRow(body, 2);
        card = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(0.5),
            ClipToBounds = true,
            Child = column,
        };
        card.Background = CardFill.Brush(card);
        card.BorderBrush = CardRim.Brush(card);

        placeholderGlyph = new PaneGlyph(PaneGlyphKind.Pane, 14);
        placeholderGlyph.Foreground = PaneTokens.Text2.Brush(placeholderGlyph);
        placeholderText = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
        placeholderText.Foreground = PaneTokens.Text2.Brush(placeholderText);
        var bringBack = TextButton(McpAppSession.BringBackText, () => app?.SetMode(McpAppDisplayMode.Inline));
        var placeholderRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(12, 0, 12, 0),
            Children = { placeholderGlyph, placeholderText, bringBack },
        };
        Grid.SetColumn(placeholderText, 1);
        Grid.SetColumn(bringBack, 2);
        placeholder = new Border
        {
            Height = PlaceholderHeight,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(0.5),
            IsVisible = false,
            Child = placeholderRow,
        };
        placeholder.Background = PlaceholderFill.Brush(placeholder);
        placeholder.BorderBrush = CardRim.Brush(placeholder);

        Child = new Panel { Children = { card, placeholder } };
        AutomationProperties.SetName(this, title.Text);

        slot.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty) app?.UpdateContext(slot, ThreadHeight);
        };
        PointerEntered += (_, _) => Touch();
        PointerPressed += (_, _) => Touch();
        PointerMoved += (_, _) => Intent();
        Show();
    }

    /// <summary>The card of an html_widget (W24's tool): the same widget for every row that draws the same id.</summary>
    public static InlineWebWidget Html(string id, string html, double? height = null) => new(null, InlineWidgetSession.For(id, html, height));

    /// <summary>
    /// The card of a live answer that is one HTML page (W24's html_widget or a fenced nth-html block), for LiveAnswerHtml.Host: null while the
    /// page is still arriving (the row says it is being written) or has no id. The widget is one per chat, id and version, so a newer view
    /// with the same id is a new page on its own site, and two chats that use the same id never share one.
    /// </summary>
    public static Control? ForLiveAnswer(ToolLiveView live, ThreadRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(runtime);
        if (!live.IsHtml || !live.Complete || live.Id.Length == 0 || live.Text.Length == 0) return null;
        return Html(WidgetKey(runtime.ThreadID, live.Id, live.Version), live.Text, live.Height is { } height and > 0 ? height : null);
    }

    /// <summary>The widget's name for the session table and its site: the chat, the view's id and its version.</summary>
    public static string WidgetKey(Guid thread, string id, int version) => thread.ToString("N")[..8] + "-" + id + "-v" + version;

    /// <summary>The app this card shows, or null for an html_widget.</summary>
    public McpAppSession? App => app;

    /// <summary>The html_widget this card shows, or null for an app.</summary>
    public InlineWidgetSession? Widget => widget;

    /// <summary>The web rectangle's place (the host goes in it while the card shows it).</summary>
    public Decorator Slot => slot;

    /// <summary>The header's words (tests).</summary>
    public string TitleText => title.Text ?? "";

    /// <summary>The words of the line that stands in for the app while it is elsewhere, or null while it is here.</summary>
    public string? PlaceholderText => placeholder.IsVisible ? placeholderText.Text : null;

    /// <summary>The trust strip shows (wide enough, and an app).</summary>
    public bool ShowsTrust => trust.IsVisible;

    /// <summary>The header's pill says "Live" (the page shows) or "Snapshot" (its last frame stands in).</summary>
    public string BadgeText => badgeText.Text ?? "";

    /// <summary>ED-NEW-web-sec-artifacts-10: the offer of a link the user clicked in the widget, with its whole address.</summary>
    internal TabulaNotice LinkNotice => linkNotice;

    /// <summary>The failure line shows, with its words.</summary>
    public string? FailureText => failure.IsVisible ? failureText.Text : null;

    /// <summary>The switch's buttons by mode (tests).</summary>
    internal Button? SwitchButton(McpAppDisplayMode mode) => segments.TryGetValue(mode, out var segment) ? segment.Button : null;

    /// <summary>The switch's pressed mode (tests).</summary>
    internal McpAppDisplayMode? PressedMode => segments.Where(pair => pair.Value.Button.Tag is true).Select(pair => (McpAppDisplayMode?)pair.Key).FirstOrDefault();

    /// <summary>The web host while it is in this card (null while the app is elsewhere).</summary>
    internal WebViewHost? LiveHost => widget?.Host ?? (app is { Mode: McpAppDisplayMode.Inline } && app.Host.Parent == slot ? app.Host : null);

    /// <summary>When the user last touched the card (the coordinator's "last touched").</summary>
    internal DateTimeOffset? TouchedAt { get; private set; }

    /// <summary>The thread's visible height (the 60 % cap is of it).</summary>
    internal double ThreadHeight => scroller?.Viewport.Height is { } height && height > 0 ? height : scroller?.Bounds.Height ?? 0;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (app is not null)
        {
            app.Changed += Show;
            app.AttachInline(slot);
        }

        if (widget is not null)
        {
            widget.Changed += Show;
            if (widget.Host.Parent is Decorator old && old != slot) McpAppSession.TakeOut(old);
            slot.Child = widget.Host;
            widget.View.SetShown(true);
        }

        scroller = this.FindAncestorOfType<ScrollViewer>();
        if (scroller is not null)
        {
            scroller.PropertyChanged += OnScrollerChanged;
            coordinator = InlineWebCoordinator.For(scroller);
            coordinator.Add(this);
        }

        if (TopLevel.GetTopLevel(this) is { } top) top.ActualThemeVariantChanged += OnTheme;
        Show();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top) top.ActualThemeVariantChanged -= OnTheme;
        coordinator?.Remove(this);
        coordinator = null;
        if (scroller is not null) scroller.PropertyChanged -= OnScrollerChanged;
        scroller = null;
        if (app is not null)
        {
            app.Changed -= Show;
            app.DetachInline(slot);
        }

        if (widget is not null)
        {
            widget.Changed -= Show;
            if (slot.Child == widget.Host)
            {
                slot.Child = null;
                widget.View.SetShown(false);
            }
        }
    }

    private void OnTheme(object? sender, EventArgs e)
    {
        offered = null; // the offer's colours follow the theme
        Show();
    }

    /// <summary>The coordinator let the page show (true) or holds its last frame up (false).</summary>
    internal void ShowLive(bool live)
    {
        badgeText.Text = live ? LiveText : SnapshotText;
        badgeDot.Background = (live ? PaneTokens.ChipDot : PaneTokens.Text3).Brush(badgeDot);
        AutomationProperties.SetName(badge, badgeText.Text);
    }

    // The pill's width with "Snapshot" in it: the dot, the gap, the words and the padding.
    private static double BadgeWidth()
    {
        var probe = new TextBlock { Text = SnapshotText, FontSize = 10.5, FontWeight = FontWeight.Medium };
        probe.Measure(Size.Infinity);
        return Math.Ceiling(7 + 6 + 5 + probe.DesiredSize.Width + 7);
    }

    private void OnScrollerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.ViewportProperty || e.Property == BoundsProperty) Show();
    }

    // Draws what the card says now: where the app is, its height, its failure, the switch's pressed mode.
    private void Show()
    {
        if (app is not null)
        {
            var away = app.PlaceholderText;
            placeholder.IsVisible = away is not null;
            card.IsVisible = away is null;
            if (away is not null)
            {
                placeholderText.Text = away;
                placeholderGlyph.Kind = app.Mode == McpAppDisplayMode.PopOut ? PaneGlyphKind.PopOut : PaneGlyphKind.Pane;
            }

            var failed = app.Mount.Failure;
            failure.IsVisible = failed is not null;
            slot.IsVisible = failed is null;
            if (failed is not null) failureText.Text = McpAppTrust.CouldNotOpen(app.Title, failed);
            slot.Height = app.InlineHeight(ThreadHeight);
            foreach (var (mode, segment) in segments) Press(segment, mode == app.Mode);
            ShowOffer(app.PendingLink, AppOpenLinkTitle, app.OpenLink);
        }
        else if (widget is not null)
        {
            slot.Height = widget.InlineHeight(ThreadHeight);
            ShowOffer(widget.PendingLink, OpenLinkTitle, widget.OpenLink);
        }

        coordinator?.Refresh(this);
    }

    // The link the widget or app offers, if any: its whole address and Open (TabulaNotice.ShowAddress), drawn again only when it changes.
    private void ShowOffer(string? link, string title, Action open)
    {
        if (link == offered && linkNotice.IsVisible == link is not null) return;
        offered = link;
        if (link is null) linkNotice.Hide();
        else linkNotice.ShowAddress(title, link, (UiText.Get("tabula.notice.open"), open));
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var compact = e.NewSize.Width > 0 && e.NewSize.Width < CompactWidth;
        if (app is not null) trust.IsVisible = !compact;
        foreach (var segment in segments.Values) segment.Label.IsVisible = !compact;
    }

    private void Touch()
    {
        TouchedAt = DateTimeOffset.UtcNow;
        coordinator?.Touched(this);
    }

    // The pointer over the card says the user may click it soon: the surface keeps its view warm (at most twice a second).
    private void Intent()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - lastIntent < TimeSpan.FromMilliseconds(500)) return;
        lastIntent = now;
        (app?.View ?? widget?.View)?.NoteIntent();
    }

    // ---- the switch: Inline / Pane / Pop out ----

    private Border Switcher()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        row.Children.Add(MakeSegment(McpAppDisplayMode.Inline, PaneGlyphKind.Inline, McpAppTrust.InlineLabel, McpAppTrust.InlineName));
        row.Children.Add(MakeSegment(McpAppDisplayMode.Pane, PaneGlyphKind.Pane, McpAppTrust.PaneLabel, McpAppTrust.PaneName));
        row.Children.Add(MakeSegment(McpAppDisplayMode.PopOut, PaneGlyphKind.PopOut, McpAppTrust.PopOutLabel, McpAppTrust.PopOutName));
        var box = new Border { Padding = new Thickness(2), CornerRadius = new CornerRadius(9), VerticalAlignment = VerticalAlignment.Center, Child = row };
        box.Background = SegmentFill.Brush(box);
        return box;
    }

    private Button MakeSegment(McpAppDisplayMode mode, PaneGlyphKind kind, string label, string name)
    {
        var glyph = new PaneGlyph(kind, 12);
        var text = new TextBlock { Text = label, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        var face = new Border
        {
            Height = 24,
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { glyph, text } },
        };
        var button = PlainControls.Button(face, () => app?.SetMode(mode));
        ToolTip.SetTip(button, name);
        AutomationProperties.SetName(button, name);
        var segment = new Segment(button, face, glyph, text);
        segments[mode] = segment;
        button.OnHover(on =>
        {
            if (button.Tag is true) return;
            face.Background = on ? PaneTokens.HoverFill.Brush(face) : Brushes.Transparent;
        });
        Press(segment, false);
        return button;
    }

    private static void Press(Segment segment, bool pressed)
    {
        segment.Button.Tag = pressed;
        segment.Face.Background = pressed ? SegmentPressed.Brush(segment.Face) : Brushes.Transparent;
        var ink = pressed ? PaneTokens.Text1 : PaneTokens.Text2;
        segment.Glyph.Foreground = ink.Brush(segment.Glyph);
        segment.Label.Foreground = ink.Brush(segment.Label);
        AutomationProperties.SetItemStatus(segment.Button, pressed ? UiText.Get("live.widget.selected") : "");
    }

    private static Button TextButton(string text, Action invoke)
    {
        var label = new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
        label.Foreground = Chrome.Accent.Brush(label);
        var face = new Border { Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Child = label };
        var button = PlainControls.Button(face, invoke);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.VerticalAlignment = VerticalAlignment.Center;
        button.OnHover(on => face.Background = on ? PaneTokens.HoverFill.Brush(face) : Brushes.Transparent);
        AutomationProperties.SetName(button, text);
        return button;
    }
}

/// <summary>The words of an app's card, its pane and its window.</summary>
public static class McpAppTrust
{
    public static string InlineLabel => UiText.Get("live.app.inline_label");
    public static string PaneLabel => UiText.Get("live.app.pane_label");
    public static string PopOutLabel => UiText.Get("live.app.pop_out_label");
    public static string InlineName => UiText.Get("live.app.inline_name");
    public static string PaneName => UiText.Get("live.app.pane_name");
    public static string PopOutName => UiText.Get("live.app.pop_out_name");

    /// <summary>The trust strip: "Sandboxed · from shipyard".</summary>
    public static string Sandboxed(string server) => UiText.Format("live.app.sandboxed", server);

    /// <summary>The card's line when the app could not load.</summary>
    public static string CouldNotOpen(string title, string reason) => UiText.Format("live.app.could_not_open", title, reason);
}

/// <summary>
/// The thread's live-or-snapshot rule for its inline web widgets (DESIGN 7.6): every one shows its snapshot while the thread
/// scrolls, and 150 ms after it stops one goes live, the one last touched, else the newest fully inside the clear area.
/// </summary>
public sealed class InlineWebCoordinator
{
    private static readonly ConditionalWeakTable<ScrollViewer, InlineWebCoordinator> Coordinators = [];
    private readonly ScrollViewer scroller;
    private readonly List<InlineWebWidget> widgets = [];
    private readonly Dictionary<InlineWebWidget, (WebViewHost Host, IDisposable Lease)> holds = [];
    private readonly DispatcherTimer idle;
    private WeakReference<ComposerArea>? composer;
    private bool evaluatePending;

    private InlineWebCoordinator(ScrollViewer scroller)
    {
        this.scroller = scroller;
        idle = new DispatcherTimer { Interval = InlineWebLive.IdleAfterScroll };
        idle.Tick += (_, _) =>
        {
            idle.Stop();
            Scrolling = false;
            Evaluate();
        };
        scroller.ScrollChanged += (_, e) =>
        {
            if (e.OffsetDelta.X != 0 || e.OffsetDelta.Y != 0) Scrolled();
            else Schedule();
        };
    }

    /// <summary>The coordinator of a thread's scroller.</summary>
    public static InlineWebCoordinator For(ScrollViewer scroller) => Coordinators.GetValue(scroller, static s => new InlineWebCoordinator(s));

    /// <summary>True from the first moved frame until the thread has been still for 150 ms.</summary>
    public bool Scrolling { get; private set; }

    /// <summary>The widget now live, if any.</summary>
    public InlineWebWidget? Live { get; private set; }

    /// <summary>The thread moved: every widget to its snapshot in this frame, and the idle clock starts again.</summary>
    public void Scrolled()
    {
        Scrolling = true;
        Live = null;
        foreach (var widget in widgets) Hold(widget);
        idle.Stop();
        idle.Start();
    }

    internal void Add(InlineWebWidget widget)
    {
        if (widgets.Contains(widget)) return;
        widgets.Add(widget);
        Hold(widget);
        Schedule();
    }

    internal void Remove(InlineWebWidget widget)
    {
        widgets.Remove(widget);
        Release(widget);
        if (Live == widget) Live = null;
        Schedule();
    }

    internal void Touched(InlineWebWidget widget) => Schedule();

    /// <summary>A widget's host came or went (its app moved to the pane or back): its hold follows it.</summary>
    internal void Refresh(InlineWebWidget widget)
    {
        if (holds.TryGetValue(widget, out var held) && held.Host != widget.LiveHost) Release(widget);
        if (Live != widget && widget.LiveHost is not null) Hold(widget);
        Schedule();
    }

    private void Schedule()
    {
        if (evaluatePending) return;
        evaluatePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            evaluatePending = false;
            Evaluate();
        }, DispatcherPriority.Background);
    }

    /// <summary>Chooses the one live widget now (the idle tick, a touch, a widget arriving or leaving).</summary>
    public void Evaluate()
    {
        var candidates = new List<InlineWebCandidate>();
        foreach (var widget in widgets)
        {
            if (widget.LiveHost is null || !widget.Slot.IsEffectivelyVisible) continue;
            if (widget.Slot.TranslatePoint(default, scroller) is not { } at) continue;
            var bounds = new InlineRect(at.X, at.Y, widget.Slot.Bounds.Width, widget.Slot.Bounds.Height);
            candidates.Add(new InlineWebCandidate(widget, bounds, (long)Math.Round(at.Y), widget.TouchedAt));
        }

        var chosen = InlineWebLive.Choose(candidates, ClearArea(), Scrolling) as InlineWebWidget;
        Live = chosen;
        foreach (var widget in widgets)
        {
            if (widget == chosen) Release(widget);
            else Hold(widget);
        }
    }

    /// <summary>The part of the thread nothing of nth's covers, in the scroller's coordinates.</summary>
    public InlineRect ClearArea()
    {
        var height = scroller.Bounds.Height;
        var top = Math.Min(height, Chrome.ContentTopInset);
        var bottom = height;
        if (Composer() is { } area && area.TranslatePoint(default, scroller) is { } at && at.Y > top && at.Y < bottom) bottom = at.Y;
        return new InlineRect(0, top, scroller.Bounds.Width, Math.Max(0, bottom - top));
    }

    private ComposerArea? Composer()
    {
        if (composer?.TryGetTarget(out var known) == true && TopLevel.GetTopLevel(known) is not null) return known;
        composer = null;
        if (TopLevel.GetTopLevel(scroller) is not { } top) return null;
        var left = scroller.TranslatePoint(default, top)?.X ?? 0;
        var right = left + scroller.Bounds.Width;
        foreach (var area in top.GetVisualDescendants().OfType<ComposerArea>())
        {
            var x = area.TranslatePoint(new Point(area.Bounds.Width / 2, 0), top)?.X ?? double.NaN;
            if (x < left || x > right) continue;
            composer = new WeakReference<ComposerArea>(area);
            return area;
        }

        return null;
    }

    private void Hold(InlineWebWidget widget)
    {
        if (widget.LiveHost is not { } host) return;
        if (holds.TryGetValue(widget, out var held))
        {
            if (held.Host == host) return;
            held.Lease.Dispose();
        }

        holds[widget] = (host, host.HoldSnapshot());
        widget.ShowLive(false);
    }

    private void Release(InlineWebWidget widget)
    {
        if (!holds.Remove(widget, out var held)) return;
        held.Lease.Dispose();
        widget.ShowLive(true);
    }

    /// <summary>True while the coordinator keeps the widget on its snapshot (tests).</summary>
    public bool IsHeld(InlineWebWidget widget) => holds.ContainsKey(widget);
}
