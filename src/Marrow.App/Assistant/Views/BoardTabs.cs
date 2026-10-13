// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.2 "Right: the Board pane, with four tabs"; the canvas's seg(), work/design/assistant/
// index.html lines 770 and 1802-1821, and its .seg CSS; ED-NEW-dextra-as8-6). No Swift twin.
// The Board pane's tab strip: Missions · Decisions (with its open count) · Feed · Team. A thumb slides under the picked tab (420 ms,
// transform only; at once with Reduce motion). Each tab is read as a tab and says whether it is selected; Left and Right move
// between them, Home and End jump to the ends.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

public enum BoardTab { Missions, Decisions, Feed, Team }

/// <summary>One tab of the strip, read as a selectable tab item.</summary>
public sealed class BoardTabButton : Button
{
    public static readonly StyledProperty<bool> IsSelectedProperty = AvaloniaProperty.Register<BoardTabButton, bool>(nameof(IsSelected));

    protected override Type StyleKeyOverride => typeof(Button);

    public BoardTabButton(BoardTab tab) => Tab = tab;

    public BoardTab Tab { get; }

    public bool IsSelected { get => GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsSelectedProperty && ControlAutomationPeer.FromElement(this) is { } peer)
            peer.RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, change.GetOldValue<bool>(), change.GetNewValue<bool>());
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(BoardTabButton owner) : ControlAutomationPeer(owner), ISelectionItemProvider, IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TabItem;

        public bool IsSelected => owner.IsSelected;

        public ISelectionProvider? SelectionContainer => null;

        public void Select() => owner.Pick();

        public void AddToSelection() => owner.Pick();

        public void RemoveFromSelection()
        {
        }

        public void Invoke() => owner.Pick();
    }

    internal event Action<BoardTabButton>? Picked;

    internal void Pick() => Picked?.Invoke(this);
}

public sealed class BoardTabs : Decorator
{
    private readonly Grid row = new();
    private readonly Border thumb = new() { Width = 100, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3), IsHitTestVisible = false };
    private readonly Dictionary<BoardTab, BoardTabButton> tabs = [];
    private readonly TextBlock count = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Border countBox = new() { Padding = new Thickness(5, 0), Height = 16, MinWidth = 16, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private BoardTab selected = BoardTab.Missions;

    public BoardTabs()
    {
        var track = new Border { Padding = new Thickness(3) };
        track.RoundedRectangle(12, RoundedCornerStyle.Continuous);
        track.Background = Chrome.Overlay(.05).Brush(track);
        thumb.RoundedRectangle(9, RoundedCornerStyle.Continuous);
        thumb.Background = Chrome.GlassTint.Brush(thumb);
        thumb.BorderThickness = new Thickness(.5);
        thumb.BorderBrush = Chrome.Overlay(.12).Brush(thumb);
        thumb.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        AutomationProperties.SetAccessibilityView(thumb, AccessibilityView.Raw);
        SwiftFont.System(10.5, SwiftFont.Weight.Semibold).MonospacedDigit().Apply(count);
        count.Foreground = Chrome.PrimaryText.Brush(count);
        countBox.Capsule(RoundedCornerStyle.Continuous);
        countBox.Background = Chrome.Overlay(.1).Brush(countBox);
        countBox.Child = count;
        AutomationProperties.SetAccessibilityView(countBox, AccessibilityView.Raw);
        var i = 0;
        foreach (var tab in Enum.GetValues<BoardTab>())
        {
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var label = TaskLook.Text(tab.ToString(), 12, SwiftFont.Weight.Medium, TaskLook.Ink);
            label.VerticalAlignment = VerticalAlignment.Center;
            var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { label } };
            if (tab == BoardTab.Decisions) content.Children.Add(countBox);
            var button = new BoardTabButton(tab) { Content = new Border { Height = 28, Padding = new Thickness(10, 0), Child = content, Background = Brushes.Transparent }, Padding = default, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            button.KeyboardFocus(9);
            AutomationProperties.SetName(button, tab.ToString());
            button.Click += (_, _) => Select(tab, true);
            button.Picked += b => Select(b.Tab, true);
            button.KeyDown += OnKey;
            Grid.SetColumn(button, i++);
            tabs[tab] = button;
            row.Children.Add(button);
        }
        var layers = new Panel { Children = { thumb, row } };
        track.Child = layers;
        Child = track;
        AutomationProperties.SetName(this, "Board");
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.Tab);
        row.SizeChanged += (_, _) => Place(false);
        SetCount(0);
        Select(BoardTab.Missions, false);
    }

    public BoardTab Selected => selected;

    public BoardTabButton Button(BoardTab tab) => tabs[tab];

    public event Action<BoardTab>? Changed;

    /// <summary>The open decisions; the tab's name says it ("Decisions, 3 open").</summary>
    public void SetCount(int open)
    {
        count.Text = open.ToString(System.Globalization.CultureInfo.InvariantCulture);
        countBox.Opacity = open == 0 ? .45 : 1;
        AutomationProperties.SetName(tabs[BoardTab.Decisions], open == 0 ? "Decisions" : $"Decisions, {open} open");
    }

    public void Select(BoardTab tab, bool animate)
    {
        var changed = tab != selected;
        selected = tab;
        foreach (var (t, b) in tabs) b.IsSelected = t == tab;
        Place(animate && !TaskLook.Still(this));
        if (changed) Changed?.Invoke(tab);
    }

    private void Place(bool animate)
    {
        var button = tabs[selected];
        var width = button.Bounds.Width;
        if (width <= 0) return;
        thumb.Transitions = animate
            ? [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(420), Easing = new Avalonia.Animation.Easings.SplineEasing(.32, .72, 0, 1) }]
            : null;
        var ops = TransformOperations.CreateBuilder(2);
        ops.AppendTranslate(button.Bounds.X, 0);
        ops.AppendScale(width / 100, 1);
        thumb.RenderTransform = ops.Build();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        var all = Enum.GetValues<BoardTab>();
        var at = Array.IndexOf(all, selected);
        var next = e.Key switch
        {
            Key.Right => all[(at + 1) % all.Length],
            Key.Left => all[(at + all.Length - 1) % all.Length],
            Key.Home => all[0],
            Key.End => all[^1],
            _ => (BoardTab?)null,
        };
        if (next is not { } tab) return;
        e.Handled = true;
        Select(tab, true);
        tabs[tab].Focus(NavigationMethod.Directional);
    }
}
