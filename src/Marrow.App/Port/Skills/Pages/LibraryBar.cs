// PORT-ONLY: first-release feature unit SK-15 (DL-17). The dialog's bar, drawn in the page's own tokens (a track at the label colour's 0.12 and the accent over it).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Pages;

/// <summary>The dialog's bar: a 4 point track and a fill that eases to each new <see cref="Value"/> on the frame clock (a step of progress is never a jump).</summary>
internal sealed class LibraryBar : Control
{
    private readonly IBrush track;
    private readonly IBrush fill;
    private readonly AnimatedScalar shown;

    public LibraryBar()
    {
        Height = 4;
        track = ChromeTokens.Overlay(0.12).Brush(this);
        fill = ChromeTokens.Accent.Brush(this);
        shown = new AnimatedScalar(this, 0, _ => InvalidateVisual());
    }

    /// <summary>The fraction the fill is drawn at now (it follows <see cref="Target"/>).</summary>
    public double Drawn => shown.Value;

    public double Target { get; private set; }

    public void Set(double value, bool animated)
    {
        var clamped = Math.Clamp(value, 0, 1);
        if (Target == clamped) return;
        Target = clamped;
        shown.Set(clamped, animated ? LibraryUi.Ease(this, LibraryUi.Glide) : null);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var height = Math.Min(Bounds.Height, 4);
        var top = (Bounds.Height - height) / 2;
        context.DrawRectangle(track, null, new RoundedRect(new Rect(0, top, Bounds.Width, height), height / 2));
        if (shown.Value > 0) context.DrawRectangle(fill, null, new RoundedRect(new Rect(0, top, Math.Max(height, Bounds.Width * shown.Value), height), height / 2));
    }
}
