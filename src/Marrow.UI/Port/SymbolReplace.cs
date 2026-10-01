// PORT-ONLY. SF Symbols' `.contentTransition(.symbolEffect(.replace))` (ComposerView.swift:834, ModelsSettingsPage.swift:417,
// SidebarView.swift:2130, MarkdownView.swift:1490): when the symbol changes, the old one goes and the new one comes in
// as two steps (the "down-up" replace), instead of an instant swap. ED-182 had this drawn statically (MARROW_DEFERRED).
//
// Measured from the reference runtime (E:\Droppy\work\reference-video, 2-chat-use.mov 87.75-88.60 s, every frame at 6x,
// the composer's arrow.up becoming stop.fill when a turn starts; INDEX.md "Findings"): the arrow draws off over about
// 0.19 s (its shaft retracting into the head, 87.98-88.17 s), then the square appears at about 30 % of its size, already
// white, and grows to full size over about 0.18 s (88.19-88.37 s). SOURCE FACT: the two steps and their timing (video).
// ENGINEERING DECISION, UNVERIFIED: the old symbol's draw-off is approximated by shrinking it to half about its top
// edge while it fades (SF's per-layer retraction is not reproduced), the new one grows from 30 % on a quadratic ease-out
// and is opaque within its first 30 ms.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>SF Symbols' replace transition between two symbol images (see the file note).</summary>
public static class SymbolReplace
{
    internal const double OutSeconds = 0.19;
    internal const double InSeconds = 0.18;
    internal const double InStartScale = 0.3;
    internal const double OutEndScale = 0.5;
    internal const double InFadeSeconds = 0.03;

    /// <summary>
    /// Animates <paramref name="outgoing"/> away and then <paramref name="incoming"/> in (both already in the tree, the
    /// outgoing one over the incoming); <paramref name="done"/> runs at the end, to remove the outgoing one.
    /// </summary>
    public static void Play(Control outgoing, Control incoming, Action done)
    {
        var outScale = new ScaleTransform(1, 1);
        outgoing.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
        outgoing.RenderTransform = outScale;
        var inScale = new ScaleTransform(InStartScale, InStartScale);
        incoming.RenderTransformOrigin = RelativePoint.Center;
        incoming.RenderTransform = inScale;
        incoming.Opacity = 0;
        var start = SwiftClock.Now;
        var frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            var seconds = (SwiftClock.Now - start).TotalSeconds;
            var outProgress = Math.Clamp(seconds / OutSeconds, 0, 1);
            var shrink = 1 - (1 - OutEndScale) * outProgress * outProgress;
            outScale.ScaleX = shrink;
            outScale.ScaleY = shrink;
            outgoing.Opacity = 1 - outProgress;
            var inTime = seconds - OutSeconds;
            if (inTime > 0)
            {
                var linear = Math.Clamp(inTime / InSeconds, 0, 1);
                var eased = 1 - (1 - linear) * (1 - linear); // quadratic ease-out: 0.54/0.75/0.89 at 35/72/107 ms (video 0.5/0.7/0.85)
                var grow = InStartScale + (1 - InStartScale) * eased;
                inScale.ScaleX = grow;
                inScale.ScaleY = grow;
                incoming.Opacity = Math.Clamp(inTime / InFadeSeconds, 0, 1);
            }
            if (seconds >= OutSeconds + InSeconds)
            {
                incoming.RenderTransform = null;
                incoming.Opacity = 1;
                done();
                return;
            }
            frames.RequestFrame(tick!);
        };
        frames.RequestFrame(tick);
    }
}
