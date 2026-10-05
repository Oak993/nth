// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-13 B; first-release voice typing, VT-14): the 20 pt round glyph buttons of a model row (DESIGN gen.py:456-459 .mx).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.UI.Port.Settings;

/// <summary>
/// A 20 pt round button with a small glyph: the `--cap` fill (`--caph` under the pointer) and the secondary text colour (the primary one under the pointer), each changing over
/// 0.12 s ease; a 2 px accent ring on keyboard focus (CzButton). The cancel cross and the trash can are this button with another glyph.
/// </summary>
public abstract class VoiceGlyphButton : CzButton
{
    private readonly CzColor fill, ink;

    protected VoiceGlyphButton(string name) : base(name)
    {
        Width = 20;
        Height = 20;
        Padding = new Thickness(0);
        CornerRadius = new CornerRadius(10);
        ChromeFill = false;
        Background = Brushes.Transparent;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        fill = new CzColor(Motion, FillTarget(), _ => InvalidateVisual());
        ink = new CzColor(Motion, InkTarget(), _ => InvalidateVisual());
        Child = new Control { IsHitTestVisible = false };
        ActualThemeVariantChanged += (_, _) => Restyle(animated: false);
    }

    /// <summary>The fill now.</summary>
    public Color FillNow => fill.Value;

    /// <summary>The glyph's colour now.</summary>
    public Color InkNow => ink.Value;

    /// <summary>The fill it moves to: `--cap`, `--caph` under the pointer.</summary>
    protected virtual Color FillTarget() => CzTheme.Token(this, IsPointerOver ? "caph" : "cap");

    /// <summary>The glyph colour it moves to: secondary text, primary under the pointer.</summary>
    protected virtual Color InkTarget() => ((SolidColorBrush)CzTheme.Text(this, !IsPointerOver)).Color;

    /// <summary>Moves the fill and the glyph to where the state says they belong (0.12 s ease).</summary>
    protected void Restyle(bool animated = true)
    {
        if (animated)
        {
            fill.To(FillTarget(), CzMotion.Ease(.12));
            ink.To(InkTarget(), CzMotion.Ease(.12));
        }
        else
        {
            fill.Jump(FillTarget());
            ink.Jump(InkTarget());
        }
    }

    protected override void Refresh()
    {
        base.Refresh();
        if (fill is not null) Restyle();
    }

    /// <summary>Draws the glyph in <paramref name="colour"/> inside the 20 pt circle.</summary>
    protected abstract void DrawGlyph(DrawingContext context, Color colour);

    public override void Render(DrawingContext context)
    {
        context.DrawEllipse(new SolidColorBrush(fill.Value), null, new Point(10, 10), 10, 10);
        DrawGlyph(context, ink.Value);
        base.Render(context);
    }
}
