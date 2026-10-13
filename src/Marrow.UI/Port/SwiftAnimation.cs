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

    // `elapsed` is PORT-ONLY (UI-THEME-047): how far into its arrival the child already is. SwiftUI keeps a view's insertion running while the
    // rest of the tree updates around it; a tree that is built again for every flush (a streaming reply's markdown) puts a new view in the place of the one
    // whose arrival is part way, which then carries on from here instead of starting again or finishing at once. Zero is an insertion that starts now.
    public static void Insert(Panel panel, int index, Control child, TimeSpan elapsed = default)
    {
        // A view with no `.transition(_:)` uses SwiftUI's default `.opacity`: documented SwiftUI behaviour, UNVERIFIED
        // under OD-0001 as ED-092 records for the other documented defaults.
        var state = attachments.GetValue(child, _ => new Attachment(Opacity));
        // OD-0076, ED-NEW-perf-motion-9: an insertion that interrupts this child's removal under way in this panel brings it back from where
        // the removal has taken it (a transition with no asymmetric part: the removal's state at r is the insertion's at 1 - r), never from
        // the insertion's start; a removal that has not moved yet (r = 0) still lets the insertion start afresh (ED-278 (10)).
        var resume = elapsed == default && state.RemovalInFlight && ReferenceEquals(state.RemovingFrom, panel) && !state.Transition.HasAsymmetricPart()
            && state.CurrentProgress is > 0 and < 1 ? 1 - state.CurrentProgress : 0;
        var removalSpeed = resume > 0 ? state.CurrentSpeed : 0;
        // ED-159 follow-up (interruption): an insertion cancels whatever transition of this child is still running; a removal still in
        // flight gives its wrapper back before the insertion places it again.
        state.Stop();
        state.Carried = null;
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
        if (elapsed > TimeSpan.Zero && elapsed.Ticks + TimeSpan.TicksPerMillisecond >= SwiftTask.Seconds(animation.Duration).Ticks)
        {
            state.Present(child, 1, inserting: true); // ED-117 (2): complete 1 ms early at most
            return;
        }
        if (resume > 0)
        {
            // ED-NEW-perf-motion-11: a spring that moves the child (a slide, a scale) turns round at the removal's speed, as SwiftUI's
            // springs keep their velocity through a retarget; a fade alone comes back from rest, so the opacity only ever rises.
            animation = state.Transition.Carrying(animation, -removalSpeed, resume);
            state.Present(child, resume, inserting: true);
            state.Run(animation, (p, _) => state.Present(child, resume + (1 - resume) * p, inserting: true), from: resume);
            return;
        }
        state.Present(child, elapsed > TimeSpan.Zero ? animation.Progress(elapsed.TotalSeconds) : 0, inserting: true);
        state.Run(animation, (p, _) => state.Present(child, p, inserting: true), elapsed);
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
        state.Carried = null;
        state.Wrapper ??= state.Transition.CreateBody(child, state.Bodies);
        panel.Children.Insert(index, state.Wrapper);
        state.Present(child, 1, inserting: true);
    }

    /// <summary>
    /// A removal runs the transition's removal half from its own start. When it interrupts an asymmetric transition's insertion still in flight
    /// (a soft arrival cut short) it carries what the insertion had put on the child at that instant, so nothing jumps: the opacity the child has
    /// multiplies the removal's, and the offset it has eases out over the removal (UI-CHAT-052, unit rec-tlr; SwiftUI keeps the arrival's in-flight values
    /// under the removal rather than flashing the view to full strength).
    /// </summary>
    public static void Remove(Panel panel, Control child) => Remove(panel, child, null);

    /// <summary>The child's removal is still running (its frames are requested): <see cref="InsertContinuing"/> would take it back.</summary>
    public static bool IsRemoving(Control child) => attachments.TryGetValue(child, out var state) && state.RemovalInFlight;

    /// <summary>
    /// OD-0076 (no change by a cut), ED-278 (10): a removal that may interrupt this child's insertion still in
    /// flight and starts from where the insertion has brought the child, never from its identity. For a transition with no
    /// asymmetric part the insertion's state at progress p is the removal's state at 1 - p (move, offset, scale, opacity, blur
    /// and modifier values each interpolate linearly between the same two ends), so the removal runs from there to its end on
    /// its own curve; p is read on the insertion's own clock at this instant. An asymmetric transition names no such state, so its removal
    /// runs from its own start (<see cref="Remove(Panel, Control)"/>), carrying the opacity and offset the insertion had put on the child
    /// at that instant (the opacity multiplies the removal's, the offset eases out over it): a soft arrival cut short never jumps to full. Opt-in: the sites that rely on the restart keep it until the
    /// app-wide OD-0076 audit (the terminal drawer closed while it is still opening, ChatView.swift:276-279,290, is the first).
    /// </summary>
    // Layout partners can share the last delivered point; standalone transitions retain their current-clock sampling.
    public static void RemoveContinuing(Panel panel, Control child, bool usePresentedProgress = false)
        => Remove(panel, child, null, continuing: true, usePresentedProgress);

    /// <summary>
    /// OD-0076 (no change by a cut), ED-278 (10): the counterpart of <see cref="RemoveContinuing"/>. An insertion that
    /// interrupts this child's removal still in flight, in the panel the removal is leaving, brings the same child back from where the
    /// removal has taken it, never from the insertion's start: for a transition with no asymmetric part the removal's state at progress
    /// r is the insertion's at 1 - r, so the insertion runs from 1 - r (the removal's presented progress at this instant, on its own
    /// clock) to its end on its own curve. The child stays in its panel at the place it has (the index is not read), its removal's
    /// frames stop and its completion is dropped (the child is not gone). An asymmetric transition, or a child with no removal in
    /// flight in this panel, is inserted as <see cref="Insert"/> does. Opt-in, like <see cref="RemoveContinuing"/>.
    /// </summary>
    public static void InsertContinuing(Panel panel, int index, Control child, bool usePresentedProgress = false)
    {
        var state = attachments.GetValue(child, _ => new Attachment(Opacity));
        if (!state.RemovalInFlight || !ReferenceEquals(state.RemovingFrom, panel) || state.Transition.HasAsymmetricPart()
            || state.Wrapper is not { } wrapper || !panel.Children.Contains(wrapper))
        {
            Insert(panel, index, child);
            return;
        }
        // Partners still present the last delivered frame when a press arrives between frames.
        var from = 1 - (usePresentedProgress ? state.PresentedProgress : state.CurrentProgress);
        var speed = -state.CurrentSpeed;
        state.Stop();
        state.Carried = null;
        state.RemovingFrom = null;
        state.RemovalCompleted = null;
        var animation = state.Transition.ForInsert().OwnAnimation() ?? Transaction.Current;
        if (animation is null)
        {
            state.Present(child, 1, inserting: true);
            return;
        }
        state.Present(child, from, inserting: true);
        var impulseSpeed = from == 1 && animation.KeepsSpeed && state.Transition.Moves() ? speed : 0;
        animation = state.Transition.Carrying(animation, speed, from);
        state.Run(animation, (p, complete) => state.Present(child, from + (1 - from) * p
            + (complete ? 0 : impulseSpeed * animation.Impulse(state.Seconds)), inserting: true), from: from, impulseSpeed: impulseSpeed);
    }

    // ED-218: disposal belongs after removal finishes, including immediate/identity removals.
    internal static void Remove(Panel panel, Control child, Action? completed) => Remove(panel, child, completed, continuing: false);

    private static void Remove(Panel panel, Control child, Action? completed, bool continuing, bool usePresentedProgress = false)
    {
        // The same `.opacity` default (UNVERIFIED under OD-0001, ED-092) for a child that carries no transition.
        var state = attachments.GetValue(child, _ => new Attachment(Opacity));
        // OD-0076, ED-NEW-perf-motion-9: a removal that interrupts an insertion under way starts from where the insertion has brought the
        // child (RemoveContinuing's rule) whenever the insertion has moved; one that has not moved yet (p = 0) still starts from the
        // removal's own start, as ED-278 (10) keeps for a stage swapped before any time passes.
        var progress = continuing && usePresentedProgress ? state.PresentedProgress : state.CurrentProgress;
        var from = state.InsertionInFlight && !state.Transition.HasAsymmetricPart() && (continuing || progress > 0)
            ? 1 - progress : 0;
        var insertionSpeed = state.InsertionInFlight && !state.Transition.HasAsymmetricPart() ? state.CurrentSpeed : 0;
        // An asymmetric transition's removal names no state of the insertion it interrupts, so it cannot start from the insertion's progress; it starts from the
        // insertion's presentation instead (the opacity and the rise the child has now), which the removal then carries (UI-CHAT-052, unit rec-tlr).
        var carries = state.InsertionInFlight && state.Transition.HasAsymmetricPart();
        if (carries) state.Carry(child); else state.Carried = null;
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
        var impulseSpeed = from == 1 && animation.KeepsSpeed && state.Transition.Moves() ? -insertionSpeed : 0;
        // ED-NEW-motion-drawer-1: continuing drawer transitions and their layout partners carry the same velocity.
        animation = state.Transition.Carrying(animation, -insertionSpeed, from);
        state.Present(child, from, inserting: false);
        state.Run(animation, (p, complete) =>
        {
            state.Present(child, from + (1 - from) * p
                + (complete ? 0 : impulseSpeed * animation.Impulse(state.Seconds)), inserting: false);
            if (!complete) return;
            if (panel.Children.Contains(wrapper)) panel.Children.Remove(wrapper);
            state.RemovingFrom = null;
            state.CompleteRemoval();
        }, from: from, impulseSpeed: impulseSpeed);
    }

    private SwiftTransition ForInsert() => kind == Kind.Asym ? first! : this;
    private SwiftTransition ForRemove() => kind == Kind.Asym ? second! : this;
    private Marrow.UI.Port.Animation? OwnAnimation() => kind == Kind.Animated ? animation :
        kind == Kind.Pair ? first!.OwnAnimation() ?? second!.OwnAnimation() : null;

    // ED-NEW-perf-motion-11: a run that continues from `from` at `speed` (progress per second) carries that speed in when the curve is a
    // spring and the transition moves the child; the carried part dies out by the curve's end (Animation.Impulse).
    private Marrow.UI.Port.Animation Carrying(Marrow.UI.Port.Animation curve, double speed, double from) =>
        from != 1 && curve.KeepsSpeed && Moves() ? curve.WithStartSpeed(speed / (1 - from)) : curve;

    // Whether the transition moves or scales the child (a fade alone does not).
    private bool Moves() => kind switch
    {
        Kind.Scale or Kind.Offset or Kind.Move or Kind.BlurReplace => true,
        Kind.Animated => first!.Moves(),
        Kind.Pair => first!.Moves() || second!.Moves(),
        _ => false,
    };

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
        // ED-NEW-perf-motion-9: an opacity is drawn between 0 and 1 (a layer's opacity is clamped when it is drawn), so a bouncy spring that carries a fade past
        // its end writes the opacity that is drawn: the value read back is what the reader sees, never 1.0025 falling back to 1.
        if (effects.OpacityTouched) child.Opacity = Math.Clamp(effects.Opacity, 0, 1);
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
        public double PresentedProgress => lastProgress;
        private bool lastInserting;
        private EventHandler<AvaloniaPropertyChangedEventArgs>? awaitingSize;

        /// <summary>An insertion is still running: its frames are requested and it presents the inserting side.</summary>
        public bool InsertionInFlight => callback is not null && lastInserting;

        /// <summary>A removal is still running: its frames are requested and it presents the removing side.</summary>
        public bool RemovalInFlight => callback is not null && !lastInserting;

        // The progress presented now, on the running animation's own clock, whether or not a frame has presented that time yet (an
        // interruption reads the state on screen at this instant, not the last frame's). A run that continued an interrupted one
        // presents from runFrom to 1 (from + (1 - from) * the curve), so that is what is read, not the curve alone.
        public double CurrentProgress
        {
            get
            {
                if (callback is null || runAnimation is null) return lastProgress;
                var elapsed = SwiftClock.Now - runStart;
                var curve = elapsed.Ticks + TimeSpan.TicksPerMillisecond >= SwiftTask.Seconds(runAnimation.Duration).Ticks
                    ? 1 : runAnimation.Progress(elapsed.TotalSeconds);
                return runFrom + (1 - runFrom) * curve + runImpulseSpeed * runAnimation.Impulse(elapsed.TotalSeconds);
            }
        }

        /// <summary>How fast the presented progress moves now, per second (0 with nothing running).</summary>
        public double CurrentSpeed
        {
            get
            {
                if (callback is null || runAnimation is null) return 0;
                var seconds = (SwiftClock.Now - runStart).TotalSeconds;
                return (1 - runFrom) * runAnimation.Speed(seconds) + (runImpulseSpeed == 0 ? 0 :
                    runImpulseSpeed * (seconds <= 0 ? 1 : runImpulseAnimation!.Speed(seconds) - runAnimation.Speed(seconds)));
            }
        }

        private Marrow.UI.Port.Animation? runAnimation;
        private ContinuousInstant runStart;
        private double runFrom;
        private double runImpulseSpeed;
        private Marrow.UI.Port.Animation? runImpulseAnimation;
        public double Seconds => (SwiftClock.Now - runStart).TotalSeconds;

        // Applies the transition at a progress. When a move could not read the child's size yet, the latest state is
        // presented again at the child's next bounds change (its first layout pass), before it is rendered.
        public void Present(Control child, double progress, bool inserting)
        {
            lastProgress = progress;
            lastInserting = inserting;
            var carried = !inserting ? Carried : null;
            if (carried is not null)
                foreach (var held in carried) { held.Target.Opacity = 1; held.Target.RenderTransform = null; } // the removal writes what it touches; the rest is the carried state alone
            var sizePending = Transition.Apply(child, Bodies, progress, inserting);
            if (carried is not null)
                foreach (var held in carried)
                {
                    held.Target.Opacity *= held.Opacity;
                    if (held.Rise is { } rise)
                    {
                        // The insertion's offset eases out to nothing over the removal, so a rise cut short is not held on a child that is fading, nor snapped away.
                        var kept = 1 - Math.Clamp(progress, 0, 1);
                        var eased = new MatrixTransform(new Matrix(
                            1 + (rise.M11 - 1) * kept, rise.M12 * kept, rise.M21 * kept, 1 + (rise.M22 - 1) * kept, rise.M31 * kept, rise.M32 * kept));
                        if (held.Target.RenderTransform is { } own)
                        {
                            var group = new TransformGroup();
                            group.Children.Add(new MatrixTransform(own.Value)); group.Children.Add(eased);
                            held.Target.RenderTransform = group;
                        }
                        else held.Target.RenderTransform = eased;
                    }
                }
            if (!sizePending || awaitingSize is not null) return;
            awaitingSize = (_, e) =>
            {
                if (e.Property != Visual.BoundsProperty) return;
                child.PropertyChanged -= awaitingSize;
                awaitingSize = null;
                Present(child, lastProgress, lastInserting);
            };
            child.PropertyChanged += awaitingSize;
        }

        /// <summary>What an insertion cut short by an asymmetric removal had put on each layer at that instant (opacity and transform): the removal runs on top of it.</summary>
        public List<(Control Target, double Opacity, Matrix? Rise)>? Carried { get; set; }

        /// <summary>Reads the insertion's presentation at this instant (its own clock, not the last frame's) so the removal that interrupts it starts from it.</summary>
        public void Carry(Control child)
        {
            Present(child, CurrentProgress, inserting: true);
            var held = new List<(Control, double, Matrix?)>();
            foreach (var target in Bodies.Append(child).Distinct())
                held.Add((target, target.Opacity, target.RenderTransform is { } transform ? transform.Value : null));
            Carried = held;
        }

        public void Stop()
        {
            if (callback is not null) source!.CancelFrame(callback);
            callback = null;
        }

        public void Run(Marrow.UI.Port.Animation animation, Action<double, bool> apply, TimeSpan elapsed = default, double from = 0, double impulseSpeed = 0)
        {
            Stop();
            source = FrameClock.Source.Current;
            var start = SwiftClock.Now - elapsed;
            runAnimation = animation;
            runStart = start;
            runFrom = from;
            runImpulseSpeed = impulseSpeed;
            runImpulseAnimation = impulseSpeed == 0 ? null : animation.WithStartSpeed(1);
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

    /// <summary>PORT-ONLY (ED-NEW-perf-motion-11): the curve's speed, in progress per second, at <paramref name="seconds"/>: what a run
    /// that a new target interrupts there carries into the next one (SwiftUI's springs keep their velocity through a retarget).</summary>
    public virtual double Speed(double seconds)
    {
        if (seconds <= 0 || seconds >= Duration) return 0;
        const double h = 1e-4;
        var before = Math.Max(0, seconds - h);
        var after = Math.Min(Duration, seconds + h);
        return (Progress(after) - Progress(before)) / (after - before);
    }

    /// <summary>PORT-ONLY (ED-NEW-perf-motion-11): the offset, at <paramref name="seconds"/>, of a run that sets out with a unit speed
    /// and comes back to where it set out, with no speed, at the curve's end: what a start speed adds to a run (for a spring, its
    /// response to a push, landed as its step response is; for a timing curve t (1 - t/T)^2).</summary>
    public virtual double Impulse(double seconds)
    {
        var end = Duration;
        if (seconds <= 0 || seconds >= end) return 0;
        var left = 1 - seconds / end;
        return seconds * left * left;
    }

    public Easing ToEasing() => new SampledEasing(this);

    /// <summary>PORT-ONLY (ED-NEW-perf-motion-11): whether a run on this curve keeps its speed through a retarget (SwiftUI's springs do).</summary>
    public virtual bool KeepsSpeed => false;

    /// <summary>PORT-ONLY (ED-NEW-perf-motion-11): this curve setting out at <paramref name="speed"/> (progress per second) instead of from
    /// rest; the carried speed dies out by the curve's end with no step (<see cref="Impulse"/>).</summary>
    public Animation WithStartSpeed(double speed) => speed == 0 || !double.IsFinite(speed) ? this : new CarriedAnimation(this, speed);

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
    // SwiftUI Animation.timingCurve(_:_:_:_:duration:): a cubic Bezier from (0, 0) to (1, 1) through the two control points
    // (used by the port-only accent picker, OD-0088, for the CSS curves its locked board names).
    public static Animation TimingCurve(double c0x, double c0y, double c1x, double c1y, double duration = 0.35) =>
        new TimingAnimation(duration, c0x, c0y, c1x, c1y);

    // A curve that also carries a start speed (in progress per second), which dies out by its end with no step (ED-NEW-perf-motion-11).
    private sealed record CarriedAnimation(Animation Inner, double StartSpeed) : Animation
    {
        public override double Duration => Inner.Duration;
        public override bool KeepsSpeed => Inner.KeepsSpeed;
        public override double Progress(double seconds) => seconds >= Duration ? 1 : Inner.Progress(seconds) + StartSpeed * Inner.Impulse(seconds);
        public override double Impulse(double seconds) => Inner.Impulse(seconds);
        public override double Speed(double seconds) => seconds == 0 ? Inner.Speed(0) + StartSpeed : base.Speed(seconds);
    }

    private sealed record SpringAnimation(double Response, double DampingRatio, double BlendDuration) : Animation
    {
        public override bool KeepsSpeed => true;
        public override double Duration => new Spring(Response, DampingRatio).SettlingDuration;

        // ED-NEW-perf-motion-10 (ED-333's UNRESOLVED run-out, the lead's ruling): the curve lands on 1 with no speed at its settling
        // time; the remainder the free spring still has there (0.79 % critically damped) is folded into the run's second half
        // (MotionLanding), so the last frame is never a jump. An underdamped spring that last crosses 1 in the second half keeps the
        // free curve up to that crossing and has only its distance from 1 drawn in after it (MotionLanding.Narrow): its overshoot past
        // the end and the times it crosses the end are the free spring's. An overdamped spring (damping above 1) is the free curve:
        // none is used.
        public override double Progress(double seconds)
        {
            if (seconds <= 0) return 0;
            if (DampingRatio > 1) return Free(seconds);
            var end = Duration;
            if (seconds >= end) return 1;
            if (LastCrossing(end) is { } crossing) return MotionLanding.Narrow(Free(seconds), seconds, crossing, end);
            return MotionLanding.Land(Free(seconds), seconds, end, 1 - Free(end), FreeSpeed(end));
        }

        public override double Speed(double seconds)
        {
            if (seconds <= 0) return 0;
            if (DampingRatio > 1) return FreeSpeed(seconds);
            var end = Duration;
            if (seconds >= end) return 0;
            if (LastCrossing(end) is { } crossing) return MotionLanding.NarrowSpeed(Free(seconds), FreeSpeed(seconds), seconds, crossing, end);
            return MotionLanding.LandSpeed(FreeSpeed(seconds), seconds, end, 1 - Free(end), FreeSpeed(end));
        }

        // The last time before the settling time at which the free underdamped curve crosses 1, when that is in the run's second half;
        // null otherwise (critically damped, or crossing earlier). 1 - e^(-zeta omega t) (cos wd t + zeta omega / wd sin wd t) is 1 where
        // tan(wd t) = -wd / (zeta omega): wd t = pi - atan(wd / (zeta omega)) + k pi.
        private double? LastCrossing(double end)
        {
            var zeta = DampingRatio;
            if (zeta >= 1) return null;
            var omega = 2 * Math.PI / Response;
            var damped = omega * Math.Sqrt(1 - zeta * zeta);
            var first = Math.PI - Math.Atan(damped / (zeta * omega));
            var turns = Math.Floor((damped * end - first) / Math.PI);
            if (turns < 0) return null;
            var crossing = (first + turns * Math.PI) / damped;
            return crossing > end * (1 - MotionLanding.Window) && crossing < end ? crossing : null;
        }

        public override double Impulse(double seconds)
        {
            if (seconds <= 0) return 0;
            var end = Duration;
            if (seconds >= end) return 0;
            var squared = Math.Pow(2 * Math.PI / Response, 2);
            return MotionLanding.Land(FreeSpeed(seconds) / squared, seconds, end, -FreeSpeed(end) / squared, FreeAcceleration(end) / squared);
        }

        private double FreeAcceleration(double seconds)
        {
            var omega = 2 * Math.PI / Response;
            var zeta = DampingRatio;
            if (zeta < 1)
            {
                var damped = omega * Math.Sqrt(1 - zeta * zeta);
                return Math.Exp(-zeta * omega * seconds) * omega * omega / damped *
                    (damped * Math.Cos(damped * seconds) - zeta * omega * Math.Sin(damped * seconds));
            }
            if (zeta == 1) return omega * omega * Math.Exp(-omega * seconds) * (1 - omega * seconds);
            var root = Math.Sqrt(zeta * zeta - 1);
            var r1 = -omega * (zeta - root);
            var r2 = -omega * (zeta + root);
            return -r1 * r2 * (r1 * Math.Exp(r1 * seconds) - r2 * Math.Exp(r2 * seconds)) / (r2 - r1);
        }

        // The spring's step response from rest, and its speed: omega^2 times the response to a unit start speed.
        private double FreeSpeed(double seconds)
        {
            var omega = 2 * Math.PI / Response;
            var zeta = DampingRatio;
            if (zeta < 1)
            {
                var damped = omega * Math.Sqrt(1 - zeta * zeta);
                return Math.Exp(-zeta * omega * seconds) * omega * omega / damped * Math.Sin(damped * seconds);
            }
            if (zeta == 1) return omega * omega * seconds * Math.Exp(-omega * seconds);
            var root = Math.Sqrt(zeta * zeta - 1);
            var r1 = -omega * (zeta - root);
            var r2 = -omega * (zeta + root);
            return -r1 * r2 * (Math.Exp(r1 * seconds) - Math.Exp(r2 * seconds)) / (r2 - r1);
        }

        private double Free(double seconds)
        {
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
        public override double Speed(double seconds) => seconds <= Seconds ? 0 : Inner.Speed(seconds - Seconds);
        public override double Impulse(double seconds) => Inner.Impulse(seconds); // a carried speed dies out at once, not after the delay
        public override bool KeepsSpeed => Inner.KeepsSpeed;
    }

    private sealed class SampledEasing(Animation animation) : Easing
    {
        public override double Ease(double progress) => animation.Progress(progress * animation.Duration);
    }
}
