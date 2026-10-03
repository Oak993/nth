using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
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
    private enum Kind { None, Fade, Scale, Offset, Move, BlurReplace, Mod, Pair, Asym, Animated }

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
    // UNVERIFIED O-51: UsageViews.swift:273,279,352,360,364 use the default downUp blurReplace (ED-159).
    public static SwiftTransition BlurReplace { get; } = new(Kind.BlurReplace);
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

    // ED-218: collection reconciliation uses the transition's actual panel child (modifier wrapper, if any).
    public static bool HasTransition(Control child) => attachments.TryGetValue(child, out _);
    public static Control Container(Control child) => attachments.TryGetValue(child, out var state) ? state.Wrapper ?? child : child;
    internal static void FinishRemoval(Panel panel, Control child)
    {
        if (!attachments.TryGetValue(child, out var state)) return;
        state.Stop(); panel.Children.Remove(state.Wrapper ?? child); state.RemovingFrom = null; state.CompleteRemoval();
    }
    internal static void CancelInsertion(Control child)
    {
        if (!attachments.TryGetValue(child, out var state)) return;
        state.Stop(); state.Present(child, 1, inserting: true);
    }

    internal static T Attach<T>(T child, SwiftTransition transition) where T : Control
    {
        attachments.Remove(child);
        attachments.Add(child, new Attachment(transition));
        return child;
    }

    public static void Insert(Panel panel, int index, Control child)
    {
        // A view with no `.transition(_:)` uses SwiftUI's default `.opacity`: documented SwiftUI behaviour, UNVERIFIED
        // under OD-0001 as ED-092 records for the other documented defaults.
        var state = attachments.GetValue(child, _ => new Attachment(Opacity));
        // ED-159 follow-up (interruption): an insertion cancels whatever transition of this child is still running and
        // starts afresh; a removal still in flight gives its wrapper back before the insertion places it again.
        state.Stop();
        if (state.RemovingFrom is { } lingering)
        {
            lingering.Children.Remove(state.Wrapper ?? child);
            state.RemovingFrom = null;
            state.CompleteRemoval();
        }
        state.Wrapper ??= state.Transition.CreateBody(child, state.Bodies);
        panel.Children.Insert(index, state.Wrapper);
        var animation = state.Transition.ForInsert().OwnAnimation() ?? Transaction.Current;
        if (animation is null)
        {
            state.Present(child, 1, inserting: true);
            return;
        }
        state.Present(child, 0, inserting: true);
        state.Run(animation, (p, _) => state.Present(child, p, inserting: true));
    }

    /// <summary>
    /// A view already in its parent when the parent first appears: the same placement as <see cref="Insert"/> without the
    /// arrival animation (SwiftUI runs a transition only for a view that joins a parent already on screen), yet with the
    /// transition's wrappers in place so a later <see cref="Remove(Panel, Control)"/> can animate the view away.
    /// </summary>
    public static void Place(Panel panel, int index, Control child)
    {
        var state = attachments.GetValue(child, _ => new Attachment(Opacity));
        state.Stop();
        state.Wrapper ??= state.Transition.CreateBody(child, state.Bodies);
        panel.Children.Insert(index, state.Wrapper);
        state.Present(child, 1, inserting: true);
    }

    public static void Remove(Panel panel, Control child) => Remove(panel, child, null);

    // ED-218: disposal belongs after removal finishes, including immediate/identity removals.
    internal static void Remove(Panel panel, Control child, Action? completed)
    {
        // The same `.opacity` default (UNVERIFIED under OD-0001, ED-092) for a child that carries no transition.
        var state = attachments.GetValue(child, _ => new Attachment(Opacity));
        // OD-0076 (no change by a cut): a removal that interrupts this child's insertion still in flight starts from where
        // the insertion has brought the child, never from its identity. For a transition with no asymmetric part, the
        // insertion's state at progress p is the removal's state at 1 - p (move, offset, scale, opacity, blur and modifier
        // values are each interpolated linearly between the same two ends), so the removal runs from there to its end on its
        // own curve. ED-NEW-parity-chatview (the drawer closed while it is still opening, ChatView.swift:276-279,290).
        var from = state.InsertionInFlight && !state.Transition.HasAsymmetricPart() ? 1 - state.LastProgress : 0;
        state.Stop();
        state.RemovalCompleted = completed;
        var wrapper = state.Wrapper ?? child;
        var animation = state.Transition.ForRemove().OwnAnimation() ?? Transaction.Current;
        // A removal half that touches nothing (`.identity`, alone or as the removal half of `.asymmetric`) leaves at
        // once even under an animation: App/Views/Chat/ChatView.swift:283-288 relies on it ("must replace in place,
        // never slide or fade"), as do UI/Markdown/MarkdownView.swift:46-53 and App/Views/Chat/ThreadTimeline.swift:316-320.
        // Documented SwiftUI behaviour, UNVERIFIED under OD-0001 as ED-092 records.
        if (animation is null || !state.Transition.Touches(inserting: false))
        {
            panel.Children.Remove(wrapper);
            state.RemovingFrom = null;
            state.CompleteRemoval();
            return;
        }
        if (state.Wrapper is null && state.Transition.ModifierCount() > 0)
            throw new InvalidOperationException(
                "A child with a modifier transition can leave with an animation only after SwiftTransition.Insert built its wrappers.");
        state.RemovingFrom = panel;
        state.Present(child, from, inserting: false);
        state.Run(animation, (p, complete) =>
        {
            state.Present(child, from + (1 - from) * p, inserting: false);
            if (!complete) return;
            if (panel.Children.Contains(wrapper)) panel.Children.Remove(wrapper);
            state.RemovingFrom = null;
            state.CompleteRemoval();
        });
    }

    private SwiftTransition ForInsert() => kind == Kind.Asym ? first! : this;
    private SwiftTransition ForRemove() => kind == Kind.Asym ? second! : this;
    private Marrow.UI.Port.Animation? OwnAnimation() => kind == Kind.Animated ? animation :
        kind == Kind.Pair ? first!.OwnAnimation() ?? second!.OwnAnimation() : null;

    // Whether the half in play for this direction changes anything: a leaf effect or a modifier (Gather's walk).
    private bool Touches(bool inserting) => kind switch
    {
        Kind.None => false,
        Kind.Animated => first!.Touches(inserting),
        Kind.Pair => first!.Touches(inserting) || second!.Touches(inserting),
        Kind.Asym => inserting ? first!.Touches(inserting) : second!.Touches(inserting),
        _ => true,
    };

    // Whether an `.asymmetric` sits anywhere in the tree: its insertion and removal states then differ, and an insertion's
    // progress names no removal state.
    private bool HasAsymmetricPart() => kind switch
    {
        Kind.Asym => true,
        Kind.Animated => first!.HasAsymmetricPart(),
        Kind.Pair => first!.HasAsymmetricPart() || second!.HasAsymmetricPart(),
        _ => false,
    };

    // The modifier transitions in the tree, counted in the order CreateBody and Gather walk it.
    private int ModifierCount() => kind switch
    {
        Kind.Mod => 1,
        Kind.Animated => first!.ModifierCount(),
        Kind.Asym or Kind.Pair => first!.ModifierCount() + second!.ModifierCount(),
        _ => 0,
    };

    // ED-098 (2): every modifier in the tree wraps the content itself, as SwiftUI applies each one: both halves of an
    // asymmetric transition (UI/Chrome/Chrome.swift:994-999 settleGlide) and both sides of a combined one. The walk
    // takes the first half, then the second; each wrapper encloses the ones before it and starts at its identity.
    private Control CreateBody(Control content, List<Control> bodies)
    {
        switch (kind)
        {
            case Kind.Mod:
                var body = identity!.Body(content);
                identity.Update(body);
                bodies.Add(body);
                return body;
            case Kind.Animated:
                return first!.CreateBody(content, bodies);
            case Kind.Asym or Kind.Pair:
                return second!.CreateBody(first!.CreateBody(content, bodies), bodies);
            default:
                return content;
        }
    }

    private bool Apply(Control child, List<Control> bodies, double progress, bool inserting)
    {
        // ED-159: gather nested transitions before writing any property, then leave untouched properties alone.
        var effects = new AppliedEffects();
        var cursor = 0;
        Gather(child, bodies, ref cursor, progress, inserting, effects);
        if (effects.OpacityTouched) child.Opacity = effects.Opacity;
        if (effects.TransformTouched)
        {
            child.RenderTransformOrigin = RelativePoint.Center;
            if (effects.Transforms.Count == 1) child.RenderTransform = effects.Transforms[0];
            else
            {
                var group = new TransformGroup();
                foreach (var transform in effects.Transforms) group.Children.Add(transform);
                child.RenderTransform = group;
            }
        }
        if (effects.EffectTouched)
            child.Effect = effects.BlurRadius > 0 ? new ImmutableBlurEffect(effects.BlurRadius) : null;
        return effects.SizePending;
    }

    private sealed class AppliedEffects
    {
        public List<Transform> Transforms { get; } = [];
        public double Opacity { get; set; } = 1;
        public double BlurRadius { get; set; }
        public bool OpacityTouched { get; set; }
        public bool TransformTouched { get; set; }
        public bool EffectTouched { get; set; }
        public bool SizePending { get; set; }
    }

    private void Gather(Control child, List<Control> bodies, ref int cursor, double progress, bool inserting, AppliedEffects effects)
    {
        switch (kind)
        {
            case Kind.None: break;
            case Kind.Fade:
                effects.OpacityTouched = true;
                effects.Opacity *= inserting ? progress : 1 - progress;
                break;
            case Kind.Scale:
                var factor = inserting ? scale + (1 - scale) * progress : 1 + (scale - 1) * progress;
                effects.TransformTouched = true;
                effects.Transforms.Add(new ScaleTransform(factor, factor));
                break;
            case Kind.Offset:
                var offset = inserting ? 1 - progress : progress;
                effects.TransformTouched = true;
                effects.Transforms.Add(new TranslateTransform(x * offset, y * offset));
                break;
            case Kind.Move:
                // ED-092 (2): the offset is the view's own size towards the edge, times the distance from identity.
                // Identity is a zero offset whatever the size. A child sized by its content (the B2 sites
                // App/Views/Chat/ChatView.swift:277-278 and App/Views/Usage/UsageViews.swift:250-251) has no size
                // before its first layout pass: it sits at zero offset and the caller presents the same state again
                // once layout gives it bounds (SizePending), so the active state is one laid-out size beyond the edge.
                var distance = inserting ? 1 - progress : progress;
                var horizontal = edge is Edge.Leading or Edge.Trailing;
                var size = horizontal ? OwnLength(child.Bounds.Width, child.Width) : OwnLength(child.Bounds.Height, child.Height);
                var length = 0.0;
                if (distance != 0)
                {
                    if (size is { } known) length = known * distance;
                    else effects.SizePending = true;
                }
                var sign = edge is Edge.Leading or Edge.Top ? -1 : 1;
                effects.TransformTouched = true;
                effects.Transforms.Add(horizontal
                    ? new TranslateTransform(sign * length, 0)
                    : new TranslateTransform(0, sign * length));
                break;
            case Kind.BlurReplace:
                // UNVERIFIED O-51: active downUp state is opacity 0, radius 7 and centre scale 0.9 (ED-159).
                var activeProgress = inserting ? 1 - progress : progress;
                effects.OpacityTouched = true;
                effects.Opacity *= 1 - activeProgress;
                effects.EffectTouched = true;
                effects.BlurRadius = Math.Max(0, 7 * activeProgress);
                effects.TransformTouched = true;
                var blurScale = 1 - 0.1 * activeProgress;
                effects.Transforms.Add(new ScaleTransform(blurScale, blurScale));
                break;
            case Kind.Mod:
                // Each modifier updates the wrapper its own Body built (ED-098 (2)).
                var body = bodies[cursor++];
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
                // UNVERIFIED O-52: combined(with:) nests first inside second (ChatView.swift:1098-1099).
                first!.Gather(child, bodies, ref cursor, progress, inserting, effects);
                second!.Gather(child, bodies, ref cursor, progress, inserting, effects);
                break;
            case Kind.Asym:
                // The half not in play stays at identity: its wrappers keep the state they were built or left in.
                if (inserting)
                {
                    first!.Gather(child, bodies, ref cursor, progress, inserting, effects);
                    cursor += second!.ModifierCount();
                }
                else
                {
                    cursor += first!.ModifierCount();
                    second!.Gather(child, bodies, ref cursor, progress, inserting, effects);
                }
                break;
            case Kind.Animated: first!.Gather(child, bodies, ref cursor, progress, inserting, effects); break;
        }
    }

    // The laid-out length when layout has run, else a fixed Width/Height, else unknown (a content-sized view before
    // its first layout pass: Width/Height are NaN there).
    private static double? OwnLength(double laidOut, double fixedLength) =>
        laidOut > 0 ? laidOut : double.IsFinite(fixedLength) && fixedLength > 0 ? fixedLength : null;

    private sealed class Attachment(SwiftTransition transition)
    {
        public SwiftTransition Transition { get; } = transition;
        public Control? Wrapper { get; set; }
        public List<Control> Bodies { get; } = [];
        public Panel? RemovingFrom { get; set; }
        public Action? RemovalCompleted { get; set; }
        public void CompleteRemoval() { var done = RemovalCompleted; RemovalCompleted = null; done?.Invoke(); }
        private Action<TimeSpan>? callback;
        private IFrameSource? source;
        private double lastProgress;
        private bool lastInserting;
        private EventHandler<AvaloniaPropertyChangedEventArgs>? awaitingSize;

        /// <summary>An insertion is still running: its frames are requested and it presents the inserting side.</summary>
        public bool InsertionInFlight => callback is not null && lastInserting;
        public double LastProgress => lastProgress;

        // Applies the transition at a progress. When a move could not read the child's size yet, the latest state is
        // presented again at the child's next bounds change (its first layout pass), before it is rendered.
        public void Present(Control child, double progress, bool inserting)
        {
            lastProgress = progress;
            lastInserting = inserting;
            if (!Transition.Apply(child, Bodies, progress, inserting) || awaitingSize is not null) return;
            awaitingSize = (_, e) =>
            {
                if (e.Property != Visual.BoundsProperty) return;
                child.PropertyChanged -= awaitingSize;
                awaitingSize = null;
                Present(child, lastProgress, lastInserting);
            };
            child.PropertyChanged += awaitingSize;
        }

        public void Stop()
        {
            if (callback is not null) source!.CancelFrame(callback);
            callback = null;
        }

        public void Run(Marrow.UI.Port.Animation animation, Action<double, bool> apply)
        {
            Stop();
            source = FrameClock.Source.Current;
            var start = SwiftClock.Now;
            Action<TimeSpan>? frame = null;
            frame = _ =>
            {
                var elapsed = SwiftClock.Now - start;
                var seconds = elapsed.TotalSeconds;
                var complete = elapsed.Ticks + TimeSpan.TicksPerMillisecond >= SwiftTask.Seconds(animation.Duration).Ticks; // ED-117 (2).
                apply(complete ? 1 : animation.Progress(seconds), complete);
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
