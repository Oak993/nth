// PORT-ONLY. SwiftUI's .popover is an NSPopover, which the system animates in: the popover grows out of its arrow and
// fades in. The port's Popup opened at full size at once.
//
// Measured from the reference runtime (E:\Droppy\work\reference-video, 2-chat-use.mov 33.70-33.95 s, every frame, the
// effort popover opening above its chip; INDEX.md "Findings"): the card's width relative to its final width is about
// 0.22 at 25 ms, 0.34 at 40 ms, 0.51 at 62 ms, 0.82 at 115 ms, 0.96 at 165 ms and 1.0 by 218 ms, growing out of the
// arrow's edge, and the card is all but opaque by about 60 ms. SOURCE FACT: the animation (video). ENGINEERING
// DECISION, UNVERIFIED fit: a critically damped spring of response 0.21 s (about 30 rad/s) from a tenth of the size,
// opacity linear over the first 60 ms. The close animation is not ported yet (open item): a light dismiss is closed
// by the Popup itself.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>NSPopover's appearance animation for a popup's content (see the file note).</summary>
public static class PopoverAppear
{
    internal const double StartScale = 0.1;
    internal const double FadeSeconds = 0.06;
    internal static readonly Animation Grow = Animation.Spring(response: 0.21, dampingFraction: 1.0);

    /// <summary>
    /// Animates <paramref name="content"/> in from its arrow's edge: the side facing the anchor, given the popup's
    /// <paramref name="placement"/> (below the anchor the arrow is on top, above it at the bottom).
    /// </summary>
    public static void Play(Control content, PlacementMode placement)
    {
        if (content.RenderTransform is not null && content.RenderTransform is not ScaleTransform) return; // a site's own transform is kept
        content.RenderTransformOrigin = Origin(placement);
        var scale = new ScaleTransform(StartScale, StartScale);
        content.RenderTransform = scale;
        content.Opacity = 0;
        var start = SwiftClock.Now;
        var frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            var seconds = (SwiftClock.Now - start).TotalSeconds;
            var done = seconds >= Grow.Duration;
            var progress = done ? 1 : Grow.Progress(seconds);
            var size = StartScale + (1 - StartScale) * progress;
            scale.ScaleX = size;
            scale.ScaleY = size;
            content.Opacity = Math.Clamp(seconds / FadeSeconds, 0, 1);
            if (done) { content.RenderTransform = null; content.Opacity = 1; return; }
            frames.RequestFrame(tick!);
        };
        frames.RequestFrame(tick);
    }

    internal static RelativePoint Origin(PlacementMode placement) => placement switch
    {
        PlacementMode.Top or PlacementMode.TopEdgeAlignedLeft or PlacementMode.TopEdgeAlignedRight => new RelativePoint(0.5, 1, RelativeUnit.Relative),
        PlacementMode.Left or PlacementMode.LeftEdgeAlignedTop or PlacementMode.LeftEdgeAlignedBottom => new RelativePoint(1, 0.5, RelativeUnit.Relative),
        PlacementMode.Right or PlacementMode.RightEdgeAlignedTop or PlacementMode.RightEdgeAlignedBottom => new RelativePoint(0, 0.5, RelativeUnit.Relative),
        _ => new RelativePoint(0.5, 0, RelativeUnit.Relative), // below the anchor (and anything else): the arrow is on top
    };
}
