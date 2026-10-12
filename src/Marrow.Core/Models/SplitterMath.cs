namespace Marrow.Core.Models;

// PORT-ONLY, no Swift twin: nth web surfaces, unit W1 (work/design/web-surfaces/DESIGN.md section 4, revision 2,
// owner-approved 2026-10-11). The arithmetic behind the one shared splitter (Marrow.UI.Chrome.Splitter): clamps, the
// rubber band past a limit, collapse and restore, the keyboard map, the value it reports to assistive technology, and
// the spring every splitter change moves on. Everything here is pure, so it is tested without a UI (ED-NEW-web-w1-1).

/// <summary>Which way a splitter moves its divider.</summary>
public enum SplitterOrientation
{
    /// <summary>A vertical divider between side-by-side panes: dragged along x.</summary>
    Vertical,

    /// <summary>A horizontal divider between stacked panes: dragged along y.</summary>
    Horizontal,

    /// <summary>A corner grip: moves width and height together.</summary>
    Both,
}

/// <summary>The side of a divider, in reading order: leading is left (or top), trailing right (or bottom).</summary>
public enum SplitterSide
{
    None,
    Leading,
    Trailing,
}

/// <summary>Which end of a splitter's value range collapses a pane: below the minimum or above the maximum.</summary>
public enum SplitterCollapse
{
    None,
    BelowMinimum,
    AboveMaximum,
}

/// <summary>The keys a focused splitter answers.</summary>
public enum SplitterKey
{
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    Enter,
    Space,
}

public enum SplitterCommandKind
{
    None,
    Step,
    ToMinimum,
    ToMaximum,
    Reset,
    Collapse,
    Restore,
    Toggle,
}

/// <summary>What a key asks a splitter to do. A step carries its logical travel (x mirrored in right-to-left).</summary>
public readonly record struct SplitterCommand(SplitterCommandKind Kind, double Dx = 0, double Dy = 0)
{
    public static SplitterCommand None { get; } = new(SplitterCommandKind.None);
}

/// <summary>
/// One splitter's numbers, in DIP of the size it controls. <see cref="Growth"/> says how logical travel toward the
/// trailing side changes the value: +1 when the value is the leading pane (the sidebar's width), -1 when it is the
/// trailing one (the terminal drawer's height, which grows as its top edge moves up).
/// </summary>
public sealed record SplitterLimits(double Minimum, double Maximum, double Default)
{
    public const double DefaultStep = 8;
    public const double DefaultLargeStep = 48;

    /// <summary>How far past the limit the pointer must go before that side collapses (DESIGN 4.2).</summary>
    public const double DefaultCollapseDistance = 56;

    public SplitterCollapse Collapse { get; init; } = SplitterCollapse.None;

    /// <summary>The value a collapsed splitter reports and draws: 0 for a hidden sidebar, the far end for collapsed panes.</summary>
    public double CollapsedValue { get; init; }

    public double CollapseDistance { get; init; } = DefaultCollapseDistance;

    public int Growth { get; init; } = 1;

    public double Step { get; init; } = DefaultStep;

    public double LargeStep { get; init; } = DefaultLargeStep;

    /// <summary>The value text while collapsed ("Sidebar hidden", "Panes collapsed").</summary>
    public string CollapsedText { get; init; } = "Collapsed";

    /// <summary>The lowest value reported: the collapsed position when the low end collapses, else the minimum.</summary>
    public double RangeMinimum => Collapse == SplitterCollapse.BelowMinimum ? Math.Min(CollapsedValue, Minimum) : Minimum;

    /// <summary>The highest value reported: the collapsed position when the high end collapses, else the maximum.</summary>
    public double RangeMaximum => Collapse == SplitterCollapse.AboveMaximum ? Math.Max(CollapsedValue, Maximum) : Math.Max(Minimum, Maximum);
}

/// <summary>A splitter's committed state: its value, whether its collapsible side is collapsed, and the size to restore.</summary>
public readonly record struct SplitterState(double Value, bool IsCollapsed = false, double? BeforeCollapse = null);

/// <summary>A drag's outcome: the state to commit and the value drawn now (with the rubber band past a limit).</summary>
public readonly record struct SplitterDrag(SplitterState State, double Shown);

