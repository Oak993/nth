// PORT-ONLY: web surfaces W3a, a workspace pane's header (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.2 "Header
// overflow", 3.4 "Each header has Expand ... Header shows Restore ... A small 'Chat is working' chip appears in the expanded
// header while a turn runs; clicking it restores", 3.12 "Header (36 DIP): kind glyph, title, optional title menu (version,
// tabs), then trailing actions, then Expand and Close"; 13.3 "Header overflow"; ED-NEW-web-w3a-1). No Swift counterpart.
// The look is the approved canvas's (index.html .ph, .hb, .tabg, .pchip, .hmore). Its fit is measured, never guessed:
// after a layout it measures its parts and PaneHeaderFit decides which stay in the row; while the workspace holds it
// (a spring or a drag is running) the set does not change, so buttons never flicker, and items that do not fit for the
// moment are clipped at the title's minimum instead of overlapping it.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.UI.Panes;

/// <summary>One of a pane's own header actions (Tabula's version chip, Copy, Save, Export; later units add theirs).</summary>
/// <param name="Name">The accessible name and the More menu's label ("Copy").</param>
/// <param name="Invoke">What it does.</param>
/// <param name="Glyph">A square glyph button, or null for a text chip.</param>
/// <param name="Text">A text chip's label ("v3"); drawn with a small chevron when <paramref name="Menu"/> is set.</param>
/// <param name="Shortcut">The chord shown in the More menu and the tooltip ("Ctrl+Shift+C"), if any.</param>
/// <param name="Menu">The action opens a menu (a chevron follows the text).</param>
public sealed record PaneHeaderAction(string Name, Action Invoke, PaneGlyphKind? Glyph = null, string? Text = null, string? Shortcut = null, bool Menu = false);

/// <summary>A tab of a cell that holds several panes.</summary>
public sealed record PaneHeaderTab(PaneKind Kind, string Name, bool IsChosen);

/// <summary>The header row of a workspace pane.</summary>
public sealed class PaneHeader : Panel
{
    /// <summary>The working chip's words (DESIGN 3.4).</summary>
    public const string WorkingText = "Chat is working";

