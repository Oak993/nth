using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Marrow.Core.Localisation;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.UI.Chrome;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W8a, Visus's tab strip (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.1 "Header row: tab strip
// (favicon, title, close on hover; private tabs show a mask glyph and a darker tint), New tab (+), the tab overflow menu, then
// Expand and Close", 3.12 "Visus puts its tab strip in the header row", 2.6 motion, 10 accessibility; the canvas's .bh .tabs
// .tab .fav .x; ED-NEW-web-w8a-2). No Swift counterpart. The strip lays its tabs out itself: each at its natural width up to
// 200, shrinking together down to 120 when the row is short, and past that a window of tabs around the chosen one with the
// rest in the overflow menu. Tabs move only by render transforms: when the order or the widths change, each tab springs from
// where it was to where it is (interruptible, the splitter's spring), and a dragged tab follows the pointer 1:1.

namespace Marrow.App.Views.Browser;

/// <summary>The strip's layout: each tab's width (0 when it is in the overflow menu) and whether the overflow button shows.</summary>
public sealed record VisusTabLayout(IReadOnlyList<double> Widths, bool ShowsOverflow)
{
    /// <summary>
    /// Lays out tabs of <paramref name="natural"/> widths in <paramref name="width"/>: natural widths when they fit; else every
    /// tab wider than a common cap shrinks to it (never under its own natural width or <paramref name="minimum"/>); else the
    /// overflow button shows and the tabs around <paramref name="selected"/> that fit at the minimum are kept.
    /// </summary>
    public static VisusTabLayout Solve(IReadOnlyList<double> natural, int selected, double width, double plus, double overflow, double gap, double minimum)
    {
        var n = natural.Count;
        if (n == 0) return new VisusTabLayout([], false);
        var floor = natural.Select(w => Math.Min(w, minimum)).ToArray();
        var room = width - plus - gap * n; // a gap after each tab, before the next tab or the + button
        if (natural.Sum() <= room + 1e-6) return new VisusTabLayout(natural.ToArray(), false);
        if (floor.Sum() <= room + 1e-6)
        {
            // Water-fill: the cap c with sum(min(natural, max(c, floor))) == room.
            double lo = minimum, hi = natural.Max();
            for (var i = 0; i < 40; i++)
            {
                var mid = (lo + hi) / 2;
                if (Capped(natural, floor, mid) > room) hi = mid;
                else lo = mid;
            }
            return new VisusTabLayout(natural.Select((w, i) => Math.Max(floor[i], Math.Min(w, lo))).ToArray(), false);
        }

        var space = room - overflow - gap;
        var widths = new double[n];
        var chosen = Math.Clamp(selected, 0, n - 1);
        var used = floor[chosen];
        widths[chosen] = floor[chosen];
        int left = chosen - 1, right = chosen + 1;
        var turnRight = true;
        while (left >= 0 || right < n)
        {
            var pick = turnRight && right < n || left < 0 ? right : left;
            turnRight = !turnRight;
            if (used + gap + floor[pick] > space + 1e-6)
            {
                if (pick == right) right = n;
                else left = -1;
                continue;
            }
            used += gap + floor[pick];
            widths[pick] = floor[pick];
            if (pick == right) right++;
            else left--;
        }
        return new VisusTabLayout(widths, true);
    }

    private static double Capped(IReadOnlyList<double> natural, double[] floor, double cap)
    {
        var sum = 0.0;
        for (var i = 0; i < natural.Count; i++) sum += Math.Max(floor[i], Math.Min(natural[i], cap));
        return sum;
    }
}

/// <summary>DESIGN 5.1: the tab strip of a Visus pane.</summary>
public sealed class VisusTabStrip : Panel
{
    /// <summary>A press must move this far along the strip before the tab is dragged.</summary>
    public const double DragThreshold = 4;

    private readonly VisusSession session;
    private readonly Dictionary<VisusTab, VisusTabView> views = [];
    private readonly Button plus;
    private readonly Button overflow;
    private readonly VisusPopover overflowMenu;
    private readonly Dictionary<VisusTabView, double> lastX = [];
    private VisusTabLayout layout = new([], false);
    private Action<TimeSpan>? pendingFrame;
    private TimeSpan? lastFrame;
    private VisusTabView? dragging;
    private double dragPointerOffset;
    private bool firstArrange = true;

