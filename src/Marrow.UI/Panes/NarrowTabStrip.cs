// PORT-ONLY: web surfaces W3c, the narrow window's tab strip (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.8 "Below ChatMin + Gap +
// GridMin1 of workspace the workspace becomes a glass segmented tab strip ...: Chat, then the open panes in order. One surface shows
// at a time"; 10 "text at least 4.5:1"; 13.3 "narrow tabs"; ED-NEW-web-w3c-1). No Swift counterpart. The look is the approved canvas's
// (index.html .ntabs): a centred pill of glass (the tinted layer with its specular rim, no second blur: the window's one blur is
// behind it), 3 DIP inside, tabs 28 DIP high with a 13 DIP glyph and the pane's short name at 12 DIP, the chosen tab filled and
// in the primary text colour; when the tabs are wider than the strip, the pill is the strip's width and the tabs scroll
// sideways with no bar. It is a tab list for assistive technology (each tab is a selectable item) and for the keyboard (one
// tab stop, the arrow keys, Home and End move the choice).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.UI.Port;

namespace Marrow.UI.Panes;

/// <summary>The segmented tab strip of the narrow window.</summary>
public sealed class NarrowTabStrip : Panel
{
    /// <summary>The pill's height: 3 DIP, a 28 DIP tab, 3 DIP (canvas .ntabs .in and button).</summary>
    public const double PillHeight = 34;

    /// <summary>A tab's height.</summary>
    public const double TabHeight = 28;

    /// <summary>The pill's inside padding and the gap between tabs (canvas padding 3px, gap 2px).</summary>
    public const double Inset = 3;

    public const double TabGap = 2;

    /// <summary>The words of the strip for assistive technology.</summary>
    public const string StripName = "Panes";

