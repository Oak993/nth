// PORT-ONLY: OD-0099 Minimize A, sd-page.css:456-493; --ez and reduced duration in skills-downloads.html:27,181.
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Skills.Pages;

/// <summary>Independent CSS scalar transitions on the app frame clock. Interruptions start from the drawn value.</summary>
internal sealed class SkillsMotionNumber
{
    private readonly Control environment;
    private readonly Action<double> apply;
    private IFrameSource? frames;
    private Action<TimeSpan>? pending;
    private double target;

    internal SkillsMotionNumber(Control owner, double initial, Action<double> apply, Control? environment = null)
    {
        this.apply = apply;
        this.environment = environment ?? owner;
        Value = target = initial; apply(initial);
        owner.DetachedFromVisualTree += (_, _) => { Stop(); Value = target; apply(Value); };
    }

    internal double Value { get; private set; }

    internal void Set(double next, double seconds, bool movement = false, double delay = 0, bool animate = true, Action? complete = null)
    {
        Stop(); target = next;
        if (!animate || !FrameClock.IsBound || Value == next)
        {
            Value = next; apply(Value); complete?.Invoke(); return;
        }
        var from = Value;
        var start = SwiftClock.Now;
        var reduced = LibraryUi.Still(environment);
        Animation Curve() => (movement ? CzMotion.Ez(reduced ? .01 : seconds) : CzMotion.Ease(reduced ? .01 : seconds)).Delay(delay);
        var curve = Curve();
        frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            var elapsed = (SwiftClock.Now - start).TotalSeconds;
            if (!reduced && LibraryUi.Still(environment))
            {
                // OD-0076: turning Reduce Motion on stills displacement immediately but preserves a short opacity fade.
                delay = Math.Max(0, delay - elapsed);
                from = Value; start = SwiftClock.Now; elapsed = 0; reduced = true; curve = Curve();
            }
            var done = elapsed >= curve.Duration;
            Value = done ? next : from + (next - from) * curve.Progress(elapsed);
            apply(Value);
            if (done) { pending = null; complete?.Invoke(); }
            else frames.RequestFrame(tick!);
        };
        pending = tick; frames.RequestFrame(tick);
    }

    internal void Stop()
    {
        if (pending is { } frame) frames?.CancelFrame(frame);
        pending = null;
    }
}

/// <summary>A permanent lead layer: CSS opacity and scale have separate durations and delays.</summary>
internal sealed class SkillsLeadFace : Decorator
{
    private readonly SkillsMotionNumber opacity, scale;
    private double offScale = .5;
    internal SkillsLeadFace(Control content)
    {
        Child = content;
        Width = Height = 20;
        RenderTransformOrigin = RelativePoint.Center;
        content.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        content.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        opacity = new(this, 0, value => Opacity = value);
        scale = new(this, 0, value => RenderTransform = LibraryUi.Still(this) ? null : new ScaleTransform(offScale + (1 - offScale) * value, offScale + (1 - offScale) * value));
        IsHitTestVisible = false;
    }

    internal void Show(bool show, bool animate, double delay = 0, bool completedRing = false)
    {
        offScale = completedRing ? .6 : .5;
        opacity.Set(show ? 1 : 0, completedRing ? .3 : .25, delay: delay, animate: animate);
        scale.Set(show ? 1 : 0, completedRing ? .3 : .32, movement: !completedRing, delay: delay, animate: animate);
    }
}

/// <summary>The compact popup's .22 s CSS ease fade and .32 s --ez movement, rather than NSPopover's spring.</summary>
internal sealed class SkillsPopoverMotion(Control anchor) : IPopoverMotion
{
    private sealed class Run
    {
        internal readonly SkillsMotionNumber Opacity, Movement;
        internal Run(Control content, Control anchor, bool opening)
        {
            Opacity = new(content, opening ? 0 : content.Opacity, value => content.Opacity = value, anchor);
            var scale = new ScaleTransform(.92, .92);
            var shift = new TranslateTransform(0, 8);
            var transform = new TransformGroup { Children = { scale, shift } };
            Movement = new(content, opening ? 0 : 1, value =>
            {
                if (LibraryUi.Still(anchor)) { content.RenderTransform = null; return; }
                content.RenderTransformOrigin = new RelativePoint(30, content.Bounds.Height, RelativeUnit.Absolute);
                scale.ScaleX = scale.ScaleY = .92 + .08 * value;
                shift.Y = 8 * (1 - value);
                content.RenderTransform = value == 1 ? null : transform;
            }, anchor);
        }
    }
    private readonly ConditionalWeakTable<Control, Run> runs = new();
    public void Open(Control content)
    {
        var run = runs.GetValue(content, c => new Run(c, anchor, opening: true));
        run.Opacity.Set(1, .22);
        run.Movement.Set(1, .32, movement: true);
    }

    public Action Close(Control content, Action closed)
    {
        var run = runs.GetValue(content, c => new Run(c, anchor, opening: false));
        run.Opacity.Set(0, .22);
        run.Movement.Set(0, .32, movement: true, complete: closed);
        return () => { run.Opacity.Stop(); run.Movement.Stop(); };
    }
}
