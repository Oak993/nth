// PORT-ONLY. SF Symbols' `.symbolEffect(.bounce, value:)` (EffortSlider.swift:670, UsageViews.swift:267): each time the
// value changes the symbol bounces once. ED-182 had it recorded and drawn statically (MARROW_DEFERRED).
//
// Measured from the reference runtime (E:\Droppy\work\reference-video, 2-chat-use.mov 47.55-48.20 s, every frame at 4x,
// the effort card's fast-mode bolt switched on): the gold bolt is about 13-14 px tall at rest, about 19 px at its peak
// some 0.10 s after the click, and back at rest about 0.30 s after it. SOURCE FACT: one swell and settle of that size
// and timing (video). ENGINEERING DECISION, UNVERIFIED fit: scale to 1.35 on an ease-out over 0.10 s, back to 1 on an
// ease-in-out over 0.20 s, about the symbol's centre.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>SF Symbols' bounce effect (see the file note).</summary>
public static class SymbolBounce
{
    internal const double Peak = 1.35;
    internal const double UpSeconds = 0.10;
    internal const double DownSeconds = 0.20;

    /// <summary>Bounces <paramref name="symbol"/> once.</summary>
    public static void Play(Control symbol)
    {
        var scale = new ScaleTransform(1, 1);
        symbol.RenderTransformOrigin = RelativePoint.Center;
        symbol.RenderTransform = scale;
        var start = SwiftClock.Now;
        var frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            var seconds = (SwiftClock.Now - start).TotalSeconds;
            double size;
            if (seconds < UpSeconds)
            {
                var p = seconds / UpSeconds;
                size = 1 + (Peak - 1) * (1 - (1 - p) * (1 - p)); // ease-out
            }
            else if (seconds < UpSeconds + DownSeconds)
            {
                var p = (seconds - UpSeconds) / DownSeconds;
                var eased = p < 0.5 ? 2 * p * p : 1 - 2 * (1 - p) * (1 - p); // ease-in-out
                size = Peak + (1 - Peak) * eased;
            }
            else
            {
                symbol.RenderTransform = null;
                return;
            }
            scale.ScaleX = size;
            scale.ScaleY = size;
            frames.RequestFrame(tick!);
        };
        frames.RequestFrame(tick);
    }
}