    private readonly PaneGlyph glyph;
    private readonly TextBlock title = new()
    {
        FontSize = 12.5,
        FontWeight = FontWeight.SemiBold,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly StackPanel tabs = new() { Orientation = Orientation.Horizontal, Spacing = 3, ClipToBounds = true };
    private readonly Trailing trailing;
    private readonly Button chip;
    private readonly Button more;
    private readonly Button expand;
    private readonly Button close;
    private readonly PaneGlyph expandGlyph = new(PaneGlyphKind.Expand);
    private readonly Popup moreMenu;
    private readonly Border hairline = new() { Height = 0.5, IsHitTestVisible = false }; // canvas .ph: border-bottom .5px var(--line)
    private readonly List<Button> actionViews = [];
    private IReadOnlyList<PaneHeaderAction> actions = [];
    private IReadOnlyList<PaneHeaderTab> tabList = [];
    private string titleText = "";
    private string? subtitleText;
    private bool expanded;
    private bool canExpand = true;
    private bool working;
    private bool held;
    private bool dirty = true;
    private double fitWidth = double.NaN;
    private bool menuOpen;
    private bool canDrag;
    private bool pressed;
    private bool lifted;
    private Point pressPoint;
    private IPointer? pressPointer;

    public PaneHeader(PaneKind kind)
    {
        Kind = kind;
        Height = PaneTokens.HeaderHeight;
        ClipToBounds = true;
        Background = Brushes.Transparent; // the header is where a drag to re-dock starts (W3b); it takes the pointer
        glyph = new PaneGlyph(PaneGlyph.For(kind), PaneTokens.GlyphSize) { Foreground = PaneTokens.Text2.Brush(this) };
        title.Foreground = PaneTokens.Text1.Brush(this);
        chip = MakeChip();
        more = MakeButton(new PaneGlyph(PaneGlyphKind.More), "More actions", () => SetMenuOpen(!menuOpen));
        expand = MakeButton(expandGlyph, "Expand", () => ExpandRequested?.Invoke());
        close = MakeButton(new PaneGlyph(PaneGlyphKind.Close), "Close", () => CloseRequested?.Invoke());
        moreMenu = PlainControls.Popover(more, new Binding<bool>(() => menuOpen, SetMenuOpen), () => new PopoverMenu(BuildMenu));
        trailing = new Trailing(this) { ClipToBounds = true };
        Children.Add(glyph);
        Children.Add(title);
        Children.Add(tabs);
        Children.Add(trailing);
        trailing.Children.Add(chip);
        trailing.Children.Add(more);
        trailing.Children.Add(expand);
        trailing.Children.Add(close);
        Children.Add(moreMenu);
        hairline.Background = PaneTokens.Line.Brush(this);
        Children.Add(hairline);
        AutomationProperties.SetName(this, PaneKinds.AccessibleName(kind));
        Rebuild();
        // The header is where a drag to re-dock starts (DESIGN 3.6): a press on its empty area, or on the chosen tab, that
        // moves 4 DIP lifts the pane. Buttons keep their clicks; a press that never moves 4 DIP is nothing.
        AddHandler(PointerPressedEvent, OnDragPress, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnDragMove, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnDragRelease, RoutingStrategies.Bubble, handledEventsToo: true);
        PointerCaptureLost += OnDragCaptureLost;
    }

    public PaneKind Kind { get; }

    /// <summary>Close (the header's x).</summary>
    public event Action? CloseRequested;

    /// <summary>Expand, or Restore when expanded.</summary>
    public event Action? ExpandRequested;

    /// <summary>A tab of the cell was chosen.</summary>
    public event Action<PaneKind>? TabChosen;

    /// <summary>The working chip was clicked (it restores).</summary>
    public event Action? WorkingChipClicked;

    /// <summary>The pane was lifted by its header (the pointer moved 4 DIP with the button down).</summary>
    public event Action<PointerEventArgs>? DragLifted;

    /// <summary>The lifted pane follows the pointer.</summary>
    public event Action<PointerEventArgs>? DragMoved;

    /// <summary>The button was released over the workspace.</summary>
    public event Action<PointerEventArgs>? DragDropped;

    /// <summary>The drag ended without a release (the capture was taken away, or <see cref="CancelDrag"/>).</summary>
    public event Action? DragCanceled;

    /// <summary>Whether a press on the header may lift its pane (the workspace says: tiled, not expanded, a grid pane).</summary>
    public bool CanDrag
    {
        get => canDrag;
        set => canDrag = value;
    }

    /// <summary>The pane is lifted and follows the pointer now.</summary>
    public bool IsLifted => lifted;

    /// <summary>Ends a drag in progress without a drop (Esc, a closed pane); the pointer capture is released.</summary>
    public void CancelDrag()
    {
        var was = lifted;
        lifted = false;
        pressed = false;
        Cursor = null;
        var pointer = pressPointer;
        pressPointer = null;
        if (pointer?.Captured == this) pointer.Capture(null);
        if (was) DragCanceled?.Invoke();
    }

    /// <summary>The fit in force: which parts stay in the row.</summary>
    public PaneHeaderFit Fit { get; private set; }

    /// <summary>The width the fit was measured at.</summary>
    public double FitWidth => fitWidth;

    /// <summary>The actions that are in the More menu now, in their order (Expand or Restore last when it is there).</summary>
    public IReadOnlyList<string> MenuItems
    {
        get
        {
            var items = actions.Skip(Fit.VisibleActions).Select(action => action.Name).ToList();
            if (canExpand && !Fit.ShowsExpand) items.Add(expanded ? "Restore" : "Expand");
            return items;
        }
    }

    /// <summary>The action buttons in the row (all of them; the hidden ones are not visible).</summary>
    public IReadOnlyList<Button> ActionViews => actionViews;

    public Button CloseButton => close;

    public Button ExpandButton => expand;

    public Button MoreButton => more;

    public Button WorkingChip => chip;

    public TextBlock TitleBlock => title;

    public StackPanel TabStrip => tabs;

    public bool IsMenuOpen => menuOpen;

    /// <summary>The title, and its detail after a middle dot ("Release notes · Markdown").</summary>
    public void SetTitle(string text, string? subtitle = null)
    {
        if (titleText == text && subtitleText == subtitle) return;
        titleText = text;
        subtitleText = subtitle;
        RebuildTitle();
        Invalidate();
    }

    public void SetActions(IReadOnlyList<PaneHeaderAction> value)
    {
        actions = value ?? [];
        Rebuild();
    }

    /// <summary>The cell's tabs when it holds several panes; the tab strip then stands where the title was.</summary>
    public void SetTabs(IReadOnlyList<PaneHeaderTab> value)
    {
        value ??= [];
        if (tabList.SequenceEqual(value)) return;
        tabList = value;
        RebuildTabs();
        Invalidate();
    }

    public bool IsExpanded
    {
        get => expanded;
        set
        {
            if (expanded == value) return;
            expanded = value;
            expandGlyph.Kind = value ? PaneGlyphKind.Restore : PaneGlyphKind.Expand;
            var name = value ? "Restore" : "Expand";
            ToolTip.SetTip(expand, name);
            AutomationProperties.SetName(expand, name);
            Invalidate();
        }
    }

    public bool CanExpand
    {
        get => canExpand;
        set
        {
            if (canExpand == value) return;
            canExpand = value;
            Invalidate();
        }
    }

    /// <summary>Shows the "Chat is working" chip (only meaningful while expanded).</summary>
    public bool ShowsWorking
    {
        get => working;
        set
        {
            if (working == value) return;
            working = value;
            Invalidate();
        }
    }

    /// <summary>
    /// While held (a spring or a drag is running) the set of parts in the row does not change; releasing measures again.
    /// </summary>
    public bool IsHeld
    {
        get => held;
        set
        {
            if (held == value) return;
            held = value;
            if (!value)
            {
                dirty = true;
                InvalidateArrange();
            }
        }
    }

    /// <summary>Measures the parts at the current width and fits them now (the workspace calls it at settle).</summary>
    public void Refit()
    {
        dirty = true;
        if (Bounds.Width > 0) ApplyFit(Bounds.Width);
        InvalidateArrange();
    }

    private void Invalidate()
    {
        dirty = true;
        InvalidateMeasure();
    }

    private bool HasTabs => tabList.Count > 1;

    private Control TitlePart => HasTabs ? tabs : title;

    // The parts as measured now. A hidden control measures to nothing, so each button's natural width is its face's,
    // measured on its own whether the button shows or not.
    private PaneHeaderParts Parts(double width)
    {
        foreach (var child in new Control[] { glyph, TitlePart }) child.Measure(Size.Infinity);
        var fixedWidth = PaneTokens.HeaderLeading + PaneTokens.GlyphSize + PaneTokens.HeaderGap + PaneTokens.HeaderTrailing;
        return new PaneHeaderParts(
            width, fixedWidth, PaneTokens.HeaderGap, PaneTokens.ButtonSize, TitlePart.DesiredSize.Width,
            actionViews.Select(NaturalWidth).ToArray(),
            expanded && working ? NaturalWidth(chip) : 0,
            canExpand);
    }

    private static double NaturalWidth(Button button)
    {
        if (button.Content is not Control face) return button.DesiredSize.Width;
        face.Measure(Size.Infinity);
        return face.DesiredSize.Width + face.Margin.Left + face.Margin.Right;
    }

    private void ApplyFit(double width)
    {
        Fit = PaneHeaderFit.Solve(Parts(width));
        fitWidth = width;
        dirty = false;
        for (var i = 0; i < actionViews.Count; i++) actionViews[i].IsVisible = i < Fit.VisibleActions;
        more.IsVisible = Fit.ShowsMore;
        expand.IsVisible = canExpand && Fit.ShowsExpand;
        chip.IsVisible = Fit.ShowsChip;
        if (!Fit.ShowsMore && menuOpen) SetMenuOpen(false);
        foreach (var item in TrailingItems()) item.Measure(Size.Infinity); // what just appeared has its size now
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(Size.Infinity);
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 0;
        return new Size(width, PaneTokens.HeaderHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = finalSize.Width;
        var height = finalSize.Height;
        if (double.IsNaN(fitWidth) || (!held && (dirty || Math.Abs(width - fitWidth) > 0.25))) ApplyFit(width);
        var x = PaneTokens.HeaderLeading;
        glyph.Arrange(Centred(x, PaneTokens.GlyphSize, PaneTokens.GlyphSize, height));
        x += PaneTokens.GlyphSize + PaneTokens.HeaderGap;
        var titleMinimum = Math.Min(TitlePart.DesiredSize.Width, PaneHeaderParts.TitleMinimum);
        var row = TrailingWidth();
        // The trailing row's width counts a gap before each item, so its first gap is the one after the title.
        var trailingX = Math.Max(width - PaneTokens.HeaderTrailing - row, x + titleMinimum);
        var trailingWidth = Math.Max(0, width - PaneTokens.HeaderTrailing - trailingX);
        trailing.Arrange(new Rect(trailingX, 0, trailingWidth, height));
        var titleWidth = Math.Max(0, Math.Min(TitlePart.DesiredSize.Width, trailingX - x));
        TitlePart.Arrange(Centred(x, titleWidth, TitlePart.DesiredSize.Height, height));
        var other = HasTabs ? (Control)title : tabs;
        other.Arrange(default);
        moreMenu.Arrange(default);
        hairline.Arrange(new Rect(0, height - 0.5, width, 0.5));
        return finalSize;
    }

    // The visible trailing items' width: each is a gap and its own width (PaneHeaderFit.Trailing on what is shown).
    private double TrailingWidth()
    {
        var width = 0.0;
        foreach (var item in TrailingItems()) width += PaneTokens.HeaderGap + item.DesiredSize.Width;
        return width;
    }

    private IEnumerable<Control> TrailingItems()
    {
        if (chip.IsVisible) yield return chip;
        foreach (var view in actionViews)
            if (view.IsVisible) yield return view;
        if (more.IsVisible) yield return more;
        if (expand.IsVisible) yield return expand;
        yield return close;
    }

    private static Rect Centred(double x, double width, double itemHeight, double rowHeight) =>
        new(x, Math.Round((rowHeight - itemHeight) / 2), width, itemHeight);

    private void Rebuild()
    {
        foreach (var view in actionViews) trailing.Children.Remove(view);
        actionViews.Clear();
        var at = trailing.Children.IndexOf(more);
        foreach (var action in actions)
        {
            var view = action.Glyph is { } kind
                ? MakeButton(new PaneGlyph(kind), action.Name, action.Invoke, action.Shortcut)
                : MakeTextButton(action);
            actionViews.Add(view);
            trailing.Children.Insert(at++, view);
        }
        RebuildTitle();
        RebuildTabs();
        Invalidate();
    }

    private void RebuildTitle()
    {
        title.Inlines ??= new Avalonia.Controls.Documents.InlineCollection();
        title.Inlines.Clear();
        title.Inlines.Add(new Avalonia.Controls.Documents.Run(titleText));
        if (!string.IsNullOrEmpty(subtitleText))
            title.Inlines.Add(new Avalonia.Controls.Documents.Run(" · " + subtitleText) { FontWeight = FontWeight.Normal, Foreground = PaneTokens.Text3.Brush(this) });
        AutomationProperties.SetName(title, string.IsNullOrEmpty(subtitleText) ? titleText : titleText + ", " + subtitleText);
    }

    private void RebuildTabs()
    {
        tabs.Children.Clear();
        tabs.IsVisible = HasTabs;
        title.IsVisible = !HasTabs;
        if (!HasTabs) return;
        foreach (var tab in tabList)
        {
            var text = new TextBlock
            {
                Text = tab.Name,
                FontSize = 11.5,
                FontWeight = FontWeight.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (tab.IsChosen ? PaneTokens.Text1 : PaneTokens.Text3).Brush(this),
            };
            var pill = new Border
            {
                Height = 24,
                Padding = new Thickness(9, 0),
                CornerRadius = new CornerRadius(12),
                Background = tab.IsChosen ? PaneTokens.HoverFill.Brush(this) : Brushes.Transparent,
                Child = text,
            };
            var kind = tab.Kind;
            var button = PlainControls.Button(pill, () => TabChosen?.Invoke(kind)).KeyboardFocus(12);
            AutomationProperties.SetName(button, tab.Name);
            button.SetValue(AutomationProperties.ControlTypeOverrideProperty, Avalonia.Automation.Peers.AutomationControlType.TabItem);
            button.Classes.Set("chosen", tab.IsChosen);
            tabs.Children.Add(button);
        }
    }

    private Button MakeButton(PaneGlyph content, string name, Action invoke, string? shortcut = null)
    {
        content.Foreground = PaneTokens.Text2.Brush(this);
        var face = new Border
        {
            Width = PaneTokens.ButtonSize,
            Height = PaneTokens.ButtonSize,
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Child = content,
        };
        var button = PlainControls.Button(face, invoke).KeyboardFocus(8);
        Hover(button, face, content);
        ToolTip.SetTip(button, shortcut is null ? name : name + "  " + shortcut);
        AutomationProperties.SetName(button, name);
        if (shortcut is not null) AutomationProperties.SetAcceleratorKey(button, shortcut);
        return button;
    }

    private Button MakeTextButton(PaneHeaderAction action)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = action.Text ?? action.Name, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Foreground = PaneTokens.Text2.Brush(this) };
        row.Children.Add(label);
        PaneGlyph? chevron = null;
        if (action.Menu)
        {
            chevron = new PaneGlyph(PaneGlyphKind.Chevron, 11) { Foreground = PaneTokens.Text2.Brush(this) };
            row.Children.Add(chevron);
        }
        var face = new Border
        {
            Height = PaneTokens.ButtonSize,
            Padding = new Thickness(7, 0),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Child = row,
        };
        var button = PlainControls.Button(face, action.Invoke).KeyboardFocus(8);
        button.OnHover(on =>
        {
            face.Background = on ? PaneTokens.HoverFill.Brush(this) : Brushes.Transparent;
            label.Foreground = (on ? PaneTokens.Text1 : PaneTokens.Text2).Brush(this);
            if (chevron is not null) chevron.Foreground = label.Foreground;
        });
        ToolTip.SetTip(button, action.Shortcut is null ? action.Name : action.Name + "  " + action.Shortcut);
        AutomationProperties.SetName(button, action.Name);
        return button;
    }

