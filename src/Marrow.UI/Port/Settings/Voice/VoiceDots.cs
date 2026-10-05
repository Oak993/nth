// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-14): five dots of a meter (DESIGN gen.py:448-449 .mdots).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.UI.Port.Settings;

/// <summary>Five 5 pt dots 3 pt apart; the first <see cref="Lit"/> are the secondary text colour and the rest the track colour (`--trk`).</summary>
public sealed class VoiceDots : Control
{
    public const int Count = 5;
    public const double DotSize = 5;
    public const double Gap = 3;

    public VoiceDots(int lit)
    {
        Lit = Math.Clamp(lit, 0, Count);
        IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>How many dots are lit.</summary>
    public int Lit { get; }

    protected override Size MeasureOverride(Size availableSize) => new(Count * DotSize + (Count - 1) * Gap, DotSize);

    public override void Render(DrawingContext context)
    {
        var on = CzTheme.Text(this, true);
        var off = CzTheme.Brush(CzTheme.Token(this, "trk"));
        for (var i = 0; i < Count; i++)
            context.DrawEllipse(i < Lit ? on : off, null, new Point(i * (DotSize + Gap) + DotSize / 2, DotSize / 2), DotSize / 2, DotSize / 2);
    }
}
