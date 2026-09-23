using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Media;
using Marrow.Core.Port;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Marrow.UI.Port;

// PORT-ONLY: SwiftUI animation values and transition mechanics shared by UI views (ED-011, ED-092, ED-098).

/// <summary>A SwiftUI spring parameterized by its response and damping ratio.</summary>
public sealed record Spring
{
    public Spring(double response, double dampingRatio)
    {
        Response = response;
        DampingRatio = dampingRatio;
    }

    public double Response { get; }
    public double DampingRatio { get; }

    public double SettlingDuration => Math.Log(1000) / (DampingRatio * (2 * Math.PI / Response));
}

public interface IAnimatableModifier
{
    ImmutableArray<double> AnimatableData { get; }
    IAnimatableModifier WithAnimatableData(ImmutableArray<double> data);
    Control Body(Control content);
    void Update(Control body);
}

public enum Edge { Top, Bottom, Leading, Trailing }

/// <summary>The values and frame-driven application of SwiftUI AnyTransition.</summary>
public sealed record SwiftTransition
{
    private enum Kind { None, Fade, Scale, Offset, Move, Mod, Pair, Asym, Animated }

    private readonly Kind kind;
    private readonly double scale;
    private readonly double x;
    private readonly double y;
    private readonly Edge edge;
    private readonly SwiftTransition? first;
    private readonly SwiftTransition? second;
    private readonly Marrow.UI.Port.Animation? animation;
    private readonly IAnimatableModifier? active;
    private readonly IAnimatableModifier? identity;

    private SwiftTransition(Kind kind, double scale = 1, double x = 0, double y = 0, Edge edge = Edge.Top,
        SwiftTransition? first = null, SwiftTransition? second = null, Marrow.UI.Port.Animation? animation = null,
        IAnimatableModifier? active = null, IAnimatableModifier? identity = null)
    {
        this.kind = kind;
        this.scale = scale;
        this.x = x;
        this.y = y;
        this.edge = edge;
        this.first = first;
        this.second = second;
        this.animation = animation;
        this.active = active;
        this.identity = identity;
    }

    public static SwiftTransition Identity { get; } = new(Kind.None);
    public static SwiftTransition Opacity { get; } = new(Kind.Fade);
    public static SwiftTransition Scale(double scale = 0) => new(Kind.Scale, scale: scale);
    public static SwiftTransition Offset(double x = 0, double y = 0) => new(Kind.Offset, x: x, y: y);
    public static SwiftTransition Move(Edge edge) => new(Kind.Move, edge: edge);
    public static SwiftTransition Modifier(IAnimatableModifier active, IAnimatableModifier identity) =>
        new(Kind.Mod, active: active, identity: identity);
    public static SwiftTransition Asymmetric(SwiftTransition insertion, SwiftTransition removal) =>
        new(Kind.Asym, first: insertion, second: removal);
    public SwiftTransition Combined(SwiftTransition other) => new(Kind.Pair, first: this, second: other);
    public SwiftTransition Animation(Marrow.UI.Port.Animation value) => new(Kind.Animated, first: this, animation: value);

    private static readonly ConditionalWeakTable<Control, Attachment> attachments = new();

    internal static T Attach<T>(T child, SwiftTransition transition) where T : Control
    {
        attachments.Remove(child);
        attachments.Add(child, new Attachment(transition));
        return child;
    }

    public static void Insert(Panel panel, int index, Control child)
    {
        var state = attachments.GetValue(child, _ => new Attachment(Identity));
        var transition = state.Transition.ForInsert();
        state.Wrapper ??= state.Transition.CreateBody(child);
        panel.Children.Insert(index, state.Wrapper);
        var animation = transition.OwnAnimation() ?? Transaction.Current;
        if (animation is null)
        {
            transition.Apply(child, state.Wrapper, 1, inserting: true);
            return;
        }
        transition.Apply(child, state.Wrapper, 0, inserting: true);
        state.Run(animation, p => transition.Apply(child, state.Wrapper, p, inserting: true));
    }

