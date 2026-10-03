using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Views.Settings;

/// <summary>
/// PORT-ONLY (OD-0076, ED-NEW-aboutupdateviews T1-T3): the place where content that swaps crossfades. <see cref="Show"/> puts a new
/// piece of content in place of the one on show: under an animation the old one fades out where it stood while the new one fades in
/// (SwiftUI's default opacity transition of a branch that is removed and inserted, <see cref="SwiftTransition.Insert"/> and
/// <see cref="SwiftTransition.Remove(Panel, Control)"/>, on the frame clock), and the slot's own size eases from the old content's
/// size to the new one's over the same animation (a label whose width changes eases its container's width). With no animation (the
/// first content, Reduce Motion, a rebuild that looks the same) the content is simply there. The content that is leaving takes no input.
/// Each piece is drawn at its own size, centred in the slot (the pattern of NewThreadPrompt's SwapSlot).
/// </summary>
internal sealed class CrossfadeSlot : Panel
{
    private Control? live;
    private Size presented;
    private Size from;
    private Size target;
    private Animation? glide;
    private ContinuousInstant glideStart;
    private IFrameSource? frames;
    private Action<TimeSpan>? pending;

    /// <summary>The content on show (the one a swap brought in last).</summary>
    public Control? Live => live;

    /// <summary>Whether a swap is still running: content is leaving or the slot is still easing to its size.</summary>
    public bool Settling => glide is not null || pending is not null;

    /// <summary>Puts <paramref name="next"/> on show in place of the content on show: crossfaded under <paramref name="animation"/>, at once with none.</summary>
    public void Show(Control next, Animation? animation)
    {
        var previous = live;
        if (ReferenceEquals(previous, next)) return;
        live = next;
        if (previous is null || animation is null)
        {
            if (previous is not null) Children.Remove(SwiftTransition.Container(previous));
            Children.Add(next);
            StopGlide();
            InvalidateMeasure();
            return;
        }

        previous.IsHitTestVisible = false; // what is leaving takes no input
        from = presented;
        Transaction.WithAnimation(animation, () =>
        {
            SwiftTransition.Remove(this, previous);
            SwiftTransition.Insert(this, Children.Count, next.Transition(SwiftTransition.Opacity));
        });
        StartGlide(animation);
        InvalidateMeasure();
    }

    private void StartGlide(Animation animation)
    {
        StopGlide();
        glide = animation;
        glideStart = SwiftClock.Now;
        frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            pending = null;
            var elapsed = SwiftClock.Now - glideStart;
            if (elapsed.Ticks + TimeSpan.TicksPerMillisecond >= SwiftTask.Seconds(animation.Duration).Ticks) glide = null; // ED-117 (2)
            else { pending = tick; frames!.RequestFrame(tick!); }
            InvalidateMeasure();
        };
        pending = tick;
        frames.RequestFrame(tick);
    }

    private void StopGlide()
    {
        if (pending is { } frame) frames?.CancelFrame(frame);
        pending = null;
        glide = null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        if (live is null) { presented = default; return presented; }
        target = live.DesiredSize;
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

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopGlide();
    }
}
