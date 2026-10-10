// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-14), moved onto the nth motion pack step 3 option B (OD-0136 amendment 2, ED-NEW-nth-motion): the bar under a downloading model row
// (DESIGN gen.py:460-462 .mbar). The pack's bars are 4 px or 3 px; the row's old 2 px track is now the 3 px bar.
using Avalonia;
using Avalonia.Controls;
using Marrow.Core.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.UI.Port.Settings;

/// <summary>
/// The nth progress bar (3 px) inset 16 pt each side, whose fill springs to the download (the light passes inside it); the bar fades in over 0.3 s while a download runs and out when it ends, and is not drawn at all while it is out.
/// </summary>
public sealed class VoiceProgressBar : Decorator
{
    public const double BarHeight = 3;

    private readonly NthProgressBar bar = new() { Height = BarHeight };
    private readonly CzNumber shown;

    public VoiceProgressBar()
    {
        var motion = new CzMotion(this);
        shown = new CzNumber(motion, 0, v => { Opacity = v; bar.IsVisible = v > 0; });
        Opacity = 0;
        bar.IsVisible = false;
        Margin = new Thickness(16, 0);
        IsHitTestVisible = false;
        Child = bar;
    }

    /// <summary>The primitive (tests and pictures).</summary>
    public NthProgressBar Bar => bar;

    /// <summary>The fill, 0 to 1, as drawn now.</summary>
    public double Fraction => bar.Shown;

    /// <summary>Where the fill is heading.</summary>
    public double Target => bar.Value;

    /// <summary>The bar's opacity now.</summary>
    public double Visibility => shown.Value;

    /// <summary>True while a download runs.</summary>
    public void SetActive(bool active) => shown.To(active ? 1 : 0, CzMotion.Ease(.3));

    /// <summary>The download's fraction: the fill springs to it.</summary>
    public void SetFraction(double value)
    {
        value = Math.Clamp(value, 0, 1);
        if (value < bar.Value) bar.Jump(value); // a new download starts from the left; the fill never runs backwards across the bar
        else bar.Value = value;
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, BarHeight);
}