/// <summary>What a splitter reports to assistive technology and its readout. <see cref="Value"/> always lies in range.</summary>
public readonly record struct SplitterReport(
    double Value,
    double Minimum,
    double Maximum,
    bool IsCollapsed,
    int Percent,
    string Text,
    string Readout);

public static class SplitterMath
{
    /// <summary>How hard a limit resists: the pointer's overshoot times this (DESIGN 4.2).</summary>
    public const double Resistance = 0.3;

    /// <summary>The most a limit gives before it stops.</summary>
    public const double MaximumGive = 24;

    public static double Clamp(double value, double minimum, double maximum) =>
        Math.Min(Math.Max(value, minimum), Math.Max(minimum, maximum));

    /// <summary>Past a limit the value resists at <see cref="Resistance"/> and gives at most <see cref="MaximumGive"/>.</summary>
    public static double RubberBand(double raw, double minimum, double maximum)
    {
        maximum = Math.Max(minimum, maximum);
        if (raw < minimum) return minimum - Math.Min(MaximumGive, (minimum - raw) * Resistance);
        if (raw > maximum) return maximum + Math.Min(MaximumGive, (raw - maximum) * Resistance);
        return raw;
    }

    /// <summary>The visual side whose pane collapses, from the value end that collapses and the growth direction.</summary>
    public static SplitterSide CollapseSide(SplitterLimits limits) => limits.Collapse switch
    {
        SplitterCollapse.BelowMinimum => limits.Growth > 0 ? SplitterSide.Leading : SplitterSide.Trailing,
        SplitterCollapse.AboveMaximum => limits.Growth > 0 ? SplitterSide.Trailing : SplitterSide.Leading,
        _ => SplitterSide.None,
    };

    /// <summary>
    /// The size a collapse remembers: the value, or nothing when it sat at the collapsing limit (a restore then goes
    /// to the default, DESIGN 4.2 "or the default if that was the minimum").
    /// </summary>
    public static double? Remembered(double value, SplitterLimits limits) => limits.Collapse switch
    {
        SplitterCollapse.BelowMinimum => value <= limits.Minimum ? null : value,
        SplitterCollapse.AboveMaximum => value >= limits.Maximum ? null : value,
        _ => value,
    };

    /// <summary>
    /// A drag from <paramref name="start"/> by <paramref name="travel"/> DIP of logical travel toward the trailing side.
    /// Inside the limits the value follows 1:1; past one it resists (no rubber band under Reduce Motion); past the
    /// collapse distance a collapsible side collapses, and dragging back un-collapses it to follow the pointer again.
    /// </summary>
    public static SplitterDrag Drag(SplitterState start, double travel, SplitterLimits limits, bool reduceMotion = false)
    {
        var from = start.IsCollapsed ? limits.CollapsedValue : start.Value;
        var raw = from + travel * limits.Growth;
        var lo = limits.Minimum;
        var hi = Math.Max(limits.Minimum, limits.Maximum);
        var collapses = limits.Collapse switch
        {
            SplitterCollapse.BelowMinimum => raw < lo - limits.CollapseDistance,
            SplitterCollapse.AboveMaximum => raw > hi + limits.CollapseDistance,
            _ => false,
        };
        if (collapses)
        {
            var remembered = start.IsCollapsed ? start.BeforeCollapse : Remembered(start.Value, limits);
            return new SplitterDrag(new SplitterState(limits.CollapsedValue, true, remembered), limits.CollapsedValue);
        }
        var committed = Clamp(raw, lo, hi);
        var shown = reduceMotion ? committed : RubberBand(raw, lo, hi);
        return new SplitterDrag(new SplitterState(committed), shown);
    }

