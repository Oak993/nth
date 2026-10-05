// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-14): the 2 px bar under a downloading model row (DESIGN gen.py:460-462 .mbar).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port;

namespace Marrow.UI.Port.Settings;

/// <summary>
/// A 2 pt track (`--trk`) inset 16 pt each side with an accent fill whose width follows the download linearly over 0.18 s (`transition: width .18s linear`); the bar fades in over
/// 0.3 s while a download runs and out when it ends.
/// </summary>
public sealed class VoiceProgressBar : Control
{
    private readonly CzNumber fraction, shown;

    public VoiceProgressBar()
    {
        var motion = new CzMotion(this);
        fraction = new CzNumber(motion, 0, _ => InvalidateVisual());
        shown = new CzNumber(motion, 0, v => Opacity = v);
        Opacity = 0;
        Margin = new Thickness(16, 0);
        IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The fill, 0 to 1, as drawn now.</summary>
    public double Fraction => fraction.Value;

    /// <summary>Where the fill is heading.</summary>
    public double Target => fraction.Target;

    /// <summary>The bar's opacity now.</summary>
    public double Visibility => shown.Value;

    /// <summary>True while a download runs.</summary>
    public void SetActive(bool active) => shown.To(active ? 1 : 0, CzMotion.Ease(.3));

    /// <summary>The download's fraction: the fill moves to it over 0.18 s, linear.</summary>
    public void SetFraction(double value)
    {
        value = Math.Clamp(value, 0, 1);
        if (value < fraction.Target) fraction.Jump(value); // a new download starts from the left; the fill never runs backwards across the bar
        else fraction.To(value, Animation.Linear(.18));
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, 2);

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        context.DrawRectangle(CzTheme.Brush(CzTheme.Token(this, "trk")), null, new RoundedRect(new Rect(0, 0, width, 2), 1));
        if (fraction.Value > 0)
            context.DrawRectangle(new SolidColorBrush(CzTheme.Accent(this)), null, new RoundedRect(new Rect(0, 0, width * fraction.Value, 2), 1));
    }
}
