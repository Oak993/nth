// PORT-ONLY CZ-5 (customization-SPEC CZ-10.1; DESIGN gen.py `.row.accsw`, `.czacl`, `.czacap`, `.czsws`): the Custom dot colour row's body. Two lines of swatches, each a radio group
// of its own (Classic: the 10 Classic accents, Signature: the 12 Signature ones, in the palette's order) behind a 62 point label in 11.5 secondary text, the lines 6 apart, the label
// and its swatches 10 apart. The swatches are drawn in the theme's hex (AccentSwatch.Hex; the Windows accent as the lifted system accent the accent picker draws), each fill easing
// over 0.32 s when the theme or the system accent changes (OD-0076). At most one line holds the pick.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Marrow.Core.Port.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class WorkingDotsSwatches : Decorator
{
    private readonly record struct Fill(SolidColorBrush Brush, CzColor Colour, AccentSwatch Swatch);

    private readonly CzMotion motion;
    private readonly List<Fill> fills = [];
    private readonly TextBlock classicLabel;
    private IPlatformSettings? platform;

    internal CzSwatchRow Classic { get; }
    internal CzSwatchRow Signature { get; }

    /// <summary>The width of the labels' column.</summary>
    internal double LabelColumn => classicLabel.Width;

    /// <summary>A swatch was picked (its accent id), in either line.</summary>
    internal event Action<string>? Picked;

    internal WorkingDotsSwatches(string selected)
    {
        motion = new CzMotion(this);
        var dark = CzTheme.Dark(this);
        Classic = Line(AccentPalette.All.Take(AccentPalette.ClassicCount), CzDotsText.ClassicGroup, selected, dark);
        Signature = Line(AccentPalette.All.Skip(AccentPalette.ClassicCount), CzDotsText.SignatureGroup, selected, dark);
        classicLabel = Label(CzDotsText.Classic);
        Child = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            Children = { Row(classicLabel, Classic), Row(Label(CzDotsText.Signature), Signature) },
        };
        Classic.SelectionChanged += id => Picked?.Invoke(id);
        Signature.SelectionChanged += id => Picked?.Invoke(id);
        AttachedToVisualTree += (_, _) =>
        {
            Refresh(animate: false);
            platform = Application.Current?.PlatformSettings;
            if (platform is not null) platform.ColorValuesChanged += OnColorValuesChanged;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (platform is not null) platform.ColorValuesChanged -= OnColorValuesChanged;
            platform = null;
        };
        ActualThemeVariantChanged += (_, _) => Refresh(animate: true);
    }

    /// <summary>Shows the pick (an accent id): its line holds it and the other lets go. Raises the lines' selection events (the owner ignores that echo).</summary>
    internal void Show(string id)
    {
        var classic = AccentPalette.IndexOf(id) is >= 0 and < AccentPalette.ClassicCount;
        var (holder, other) = classic ? (Classic, Signature) : (Signature, Classic);
        other.ShowNone();
        if (holder.Buttons.Any(b => b.Id == id)) holder.Show(id);
        else holder.ShowNone();
    }

    private CzSwatchRow Line(IEnumerable<AccentSwatch> swatches, string name, string selected, bool dark)
    {
        var line = swatches.ToArray();
        var brushes = line.Select(s => new SolidColorBrush(Colour(s, dark))).ToArray();
        var row = new CzSwatchRow(name, line.Select((s, i) => (s.Id, s.Name, (IBrush)brushes[i])).ToArray(), line.Any(s => s.Id == selected) ? selected : null);
        for (var i = 0; i < line.Length; i++)
        {
            var (brush, button) = (brushes[i], row.Buttons[i]);
            fills.Add(new Fill(brush, new CzColor(motion, brush.Color, c => { brush.Color = c; button.InvalidateVisual(); }), line[i]));
        }
        return row;
    }

    // the theme's hex; the Windows accent is the system accent as the accent picker lifts it (SwiftColor), whatever the theme
    private static Color Colour(AccentSwatch swatch, bool dark) => swatch.Id == AccentPalette.WindowsId
        ? SwiftColor.ControlAccentColor.Resolve(dark ? ThemeVariant.Dark : ThemeVariant.Light)
        : Color.Parse(swatch.Hex(dark));

    private void Refresh(bool animate)
    {
        var dark = CzTheme.Dark(this);
        foreach (var fill in fills)
        {
            var colour = Colour(fill.Swatch, dark);
            if (animate) fill.Colour.To(colour, CzMotion.Ease(.32));
            else fill.Colour.Jump(colour);
        }
    }

    private void OnColorValuesChanged(object? sender, PlatformColorValues values)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Refresh(animate: true);
        else Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (TopLevel.GetTopLevel(this) is not null) Refresh(animate: true); });
    }

    private static TextBlock Label(string text)
    {
        var label = new TextBlock { Text = text, Width = 62, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(11.5, SwiftFont.Weight.Regular).Apply(label);
        label.Foreground = Chrome.SecondaryText.Brush(label);
        return label;
    }

    private static StackPanel Row(TextBlock label, CzSwatchRow swatches) =>
        new() { Orientation = Orientation.Horizontal, Spacing = 10, Children = { label, swatches } };
}
