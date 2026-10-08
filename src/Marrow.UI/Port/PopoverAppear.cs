// PORT-ONLY. SwiftUI's .popover is an NSPopover, which the system animates in: the popover grows out of its arrow and
// fades in. The port's Popup opened at full size at once.
//
// Measured from the reference runtime (E:\Droppy\work\reference-video, 2-chat-use.mov 33.70-33.95 s, every frame, the
// effort popover opening above its chip; INDEX.md "Findings"): the card's width relative to its final width is about
// 0.22 at 25 ms, 0.34 at 40 ms, 0.51 at 62 ms, 0.82 at 115 ms, 0.96 at 165 ms and 1.0 by 218 ms, growing out of the
// arrow's edge, and the card is all but opaque by about 60 ms. SOURCE FACT: the animation (video). ENGINEERING
// DECISION, UNVERIFIED fit: a critically damped spring of response 0.21 s (about 30 rad/s) from a tenth of the size,
// opacity linear over the first 60 ms. Closing (2-chat-use.mov 42.77-42.94 s, the model list after a choice): the card
// shrinks back into its arrow while fading, its relative size about 0.62 at 43 ms, 0.33 at 93 ms, 0.15 at 135 ms, gone
// by about 170 ms; fitted as a decay of time constant 0.08 s to a tenth and a linear fade over 0.17 s (UNVERIFIED fit).
// ED-NEW-T45: light dismissal goes through the same motion; replacing a motion cancels its outstanding frame.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>NSPopover's appearance animation for a popup's content (see the file note).</summary>
public static class PopoverAppear
{
    /// <summary>Opt-in Q-RM A sheet motion. Other popovers keep the measured scale by default.</summary>
    public static readonly AttachedProperty<bool> StillProperty = AvaloniaProperty.RegisterAttached<Control, bool>("Still", typeof(PopoverAppear));
    private static readonly AttachedProperty<int> FadeVersionProperty = AvaloniaProperty.RegisterAttached<Control, int>("FadeVersion", typeof(PopoverAppear));
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, Action> running = new();

    private static void Stop(Control content)
    {
        if (!running.TryGetValue(content, out var cancel)) return;
        running.Remove(content);
        cancel();
    }

    internal static void Cancel(Control content) => Stop(content);