    /// <summary>The state after <paramref name="command"/>. A step moves along the splitter's own axis.</summary>
    public static SplitterState Apply(SplitterState state, SplitterCommand command, SplitterLimits limits, SplitterOrientation orientation = SplitterOrientation.Vertical)
    {
        var lo = limits.Minimum;
        var hi = Math.Max(limits.Minimum, limits.Maximum);
        switch (command.Kind)
        {
            case SplitterCommandKind.Step:
                if (state.IsCollapsed) return state; // a collapsed side comes back with Space, Ctrl+Arrow, Enter, Home or End
                var travel = orientation == SplitterOrientation.Horizontal ? command.Dy : command.Dx;
                return new SplitterState(Clamp(state.Value + travel * limits.Growth, lo, hi));
            case SplitterCommandKind.ToMinimum:
                return new SplitterState(lo);
            case SplitterCommandKind.ToMaximum:
                return new SplitterState(hi);
            case SplitterCommandKind.Reset:
                return new SplitterState(Clamp(limits.Default, lo, hi));
            case SplitterCommandKind.Collapse:
                if (limits.Collapse == SplitterCollapse.None || state.IsCollapsed) return state;
                return new SplitterState(limits.CollapsedValue, true, Remembered(state.Value, limits));
            case SplitterCommandKind.Restore:
                if (!state.IsCollapsed) return state;
                return new SplitterState(Clamp(state.BeforeCollapse ?? limits.Default, lo, hi));
            case SplitterCommandKind.Toggle:
                return Apply(state, new SplitterCommand(state.IsCollapsed ? SplitterCommandKind.Restore : SplitterCommandKind.Collapse), limits, orientation);
            default:
                return state;
        }
    }

    /// <summary>
    /// The keyboard map (DESIGN 4.2): arrows along the splitter's axis move <paramref name="step"/> DIP (Shift
    /// <paramref name="largeStep"/>), following the visual direction (so the x travel is negated when mirrored);
    /// Home and End go to the minimum and maximum; Enter resets; Space toggles the collapsible side; Ctrl+Arrow toward
    /// that side collapses it and away from it restores it. A corner moves width with Left and Right and height with
    /// Up and Down, and answers Enter only besides.
    /// </summary>
    public static SplitterCommand Command(
        SplitterKey key,
        bool shift,
        bool control,
        SplitterOrientation orientation,
        bool mirrored = false,
        SplitterSide collapseSide = SplitterSide.None,
        double step = SplitterLimits.DefaultStep,
        double largeStep = SplitterLimits.DefaultLargeStep)
    {
        var amount = shift ? largeStep : step;
        var horizontalKey = key is SplitterKey.Left or SplitterKey.Right;
        var verticalKey = key is SplitterKey.Up or SplitterKey.Down;
        if (orientation == SplitterOrientation.Both)
        {
            if (horizontalKey)
            {
                var sign = (key == SplitterKey.Right ? 1 : -1) * (mirrored ? -1 : 1);
                return new SplitterCommand(SplitterCommandKind.Step, sign * amount, 0);
            }
            if (verticalKey) return new SplitterCommand(SplitterCommandKind.Step, 0, (key == SplitterKey.Down ? 1 : -1) * amount);
            return key == SplitterKey.Enter ? new SplitterCommand(SplitterCommandKind.Reset) : SplitterCommand.None;
        }
        var alongAxis = orientation == SplitterOrientation.Vertical ? horizontalKey : verticalKey;
        if (alongAxis)
        {
            var visual = key is SplitterKey.Right or SplitterKey.Down ? 1 : -1;
            var logical = orientation == SplitterOrientation.Vertical && mirrored ? -visual : visual;
            if (control && collapseSide != SplitterSide.None)
            {
                var toward = logical < 0 ? SplitterSide.Leading : SplitterSide.Trailing;
                return new SplitterCommand(toward == collapseSide ? SplitterCommandKind.Collapse : SplitterCommandKind.Restore);
            }
            return orientation == SplitterOrientation.Vertical
                ? new SplitterCommand(SplitterCommandKind.Step, logical * amount, 0)
                : new SplitterCommand(SplitterCommandKind.Step, 0, logical * amount);
        }
        return key switch
        {
            SplitterKey.Home => new SplitterCommand(SplitterCommandKind.ToMinimum),
            SplitterKey.End => new SplitterCommand(SplitterCommandKind.ToMaximum),
            SplitterKey.Enter => new SplitterCommand(SplitterCommandKind.Reset),
            SplitterKey.Space when collapseSide != SplitterSide.None => new SplitterCommand(SplitterCommandKind.Toggle),
            _ => SplitterCommand.None,
        };
    }

