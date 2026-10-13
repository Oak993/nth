using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Localisation;
using Marrow.Core.Web;
using Marrow.UI.Panes;
using Marrow.UI.Port;

namespace Marrow.App.Views.Browser;

// PORT-ONLY: DESIGN 5.7-5.8, index.html .pbar/.seg/.scaled and glyphs; ED-NEW-web-w9-3.
public sealed class DevicePresetBar : Border
{
    private readonly ProjectPreviewBody owner;
    private readonly TextBlock size = Text(11.5);
    private readonly Button console;
    private readonly Button reload;
    private readonly Dictionary<DevicePreset, Button> presets = [];
    public DevicePresetBar(ProjectPreviewBody owner)
    {
        this.owner = owner;
        Height = 38; Padding = new Thickness(8, 0); BorderThickness = new Thickness(0, 0, 0, .5); BorderBrush = PaneTokens.Line.Brush(this);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var segment = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        foreach (var preset in Enum.GetValues<DevicePreset>())
        {
            var chosen = preset;
            var button = Action(PresetName(preset), () => owner.SetDevice(chosen), new PreviewGlyph(preset.ToString()) { Width = 13, Height = 13 }, toggle: true);
            button.Padding = new Thickness(9, 0); button.CornerRadius = new CornerRadius(8);
            presets[chosen] = button; segment.Children.Add(button);
        }
        row.Children.Add(new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(2), Child = segment, Background = PaneTokens.HoverFill.Brush(this), BorderThickness = new Thickness(.5), BorderBrush = PaneTokens.Line.Brush(this) });
        size.VerticalAlignment = VerticalAlignment.Center; size.FontFamily = new FontFamily("Cascadia Mono"); size.FontWeight = FontWeight.Medium; Grid.SetColumn(size, 1); row.Children.Add(size);
        var rotate = Action(UiText.Get("visus.preview.rotate"), owner.Rotate); rotate.Content = new PreviewGlyph("Rotate"); rotate.Width = 26; rotate.Padding = new Thickness(0); rotate.CornerRadius = new CornerRadius(8); Grid.SetColumn(rotate, 2); row.Children.Add(rotate);
        reload = Action(UiText.Get("visus.preview.reload_on_save"), () => { owner.Tab.SetPreviewState(owner.Tab.PreviewState with { ReloadOnSave = !owner.Tab.PreviewState.ReloadOnSave }); owner.Refresh(); }, new PreviewGlyph("Reload") { Width = 13, Height = 13 }, toggle: true);
        ((StackPanel)reload.Content!).Spacing = 6;
        reload.Padding = new Thickness(10, 0); reload.CornerRadius = new CornerRadius(13); reload.BorderThickness = new Thickness(1); reload.BorderBrush = PaneTokens.Line.Brush(this); Grid.SetColumn(reload, 4); row.Children.Add(reload);
        console = Action(UiText.Get("visus.preview.console"), owner.ToggleConsole, new PreviewGlyph("Console") { Width = 13, Height = 13 }, toggle: true); console.Padding = new Thickness(10, 0); console.CornerRadius = new CornerRadius(13); console.BorderThickness = new Thickness(1); console.BorderBrush = PaneTokens.Line.Brush(this); Grid.SetColumn(console, 5); row.Children.Add(console);
        Child = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = row };
        ActualThemeVariantChanged += (_, _) => Refresh();
        AutomationProperties.SetName(this, UiText.Get("visus.preview.device_sizes"));
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EnvironmentValues.AccessibilityHighContrastProperty) Refresh(); // W20
    }
    public string SizeText => size.Text ?? "";
    public DeviceMetrics Metrics => owner.Metrics;
    public void Refresh(DeviceMetrics? metrics = null)
    {
        metrics ??= owner.Tab.PreviewState.Metrics(1000, 1000);
        size.Text = metrics.Width.ToString(CultureInfo.InvariantCulture) + " × " + metrics.Height.ToString(CultureInfo.InvariantCulture);
        foreach (var (preset, button) in presets)
        {
            ((ToggleButton)button).IsChecked = owner.Tab.PreviewState.Preset == preset;
            button.Background = owner.Tab.PreviewState.Preset == preset ? PaneTokens.HoverFill.Brush(this) : Brushes.Transparent;
            ContrastCue.Toggle(button, owner.Tab.PreviewState.Preset == preset, null, new Thickness(0)); // W20: outlined under a contrast theme
        }
        ((ToggleButton)reload).IsChecked = owner.Tab.PreviewState.ReloadOnSave;
        ((ToggleButton)console).IsChecked = owner.Drawer?.State.Open ?? owner.Tab.PreviewState.ConsoleOpen;
        reload.Background = ((ToggleButton)reload).IsChecked == true ? PaneTokens.HoverFill.Brush(this) : Brushes.Transparent;
        console.Background = ((ToggleButton)console).IsChecked == true ? PaneTokens.HoverFill.Brush(this) : Brushes.Transparent;
        ContrastCue.Toggle(reload, ((ToggleButton)reload).IsChecked == true, PaneTokens.Line.Brush(this), new Thickness(1));
        ContrastCue.Toggle(console, ((ToggleButton)console).IsChecked == true, PaneTokens.Line.Brush(this), new Thickness(1));
        var consoleLabel = (StackPanel)Label(UiText.Get("visus.preview.console"), new PreviewGlyph("Console") { Width = 13, Height = 13 }); consoleLabel.Spacing = 6;
        if (owner.Runtime.Diagnostics?.Errors is > 0 and var errors)
            consoleLabel.Children.Add(new Border
            {
                MinWidth = 16, Height = 16, Padding = new Thickness(4, 0), CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.Parse("#FF7A7A")), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = errors.ToString(CultureInfo.InvariantCulture), FontSize = 10, FontWeight = FontWeight.SemiBold, LineHeight = 16, Foreground = new SolidColorBrush(Color.Parse("#1a0b0b")), TextAlignment = TextAlignment.Center },
            });
        console.Content = consoleLabel;
        size.Foreground = PaneTokens.Text2.Brush(this);
    }
    /// <summary>The words of a device preset (the glyph keeps the enum name as its id).</summary>
    internal static string PresetName(DevicePreset preset) => preset switch
    {
        DevicePreset.Phone => UiText.Get("visus.preview.device_phone"),
        DevicePreset.Tablet => UiText.Get("visus.preview.device_tablet"),
        DevicePreset.Laptop => UiText.Get("visus.preview.device_laptop"),
        DevicePreset.Fill => UiText.Get("visus.preview.device_fill"),
        DevicePreset.Responsive => UiText.Get("visus.preview.device_responsive"),
        _ => preset.ToString(),
    };
    internal static TextBlock Text(double font = 12) => new() { FontSize = font, VerticalAlignment = VerticalAlignment.Center };
    internal static Control Label(string text, Control? glyph)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        if (glyph is not null) row.Children.Add(glyph);
        row.Children.Add(new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
    internal static Button Action(string name, System.Action run, Control? glyph = null, bool toggle = false)
    {
        Button button = toggle ? new ToggleButton() : new Button();
        button.Content = Label(name, glyph); button.Height = 26; button.Padding = new Thickness(7, 0); button.CornerRadius = new CornerRadius(6); button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.VerticalAlignment = VerticalAlignment.Center;
        button.Click += (_, _) => run(); AutomationProperties.SetName(button, name); ToolTip.SetTip(button, name);
        button.ActualThemeVariantChanged += (_, _) => button.Foreground = PaneTokens.Text1.Brush(button);
        button.Foreground = PaneTokens.Text1.Brush(button); return button;
    }
}

