// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-14): the primary capsule of the recommendation box (DESIGN gen.py:406-408 .czbtn.pri).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.UI.Port.Settings;

/// <summary>
/// `Use recommended`: the capsule filled with the accent, a white top light fading over 60 % of the height (rgba(255,255,255,.18) to 0), an inset 1 px highlight at the top (.25) and a
/// 1 pt soft shadow below; the label is white, and #06121C on the dark window (`.czwin.wd .czbtn.pri`). Hover lifts it 8 % (`filter: brightness(1.08)`).
/// </summary>
public sealed class VoicePrimaryButton : CzButton
{
    private static readonly Color DarkWindowLabel = Color.FromRgb(0x06, 0x12, 0x1C);

    public VoicePrimaryButton(string label) : base(label)
    {
        ChromeFill = false;
        Background = Brushes.Transparent;
        PropertyChanged += (_, e) => { if (e.Property == IsPointerOverProperty) InvalidateVisual(); };
        ActualThemeVariantChanged += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>The fill now: the accent, lifted 8 % while the pointer is over it.</summary>
    public Color Fill()
    {
        var accent = CzTheme.Accent(this);
        return IsPointerOver ? Color.FromRgb(Lift(accent.R), Lift(accent.G), Lift(accent.B)) : accent;
    }

    /// <summary>The label's colour: white, or #06121C on the dark window.</summary>
    public Color LabelColor() => CzTheme.Dark(this) ? DarkWindowLabel : Colors.White;

    private static byte Lift(byte channel) => (byte)Math.Min(255, Math.Round(channel * 1.08));

    protected override void Refresh()
    {
        base.Refresh();
        if (Child is TextBlock text) text.Foreground = new SolidColorBrush(LabelColor());
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var radius = CornerRadius.TopLeft;
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(51, 0, 0, 0)), null, new RoundedRect(bounds.Translate(new Vector(0, 1)), radius)); // 0 1px 2px rgba(0,0,0,.2)
        context.DrawRectangle(new SolidColorBrush(Fill()), null, new RoundedRect(bounds, radius));
        var light = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, .6, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(46, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) },
        };
        context.DrawRectangle(light, null, new RoundedRect(bounds, radius));
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)), 1), new Point(radius * .6, .5), new Point(bounds.Width - radius * .6, .5)); // inset 0 1px 0 rgba(255,255,255,.25)
        base.Render(context);
    }
}