    public static void Remove(Panel panel, Control child)
    {
        if (!attachments.TryGetValue(child, out var state))
        {
            panel.Children.Remove(child);
            return;
        }
        state.Stop();
        var transition = state.Transition.ForRemove();
        var wrapper = state.Wrapper ?? child;
        var animation = transition.OwnAnimation() ?? Transaction.Current;
        if (animation is null)
        {
            panel.Children.Remove(wrapper);
            return;
        }
        transition.Apply(child, wrapper, 0, inserting: false);
        state.Run(animation, p =>
        {
            transition.Apply(child, wrapper, p, inserting: false);
            if (p >= 1 && panel.Children.Contains(wrapper)) panel.Children.Remove(wrapper);
        });
    }

    private SwiftTransition ForInsert() => kind == Kind.Asym ? first! : this;
    private SwiftTransition ForRemove() => kind == Kind.Asym ? second! : this;
    private Marrow.UI.Port.Animation? OwnAnimation() => kind == Kind.Animated ? animation :
        kind == Kind.Pair ? first!.OwnAnimation() ?? second!.OwnAnimation() : null;

    private Control CreateBody(Control child)
    {
        if (kind == Kind.Mod) return identity!.Body(child);
        if (kind == Kind.Animated) return first!.CreateBody(child);
        if (kind == Kind.Asym)
        {
            var body = first!.CreateBody(child);
            return ReferenceEquals(body, child) ? second!.CreateBody(child) : body;
        }
        if (kind == Kind.Pair)
        {
            var body = first!.CreateBody(child);
            return ReferenceEquals(body, child) ? second!.CreateBody(child) : body;
        }
        return child;
    }

    private void Apply(Control child, Control body, double progress, bool inserting)
    {
        switch (kind)
        {
            case Kind.None: break;
            case Kind.Fade: child.Opacity = inserting ? progress : 1 - progress; break;
            case Kind.Scale:
                var factor = inserting ? scale + (1 - scale) * progress : 1 + (scale - 1) * progress;
                child.RenderTransformOrigin = RelativePoint.Center;
                child.RenderTransform = new ScaleTransform(factor, factor);
                break;
            case Kind.Offset:
                var offset = inserting ? 1 - progress : progress;
                child.RenderTransform = new TranslateTransform(x * offset, y * offset);
                break;
            case Kind.Move:
                var width = child.Bounds.Width > 0 ? child.Bounds.Width : child.Width;
                var height = child.Bounds.Height > 0 ? child.Bounds.Height : child.Height;
                var distance = inserting ? 1 - progress : progress;
                var dx = edge == Edge.Leading ? -width : edge == Edge.Trailing ? width : 0;
                var dy = edge == Edge.Top ? -height : edge == Edge.Bottom ? height : 0;
                child.RenderTransform = new TranslateTransform(dx * distance, dy * distance);
                break;
            case Kind.Mod:
                var from = inserting ? active! : identity!;
                var to = inserting ? identity! : active!;
                var a = from.AnimatableData;
                var b = to.AnimatableData;
                if (a.Length == b.Length && a.Length > 0)
                {
                    var values = ImmutableArray.CreateBuilder<double>(a.Length);
                    for (var i = 0; i < a.Length; i++) values.Add(a[i] + (b[i] - a[i]) * progress);
                    from.WithAnimatableData(values.MoveToImmutable()).Update(body);
                }
                else to.Update(body);
                break;
            case Kind.Pair:
                first!.Apply(child, body, progress, inserting);
                second!.Apply(child, body, progress, inserting);
                break;
            case Kind.Asym: (inserting ? first! : second!).Apply(child, body, progress, inserting); break;
            case Kind.Animated: first!.Apply(child, body, progress, inserting); break;
        }
    }

    private sealed class Attachment(SwiftTransition transition)
    {
        public SwiftTransition Transition { get; } = transition;
        public Control? Wrapper { get; set; }
        private Action<TimeSpan>? callback;
        private IFrameSource? source;

        public void Stop()
        {
            if (callback is not null) source!.CancelFrame(callback);
            callback = null;
        }

