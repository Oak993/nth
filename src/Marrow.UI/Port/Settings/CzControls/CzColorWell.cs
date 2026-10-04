// PORT-ONLY CZ-1: labelled 20px well (CZ-12.6); the CZ-2 picker is supplied by the caller, never imitated here.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models.ComposerLights;
namespace Marrow.UI.Port.Settings;

public sealed class CzColorWell : CzButton
{
    private readonly Border dot;
    private readonly CzColor fill;
    public string Hex { get; private set; }
    /// <summary>The colour the well's dot is eased to (its alpha included).</summary>
    public Color Fill => fill.Target;
    public event Action<string>? ColorChanged;
    public CzColorWell(string accessibleName, string label, string hex, Action<CzColorWell> openPicker) : base(accessibleName)
    {
        ArgumentNullException.ThrowIfNull(openPicker);
        Hex = UiverseAuraOptions.NormalizedHex(hex) ?? throw new ArgumentException("Expected #RRGGBB or #RRGGBBAA colour.", nameof(hex));
        Height = 22; Padding = new Thickness(0); ChromeFill = false; Background = Brushes.Transparent;
        dot = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Read(Hex)),
            BorderThickness = new Thickness(.5), BorderBrush = CzTheme.Brush(CzTheme.Token(this, "rim")) };
        fill = new CzColor(Motion, Read(Hex), c => dot.Background = new SolidColorBrush(c));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(dot); row.Children.Add(new TextBlock { Text = label, FontFamily = new FontFamily("fonts:Inter#Inter"), FontSize = 12,
            Foreground = CzTheme.Text(this, true), VerticalAlignment = VerticalAlignment.Center }); Child = row;
        Invoked += () => openPicker(this);
    }
    protected override void Refresh()
    {
        base.Refresh();
        if (dot is not null) dot.BorderBrush = CzTheme.Brush(CzTheme.Token(this, "rim"));
        if (Child is StackPanel row && row.Children[1] is TextBlock label) label.Foreground = CzTheme.Text(this, true);
    }
    // OD-0099 Q-CZ-3 B: a custom colour is stored #RRGGBBAA (alpha last); Avalonia's Color.Parse reads eight digits as AARRGGBB.
    private static Color Read(string hex)
    {
        var rgb = Color.Parse(UiverseAuraOptions.OpaqueHex(hex));
        return Color.FromArgb((byte)Math.Round(UiverseAuraOptions.AlphaOf(hex) * 255), rgb.R, rgb.G, rgb.B);
    }

    public void SetColor(string hex)
    {
        if (!IsEffectivelyEnabled) return;
        Hex = UiverseAuraOptions.NormalizedHex(hex) ?? throw new ArgumentException("Expected #RRGGBB or #RRGGBBAA colour.", nameof(hex));
        fill.To(Read(Hex), CzMotion.Ease(.3)); ColorChanged?.Invoke(Hex);
    }
}