    public VisusTabStrip(VisusSession session)
    {
        this.session = session;
        Height = VisusTokens.TabHeight;
        ClipToBounds = true;
        Background = Brushes.Transparent;
        plus = StripButton(PaneGlyphKind.Plus, UiText.Get("visus.tabs.new_tab"), "Ctrl+T", () => session.NewTab());
        plus.ContextMenu = NewTabMenu();
        overflow = StripButton(PaneGlyphKind.Chevron, UiText.Get("visus.tabs.all_tabs"), null, () => SetOverflowOpen(!overflowMenu!.IsOpen));
        overflowMenu = new VisusPopover(overflow, () => new PopoverMenu(BuildOverflowMenu));
        Children.Add(plus);
        Children.Add(overflow);
        Children.Add(overflowMenu.Popup);
        AutomationProperties.SetName(this, UiText.Get("visus.tabs.strip_name"));
        SetValue(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.Tab);
        Sync();
    }

    /// <summary>The tab views, in the strip's order.</summary>
    public IReadOnlyList<VisusTabView> TabViews => session.Tabs.Where(views.ContainsKey).Select(tab => views[tab]).ToList();

    public Button NewTabButton => plus;

    public Button OverflowButton => overflow;

    /// <summary>The layout in force (tests read which tabs are in the overflow menu).</summary>
    public VisusTabLayout Layout => layout;

    public bool IsOverflowMenuOpen => overflowMenu.IsOpen;

