// PORT-ONLY: Tactus pill v2, the first frames (OD-0145, ED-NEW-pcctl-24; DESIGN.md 10.6 "First frames": "When an overlay is
// created, the card's visuals, its brushes and the glass effect are drawn once into a 1 x 1 off-screen target, so the
// first expand does not pay for first-time GPU work"; the canvas's first expand stalled 67 to 88 ms in a fresh profile).
// Two passes, both invisible: the content is drawn once into a 1 x 1 bitmap (text shaping, glyph and path caches), and
// once in the overlay itself for two composition frames at 1 % opacity in a window that never takes input (the spike's
// stand-in for the GPU pipelines; NP-16 measures the first expand with and without it). It is not a fade: nothing the eye
// can see appears or leaves.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Marrow.UI.Port.PcControl;

public static class PillWarmup
{
    /// <summary>The in-window pass's opacity: drawn by the GPU, below what the eye can see.</summary>
    public const double Opacity = 0.01;

    /// <summary>Composition frames the in-window pass stays for.</summary>
    public const int Frames = 2;

    /// <summary>Draws <paramref name="content"/> laid out at <paramref name="size"/> into a 1 x 1 bitmap.</summary>
    public static void Shape(Control content, Size size)
    {
        content.Measure(size);
        content.Arrange(new Rect(size));
        using var target = new RenderTargetBitmap(new PixelSize(1, 1));
        target.Render(content);
    }

    /// <summary>
    /// Adds <paramref name="content"/> to <paramref name="host"/> (a click-through window's panel) at 1 % opacity, not
    /// hit-testable and hidden from assistive technology, and removes it after <see cref="Frames"/> composition frames.
    /// </summary>
    /// <param name="frames">The frame source (the views' seam, ED-083); null takes <see cref="FrameClock.Source"/>.</param>
    public static void Draw(Panel host, Control content, Action? done = null, IFrameSource? frames = null)
    {
        content.Opacity = Opacity;
        content.IsHitTestVisible = false;
        Avalonia.Automation.AutomationProperties.SetAccessibilityView(content, Avalonia.Automation.AccessibilityView.Raw);
        host.Children.Add(content);
        if (TopLevel.GetTopLevel(host) is null)
        {
            host.Children.Remove(content);
            done?.Invoke();
            return;
        }
        // An animation-frame callback runs before its frame is drawn: Frames + 1 callbacks leave it drawn in Frames frames.
        var source = frames ?? FrameClock.Source.Current;
        var left = Frames + 1;
        void Next(TimeSpan _)
        {
            if (--left > 0)
            {
                source.RequestFrame(Next);
                return;
            }
            host.Children.Remove(content);
            done?.Invoke();
        }
        source.RequestFrame(Next);
    }
}
