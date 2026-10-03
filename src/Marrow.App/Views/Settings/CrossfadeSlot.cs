using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Views.Settings;

/// <summary>
/// PORT-ONLY (OD-0076, ED-261 T1-T3): the place where content that swaps crossfades. <see cref="Show"/> puts a new
/// piece of content in place of the one on show: under an animation the old one fades out where it stood while the new one fades in
/// (SwiftUI's default opacity transition of a branch that is removed and inserted, AboutUpdateViews.swift:671), on the frame clock,
/// and the slot's own size eases from where it is to the new content's size over the same animation (a label whose width changes
/// eases its container's width). With no animation (the first content, Reduce Motion, a rebuild that looks the same and meets no swap)
/// the content is simply there. Each layer's fade goes on from the opacity it has, so a second swap that meets one in flight never jumps
/// a layer (the one that was coming in leaves from the opacity it had reached). The content that is leaving takes no input and is taken
/// out of the tree when it has gone. Each piece is drawn at its own size, centred in the slot (the pattern of NewThreadPrompt's SwapSlot).
/// </summary>
internal sealed class CrossfadeSlot : Panel
{
    private sealed class Fade
    {
        public double From, To;
        public ContinuousInstant Start;
        public Animation Animation = null!;
        public bool Leaving;
    }

    private readonly Dictionary<Control, Fade> fades = [];
    private Control? live;
    private Size presented;
    private Size from;
    private Animation? glide;
    private ContinuousInstant glideStart;
    private IFrameSource? frames;
    private Action<TimeSpan>? pending;

    /// <summary>The content on show (the one a swap brought in last).</summary>
    public Control? Live => live;

    /// <summary>Whether a swap is still running: content is fading, or the slot is still easing to its size.</summary>
    public bool Settling => glide is not null || fades.Count > 0;

    /// <summary>Puts <paramref name="next"/> on show in place of the content on show: crossfaded under <paramref name="animation"/>, at once with none.</summary>
    public void Show(Control next, Animation? animation)
    {
        var previous = live;
        if (ReferenceEquals(previous, next)) return;
        live = next;
        if (previous is null || animation is null)
        {
            foreach (var layer in Children.ToArray()) { if (!ReferenceEquals(layer, next)) Children.Remove(layer); }
            fades.Clear(); glide = null; Halt();
            next.Opacity = 1; next.IsHitTestVisible = true;
            if (!Children.Contains(next)) Children.Add(next);
            InvalidateMeasure();
            return;
        }

        previous.IsHitTestVisible = false; // what is leaving takes no input
        from = presented;
        var now = SwiftClock.Now;
        fades[previous] = new Fade { From = previous.Opacity, To = 0, Start = now, Animation = animation, Leaving = true };
        next.Opacity = 0;
        Children.Add(next);
        fades[next] = new Fade { From = 0, To = 1, Start = now, Animation = animation };
        glide = animation; glideStart = now;
        Run();
        InvalidateMeasure();
    }

    private void Run()
    {
        if (pending is not null) return;
        frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            pending = null;
            var now = SwiftClock.Now;
            foreach (var (layer, fade) in fades.ToArray())
            {
                var elapsed = now - fade.Start;
                var done = elapsed.Ticks + TimeSpan.TicksPerMillisecond >= SwiftTask.Seconds(fade.Animation.Duration).Ticks; // ED-117 (2)
                layer.Opacity = done ? fade.To : fade.From + (fade.To - fade.From) * fade.Animation.Progress(elapsed.TotalSeconds);
                if (!done) continue;
                fades.Remove(layer);
                if (fade.Leaving) Children.Remove(layer);
            }
            if (glide is { } animation && (now - glideStart).Ticks + TimeSpan.TicksPerMillisecond >= SwiftTask.Seconds(animation.Duration).Ticks) glide = null;
            if (fades.Count > 0 || glide is not null) { pending = tick; frames!.RequestFrame(tick!); }
            InvalidateMeasure();
        };
        pending = tick;
        frames.RequestFrame(tick);
    }

    private void Halt()
    {
        if (pending is { } frame) frames?.CancelFrame(frame);
        pending = null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        if (live is null) { presented = default; return presented; }
        var target = live.DesiredSize;
        if (glide is { } animation)
        {
            var seconds = (SwiftClock.Now - glideStart).TotalSeconds;
            var progress = animation.Progress(Math.Min(seconds, animation.Duration));
            presented = new Size(from.Width + (target.Width - from.Width) * progress, from.Height + (target.Height - from.Height) * progress);
        }
        else presented = target;
        return presented;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            child.Arrange(new Rect((finalSize.Width - size.Width) / 2, (finalSize.Height - size.Height) / 2, size.Width, size.Height));
        }
        return finalSize;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // Out of the tree nothing is seen: the swap ends where it was going (the leaving content goes, the content on show is whole).
        Halt();
        foreach (var layer in Children.ToArray())
        {
            if (ReferenceEquals(layer, live)) { layer.Opacity = 1; layer.IsHitTestVisible = true; }
            else Children.Remove(layer);
        }
        fades.Clear(); glide = null;
    }
}