    /// <summary>Whether a tab is being dragged along the strip now.</summary>
    public bool IsDragging => dragging is not null;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        session.Changed += Sync;
        Sync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        session.Changed -= Sync;
        overflowMenu.SetOpen(false);
        base.OnDetachedFromVisualTree(e);
    }

    private void Sync()
    {
        foreach (var gone in views.Keys.Where(tab => !session.Tabs.Contains(tab)).ToList())
        {
            var view = views[gone];
            views.Remove(gone);
            lastX.Remove(view);
            view.Detach();
            Children.Remove(view);
            if (ReferenceEquals(dragging, view)) dragging = null;
        }
        foreach (var tab in session.Tabs)
        {
            if (views.TryGetValue(tab, out var existing))
            {
                existing.IsChosen = ReferenceEquals(tab, session.Selected);
                continue;
            }
            var view = new VisusTabView(this, tab) { IsChosen = ReferenceEquals(tab, session.Selected) };
            views[tab] = view;
            Children.Insert(0, view);
            if (!firstArrange) view.Appear.Snap(0); // a new tab fades in where it lands
        }
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(Size.Infinity);
        var natural = session.Tabs.Where(views.ContainsKey).Sum(tab => views[tab].NaturalWidth + VisusTokens.TabGap) + plus.DesiredSize.Width;
        var width = double.IsFinite(availableSize.Width) ? Math.Min(natural, availableSize.Width) : natural;
        return new Size(width, VisusTokens.TabHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var ordered = session.Tabs.Where(views.ContainsKey).Select(tab => views[tab]).ToList();
        var selected = session.Selected is { } chosen ? session.Tabs.ToList().IndexOf(chosen) : 0;
        layout = VisusTabLayout.Solve(
            ordered.Select(view => view.NaturalWidth).ToList(), selected, finalSize.Width,
            plus.DesiredSize.Width, overflow.DesiredSize.Width, VisusTokens.TabGap, VisusTokens.TabMinWidth);
        var x = 0.0;
        var reduce = ReduceMotion;
        var animate = !firstArrange && FrameClock.IsBound;
        for (var i = 0; i < ordered.Count; i++)
        {
            var view = ordered[i];
            var w = layout.Widths[i];
            view.IsVisible = w > 0;
            if (w <= 0)
            {
                view.Arrange(default);
                lastX.Remove(view);
                continue;
            }
            view.Arrange(new Rect(x, 0, w, VisusTokens.TabHeight));
            if (ReferenceEquals(view, dragging))
            {
                view.Slide.Snap(view.Slide.Value + (lastX.TryGetValue(view, out var was) ? was - x : 0));
            }
            else if (animate && lastX.TryGetValue(view, out var before) && Math.Abs(before - x) > 0.5)
            {
                // FLIP: the tab is drawn where it was and springs to its new place (a render transform only).
                view.Slide.Snap(view.Slide.Value + before - x);
                view.Slide.SetTarget(0, reduce);
            }
            lastX[view] = x;
            x += w + VisusTokens.TabGap;
        }
        if (layout.ShowsOverflow)
        {
            overflow.IsVisible = true;
            overflow.Arrange(new Rect(x, Centre(overflow, finalSize.Height), overflow.DesiredSize.Width, overflow.DesiredSize.Height));
            x += overflow.DesiredSize.Width + VisusTokens.TabGap;
        }
        else
        {
            overflow.IsVisible = false;
            overflow.Arrange(default);
            if (overflowMenu.IsOpen) SetOverflowOpen(false);
        }
        plus.Arrange(new Rect(x, Centre(plus, finalSize.Height), plus.DesiredSize.Width, plus.DesiredSize.Height));
        overflowMenu.Popup.Arrange(default);
        firstArrange = false;
        foreach (var view in ordered)
        {
            if (!view.Appear.IsMoving && view.Appear.Value < 1 && view.Appear.Target < 1) view.Appear.SetTarget(1, reduce);
            view.ApplyMotion();
        }
        if (ordered.Any(view => view.Slide.IsMoving || view.Appear.IsMoving)) RequestFrame();
        return finalSize;
    }

    private static double Centre(Control control, double height) => Math.Round((height - control.DesiredSize.Height) / 2);

    private bool ReduceMotion =>
        EnvironmentValues.GetAccessibilityReduceMotion(this) || Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;

    private void RequestFrame()
    {
        if (pendingFrame is not null || !FrameClock.IsBound) return;
        pendingFrame = OnFrame;
        FrameClock.Source.Current.RequestFrame(pendingFrame);
    }

    private void OnFrame(TimeSpan now)
    {
        pendingFrame = null;
        var dt = lastFrame is { } last ? Math.Clamp((now - last).TotalSeconds, 0, 0.034) : 1.0 / 60;
        lastFrame = now;
        var moving = false;
        foreach (var view in views.Values)
        {
            if (!ReferenceEquals(view, dragging)) moving |= view.Slide.Step(dt);
            moving |= view.Appear.Step(dt);
            view.ApplyMotion();
        }
        if (moving) RequestFrame();
        else lastFrame = null;
    }

    // Dragging a tab along the strip (DESIGN 2.6: 1:1 under the pointer, the others spring aside).
    internal void BeginDrag(VisusTabView view, double pointerX)
    {
        dragging = view;
        view.Slide.Snap(view.Slide.Value);
        dragPointerOffset = pointerX - (lastX.TryGetValue(view, out var x) ? x : 0) - view.Slide.Value;
        view.ZIndex = 1;
    }

    internal void Drag(VisusTabView view, double pointerX)
    {
        if (!ReferenceEquals(view, dragging) || !lastX.TryGetValue(view, out var slot)) return;
        var left = pointerX - dragPointerOffset;
        view.Slide.Snap(left - slot);
        view.ApplyMotion();
        var centre = left + view.Bounds.Width / 2;
        var tabs = session.Tabs.ToList();
        var from = tabs.IndexOf(view.Tab);
        var to = from;
        foreach (var other in tabs)
        {
            if (ReferenceEquals(other, view.Tab) || !views.TryGetValue(other, out var otherView) || !lastX.TryGetValue(otherView, out var ox)) continue;
            var index = tabs.IndexOf(other);
            var otherCentre = ox + otherView.Bounds.Width / 2;
            if (index > from && centre > otherCentre) to = Math.Max(to, index);
            if (index < from && centre < otherCentre) to = Math.Min(to, index);
        }
        if (to != from) session.Move(view.Tab, to);
    }

    internal void EndDrag(VisusTabView view)
    {
        if (!ReferenceEquals(view, dragging)) return;
        dragging = null;
        view.ZIndex = 0;
        view.Slide.SetTarget(0, ReduceMotion);
        RequestFrame();
    }

    internal void Choose(VisusTab tab) => session.Select(tab);

    internal void CloseTab(VisusTab tab) => session.Close(tab);

    /// <summary>Arrow keys move focus along the tabs (a roving tab list).</summary>
    internal void FocusNeighbour(VisusTabView view, int step)
    {
        var shown = TabViews.Where(v => v.IsVisible).ToList();
        var at = shown.IndexOf(view);
        if (at < 0 || shown.Count == 0) return;
        shown[(at + step + shown.Count) % shown.Count].Focus(NavigationMethod.Directional);
    }

    internal ContextMenu MenuFor(VisusTab tab)
    {
        var menu = new ContextMenu();
        menu.Opening += (_, _) =>
        {
            var items = new List<Control>
            {
                Item(UiText.Get("visus.tabs.new_tab_right"), () => session.NewTab(VisusTabKind.Browse, at: session.Tabs.ToList().IndexOf(tab) + 1)),
                new Separator(),
                Item(UiText.Get("visus.toolbar.reload"), () => tab.Reload(), enabled: !tab.IsBlank),
                Item(UiText.Get("visus.tabs.duplicate"), () => session.Duplicate(tab), enabled: !tab.IsBlank),
                Item(UiText.Get("visus.menu.copy_link"), () => VisusPaneBody.CopyText(this, tab.Url), enabled: WebPolicy.IsWeb(tab.Url)),
                new Separator(),
                Item(UiText.Get("visus.tabs.close_tab"), () => session.Close(tab)),
                Item(UiText.Get("visus.tabs.close_others"), () => session.CloseOthers(tab), enabled: session.Tabs.Count > 1),
                Item(UiText.Get("visus.tabs.reopen_closed"), () => session.ReopenClosed(), enabled: session.CanReopenClosed),
            };
            menu.ItemsSource = items;
        };
        return menu;
    }

    private ContextMenu NewTabMenu()
    {
        var menu = new ContextMenu();
        menu.Opening += (_, _) => menu.ItemsSource = new List<Control>
        {
            Item(UiText.Get("visus.tabs.new_tab"), () => session.NewTab()),
            Item(UiText.Get("visus.tabs.new_private_tab"), () => session.NewTab(VisusTabKind.Private)),
        };
        return menu;
    }

    private static MenuItem Item(string header, Action run, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => run();
        return item;
    }

    private Control BuildOverflowMenu()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var tab in session.Tabs)
        {
            var chosen = tab;
            stack.Children.Add(new PopoverItem(tab.Title, () =>
            {
                SetOverflowOpen(false);
                session.Select(chosen);
            }, detail: tab.Kind == VisusTabKind.Private ? UiText.Get("visus.tabs.private_label") : null, isChecked: ReferenceEquals(tab, session.Selected)));
        }
        return stack;
    }

    private void SetOverflowOpen(bool open) => overflowMenu.SetOpen(open && layout.ShowsOverflow);

    private Button StripButton(PaneGlyphKind kind, string name, string? shortcut, Action invoke)
    {
        var glyph = new PaneGlyph(kind) { Foreground = PaneTokens.Text2.Brush(this) };
        var face = new Border
        {
            Width = PaneTokens.ButtonSize,
            Height = PaneTokens.ButtonSize,
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Child = glyph,
        };
        var button = PlainControls.Button(face, invoke).KeyboardFocus(8);
        button.OnHover(on =>
        {
            face.Background = on ? PaneTokens.HoverFill.Brush(this) : Brushes.Transparent;
            glyph.Foreground = (on ? PaneTokens.Text1 : PaneTokens.Text2).Brush(this);
        });
        ToolTip.SetTip(button, shortcut is null ? name : UiText.Format("visus.toolbar.tip_with_shortcut", name, shortcut));
        AutomationProperties.SetName(button, name);
        if (shortcut is not null) AutomationProperties.SetAcceleratorKey(button, shortcut);
        return button;
    }
}

