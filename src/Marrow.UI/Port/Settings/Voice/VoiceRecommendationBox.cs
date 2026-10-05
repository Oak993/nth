// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-14): the recommendation box of the hardware card (DESIGN gen.py:426-430 .hwrec).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.UI.Port.Settings;

/// <summary>A tile (`--tile`: white at .07 in Dark, black at .05 in Light) with radius 10, margin 0 12 12 and padding 10 12 12, holding the recommendation's name, reason and buttons.</summary>
public sealed class VoiceRecommendationBox : Decorator
{
    public VoiceRecommendationBox(Control content)
    {
        Child = content;
        Margin = new Thickness(12, 0, 12, 12);
        Padding = new Thickness(12, 10, 12, 12);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(CzTheme.Brush(CzTheme.Token(this, "tile")), null, new RoundedRect(new Rect(Bounds.Size), 10));
        base.Render(context);
    }
}
