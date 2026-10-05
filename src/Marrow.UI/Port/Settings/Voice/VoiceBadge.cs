// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-14): the `Recommended` badge of a model row (DESIGN gen.py:438-439 .vbadge).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Marrow.UI.Port.Settings;

/// <summary>
/// 10.5 / 15 medium text in the accent on 14 % of the accent, 0 6 padding, radius 5. It is always in the row (so the name never moves) and shown by fading in and scaling up from
/// .85 (opacity 0.25 s ease, scale 0.3 s ez); hidden it is fully transparent and not hit-testable.
/// </summary>
public sealed class VoiceBadge : Decorator
{
    private readonly CzNumber fade, scale;
    private bool shown;

    public VoiceBadge(string text)
    {
        var label = new TextBlock { Text = text, FontFamily = new FontFamily("fonts:Inter#Inter"), FontSize = 10.5, LineHeight = 15, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
        Child = label;
        Padding = new Thickness(6, 0);
        Opacity = 0;
        IsHitTestVisible = false;
        VerticalAlignment = VerticalAlignment.Center;
        RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        RenderTransform = new ScaleTransform(.85, .85);
        var motion = new CzMotion(this);
        fade = new CzNumber(motion, 0, v => Opacity = v);
        scale = new CzNumber(motion, .85, v => RenderTransform = new ScaleTransform(v, v));
        ActualThemeVariantChanged += (_, _) => Restyle();
        Restyle();
    }

    /// <summary>True while the badge is meant to show.</summary>
    public bool Shown => shown;

    public double BadgeOpacity => fade.Value;

    public double BadgeScale => scale.Value;

    public void SetShown(bool value)
    {
        if (shown == value) return;
        shown = value;
        fade.To(value ? 1 : 0, CzMotion.Ease(.25));
        scale.To(value ? 1 : .85, CzMotion.Ez(.3));
    }

    private void Restyle()
    {
        if (Child is TextBlock label) label.Foreground = new SolidColorBrush(CzTheme.Accent(this));
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var accent = CzTheme.Accent(this);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * .14), accent.R, accent.G, accent.B)), null, new RoundedRect(new Rect(Bounds.Size), 5));
        base.Render(context);
    }
}