    public static Action Fade(Control content, bool opening, Action? closed = null)
    {
        Stop(content);
        var version = content.GetValue(FadeVersionProperty) + 1;
        content.SetValue(FadeVersionProperty, version);
        if (!FrameClock.IsBound) { content.Opacity = opening ? 1 : 0; closed?.Invoke(); return () => { }; }
        var from = opening && content.Opacity >= 1 ? 0 : content.Opacity;
        content.Opacity = from;
        var start = SwiftClock.Now;
        var duration = opening ? FadeSeconds : CloseSeconds;
        var frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            if (content.GetValue(FadeVersionProperty) != version) return;
            var fraction = Math.Clamp((SwiftClock.Now - start).TotalSeconds / duration, 0, 1);
            content.Opacity = opening ? from + (1 - from) * fraction : from * (1 - fraction);
            if (fraction == 1) { running.Remove(content); closed?.Invoke(); return; }
            frames.RequestFrame(tick!);
        };
        frames.RequestFrame(tick);
        running.AddOrUpdate(content, () => { content.SetValue(FadeVersionProperty, version + 1); frames.CancelFrame(tick); });
        return () => { content.SetValue(FadeVersionProperty, version + 1); frames.CancelFrame(tick); content.Opacity = 1; };
    }
    internal const double StartScale = 0.1;
    internal const double FadeSeconds = 0.06;
    internal static readonly Animation Grow = Animation.Spring(response: 0.21, dampingFraction: 1.0);

    /// <summary>
    /// Animates <paramref name="content"/> in from its arrow's edge: the side facing the anchor, given the popup's
    /// <paramref name="placement"/> (below the anchor the arrow is on top, above it at the bottom).
    /// </summary>
    public static void Play(Control content, PlacementMode placement)
    {
        Stop(content);
        if (content.GetValue(StillProperty)) { Fade(content, opening: true); return; }
        if (content.RenderTransform is not null && content.RenderTransform is not ScaleTransform) return; // a site's own transform is kept
        content.RenderTransformOrigin = Origin(content, placement);
        var from = content.RenderTransform is ScaleTransform current ? current.ScaleX : StartScale;
        var fromOpacity = content.RenderTransform is ScaleTransform ? content.Opacity : 0;
        var scale = new ScaleTransform(from, from);
        content.RenderTransform = scale;
        content.Opacity = fromOpacity;
        var start = SwiftClock.Now;
        var frames = FrameClock.Source.Current;
        var version = content.GetValue(FadeVersionProperty) + 1;
        content.SetValue(FadeVersionProperty, version);
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            if (content.GetValue(FadeVersionProperty) != version) return;
            var seconds = (SwiftClock.Now - start).TotalSeconds;
            var done = seconds >= Grow.Duration;
            var progress = done ? 1 : Grow.Progress(seconds);
            var size = from + (1 - from) * progress;
            scale.ScaleX = size;
            scale.ScaleY = size;
            content.Opacity = fromOpacity + (1 - fromOpacity) * Math.Clamp(seconds / FadeSeconds, 0, 1);
            if (done) { running.Remove(content); content.RenderTransform = null; content.Opacity = 1; return; }
            frames.RequestFrame(tick!);
        };
        frames.RequestFrame(tick);
        running.AddOrUpdate(content, () => { content.SetValue(FadeVersionProperty, version + 1); frames.CancelFrame(tick); });
    }

    internal const double CloseSeconds = 0.17;
    internal const double CloseDecay = 0.08;

    /// <summary>
    /// Animates <paramref name="content"/> back into its arrow's edge, then calls <paramref name="closed"/>. The returned
    /// action stops it (the popover was presented again) and restores the content at once.
    /// </summary>
    public static Action Dismiss(Control content, PlacementMode placement, Action closed)
    {
        Stop(content);
        if (content.GetValue(StillProperty)) return Fade(content, opening: false, closed);
        if (content.RenderTransform is not null && content.RenderTransform is not ScaleTransform) { closed(); return () => { }; }
        content.RenderTransformOrigin = Origin(content, placement);
        var from = content.RenderTransform is ScaleTransform current ? current.ScaleX : 1;
        var scale = new ScaleTransform(from, from);
        content.RenderTransform = scale;
        var fromOpacity = content.Opacity;
        var start = SwiftClock.Now;
        var frames = FrameClock.Source.Current;
        var stopped = false;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            if (stopped) return;
            var seconds = (SwiftClock.Now - start).TotalSeconds;
            if (seconds >= CloseSeconds)
            {
                stopped = true;
                running.Remove(content);
                content.RenderTransform = null;
                content.Opacity = 1;
                closed();
                return;
            }
            var size = StartScale + (from - StartScale) * Math.Exp(-seconds / CloseDecay);
            scale.ScaleX = size;
            scale.ScaleY = size;
            content.Opacity = fromOpacity * (1 - seconds / CloseSeconds);
            frames.RequestFrame(tick!);
        };
        frames.RequestFrame(tick);
        running.AddOrUpdate(content, () => { stopped = true; frames.CancelFrame(tick); });
        return () =>
        {
            if (stopped) return;
            stopped = true;
            running.Remove(content);
            frames.CancelFrame(tick);
            content.RenderTransform = null;
            content.Opacity = 1;
        };
    }

    // OD-0095: a popover on its card grows out of the card's tip, wherever it stands (the card may have flipped); any
    // other content from the side facing its anchor.
    private static RelativePoint Origin(Control content, PlacementMode placement) =>
        content is PopoverCard card ? card.TipOrigin : Origin(placement);

    internal static RelativePoint Origin(PlacementMode placement) => placement switch
    {
        PlacementMode.Top or PlacementMode.TopEdgeAlignedLeft or PlacementMode.TopEdgeAlignedRight => new RelativePoint(0.5, 1, RelativeUnit.Relative),
        PlacementMode.Left or PlacementMode.LeftEdgeAlignedTop or PlacementMode.LeftEdgeAlignedBottom => new RelativePoint(1, 0.5, RelativeUnit.Relative),
        PlacementMode.Right or PlacementMode.RightEdgeAlignedTop or PlacementMode.RightEdgeAlignedBottom => new RelativePoint(0, 0.5, RelativeUnit.Relative),
        _ => new RelativePoint(0.5, 0, RelativeUnit.Relative), // below the anchor (and anything else): the arrow is on top
    };
}