    /// <summary>
    /// The value reported for <paramref name="value"/> (or the collapsed position), clamped into the range that includes
    /// the collapsed position, with its place in that range as a percentage, the value text (the number, or the
    /// collapsed state's name) and the readout ("268 DIP").
    /// </summary>
    public static SplitterReport Report(double value, bool collapsed, SplitterLimits limits, string? text = null, string? readout = null)
    {
        var lo = limits.RangeMinimum;
        var hi = Math.Max(lo, limits.RangeMaximum);
        var reported = Clamp(collapsed ? limits.CollapsedValue : value, lo, hi);
        var percent = hi > lo ? (int)Math.Round((reported - lo) / (hi - lo) * 100, MidpointRounding.AwayFromZero) : 0;
        var number = Math.Round(reported, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        return new SplitterReport(
            reported, lo, hi, collapsed, percent,
            text ?? (collapsed ? limits.CollapsedText : number),
            readout ?? (collapsed ? limits.CollapsedText : number + " DIP"));
    }

    /// <summary>The report for a committed <paramref name="state"/>.</summary>
    public static SplitterReport Report(SplitterState state, SplitterLimits limits) => Report(state.Value, state.IsCollapsed, limits);

    /// <summary><c>cubic-bezier(0, 0, 0.58, 1)</c>, the ease-out Chrome.PanelSlide takes under Reduce Motion.</summary>
    public static double EaseOut(double progress) => CubicBezier(0, 0, 0.58, 1, progress);

    /// <summary>A CSS timing curve evaluated at <paramref name="progress"/> (0 to 1), solved for x by bisection.</summary>
    public static double CubicBezier(double x1, double y1, double x2, double y2, double progress)
    {
        if (progress <= 0) return 0;
        if (progress >= 1) return 1;
        static double Curve(double a, double b, double t) => 3 * a * t * (1 - t) * (1 - t) + 3 * b * t * t * (1 - t) + t * t * t;
        double low = 0, high = 1, t = progress;
        for (var i = 0; i < 40; i++)
        {
            t = (low + high) / 2;
            var x = Curve(x1, x2, t);
            if (Math.Abs(x - progress) < 1e-7) break;
            if (x < progress) low = t; else high = t;
        }
        return Curve(y1, y2, t);
    }
}

/// <summary>
/// The spring every splitter change moves on: Chrome.PanelSlide's response 0.32 s and damping 0.9 (DESIGN 2.6), stepped
/// with its velocity so a new target bends the motion instead of restarting it (interruptible). Under Reduce Motion a
/// new target runs the 0.18 s ease-out from where the value is. <see cref="Settle"/> is pointer-down settling: the
/// value lands on its target at once and nothing is left moving.
/// </summary>
public sealed class SplitterSpring
{
    public const double DefaultResponse = 0.32;
    public const double DefaultDamping = 0.9;
    public const double ReducedDuration = 0.18;

    private readonly double stiffness;
    private readonly double friction;
    private readonly double epsilon;
    private double easeFrom;
    private double easeElapsed = double.NaN;

    public SplitterSpring(double value, double response = DefaultResponse, double damping = DefaultDamping, double epsilon = 0.05)
    {
        Value = Target = value;
        var omega = 2 * Math.PI / response;
        stiffness = omega * omega;
        friction = 2 * damping * omega;
        this.epsilon = epsilon;
    }

    public double Value { get; private set; }

    public double Target { get; private set; }

    public double Velocity { get; private set; }

    /// <summary>Whether a step would still move the value.</summary>
    public bool IsMoving => !double.IsNaN(easeElapsed) || Math.Abs(Target - Value) > epsilon || Math.Abs(Velocity) > epsilon;

    /// <summary>Moves toward <paramref name="target"/> from where the value is, keeping its velocity (or easing under Reduce Motion).</summary>
    public void SetTarget(double target, bool reduceMotion = false)
    {
        if (reduceMotion)
        {
            Velocity = 0;
            easeFrom = Value;
            easeElapsed = Value == target ? double.NaN : 0;
            Target = target;
            if (double.IsNaN(easeElapsed)) Value = target;
            return;
        }
        easeElapsed = double.NaN;
        Target = target;
    }

    /// <summary>Jumps to <paramref name="value"/> with nothing left moving.</summary>
    public void Snap(double value)
    {
        Value = Target = value;
        Velocity = 0;
        easeElapsed = double.NaN;
    }

    /// <summary>Pointer-down settling: the value lands on its target now.</summary>
    public void Settle() => Snap(Target);

