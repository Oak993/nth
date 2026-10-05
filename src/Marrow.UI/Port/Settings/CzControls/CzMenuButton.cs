// PORT-ONLY CZ-1: shared row menu, CZ-12.12; the design's language menu promoted to a reusable control.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
namespace Marrow.UI.Port.Settings;

public sealed class CzMenuButton : CzButton
{
    private readonly Popup popup;
    private readonly Border surface;
    private readonly CzNumber fade, grow, lift, valueWidth;
    private TopLevel? watched;
    private int focused;
    public CzCrossfadeText ValueLabel { get; }
    public CzChevron Chevron { get; }
    public IReadOnlyList<CzMenuItem> Items { get; }
    public string SelectedId { get; private set; }
    public double WidestLabel { get; private set; }
    public double MenuWidth { get; private set; } = 220;
    public bool IsOpen { get; private set; }
    public bool StackedRow { get; set; }
    public event Action<string>? SelectionChanged;
    public double MenuOpacity => fade.Value;
    public double MenuScale => grow.Value;
    public double ValueWidth => valueWidth.Value;
    public CzMenuButton(string title, IReadOnlyList<(string Id, string Text)> options, string selected, string detail = "") : base(title, detail)
    {
        if (options.Count == 0 || !options.Any(x => x.Id == selected)) throw new ArgumentException("The selected menu option must exist.", nameof(selected));
        if (options.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != options.Count) throw new ArgumentException("Option IDs must be unique.", nameof(options));
        SelectedId = selected; Height = 28; CornerRadius = new CornerRadius(14); Padding = new Thickness(10, 0); PressScale = .97;
        ValueLabel = new CzCrossfadeText(options.First(x => x.Id == selected).Text) { FontSize = 13 };
        Chevron = new CzChevron { VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(ValueLabel); line.Children.Add(Chevron);
        var list = new StackPanel(); Items = options.Select(x => new CzMenuItem(this, x.Id, x.Text)).ToArray();
        foreach (var item in Items) list.Children.Add(item);
        surface = new Border { Padding = new Thickness(5), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            Background = CzTheme.Brush(CzTheme.Token(this, "popbg")), BorderBrush = CzTheme.Brush(CzTheme.Token(this, "popedge")), Child = list,
            BoxShadow = BoxShadows.Parse("0 16 40 0 #52000000, 0 2 8 0 #29000000"), Opacity = 0,
            RenderTransformOrigin = new RelativePoint(.5, 0, RelativeUnit.Relative) };
        popup = new Popup { Child = surface, PlacementTarget = this, IsLightDismissEnabled = true, VerticalOffset = 6 };
        // The existing presenter opt-out suppresses its popover tip; automation remains a menu.
        PopoverHost.SetIsSheet(popup, true);
        line.Children.Add(popup); Child = line;
        fade = new CzNumber(Motion, 0, v => { surface.Opacity = v; if (!IsOpen && v == 0) Presentation.SetPresented(popup, false, this, () => CloseMenu()); });
        grow = new CzNumber(Motion, .96, _ => Transform()); lift = new CzNumber(Motion, -4, _ => Transform());
        valueWidth = new CzNumber(Motion, 0, v => ValueLabel.Width = v);
        popup.Closed += (_, _) => { if (IsOpen) CloseMenu(false); };
        Invoked += () => { if (IsOpen) CloseMenu(); else OpenMenu(); };
        DetachedFromVisualTree += (_, _) => { CloseMenu(false); popup.IsOpen = false; };
        UpdateChecks(); MeasureLabels(initial: true);
    }
    private void Transform() => surface.RenderTransform = new TransformGroup { Children = { new ScaleTransform(grow.Value, grow.Value), new TranslateTransform(0, lift.Value) } };
    private void MeasureLabels(bool initial = false)
    {
        // Actual rendered font metrics, not string length or a fixed-width approximation.
        foreach (var item in Items) item.Label.Measure(Size.Infinity);
        WidestLabel = Items.Max(x => x.Label.DesiredSize.Width); MenuWidth = Math.Max(220, Math.Ceiling(WidestLabel) + 48); surface.Width = MenuWidth;
        var current = Items.First(x => x.Id == SelectedId);
        var width = Math.Ceiling(current.Label.DesiredSize.Width);
        if (initial) valueWidth.Jump(width); else valueWidth.To(width, CzMotion.Ez(.3));
    }
    public void Select(string id)
    {
        if (!IsEffectivelyEnabled) return;
        var item = Items.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentOutOfRangeException(nameof(id));
        Show(id); SelectionChanged?.Invoke(id); CloseMenu();
    }
    /// <summary>Mirrors an external setting without emitting a user's pick, also while dimmed.</summary>
    public void Show(string id)
    {
        var item = Items.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentOutOfRangeException(nameof(id));
        SelectedId = id; ValueLabel.SetText(item.Label.Text!); UpdateChecks(); MeasureLabels();
    }
    private void UpdateChecks() { foreach (var item in Items) item.SetChecked(item.Id == SelectedId); }
    public Rect Place(Rect button, Rect viewport, bool stacked)
    {
        var height = Items.Count * 28 + 12;
        var y = button.Bottom + 6;
        if (y + height > viewport.Bottom) y = button.Top - 6 - height;
        return new Rect(stacked ? button.Left : button.Right - MenuWidth, y, MenuWidth, height);
    }
    public void OpenMenu()
    {
        if (!IsEffectivelyEnabled || IsOpen) return;
        MeasureLabels(); IsOpen = true; Chevron.SetOpen(true);
        var top = TopLevel.GetTopLevel(this);
        var at = top is null ? null : this.TranslatePoint(default, top);
        var above = at.HasValue && at.Value.Y + Bounds.Height + 6 + Items.Count * 28 + 12 > top!.Bounds.Height;
        popup.Placement = above
            ? StackedRow ? PlacementMode.TopEdgeAlignedLeft : PlacementMode.TopEdgeAlignedRight
            : StackedRow ? PlacementMode.BottomEdgeAlignedLeft : PlacementMode.BottomEdgeAlignedRight;
        popup.VerticalOffset = above ? -6 : 6;
        surface.RenderTransformOrigin = new RelativePoint(StackedRow ? 0 : 1, above ? 1 : 0, RelativeUnit.Relative);
        Transform(); Presentation.SetPresented(popup, true, this, () => CloseMenu()); fade.To(1, CzMotion.Ease(.2)); grow.To(1, CzMotion.Pop(.22)); lift.To(0, CzMotion.Pop(.22));
        watched = TopLevel.GetTopLevel(this);
        if (watched is not null) watched.AddHandler(KeyDownEvent, MenuKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        surface.AddHandler(KeyDownEvent, MenuKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        focused = Items.ToList().FindIndex(x => x.Id == SelectedId); Items[focused].Focus(NavigationMethod.Directional);
        Refresh();
    }
    public void CloseMenu(bool refocus = true)
    {
        if (!IsOpen) return;
        IsOpen = false; WindowTransients.Hide(popup); Chevron.SetOpen(false);
        watched?.RemoveHandler(KeyDownEvent, MenuKey); watched = null; surface.RemoveHandler(KeyDownEvent, MenuKey);
        fade.To(0, CzMotion.Ease(.15)); grow.To(.96, CzMotion.Ease(.15)); lift.To(-4, CzMotion.Ease(.15));
        if (refocus) Focus(NavigationMethod.Directional); Refresh();
    }
    public void LightDismiss() => CloseMenu(false);
    private void MenuKey(object? sender, KeyEventArgs e)
    {
        if (!IsOpen || e.Handled) return;
        switch (e.Key)
        {
            case Key.Up: focused = (focused - 1 + Items.Count) % Items.Count; break;
            case Key.Down: focused = (focused + 1) % Items.Count; break;
            case Key.Home: focused = 0; break;
            case Key.End: focused = Items.Count - 1; break;
            case Key.Enter: case Key.Space: Select(Items[focused].Id); e.Handled = true; return;
            case Key.Escape: CloseMenu(); e.Handled = true; return;
            default: return;
        }
        Items[focused].Focus(NavigationMethod.Directional); e.Handled = true;
    }
    protected override void Refresh()
    {
        base.Refresh();
        if (surface is not null) { surface.Background = CzTheme.Brush(CzTheme.Token(this, "popbg")); surface.BorderBrush = CzTheme.Brush(CzTheme.Token(this, "popedge")); }
        if (IsOpen) Background = CzTheme.Brush(CzTheme.Token(this, "caph"));
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new CzMenuButtonPeer(this);
}
