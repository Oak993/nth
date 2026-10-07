// PORT-ONLY: locked page .hc, .snote, .mkd, .btn.sm. CSS inset rims are drawn inside the extent, without changing content padding.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Core.Models;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal enum OpinionSurfaceKind { Card, Note, Mark, Dot }
internal sealed class OpinionSurface(OpinionSurfaceKind kind, double radius, ThreadAccount? account = null) : Decorator
{
    protected override Size MeasureOverride(Size available)
    {
        Child?.Measure(new Size(Math.Max(0, available.Width - Padding.Left - Padding.Right), Math.Max(0, available.Height - Padding.Top - Padding.Bottom)));
        var size = Child?.DesiredSize ?? default;
        return new Size(size.Width + Padding.Left + Padding.Right, size.Height + Padding.Top + Padding.Bottom);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(Padding.Left, Padding.Top, Math.Max(0, finalSize.Width - Padding.Left - Padding.Right), Math.Max(0, finalSize.Height - Padding.Top - Padding.Bottom)));
        return finalSize;
    }
    public override void Render(DrawingContext context)
    {
        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var bounds = new Rect(Bounds.Size);
        var fill = kind switch
        {
            OpinionSurfaceKind.Card => dark ? Rgba(30, 30, 34, .66) : Rgba(250, 249, 251, .72),
            OpinionSurfaceKind.Note => dark ? Rgba(255, 255, 255, .07) : Rgba(0, 0, 0, .05),
            OpinionSurfaceKind.Dot => Tint(1),
            _ => Tint(.22),
        };
        context.DrawRectangle(new SolidColorBrush(fill), null, bounds, radius, radius);
        if (kind == OpinionSurfaceKind.Dot) return;
        if (kind == OpinionSurfaceKind.Card)
        {
            // CSS paints the first shadow on top of the second: edge 1px, rim .5px.
            Stroke(dark ? Rgba(0, 0, 0, .35) : Rgba(40, 30, 60, .14), 1);
            Stroke(dark ? Rgba(255, 255, 255, .16) : Rgba(255, 255, 255, .75), .5);
        }
        if (kind == OpinionSurfaceKind.Mark)
        {
            // CSS lists the rim first, so it paints above the white inset highlight.
            context.DrawRectangle(null, null, new RoundedRect(bounds, radius), new BoxShadows(new BoxShadow { IsInset = true, OffsetY = 1, Color = Rgba(255, 255, 255, .18) }));
            Stroke(Tint(.62), 1);
        }
        else if (kind == OpinionSurfaceKind.Note) Stroke(dark ? Rgba(255, 255, 255, .22) : Rgba(0, 0, 0, .22), .5);
        void Stroke(Color color, double width) { context.DrawRectangle(null, new Pen(new SolidColorBrush(color), width), bounds.Deflate(width / 2), Math.Max(0, radius - width / 2), Math.Max(0, radius - width / 2)); }
    }
    private Color Tint(double opacity)
    {
        // Locked CAT palette, matching the composer (ComposerCapsuleModel) per service/order.
        string[] colors = account?.Service switch
        {
            ProviderKind.Claude => ["#D97757", "#8E7CFF", "#2BB3A3"],
            ProviderKind.Codex => ["#10A37F", "#5B8DEF"],
            ProviderKind.OpenRouter => ["#4D6BFE", "#E0A030"],
            ProviderKind.Local => ["#8A94A6", "#B08D57"],
            _ => ["#8A94A6"],
        };
        var color = Color.Parse(colors[((account?.Number ?? 1) - 1) % colors.Length]);
        return Color.FromArgb((byte)Math.Round(opacity * 255), color.R, color.G, color.B);
    }
    private static Color Rgba(byte r, byte g, byte b, double opacity) => Color.FromArgb((byte)Math.Round(opacity * 255), r, g, b);
}