    private Button MakeChip()
    {
        var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = PaneTokens.ChipDot.Brush(this), VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock { Text = WorkingText, FontSize = 11, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center, Foreground = PaneTokens.Text1.Brush(this) };
        var face = new Border
        {
            Height = 22,
            Padding = new Thickness(9, 0),
            CornerRadius = new CornerRadius(11),
            Background = PaneTokens.ChipFill.Brush(this),
            BorderBrush = PaneTokens.ChipRim.Brush(this),
            BorderThickness = new Thickness(0.5),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { dot, text } },
        };
        var button = PlainControls.Button(face, () => WorkingChipClicked?.Invoke()).KeyboardFocus(11);
        ToolTip.SetTip(button, "Restore");
        AutomationProperties.SetName(button, WorkingText + ", restore");
        button.IsVisible = false;
        return button;
    }

    private void Hover(Button button, Border face, PaneGlyph content) => button.OnHover(on =>
    {
        face.Background = on ? PaneTokens.HoverFill.Brush(this) : Brushes.Transparent;
        content.Foreground = (on ? PaneTokens.Text1 : PaneTokens.Text2).Brush(this);
    });

    private void SetMenuOpen(bool open)
    {
        if (menuOpen == open) return;
        menuOpen = open && Fit.ShowsMore;
        moreMenu.IsOpen = menuOpen;
    }

    /// <summary>The More menu: the actions that left the row, in their order, with their shortcuts; then Expand or Restore if it left too.</summary>
    internal Control BuildMenu()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        foreach (var action in actions.Skip(Fit.VisibleActions))
        {
            var run = action.Invoke;
            stack.Children.Add(new PopoverItem(action.Name, () => { SetMenuOpen(false); run(); }, detail: action.Shortcut));
        }
        if (canExpand && !Fit.ShowsExpand)
            stack.Children.Add(new PopoverItem(expanded ? "Restore" : "Expand", () => { SetMenuOpen(false); ExpandRequested?.Invoke(); }));
        return stack;
    }

    // A press on a button keeps its click, except on the chosen tab of a group, which lifts the pane like the empty area.
    private static bool IsDraggableSource(Visual? source, PaneHeader header, out bool onButton)
    {
        onButton = false;
        for (var at = source; at is not null && !ReferenceEquals(at, header); at = at.GetVisualParent())
        {
            if (at is not Button button) continue;
            onButton = true;
            return button.Classes.Contains("chosen");
        }
        return true;
    }

    private void OnDragPress(object? sender, PointerPressedEventArgs e)
    {
        if (!canDrag || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.ClickCount != 1) return;
        pressed = false;
        if (!IsDraggableSource(e.Source as Visual, this, out var onButton)) return;
        pressed = true;
        lifted = false;
        pressPoint = e.GetPosition(this);
        pressPointer = e.Pointer;
        if (!onButton) e.Pointer.Capture(this); // moves over the empty area come here until the button is released
    }

    private void OnDragMove(object? sender, PointerEventArgs e)
    {
        if (!pressed || !ReferenceEquals(e.Pointer, pressPointer)) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            pressed = false; // the release was missed (a lost capture, a deactivated window)
            return;
        }
        if (!lifted)
        {
            var travel = e.GetPosition(this) - pressPoint;
            if (Math.Sqrt(travel.X * travel.X + travel.Y * travel.Y) < PaneRedock.LiftDistance) return;
            lifted = true;
            e.Pointer.Capture(this); // a pressed tab gives its capture up: no click follows
            Cursor = new Cursor(StandardCursorType.SizeAll);
            DragLifted?.Invoke(e);
            if (!lifted) return;
        }
        DragMoved?.Invoke(e);
    }

    private void OnDragRelease(object? sender, PointerReleasedEventArgs e)
    {
        if (!pressed) return;
        pressed = false;
        var was = lifted;
        lifted = false;
        Cursor = null;
        var pointer = pressPointer;
        pressPointer = null;
        if (pointer?.Captured == this) pointer.Capture(null);
        if (was) DragDropped?.Invoke(e);
    }

    private void OnDragCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        pressed = false;
        if (!lifted) return;
        lifted = false;
        Cursor = null;
        pressPointer = null;
        DragCanceled?.Invoke();
    }

    /// <summary>The trailing items, right-aligned and clipped at the title's minimum while a drag holds the set.</summary>
    private sealed class Trailing(PaneHeader owner) : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var child in Children) child.Measure(Size.Infinity);
            return default;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var right = finalSize.Width;
            var items = owner.TrailingItems().ToList();
            for (var i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                var width = item.DesiredSize.Width;
                right -= width;
                item.Arrange(Centred(right, width, item.DesiredSize.Height, finalSize.Height));
                right -= PaneTokens.HeaderGap;
            }
            foreach (var child in Children)
                if (!child.IsVisible) child.Arrange(default);
            return finalSize;
        }
    }
}
