using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Localisation;
using Marrow.Core.Models;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W3b, the collapse rail (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.5 "Collapse ... a 22 DIP
// rail with the pane icons. Dragging back out past the minimum, clicking a rail icon, double-clicking the rail, or Space on
// the focused splitter restores it", 3.12 "the collapse rails ... are tinted layers", 4.2 "Collapsed: the rail (22 DIP) shows
// the pane icons and a small chevron; the splitter sits on the rail's edge"; ED-NEW-web-w3b-3). No Swift counterpart. The look
// is the approved canvas's .rail: 11 DIP radius, a column of 18 x 22 buttons with 13 DIP glyphs, 4 DIP apart, 8 DIP from the
// top. It is a tinted layer like the pane cards (PaneTokens), never a second blur. The chevron points toward the panes it
// brings back and is the rail's own restore button.

namespace Marrow.App.Views.Panes;

/// <summary>The 22 DIP strip a collapsed side folds into.</summary>
public sealed class PaneRailStrip : Border
{
    /// <summary>An icon button (canvas .rail button).</summary>
    public const double ButtonWidth = 18;

    public const double ButtonHeight = 22;

    public const double GlyphSize = 13;

    public const double IconGap = 4;

    public const double TopPadding = 8;

    private readonly StackPanel icons = new() { Orientation = Orientation.Vertical, Spacing = IconGap, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button chevron;
    private readonly PaneGlyph chevronGlyph = new(PaneGlyphKind.Chevron, GlyphSize);
    private readonly RotateTransform turn = new(90);
    private readonly List<Button> buttons = [];
    private IReadOnlyList<PaneKind> panes = [];

    public PaneRailStrip(string key)
    {
        Key = key;
        Width = PaneKinds.Rail;
        CornerRadius = new CornerRadius(PaneTokens.RailCornerRadius);
        BorderThickness = new Thickness(1);
        ClipToBounds = true;
        Name = "Rail_" + key;
        chevronGlyph.Foreground = PaneTokens.Text2.Brush(this);
        chevronGlyph.RenderTransform = turn;
        chevronGlyph.RenderTransformOrigin = RelativePoint.Center;
        var face = new Border { Width = ButtonWidth, Height = ButtonHeight, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Child = chevronGlyph };
        chevron = PlainControls.Button(face, () => RestoreRequested?.Invoke(null)).KeyboardFocus(6);
        Hover(chevron, face, chevronGlyph);
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(0, TopPadding, 0, TopPadding) };
        Grid.SetRow(icons, 0);
        Grid.SetRow(chevron, 1);
        chevron.HorizontalAlignment = HorizontalAlignment.Center;
        chevron.VerticalAlignment = VerticalAlignment.Bottom;
        layout.Children.Add(icons);
        layout.Children.Add(chevron);
        Child = layout;
        AutomationProperties.SetName(this, key == PaneGutterKeys.Side ? UiText.Get("panes.rail.collapsed_side_chat") : UiText.Get("panes.rail.collapsed_panes"));
        AutomationProperties.SetLandmarkType(this, AutomationLandmarkType.Region);
        var restoreName = key == PaneGutterKeys.Side ? UiText.Get("panes.rail.restore_side_chat") : UiText.Get("panes.rail.restore_panes");
        AutomationProperties.SetName(chevron, restoreName);
        ToolTip.SetTip(chevron, restoreName);
        DoubleTapped += (_, e) =>
        {
            if (e.Source is Visual source && source.FindAncestorButton() is not null) return;
            RestoreRequested?.Invoke(null);
            e.Handled = true;
        };
        ActualThemeVariantChanged += (_, _) => ApplyLook();
        ApplyLook();
    }

    /// <summary>The gutter key this rail folds: <see cref="PaneGutterKeys.Chat"/> (every pane) or <see cref="PaneGutterKeys.Side"/>.</summary>
    public string Key { get; }

    /// <summary>The panes on the rail, in order, as icon buttons.</summary>
    public IReadOnlyList<PaneKind> Panes => panes;

    /// <summary>The icon buttons, in order.</summary>
    public IReadOnlyList<Button> IconButtons => buttons;

    /// <summary>The chevron, the rail's own restore button.</summary>
    public Button Chevron => chevron;

    /// <summary>A rail icon (the pane) or the chevron or a double-click (null) asked to restore the folded side.</summary>
    public event Action<PaneKind?>? RestoreRequested;

    /// <summary>The icons of the panes the rail holds.</summary>
    public void SetPanes(IReadOnlyList<PaneKind> value)
    {
        if (panes.SequenceEqual(value)) return;
        panes = [.. value];
        icons.Children.Clear();
        buttons.Clear();
        foreach (var kind in panes)
        {
            var name = PaneKinds.ShortName(kind);
            var glyph = new PaneGlyph(PaneGlyph.For(kind), GlyphSize) { Foreground = PaneTokens.Text2.Brush(this) };
            var face = new Border { Width = ButtonWidth, Height = ButtonHeight, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Child = glyph };
            var pane = kind;
            var button = PlainControls.Button(face, () => RestoreRequested?.Invoke(pane)).KeyboardFocus(6);
            Hover(button, face, glyph);
            ToolTip.SetTip(button, name);
            AutomationProperties.SetName(button, UiText.Format("panes.rail.restore_pane", name));
            buttons.Add(button);
            icons.Children.Add(button);
        }
    }

    /// <summary>Points the chevron toward the panes it brings back: leftward in a left-to-right window, mirrored in a right-to-left one.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FlowDirectionProperty) turn.Angle = FlowDirection == FlowDirection.RightToLeft ? -90 : 90;
        if (change.Property == EnvironmentValues.AccessibilityHighContrastProperty) ApplyLook(); // W20: solid under a contrast theme
    }

    private void ApplyLook()
    {
        ThemeVariant theme = ActualThemeVariant;
        var contrast = EnvironmentValues.GetAccessibilityHighContrast(this);
        ContrastSurface.MatchTheme(this); // its glyphs take the system's colours on the solid rail
        Background = PaneTokens.CardFill(theme, expanded: false, contrast);
        BorderBrush = PaneTokens.Rim(theme, focused: false, contrast);
        BoxShadow = PaneTokens.Shadow(theme, lifted: false, contrast);
    }

    private void Hover(Button button, Border face, PaneGlyph glyph) => button.OnHover(on =>
    {
        face.Background = on ? PaneTokens.HoverFill.Brush(this) : Brushes.Transparent;
        glyph.Foreground = (on ? PaneTokens.Text1 : PaneTokens.Text2).Brush(this);
    });
}

internal static class PaneRailExtensions
{
    internal static Button? FindAncestorButton(this Visual visual)
    {
        for (Visual? at = visual; at is not null; at = Avalonia.VisualTree.VisualExtensions.GetVisualParent(at))
            if (at is Button button) return button;
        return null;
    }
}
