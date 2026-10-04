// PORT-ONLY CZ-3 (customization-SPEC CZ-4.5.1, CZ-14.1; DESIGN gen.py .czdot): the maker's dot. 10 px, the maker's toned colour, a soft light from the upper left and a
// 0.5 px rim. Its colour eases over 0.35 s when the maker changes.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class PreviewDot : Control
{
    internal const double Size = 10;
    private static readonly IBrush Sheen = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromArgb(77, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), .62) },
    }.ToImmutable();
    private readonly CzColor fill;

    internal PreviewDot(Color initial)
    {
        Width = Height = Size;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        IsHitTestVisible = false;
        fill = new CzColor(new CzMotion(this), initial, _ => InvalidateVisual());
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The colour now drawn (mid-ease while it changes).</summary>
    internal Color Value => fill.Value;

    /// <summary>The colour it is easing to.</summary>
    internal Color Target => fill.Target;

    /// <summary>The maker changed: ease to its colour over 0.35 s ease.</summary>
    internal void Ease(Color to) => fill.To(to, CzMotion.Ease(.35));

    /// <summary>The theme changed: the new colour at once (the Settings window's own turnover carries the eye).</summary>
    internal void Set(Color to) => fill.Jump(to);

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(0, 0, Size, Size);
        context.DrawEllipse(new SolidColorBrush(fill.Value), null, rect);
        context.DrawEllipse(Sheen, null, rect);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(CzTheme.Token(this, "rim")), .5), rect.Deflate(.25));
    }
}
