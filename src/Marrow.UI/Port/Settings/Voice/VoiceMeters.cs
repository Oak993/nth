// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-14): a model row's two meters (DESIGN gen.py:445-449 .meters, .mtr, .mdots).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Marrow.UI.Port.Settings;

/// <summary>
/// `Speed` and `Accuracy`: two lines 3 px apart in a 112 px column, each its name (11 / 13, secondary) at the leading edge and five 5 px dots, 3 px apart, at the trailing edge; the
/// first <c>n</c> dots are lit (secondary text colour) and the rest are the track colour. One image to a screen reader, named `{name}: speed {n} of 5, accuracy {m} of 5`.
/// </summary>
public sealed class VoiceMeters : Grid
{
    /// <summary>The column's width (DESIGN gen.py:445).</summary>
    public const double ColumnWidth = 112;

    public VoiceMeters(int speed, int accuracy, string name)
    {
        Width = ColumnWidth;
        RowDefinitions = new RowDefinitions("Auto,3,Auto");
        Speed = new VoiceDots(speed);
        Accuracy = new VoiceDots(accuracy);
        Add(0, "Speed", Speed);
        Add(2, "Accuracy", Accuracy);
        AutomationProperties.SetName(this, name);
        Focusable = false;
    }

    /// <summary>The speed dots.</summary>
    public VoiceDots Speed { get; }

    /// <summary>The accuracy dots.</summary>
    public VoiceDots Accuracy { get; }

    private void Add(int row, string text, VoiceDots dots)
    {
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto") };
        var label = new TextBlock { Text = text, FontFamily = new FontFamily("fonts:Inter#Inter"), FontSize = 11, LineHeight = 13, VerticalAlignment = VerticalAlignment.Center };
        label.Foreground = CzTheme.Text(this, true);
        ActualThemeVariantChanged += (_, _) => label.Foreground = CzTheme.Text(this, true);
        AutomationProperties.SetAccessibilityView(label, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(dots, AccessibilityView.Raw);
        dots.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(label, 0);
        SetColumn(dots, 2);
        line.Children.Add(label);
        line.Children.Add(dots);
        SetRow(line, row);
        Children.Add(line);
    }
}