internal sealed class PreviewGlyph(string kind) : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var pen = new Pen(PaneTokens.Text1.Brush(this), 1.5);
        var path = kind switch
        {
            "Phone" => "M7 5.5 Q7 3 9.5 3 H14.5 Q17 3 17 5.5 V18.5 Q17 21 14.5 21 H9.5 Q7 21 7 18.5 Z M11 18 H13",
            "Tablet" => "M7 3 H17 Q19 3 19 5 V19 Q19 21 17 21 H7 Q5 21 5 19 V5 Q5 3 7 3 Z M11 18 H13",
            "Laptop" => "M6.5 5 H17.5 Q19 5 19 6.5 V13.5 Q19 15 17.5 15 H6.5 Q5 15 5 13.5 V6.5 Q5 5 6.5 5 Z M3 19 H21",
            "Fill" => "M5 5 H19 Q21 5 21 7 V17 Q21 19 19 19 H5 Q3 19 3 17 V7 Q3 5 5 5 Z",
            "Responsive" => "M3 12 H21 M6.5 8.5 L3 12 L6.5 15.5 M17.5 8.5 L21 12 L17.5 15.5",
            "Rotate" => "M4.5 12 A7.5 7.5 0 0 1 17.5 6.9 M18 3 V7 H14 M19.5 12 A7.5 7.5 0 0 1 6.5 17.1 M6 21 V17 H10",
            "Reload" => "M19.5 12 A7.5 7.5 0 1 1 17.3 6.7 M19.5 4.5 V9 H15",
            "Picker" => "M5 4 L10.5 19 L12.7 12.7 L19 10.5 Z M13 13 L18 18",
            _ => "M5 5 H19 Q21 5 21 7 V17 Q21 19 19 19 H5 Q3 19 3 17 V7 Q3 5 5 5 Z M7 10 L10 12 L7 14 M12.5 15 H17",
        };
        using (context.PushTransform(Matrix.CreateScale(Bounds.Width / 24, Bounds.Height / 24))) context.DrawGeometry(null, pen, Geometry.Parse(path));
    }
    protected override Size MeasureOverride(Size availableSize) => new(16, 16);
}
