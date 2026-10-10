// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-14): the hardware card's capsule (DESIGN gen.py:409-414 .hwgo, voice.js:66-69).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Marrow.UI.Port.Settings;

/// <summary>
/// The `Check my hardware` / `Checking` / `Check again` capsule: a 26 pt capsule (CzButton) whose label crossfades and whose width eases to the new label over 0.3 s ez (`.czbtn .xf`),
/// with a spinner that grows in before the label while a check runs (width 0.25 s ez, opacity 0.2 s) and takes the room back after. While busy it is inert
/// (`.hwgo.busy{pointer-events:none}`, aria-disabled).
/// </summary>
public sealed class VoiceCheckButton : CzButton
{
    private const double SpinnerSize = 11;
    private const double SpinnerGap = 5;

    private readonly Nth.NthSpinner spinner = new() { Width = SpinnerSize, Height = SpinnerSize, VerticalAlignment = VerticalAlignment.Center }; // step 1 B, drawn at the design's 11 (the slot's room maths counts 11)
    private readonly Decorator spinnerSlot;
    private readonly CzNumber grow, fade, labelWidth;
    private bool busy;

    /// <summary>The label, crossfading.</summary>
    public CzCrossfadeText Label { get; }

    /// <summary>The label's width now (eased).</summary>
    public double LabelWidth => labelWidth.Value;

    /// <summary>The room the spinner takes now, 0 at rest and 11 + the gap while busy.</summary>
    public double SpinnerRoom => grow.Value * (SpinnerSize + SpinnerGap);

    /// <summary>The spinner's opacity now (0.85 when fully shown).</summary>
    public double SpinnerOpacity => fade.Value * .85;

    public VoiceCheckButton(string label) : base(label)
    {
        Label = new CzCrossfadeText(label) { FontSize = 12, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
        spinnerSlot = new Decorator { Child = spinner, Width = 0, Opacity = 0, ClipToBounds = false, VerticalAlignment = VerticalAlignment.Center };
        grow = new CzNumber(Motion, 0, v => { spinnerSlot.Width = v * SpinnerSize; spinnerSlot.Margin = new Thickness(0, 0, v * SpinnerGap, 0); });
        fade = new CzNumber(Motion, 0, v => spinnerSlot.Opacity = v * .85);
        labelWidth = new CzNumber(Motion, Math.Ceiling(Measure(label)), v => Label.Width = v);
        Label.Width = labelWidth.Value;
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(spinnerSlot);
        line.Children.Add(Label);
        Child = line;
    }

    /// <summary>True while a check runs: the spinner grows in and the capsule is inert.</summary>
    public bool Busy
    {
        get => busy;
        set
        {
            if (busy == value) return;
            busy = value;
            IsEnabled = !value;
            Focusable = !value;
            grow.To(value ? 1 : 0, CzMotion.Ez(.25));
            fade.To(value ? 1 : 0, CzMotion.Ease(.2));
        }
    }

    /// <summary>Shows a new label: it crossfades and the capsule's width eases to it.</summary>
    public void SetLabel(string text)
    {
        if (Label.Text == text) return;
        Label.SetText(text);
        labelWidth.To(Math.Ceiling(Measure(text)), CzMotion.Ez(.3));
        AutomationProperties.SetName(this, text);
    }

    // The width of the label's own text, measured as the menu button measures its value (12 medium Inter).
    private static double Measure(string text)
    {
        var probe = new TextBlock { Text = text, FontFamily = new FontFamily("fonts:Inter#Inter"), FontSize = 12, FontWeight = FontWeight.Medium };
        probe.Measure(Size.Infinity);
        return probe.DesiredSize.Width;
    }
}