    /// <summary>Advances <paramref name="seconds"/>; returns whether the value is still moving.</summary>
    public bool Step(double seconds)
    {
        if (seconds <= 0) return IsMoving;
        if (!double.IsNaN(easeElapsed))
        {
            easeElapsed += seconds;
            var progress = Math.Min(1, easeElapsed / ReducedDuration);
            Value = easeFrom + (Target - easeFrom) * SplitterMath.EaseOut(progress);
            if (progress >= 1)
            {
                Value = Target;
                easeElapsed = double.NaN;
            }
            return IsMoving;
        }
        if (!IsMoving)
        {
            Snap(Target);
            return false;
        }
        var steps = Math.Max(1, (int)Math.Ceiling(seconds / 0.004));
        var h = seconds / steps;
        for (var i = 0; i < steps; i++)
        {
            var acceleration = stiffness * (Target - Value) - friction * Velocity;
            Velocity += acceleration * h;
            Value += Velocity * h;
        }
        if (!IsMoving) Snap(Target);
        return IsMoving;
    }
}

/// <summary>
/// One splitter's value over time: the committed state, the drag in progress and the spring that draws it. Pointer
/// down settles the spring first; inside the limits a drag tracks the pointer 1:1 with no spring, except that a
/// collapse or restore mid-drag springs and tracking resumes once that spring has landed (DESIGN 4.2).
/// </summary>
public sealed class SplitterModel
{
    private SplitterLimits limits;
    private SplitterState dragStart;

    public SplitterModel(SplitterLimits limits, SplitterState state)
    {
        this.limits = limits;
        State = Clamped(state, limits);
        Shown = new SplitterSpring(State.Value);
    }

    public SplitterLimits Limits
    {
        get => limits;
        set
        {
            limits = value;
            var clamped = Clamped(State, value);
            if (clamped == State) return;
            State = clamped;
            if (!IsDragging) Shown.Snap(State.Value);
        }
    }

    public SplitterState State { get; private set; }

    /// <summary>The value drawn now.</summary>
    public SplitterSpring Shown { get; }

    public bool IsDragging { get; private set; }

    public bool ReduceMotion { get; set; }

    /// <summary>Pointer-down settling: whatever spring is still in flight lands on its target in this frame.</summary>
    public void PointerDown() => Shown.Settle();

    public void BeginDrag()
    {
        PointerDown();
        dragStart = State;
        IsDragging = true;
    }

    /// <summary>Follows a drag of <paramref name="travel"/> logical DIP since <see cref="BeginDrag"/>; returns whether the collapsed state changed.</summary>
    public bool Drag(double travel)
    {
        if (!IsDragging) BeginDrag();
        var drag = SplitterMath.Drag(dragStart, travel, limits, ReduceMotion);
        var changed = drag.State.IsCollapsed != State.IsCollapsed;
        State = drag.State;
        if (changed || Shown.IsMoving) Shown.SetTarget(drag.Shown, ReduceMotion);
        else Shown.Snap(drag.Shown);
        return changed;
    }

    /// <summary>Release: a value held past a limit springs back to it.</summary>
    public void EndDrag()
    {
        IsDragging = false;
        if (Shown.Value != State.Value || Shown.IsMoving) Shown.SetTarget(State.Value, ReduceMotion);
    }

    /// <summary>Applies a keyboard or reset command; the change springs. Returns whether the state changed.</summary>
    public bool Apply(SplitterCommand command, SplitterOrientation orientation = SplitterOrientation.Vertical)
    {
        var next = SplitterMath.Apply(State, command, limits, orientation);
        if (next == State) return false;
        State = next;
        Shown.SetTarget(State.Value, ReduceMotion);
        return true;
    }

    /// <summary>
    /// Takes a state set elsewhere (the stored value changed under the splitter). A drag under way keeps its own; the
    /// same value and collapse leave a spring in flight alone (a committed step coming back through the store).
    /// </summary>
    public void Load(SplitterState state)
    {
        if (IsDragging) return;
        var next = Clamped(state, limits);
        if (next.Value == State.Value && next.IsCollapsed == State.IsCollapsed) return;
        State = next;
        Shown.Snap(State.Value);
    }

    public SplitterReport Report() => SplitterMath.Report(State, limits);

    private static SplitterState Clamped(SplitterState state, SplitterLimits limits) => state.IsCollapsed
        ? state with { Value = limits.CollapsedValue }
        : state with { Value = SplitterMath.Clamp(state.Value, limits.Minimum, limits.Maximum) };
}
