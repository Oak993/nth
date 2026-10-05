// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-14): the version button of a model row (DESIGN gen.py:440-444 .mver).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Marrow.UI.Port.Settings;

/// <summary>
/// `{Version}, {size}` (and `, English only`) in 12 / 16 secondary text with a chevron after it, 4 pt apart, no fill: under the pointer the text reads as the primary colour (opacity from .7 to 1
/// over 0.12 s ease: the secondary colour is the primary at about .7); the chevron turns 180 degrees over 0.3 s ez while the row's versions are open. The text crossfades when it changes.
/// </summary>
public sealed class VoiceVersionButton : CzButton
{
    private readonly CzNumber hover;

    /// <summary>The text, crossfading.</summary>
    public CzCrossfadeText Label { get; }

    /// <summary>The chevron.</summary>
    public CzChevron Chevron { get; }

    /// <summary>The content's opacity now (.7 at rest, 1 under the pointer).</summary>
    public double InkOpacity => hover.Value;

    public VoiceVersionButton(string text, string name) : base(name)
    {
        ChromeFill = false;
        Background = Brushes.Transparent;
        Padding = new Thickness(0);
        PressScale = 1;
        Height = double.NaN;
        MinHeight = 16;
        HorizontalAlignment = HorizontalAlignment.Left;
        Label = new CzCrossfadeText(text) { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Chevron = new CzChevron { VerticalAlignment = VerticalAlignment.Center, Opacity = 1 };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Opacity = .7, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(Label);
        line.Children.Add(Chevron);
        Child = line;
        hover = new CzNumber(Motion, .7, v => line.Opacity = v);
        PropertyChanged += (_, e) => { if (e.Property == IsPointerOverProperty) hover.To(IsPointerOver ? 1 : .7, CzMotion.Ease(.12)); };
    }

    /// <summary>Shows a new text (it crossfades) and the button's name follows it.</summary>
    public void SetText(string text)
    {
        Label.SetText(text);
        Avalonia.Automation.AutomationProperties.SetHelpText(this, text);
    }

    /// <summary>True while the row's versions are folded open: the chevron turns.</summary>
    public void SetOpen(bool open) => Chevron.SetOpen(open);
}