    private readonly Border pill;
    private readonly ScrollViewer scroller;
    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = TabGap };
    private readonly List<NarrowTab> tabs = [];
    private IReadOnlyList<PaneKind> kinds = [];
    private PaneKind selected = PaneKind.Chat;
    private double natural;

    public NarrowTabStrip()
    {
        scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = row,
            Template = ScrollerTemplate,
        };
        pill = new Border
        {
            Height = PillHeight,
            CornerRadius = new CornerRadius(PillHeight / 2),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(Inset - 1),
            ClipToBounds = true,
            Child = scroller,
        };
        Children.Add(pill);
        AutomationProperties.SetName(this, StripName);
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Bubble);
        ActualThemeVariantChanged += (_, _) => ApplyLook();
        ApplyLook();
    }

    // The app carries no theme package: a ScrollViewer needs its own template (the recipe of PortControlThemes and ThreadTimeline: a
    // content presenter that scrolls by wheel and touch, no bar).
    private static readonly Avalonia.Controls.Templates.FuncControlTemplate<ScrollViewer> ScrollerTemplate = new((viewer, scope) =>
    {
        var presenter = new ScrollContentPresenter
        {
            Name = "PART_ContentPresenter",
            Background = Brushes.Transparent,
            [!ContentPresenter.ContentProperty] = viewer[!ContentControl.ContentProperty],
        };
        presenter.GestureRecognizers.Add(new ScrollGestureRecognizer { CanHorizontallyScroll = true });
        return presenter.RegisterInNameScope(scope);
    });

    /// <summary>A tab was chosen with the pointer or the keyboard.</summary>
    public event Action<PaneKind>? TabChosen;

    /// <summary>The tabs, in order.</summary>
    public IReadOnlyList<PaneKind> Kinds => kinds;

    /// <summary>The chosen tab.</summary>
    public PaneKind Selected => selected;

    /// <summary>The tab buttons, in order.</summary>
    public IReadOnlyList<Button> TabButtons => tabs;

    /// <summary>The pill (the glass).</summary>
    public Border Pill => pill;

    /// <summary>The scroller the tabs sit in.</summary>
    public ScrollViewer Scroller => scroller;

    /// <summary>The pill's width when every tab shows.</summary>
    public double NaturalWidth => natural;

    /// <summary>Sets the tabs and the chosen one. The buttons are rebuilt only when the tabs change.</summary>
    public void SetTabs(IReadOnlyList<PaneKind> value, PaneKind chosen)
    {
        value ??= [];
        var rebuilt = !kinds.SequenceEqual(value);
        if (rebuilt)
        {
            kinds = [.. value];
            Rebuild();
        }
        if (!rebuilt && selected == chosen) return;
        selected = chosen;
        foreach (var tab in tabs) tab.Look(tab.Kind == chosen);
        InvalidateMeasure();
        Dispatcher.UIThread.Post(() =>
        {
            if (tabs.FirstOrDefault(tab => tab.Kind == selected) is { } tab && tab.IsEffectivelyVisible) tab.BringIntoView();
        });
    }

    private void Rebuild()
    {
        row.Children.Clear();
        tabs.Clear();
        foreach (var kind in kinds)
        {
            var tab = new NarrowTab(kind, this, () => Choose(kind));
            tab.Look(kind == selected);
            tabs.Add(tab);
            row.Children.Add(tab);
        }
        InvalidateMeasure();
    }

    private void Choose(PaneKind kind)
    {
        if (!kinds.Contains(kind)) return;
        TabChosen?.Invoke(kind);
    }

    private void ApplyLook()
    {
        var theme = ActualThemeVariant;
        pill.Background = PaneTokens.CardFill(theme, expanded: false);
        pill.BorderBrush = PaneTokens.Rim(theme, focused: false);
        pill.BoxShadow = PaneTokens.Shadow(theme);
        foreach (var tab in tabs) tab.Look(tab.Kind == selected);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || tabs.Count == 0) return;
        var from = tabs.FindIndex(tab => tab.IsKeyboardFocusWithin);
        if (from < 0) return;
        var rtl = FlowDirection == FlowDirection.RightToLeft;
        var to = e.Key switch
        {
            Key.Right => rtl ? from - 1 : from + 1,
            Key.Left => rtl ? from + 1 : from - 1,
            Key.Home => 0,
            Key.End => tabs.Count - 1,
            _ => -1,
        };
        if (to < 0 || to >= tabs.Count)
        {
            e.Handled = e.Key is Key.Left or Key.Right or Key.Home or Key.End;
            return;
        }
        e.Handled = true;
        var next = tabs[to];
        Choose(next.Kind);
        next.Focus(NavigationMethod.Directional);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        pill.Measure(Size.Infinity);
        natural = pill.DesiredSize.Width;
        var width = double.IsFinite(availableSize.Width) ? Math.Min(natural, availableSize.Width) : natural;
        return new Size(Math.Max(0, width), PillHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = Math.Max(0, Math.Min(natural, finalSize.Width));
        pill.Arrange(new Rect(Math.Round((finalSize.Width - width) / 2), Math.Round((finalSize.Height - PillHeight) / 2), width, PillHeight));
        return finalSize;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new StripPeer(this);

    /// <summary>The strip as a tab list: its chosen tab is its selection.</summary>
    private sealed class StripPeer(NarrowTabStrip owner) : ControlAutomationPeer(owner), ISelectionProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Tab;

        public bool CanSelectMultiple => false;

        public bool IsSelectionRequired => true;

        public IReadOnlyList<AutomationPeer> GetSelection() =>
            owner.tabs.Where(tab => tab.Kind == owner.selected).Select(tab => ControlAutomationPeer.CreatePeerForElement(tab)).ToList();
    }

    /// <summary>One tab: a plain button with the glyph and the name, a selectable item for assistive technology.</summary>
    private sealed class NarrowTab : Button
    {
        private readonly NarrowTabStrip strip;
        private readonly PaneGlyph glyph;
        private readonly TextBlock text;
        private readonly Border face;
        private readonly Action choose;
        private bool hovered;
        private bool chosen;

        public NarrowTab(PaneKind kind, NarrowTabStrip strip, Action choose)
        {
            Kind = kind;
            this.strip = strip;
            this.choose = choose;
            glyph = new PaneGlyph(PaneGlyph.For(kind), 13);
            text = new TextBlock { Text = PaneKinds.ShortName(kind), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { glyph, text } };
            face = new Border
            {
                Height = TabHeight,
                Padding = new Thickness(9, 0, 11, 0),
                CornerRadius = new CornerRadius(TabHeight / 2),
                Background = Brushes.Transparent,
                Child = content,
            };
            Content = face;
            Padding = default;
            Focusable = true;
            this.KeyboardFocus(TabHeight / 2);
            Click += (_, _) => choose();
            PointerEntered += (_, _) =>
            {
                hovered = true;
                Look(chosen);
            };
            PointerExited += (_, _) =>
            {
                hovered = false;
                Look(chosen);
            };
            AutomationProperties.SetName(this, PaneKinds.AccessibleName(kind)); // the label says the same in fewer words: no tooltip
        }

        public PaneKind Kind { get; }

        // The chosen tab is the one tab stop of the strip (the arrow keys move within it).
        public void Look(bool isChosen)
        {
            chosen = isChosen;
            IsTabStop = isChosen;
            var foreground = (isChosen ? PaneTokens.Text1 : PaneTokens.Text2).Brush(this);
            face.Background = isChosen ? PaneTokens.NarrowTabFill.Brush(this) : hovered ? PaneTokens.HoverFill.Brush(this) : Brushes.Transparent;
            text.Foreground = hovered && !isChosen ? PaneTokens.Text1.Brush(this) : foreground;
            glyph.Foreground = text.Foreground;
        }

        internal void Activate()
        {
            if (IsEffectivelyEnabled) choose();
        }

        internal bool IsChosen => chosen;

        internal NarrowTabStrip Strip => strip;

        protected override AutomationPeer OnCreateAutomationPeer() => new TabPeer(this);
    }

    private sealed class TabPeer(NarrowTab owner) : ControlAutomationPeer(owner), ISelectionItemProvider, IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TabItem;

        bool ISelectionItemProvider.IsSelected => owner.IsChosen;

        ISelectionProvider? ISelectionItemProvider.SelectionContainer => ControlAutomationPeer.CreatePeerForElement(owner.Strip) as ISelectionProvider;

        void ISelectionItemProvider.AddToSelection() { }

        void ISelectionItemProvider.RemoveFromSelection() { }

        void ISelectionItemProvider.Select()
        {
            if (!owner.IsChosen) owner.Activate();
        }

        void IInvokeProvider.Invoke() => owner.Activate();
    }
}