/// <summary>One tab of the strip: its favicon or glyph, its title, and its close button.</summary>
public sealed class VisusTabView : Button
{
    private readonly VisusTabStrip strip;
    private readonly Border face;
    private readonly Border icon = new() { Width = VisusTokens.FaviconSize, Height = VisusTokens.FaviconSize, CornerRadius = new CornerRadius(4), ClipToBounds = true };
    private readonly VisusSpinner spinner = new() { Width = VisusTokens.FaviconSize, Height = VisusTokens.FaviconSize, IsVisible = false };
    // W8b: a tab off screen that waits on a question (a download, a permission, a dialog) shows a small dot on its icon.
    private readonly Avalonia.Controls.Shapes.Ellipse waitingDot = new()
    {
        Width = 6,
        Height = 6,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, -2, -3, 0),
        IsVisible = false,
        IsHitTestVisible = false,
    };
    private readonly TextBlock title = new()
    {
        FontSize = VisusTokens.TabFontSize,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Button close;
    private readonly PaneGlyph closeGlyph = new(PaneGlyphKind.Close, 10);
    private readonly TranslateTransform offset = new();
    private bool chosen;
    private bool hovered;
    private bool pressed;
    private bool dragged;
    private Point pressAt;
    private byte[]? shownFavicon;

    internal VisusTabView(VisusTabStrip strip, VisusTab tab)
    {
        this.strip = strip;
        Tab = tab;
        Padding = default;
        Background = Brushes.Transparent;
        BorderThickness = default;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        ClickMode = ClickMode.Press;
        Focusable = true;
        RenderTransform = offset;
        PlainControls.KeyboardFocus(this, 9);
        var closeFace = new Border { Width = VisusTokens.TabCloseSize, Height = VisusTokens.TabCloseSize, CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Child = closeGlyph };
        close = PlainControls.Button(closeFace, () => strip.CloseTab(Tab));
        close.Focusable = false; // Ctrl+W and Delete close the focused tab; the x is for the pointer
        close.Opacity = 0;
        close.OnHover(on =>
        {
            closeFace.Background = on ? PaneTokens.HoverFill.Brush(this) : Brushes.Transparent;
            close.Opacity = on ? 1 : CloseOpacity;
        });
        ToolTip.SetTip(close, UiText.Format("visus.toolbar.tip_with_shortcut", UiText.Get("visus.tabs.close_tab"), "Ctrl+W"));
        AutomationProperties.SetName(close, UiText.Get("visus.tabs.close_tab"));
        var leading = new Panel { Width = VisusTokens.FaviconSize, Height = VisusTokens.FaviconSize, Children = { icon, spinner, waitingDot } };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(leading, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        leading.Margin = new Thickness(0, 0, 7, 0);
        close.Margin = new Thickness(4, 0, 0, 0);
        leading.VerticalAlignment = VerticalAlignment.Center;
        close.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(leading);
        row.Children.Add(close);
        row.Children.Add(title);
        face = new Border
        {
            Height = VisusTokens.TabHeight,
            Padding = new Thickness(9, 0, 6, 0),
            CornerRadius = new CornerRadius(9),
            Child = row,
        };
        Content = face;
        ContextMenu = strip.MenuFor(tab);
        SetValue(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.TabItem);
        PointerEntered += (_, _) => SetHovered(true);
        PointerExited += (_, _) => SetHovered(false);
        AddHandler(PointerPressedEvent, OnPress, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnMove, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnRelease, RoutingStrategies.Tunnel);
        PointerCaptureLost += (_, _) => EndPress();
        ActualThemeVariantChanged += (_, _) => ApplyLook();
        Apply();
    }

    public VisusTab Tab { get; }

    /// <summary>The tab's offset from its slot (a render transform: the reflow and the drag).</summary>
    internal SplitterSpring Slide { get; } = new(0, epsilon: 0.1);

    /// <summary>The tab's opacity as it appears.</summary>
    internal SplitterSpring Appear { get; } = new(1, epsilon: 0.002);

    /// <summary>The close button (shown on hover and on the chosen tab).</summary>
    public Button CloseButton => close;

    public TextBlock TitleBlock => title;

    /// <summary>The width the tab asks for: its content, at most 200.</summary>
    public double NaturalWidth
    {
        get
        {
            face.Measure(Size.Infinity);
            return Math.Min(VisusTokens.TabMaxWidth, Math.Ceiling(face.DesiredSize.Width));
        }
    }

    public bool IsChosen
    {
        get => chosen;
        set
        {
            if (chosen == value) return;
            chosen = value;
            ApplyLook();
        }
    }

    private double CloseOpacity => chosen || hovered ? VisusTokens.TabCloseOpacity : 0;

    internal void Detach() => Tab.Changed -= Apply;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Tab.Changed -= Apply;
        Tab.Changed += Apply;
        Apply();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Tab.Changed -= Apply;
        base.OnDetachedFromVisualTree(e);
    }

    internal void ApplyMotion()
    {
        offset.X = Math.Round(Slide.Value * 2) / 2;
        Opacity = Appear.Value;
    }

    protected override void OnClick()
    {
        // Press mode: choosing happens on the press (OnPress), like a browser's tab; the click does nothing more.
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Handled) return;
        switch (e.Key)
        {
            case Key.Enter or Key.Space when e.KeyModifiers == KeyModifiers.None:
                strip.Choose(Tab);
                e.Handled = true;
                return;
            case Key.Left when e.KeyModifiers == KeyModifiers.None:
                strip.FocusNeighbour(this, -1);
                e.Handled = true;
                return;
            case Key.Right when e.KeyModifiers == KeyModifiers.None:
                strip.FocusNeighbour(this, 1);
                e.Handled = true;
                return;
            case Key.Delete when e.KeyModifiers == KeyModifiers.None:
                strip.CloseTab(Tab);
                e.Handled = true;
                return;
        }
        base.OnKeyDown(e);
    }

    private void OnPress(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsMiddleButtonPressed)
        {
            strip.CloseTab(Tab); // a middle click closes, as in a browser
            e.Handled = true;
            return;
        }
        if (!point.Properties.IsLeftButtonPressed || IsOnClose(e.Source)) return;
        strip.Choose(Tab);
        pressed = true;
        dragged = false;
        pressAt = e.GetPosition(strip);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnMove(object? sender, PointerEventArgs e)
    {
        if (!pressed) return;
        var at = e.GetPosition(strip);
        if (!dragged)
        {
            if (Math.Abs(at.X - pressAt.X) < VisusTabStrip.DragThreshold) return;
            dragged = true;
            strip.BeginDrag(this, pressAt.X);
        }
        strip.Drag(this, at.X);
        e.Handled = true;
    }

    private void OnRelease(object? sender, PointerReleasedEventArgs e)
    {
        if (!pressed) return;
        e.Pointer.Capture(null);
        EndPress();
        e.Handled = true;
    }

    private void EndPress()
    {
        if (!pressed) return;
        pressed = false;
        if (dragged) strip.EndDrag(this);
        dragged = false;
    }

    private bool IsOnClose(object? source)
    {
        for (var at = source as Visual; at is not null && !ReferenceEquals(at, this); at = at.GetVisualParent())
            if (ReferenceEquals(at, close)) return true;
        return false;
    }

    private void SetHovered(bool value)
    {
        hovered = value;
        close.Opacity = CloseOpacity;
    }

    private void Apply()
    {
        title.Inlines ??= new InlineCollection();
        title.Inlines.Clear();
        title.Inlines.Add(new Run(Tab.Title));
        // The canvas's preview tab: "Orbit · localhost:5173", the host in the dimmer --t3.
        var detail = Tab.Chip.Kind == SiteChipKind.Preview && !string.IsNullOrWhiteSpace(Tab.PageTitle) ? Tab.Source : null;
        if (detail is not null) title.Inlines.Add(new Run(" · " + detail) { Foreground = PaneTokens.Text3.Brush(this) });
        spinner.IsVisible = Tab.IsLoading;
        icon.IsVisible = !Tab.IsLoading;
        if (!ReferenceEquals(shownFavicon, Tab.Favicon) || icon.Child is null)
        {
            shownFavicon = Tab.Favicon;
            icon.Child = IconFor(Tab);
        }
        waitingDot.IsVisible = Tab.Asks.Count > 0 && !Tab.IsShown;
        waitingDot.Fill = Marrow.UI.Chrome.Chrome.Accent.Brush(this);
        var name = Tab.Title;
        if (Tab.Kind == VisusTabKind.Private) name = UiText.Format("visus.tabs.name_with_state", name, UiText.Get("visus.tabs.state_private"));
        if (Tab.IsLoading) name = UiText.Format("visus.tabs.name_with_state", name, UiText.Get("visus.tabs.state_loading"));
        if (waitingDot.IsVisible) name = UiText.Format("visus.tabs.name_with_state", name, UiText.Get("visus.tabs.state_waiting"));
        AutomationProperties.SetName(this, name);
        ToolTip.SetTip(this, Tab.IsBlank ? Tab.Title : Tab.Title + "\n" + VisusAddress.Display(Tab.Url));
        ApplyLook();
        InvalidateMeasure();
        strip.InvalidateMeasure();
    }

    /// <summary>The tab is off screen and waits on a question (the dot on its icon).</summary>
    public bool ShowsWaiting => waitingDot.IsVisible;

    private Control IconFor(VisusTab tab)
    {
        if (tab.Favicon is { Length: > 0 } png)
        {
            try
            {
                using var stream = new MemoryStream(png);
                var bitmap = new Bitmap(stream);
                var image = new Image { Source = bitmap, Width = VisusTokens.FaviconSize, Height = VisusTokens.FaviconSize, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
                return image;
            }
            catch (Exception)
            {
                // a page's favicon that does not decode shows the glyph
            }
        }
        var kind = tab.Kind == VisusTabKind.Private ? PaneGlyphKind.Mask : PaneGlyphKind.Browser;
        return new PaneGlyph(kind, 13) { Foreground = PaneTokens.Text2.Brush(this), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    private void ApplyLook()
    {
        face.Background = chosen
            ? (Tab.Kind == VisusTabKind.Private ? VisusTokens.TabChosenPrivate : VisusTokens.TabChosen).Brush(this)
            : Brushes.Transparent;
        title.Foreground = (chosen ? PaneTokens.Text1 : PaneTokens.Text2).Brush(this);
        closeGlyph.Foreground = PaneTokens.Text1.Brush(this);
        spinner.Foreground = PaneTokens.Text2.Brush(this);
        close.Opacity = CloseOpacity;
        if (icon.Child is PaneGlyph glyph) glyph.Foreground = PaneTokens.Text2.Brush(this);
    }
}

/// <summary>The favicon slot while a page loads: an arc turning by a render transform (still under Reduce Motion).</summary>
public sealed class VisusSpinner : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<VisusSpinner>();

    private readonly RotateTransform turn = new();
    private Action<TimeSpan>? frame;
    private TimeSpan? start;

    static VisusSpinner()
    {
        AffectsRender<VisusSpinner>(ForegroundProperty);
    }

    public VisusSpinner()
    {
        RenderTransform = turn;
        RenderTransformOrigin = RelativePoint.Center;
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The arc's angle now (tests read that it turns).</summary>
    public double Angle => turn.Angle;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty) Schedule();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Schedule();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (frame is not null) FrameClock.Source.Current.CancelFrame(frame);
        frame = null;
        start = null;
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } brush) return;
        var r = Math.Min(Bounds.Width, Bounds.Height) / 2 - 1.5;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(c.X, c.Y - r), false);
            g.ArcTo(new Point(c.X + r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(brush, 1.6, lineCap: PenLineCap.Round), geometry);
    }

    private void Schedule()
    {
        var reduce = EnvironmentValues.GetAccessibilityReduceMotion(this) || Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;
        if (!IsVisible || VisualRoot is null || reduce || !FrameClock.IsBound || frame is not null) return;
        frame = OnFrame;
        FrameClock.Source.Current.RequestFrame(frame);
    }

    private void OnFrame(TimeSpan now)
    {
        frame = null;
        start ??= now;
        turn.Angle = (now - start.Value).TotalSeconds * 360 / 0.9 % 360; // one turn in 0.9 s
        Schedule();
    }
}