        public void Run(Marrow.UI.Port.Animation animation, Action<double> apply)
        {
            Stop();
            source = FrameClock.Source.Current;
            var start = SwiftClock.Now;
            Action<TimeSpan>? frame = null;
            frame = _ =>
            {
                var seconds = (SwiftClock.Now - start).TotalSeconds;
                var complete = seconds >= animation.Duration;
                apply(complete ? 1 : animation.Progress(seconds));
                if (complete) callback = null;
                else source.RequestFrame(frame!);
            };
            callback = frame;
            source.RequestFrame(frame);
        }
    }
}

public static class SwiftTransitionExtensions
{
    public static T Transition<T>(this T child, SwiftTransition transition) where T : Control =>
        SwiftTransition.Attach(child, transition);
}

public abstract partial record Animation
{
    public abstract double Duration { get; }
    public abstract double Progress(double seconds);

    public Easing ToEasing() => new SampledEasing(this);

    public Animation Delay(double seconds) => new DelayedAnimation(this, seconds);

    public static Animation Spring(Spring spring) => new SpringAnimation(spring.Response, spring.DampingRatio, 0);
    public static Animation Spring(double response, double dampingFraction, double blendDuration = 0) =>
        new SpringAnimation(response, dampingFraction, blendDuration);
    public static Animation Spring(double duration = 0.5, double bounce = 0) =>
        new SpringAnimation(duration, bounce >= 0 ? 1 - bounce : 1 / (1 + bounce), 0);
    public static Animation Smooth(double duration = 0.5) => Spring(duration: duration, bounce: 0);
    public static Animation Snappy(double duration = 0.5) => Spring(duration: duration, bounce: 0.15);
    public static Animation EaseIn(double duration = 0.35) => new TimingAnimation(duration, 0.42, 0, 1, 1);
    public static Animation EaseOut(double duration = 0.35) => new TimingAnimation(duration, 0, 0, 0.58, 1);
    public static Animation EaseInOut(double duration = 0.35) => new TimingAnimation(duration, 0.42, 0, 0.58, 1);
    public static Animation Linear(double duration = 0.35) => new TimingAnimation(duration, 0, 0, 1, 1);

    private sealed record SpringAnimation(double Response, double DampingRatio, double BlendDuration) : Animation
    {
        public override double Duration => new Spring(Response, DampingRatio).SettlingDuration;

        public override double Progress(double seconds)
        {
            if (seconds <= 0) return 0;
            var omega = 2 * Math.PI / Response;
            var zeta = DampingRatio;
            if (zeta < 1)
            {
                var damped = omega * Math.Sqrt(1 - zeta * zeta);
                return 1 - Math.Exp(-zeta * omega * seconds) *
                    (Math.Cos(damped * seconds) + zeta * omega / damped * Math.Sin(damped * seconds));
            }
            if (zeta == 1) return 1 - Math.Exp(-omega * seconds) * (1 + omega * seconds);
            var root = Math.Sqrt(zeta * zeta - 1);
            var r1 = -omega * (zeta - root);
            var r2 = -omega * (zeta + root);
            return 1 - (r2 * Math.Exp(r1 * seconds) - r1 * Math.Exp(r2 * seconds)) / (r2 - r1);
        }
    }

    private sealed record TimingAnimation(double Seconds, double X1, double Y1, double X2, double Y2) : Animation
    {
        public override double Duration => Seconds;

        public override double Progress(double seconds)
        {
            if (seconds <= 0) return 0;
            if (seconds >= Seconds) return 1;
            var x = seconds / Seconds;
            var low = 0.0;
            var high = 1.0;
            for (var i = 0; i < 60; i++)
            {
                var mid = (low + high) / 2;
                if (Bezier(mid, X1, X2) < x) low = mid;
                else high = mid;
            }
            return Bezier((low + high) / 2, Y1, Y2);
        }

        private static double Bezier(double t, double a, double b) =>
            3 * (1 - t) * (1 - t) * t * a + 3 * (1 - t) * t * t * b + t * t * t;
    }

    private sealed record DelayedAnimation(Animation Inner, double Seconds) : Animation
    {
        public override double Duration => Inner.Duration + Seconds;
        public override double Progress(double seconds) => seconds <= Seconds ? 0 : Inner.Progress(seconds - Seconds);
    }

    private sealed class SampledEasing(Animation animation) : Easing
    {
        public override double Ease(double progress) => animation.Progress(progress * animation.Duration);
    }
}
