// PORT-ONLY (OD-0071, ED-246, ED-NEW-softscroll-1, ED-NEW-softscroll-2): the one soft, weighted scrolling behaviour every
// scroll area of the app uses.
// Taken from unit/premium-scroll (9964d3af4, not merged), first for the chat timeline only (UI-CHAT-001, ED-333); since
// ED-NEW-softscroll-1 it is the default of EVERY ScrollViewer of the process (SoftScroll.IsEnabled defaults to true): the
// chat timeline, the diff panel, Settings' pages and sidebar, the thread sidebar, popovers, menus, sheets and panels,
// text editors' own scroll views (their caret scrolling moves the offset at once, which a motion carries along, see
// ScrollDriver). The opt-outs, each named where it is made:
//   * SoftScroll.SetIsEnabled(viewer, false): the viewer (and the scroll views inside it, the property inherits) keeps
//     Avalonia's own stepping; no scroll view of the app sets it today;
//   * a viewer presenting ILogicalScrollable content (logical units, not points: a 100-point notch would be 100 items) is
//     left to Avalonia's logical stepping automatically; none exists in the app today;
//   * SoftScroll.OwnsWheel on a control that takes the wheel itself: the scroll views around it leave its wheel alone.
//     The terminal sets it (TerminalSurface): it scrolls its ConPTY history by whole lines and reports the wheel to a
//     program that asked for the mouse (SwiftTerm, MacTerminalView.swift:3204-3254), so it is no ScrollViewer to glide;
//   * no frame clock bound (FrameClock.IsBound false, never in the app): nothing could animate, so input steps at once.
// The overlay indicator of unit/premium-scroll (ScrollIndicator) is not taken.
//
// Avalonia's ScrollViewer jumps its offset on every input (a wheel notch is 50 points at once, Page Up/Down a page at
// once, a touch flick stops dead on release), which the owner ruled is not the Mac's feel (OD-0071). Here every
// ScrollViewer, whatever its template, is driven by one model on the frame clock (FrameClock.Source, ED-237). Every
// input is a distance delivered over time, and the view follows it, solved exactly between the input's breakpoints, so a
// motion is the same curve at 60 and at 240 Hz:
//   * a wheel (a notch, or a high-resolution wheel's fraction of one; ED-NEW-softscroll-2, the owner's "smooth premium
//     weighted scrolling"): a notch is Windows' "lines to scroll" setting x 33 1/3 points (SystemWheel: 100 points at the
//     default 3 lines, a page for "one screen at a time"). The notches add up to one target, and the view follows it
//     through two critically damped stages of 0.050 s each: x(t) = D (1 - (1 + t/0.05) e^(-t/0.05)) for one notch of D,
//     from no speed, its speed highest at 50 ms (D / (0.05 e), 736 points a second for 100 points), half way at 84 ms,
//     90 % at 194 ms, resting at 0.46 s. At 60 Hz a 100-point notch's frames move 4.5, 10.0, 12.0, 12.1, 11.2, 9.8, ...
//     points. A notch during a glide moves its target with no step in position or speed; a notch the other way turns it
//     round from where the view is, from rest;
//   * a precision touchpad's stream (Windows sends it as WHEEL_DELTA fractions that share no wheel quantum, see
//     WheelClassifier), 50 points per WHEEL_DELTA as ED-246 set it, is spread over the interval since its previous
//     report (a stream's first over 8 ms) and followed through a first-order lag of 4 ms: a steady stroke moves steadily
//     from its first frame. The port adds no coast of its own to it: Windows continues the stream with its own inertial
//     deltas after a lift, and fingers resting on the pad send nothing, so the stream itself says when to stop (ED-246,
//     UNVERIFIED on a recording, probe in E:/Droppy/work/review-scroll-physics/probe). A stream whose first reports looked
//     like a wheel's (and took a notch's weight) takes the excess back when it shows itself a touchpad's;
//   * a touch flick (the presenter moves the view itself while the finger is down) coasts on after the release with
//     UIKit's normal deceleration rate, 0.998 per ms (UNVERIFIED, parked: no trackpad in the reference videos);
//   * every motion ends exactly on its stop: it aims a hair past it (2 points a second's worth) and rests the moment it
//     gets there, so the last step is the smallest, never a settle jump;
//   * the ends: no overscroll (every Mac case measured stops at the end, section 6). A motion headed past an end keeps
//     its speed until it is within 12 ms of it and then brakes onto it (about 40 ms), where the Mac stops in one frame;
//   * keyboard (Page Up/Down, Home/End, when the focused control leaves them unhandled) and programmatic scrolls animate
//     along a SwiftUI curve, `.smooth(duration: 0.25)` for keys, and carry a running speed in;
//   * reduce motion (\.accessibilityReduceMotion, ED-084): every input steps the offset at once;
//   * nested scroll views: the innermost on the wheel's route that can still move that way takes it, and one resting at
//     its end hands it to the one around it (Avalonia's scroll chaining, ScrollViewer.IsScrollChainingEnabled);
//   * a scroll bar's thumb taken by the pointer stops the motion where it is (the thumb moves the offset itself).
// Work per frame is O(1): a few exponentials per axis (sampled every 1 ms while headed past an end) and one Offset
// write. Work per wheel report is a walk up its route.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.VisualTree;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>PORT-ONLY. SwiftUI's <c>ScrollPhase</c>, as far as the port can tell it (a wheel reports none).</summary>
public enum ScrollPhase
{
    Idle,
    Interacting,
    Decelerating,
    Animating,
}

/// <summary>PORT-ONLY. What a wheel report came from (see <see cref="WheelClassifier"/>).</summary>
public enum WheelKind
{
    /// <summary>A notched wheel: whole multiples of WHEEL_DELTA.</summary>
    Notch,

    /// <summary>A high-resolution or free-spinning wheel: every report a multiple of one fraction of WHEEL_DELTA.</summary>
    FineWheel,

    /// <summary>A precision touchpad: fractions of WHEEL_DELTA that share no wheel quantum.</summary>
    Touchpad,
}

/// <summary>
/// PORT-ONLY. Tells a wheel report's source from the stream it belongs to. Windows hands Avalonia every wheel as
/// WM_MOUSEWHEEL with a delta in WHEEL_DELTA (120) units and nothing else: a notched wheel sends multiples of 120, a
/// high-resolution wheel multiples of 120/N (Logitech's 15 for N = 8), and a precision touchpad arbitrary small values.
/// A stream (reports less than <see cref="ScrollPhysics.StreamGap"/> apart) is a wheel while the greatest common divisor
/// of its reports divides 120 and is at least 8, a touchpad once it is not; a touchpad stream stays one to its end.
/// </summary>
public sealed class WheelClassifier
{
    private double last = double.NegativeInfinity;
    private long divisor;
    private WheelKind kind;

    /// <summary>Whether the last report classified started a new stream.</summary>
    public bool IsFresh { get; private set; }

    /// <summary>The kind of a report of <paramref name="lines"/> (Avalonia's delta: units / 120) at <paramref name="now"/>.</summary>
    public WheelKind Classify(double lines, double now)
    {
        var fresh = IsFresh = now - last > ScrollPhysics.StreamGap || now < last;
        last = now;
        if (fresh)
        {
            divisor = 0;
            kind = WheelKind.Notch;
        }
        else if (kind == WheelKind.Touchpad) return kind;
        var raw = Math.Abs(lines) * 120;
        var units = Math.Round(raw);
        if (units == 0 || Math.Abs(raw - units) > 1e-3) return kind = WheelKind.Touchpad;
        divisor = Gcd(divisor, (long)units);
        return kind = divisor % 120 == 0 ? WheelKind.Notch
            : divisor >= 8 && 120 % divisor == 0 ? WheelKind.FineWheel
            : WheelKind.Touchpad;
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}

/// <summary>
/// PORT-ONLY. One axis of the soft scrolling model (see the file note). Times are seconds on any monotonic base; the
/// state is always at <see cref="Time"/>, and every call first advances it to the time it is given.
/// </summary>
public sealed class ScrollPhysics
{
    /// <summary>Points per WHEEL_DELTA of a precision touchpad's stream: Avalonia's own wheel step (ScrollContentPresenter, 50
    /// per 1.0 of delta), as ED-246 set it. A wheel's notch is Windows' lines setting's instead (<see cref="SystemWheel"/>).</summary>
    public const double WheelLine = 50;

    /// <summary>Each of the wheel glide's two critically damped stages (ED-NEW-softscroll-2): the view follows a point that
    /// follows the notches' one target, both with this time constant, so a glide starts from no speed and peaks at it.</summary>
    public const double GlideStage = 0.050;

    /// <summary>The touchpad's lag behind its spread-out stream (keeps the report and display cadences apart).</summary>
    public const double FollowLag = 0.004;

    /// <summary>A stream's first report is spread over this (a 125 Hz report interval) until the stream shows its own.</summary>
    public const double Cadence = 0.008;

    /// <summary>The shortest spread (reports closer than this were coalesced on their way).</summary>
    public const double MinSpread = 0.002;

    /// <summary>The longest spread: reports further apart than this start a new burst.</summary>
    public const double MaxSpread = 0.034;

    /// <summary>Reports further apart than this belong to different streams.</summary>
    public const double StreamGap = 0.1;

    /// <summary>The touch coast's decay (UNVERIFIED, see the file note).</summary>
    public const double MomentumDecay = 0.4995;

    /// <summary>A motion headed past an end brakes onto it from this far out in time (its speed times this).</summary>
    public const double LandingDecay = 0.012;

    /// <summary>The speed every motion ends at, the moment it reaches its stop (points per second).</summary>
    public const double RestSpeed = 2;

    /// <summary>The longest a stream that stopped between rows takes to reach the next row (the terminal).</summary>
    public const double RowSettle = 0.25;

    /// <summary>The fastest coast (points per second); the Mac's wheel flicks reach 38 000.</summary>
    public const double MaxSpeed = 20000;

    /// <summary>The span of recent touch movement the release speed is measured over.</summary>
    public const double VelocityWindow = 0.06;

    /// <summary>A programmatic spring is run out until it is this close to its destination (points).</summary>
    public const double AnimationRest = 0.05;

    /// <summary>The longest time a carried speed takes to die out inside an animation.</summary>
    public const double CarryDecay = 0.04;

    private const int SpreadSlots = 32;

    private enum Mode { Idle, Lag, Held, Animate }

    private enum Kind { Glide, Follow, Coast }

    private Mode mode;
    private Kind kind;
    private double min;
    private double max = double.MaxValue;
    // Lag: the input path (where the input has got to at Time, unclamped), its spreads still delivering, the lag.
    private double tau = GlideStage;
    private double baseTau = GlideStage;
    // The wheel's two-stage glide (staged): the first stage, which follows the aim while the view follows it. Its input has
    // all arrived (the notches' sum is `total`), so it is always resting on its stop.
    private bool staged;
    private double inner;
    private double input;
    private double total; // where the input path ends, summed exactly from the reports
    private readonly double[] rates = new double[SpreadSlots];
    private readonly double[] ends = new double[SpreadSlots];
    private int spreads;
    // Once the spreads have run out: the stop the view rests on and the aim a hair past it.
    private bool resting;
    private double stop;
    private double aim;
    private double reach; // the stop before the ends clamp it
    private double settledInput; // the input path when it ran out
    private int landing; // +1 braking onto the end at max, -1 onto min
    private double lastReport = double.NegativeInfinity;
    private bool holdRest;
    private double cadence = Cadence;
    // Held (a touch drag): where the finger has taken the view, and recent samples for the release speed.
    private double finger;
    private readonly double[] sampleTimes = new double[16];
    private readonly double[] sampleDistances = new double[16];
    private int sampleCount;
    private int sampleNext;
    // Animate.
    private double from;
    private double to;
    private double start;
    private double carry;
    private double scale = 1;
    private double finish;
    private Animation? curve;

    /// <summary>Where the view is.</summary>
    public double Position { get; private set; }

    /// <summary>The time the state is at.</summary>
    public double Time { get; private set; }

    /// <summary>A whole step the view rests on (the terminal's row height); 0 for none.</summary>
    public double Quantum { get; set; }

    /// <summary>Whether anything is moving.</summary>
    public bool IsMoving => mode != Mode.Idle;

    /// <summary>The speed at <see cref="Time"/> (points per second).</summary>
    public double Velocity => mode switch
    {
        Mode.Lag when staged => (inner - Position) / tau,
        Mode.Lag => ((resting ? aim : input) - Position) / tau,
        Mode.Held => Speed(Time),
        Mode.Animate => (AnimatedAt(Time - start + 1e-6) - AnimatedAt(Time - start)) / 1e-6,
        _ => 0,
    };

    /// <summary>Where the view will come to rest if nothing else happens.</summary>
    public double Target => mode switch
    {
        Mode.Lag when resting => stop,
        Mode.Lag when landing != 0 => landing > 0 ? max : min,
        Mode.Lag => Clamp(Quantize(Pending(), Math.Sign(Pending() - Position))),
        Mode.Animate => Clamp(to),
        _ => Position,
    };

    /// <summary>The phase this motion reports: a wheel glide reports none (ThreadTimeline.swift:466-467).</summary>
    public ScrollPhase Phase => mode switch
    {
        Mode.Held => ScrollPhase.Interacting,
        Mode.Lag => kind switch
        {
            Kind.Follow => ScrollPhase.Interacting,
            Kind.Coast => ScrollPhase.Decelerating,
            _ => ScrollPhase.Idle,
        },
        Mode.Animate => ScrollPhase.Animating,
        _ => ScrollPhase.Idle,
    };

    /// <summary>Stops everything at <paramref name="position"/>, at time <paramref name="now"/>.</summary>
    public void Reset(double position, double now)
    {
        Time = now;
        Settle(position);
    }

    /// <summary>The scrollable range; what lies outside it is pulled in.</summary>
    public void SetBounds(double minimum, double maximum)
    {
        min = minimum;
        max = Math.Max(minimum, maximum);
        Position = Clamp(Position);
        finger = Clamp(finger);
        if (mode == Mode.Lag && staged) Aim(); // the stop the notches' sum comes to in the new range
        else if (mode == Mode.Lag && resting && (stop < min || stop > max))
        {
            stop = Clamp(stop);
            aim = stop + Math.Sign(stop - Position) * RestSpeed * tau;
        }
    }

    /// <summary>Someone else moved the view by <paramref name="delta"/>: the motion moves with it.</summary>
    public void Shift(double delta)
    {
        Position += delta;
        inner += delta;
        input += delta;
        total += delta;
        stop += delta;
        aim += delta;
        reach += delta;
        settledInput += delta;
        finger += delta;
        from += delta;
        to += delta;
    }

    /// <summary>Wheel input (a notch, or a fine wheel's fraction of one) of <paramref name="distance"/> points.</summary>
    public void Notch(double distance, double now, bool instant) => Report(distance, now, instant, Kind.Glide);

    /// <summary>A touchpad's report of <paramref name="distance"/> points.</summary>
    public void Precise(double distance, double now, bool instant) => Report(distance, now, instant, Kind.Follow);

    private void Report(double distance, double now, bool instant, Kind next)
    {
        // A spread that ends just as the next report arrives does not come to rest in between: the stream goes on.
        holdRest = true;
        Advance(now);
        holdRest = false;
        var interval = now - lastReport;
        lastReport = now;
        if (distance == 0) return;
        if (instant)
        {
            var side = Math.Sign(distance);
            Settle(Clamp(Quantize(Target + distance, side)));
            return;
        }
        var direction = Math.Sign(distance);
        if (mode == Mode.Lag && landing != 0)
        {
            if (direction == landing) return; // already braking onto the end it is going to
            Rebase();
        }
        else if (mode == Mode.Lag && Heading() == -direction) Rebase(); // turned back: from where the view is, from rest
        if (next == Kind.Glide)
        {
            Glide(distance);
            return;
        }
        if (mode == Mode.Lag && staged) Unstage(FollowLag);
        const double lag = FollowLag; // the spread path below is the touchpad's (the wheel's glide is Glide's)
        if (mode != Mode.Lag) Enter(lag, carry: Math.Sign(Velocity) != -direction);
        else if (lag != baseTau || resting) Retune(lag);
        kind = next;
        // Each report is the movement since the one before it: spread over that interval (a stream's first, or the
        // first after a pause, over the stream's usual cadence), so a steady stream is a steady motion.
        double width;
        if (interval >= MinSpread && interval <= MaxSpread)
        {
            width = interval;
            cadence = 0.75 * cadence + 0.25 * interval;
        }
        else width = interval >= 0 && interval < MinSpread ? MinSpread : cadence;
        AddSpread(distance, width);
    }

    // The way the motion is going: to its pending stop, or along its speed when it is there.
    private int Heading()
    {
        var pending = resting ? stop : Pending();
        return pending != Position ? Math.Sign(pending - Position) : Math.Sign(Velocity);
    }

    // Where the input path ends once its spreads have delivered.
    private double Pending() => spreads > 0 ? total : input;

    // A wheel's report (a notch, or a fine wheel's fraction of one): the notches add up to one target, which the view
    // follows through two critically damped stages (ED-NEW-softscroll-2). A glide under way takes the notch into its
    // target with no step in position or speed; anything else hands over where it was going and the speed it has (a notch
    // that turns it back started from where the view is, at rest, above).
    private void Glide(double distance)
    {
        if (mode != Mode.Lag || !staged)
        {
            var speed = mode == Mode.Idle ? 0 : Velocity;
            var target = mode == Mode.Idle ? Position : Target;
            if (Math.Sign(speed) == -Math.Sign(distance) || Math.Sign(target - Position) == -Math.Sign(distance))
            {
                speed = 0;
                target = Position;
            }
            mode = Mode.Lag;
            kind = Kind.Glide;
            staged = true;
            tau = baseTau = GlideStage;
            inner = Position + speed * tau; // the view's speed is (inner - Position) / tau
            input = total = target;
            spreads = 0;
            landing = 0;
            curve = null;
        }
        total += distance;
        Aim();
    }

    // The glide's one target: the notches' sum (the next whole row ahead, for the terminal's rows), the stop it rests on,
    // and the aim a hair past it, RestSpeed's worth over a stage, so the view reaches its stop and rests there at about
    // that speed. Headed past an end, it aims where it was going (braking onto the end is Brakes' and Land's).
    private void Aim()
    {
        resting = true;
        input = settledInput = total;
        var side = Math.Sign(total - Position);
        reach = Quantize(total, side != 0 ? side : Math.Sign(Velocity));
        stop = Clamp(reach);
        aim = (reach != stop ? reach : stop) + Math.Sign(stop - Position) * RestSpeed * tau;
    }

    // The glide hands over to the touchpad's first-order follow (a stream taken for a fine wheel's turned out to be a
    // touchpad's): position and speed carry on, and what the glide still had to go is spread over a stage, nothing lost.
    private void Unstage(double lag)
    {
        var speed = Velocity;
        var pending = total;
        staged = false;
        resting = false;
        spreads = 0;
        tau = baseTau = lag;
        input = total = Position + speed * lag;
        var rest = pending - input;
        if (rest != 0 && Math.Sign(rest) != -Math.Sign(speed)) AddSpread(rest, GlideStage);
    }

    /// <summary>Wheel input already given was weighed as a wheel's and turned out to be a touchpad's: where the glide is
    /// going moves by <paramref name="delta"/> points; position and speed carry on.</summary>
    public void Amend(double delta, double now)
    {
        Advance(now);
        if (delta == 0 || mode != Mode.Lag || !staged) return;
        total += delta;
        Aim();
    }

    // Input taking over from rest, a touch or an animation: its speed carries on, unless the input turns it back.
    private void Enter(double lag, bool carry)
    {
        var speed = mode == Mode.Idle || !carry ? 0 : Velocity;
        staged = false;
        mode = Mode.Lag;
        tau = baseTau = lag;
        input = total = Position + speed * lag; // the speed carries on
        spreads = 0;
        resting = false;
        landing = 0;
        curve = null;
    }

    // A new lag (a wheel stream turned out to be a touchpad's, or the reverse) or new input after the spreads ran out:
    // the speed carries on, and what was still to come is delivered over the old lag.
    private void Retune(double lag)
    {
        if (resting)
        {
            // Resumed at the lag of its kind, from the speed it has; the aim past the stop is taken back over that lag,
            // so the input still adds up exactly.
            var now = Velocity;
            resting = false;
            tau = baseTau;
            input = total = Position + now * tau;
            if (settledInput != input) AddSpread(settledInput - input, tau);
        }
        if (lag == tau) return;
        var speed = Velocity;
        var old = tau;
        var pending = Pending();
        spreads = 0;
        tau = baseTau = lag;
        input = total = Position + speed * lag;
        var rest = pending - input;
        if (rest != 0 && Math.Sign(rest) == Math.Sign(speed)) AddSpread(rest, old);
    }

    private void Rebase()
    {
        staged = false;
        inner = input = total = Position;
        tau = baseTau;
        spreads = 0;
        resting = false;
        landing = 0;
    }

    // `distance` delivered evenly over the next `width` seconds.
    private void AddSpread(double distance, double width)
    {
        total += distance;
        var rate = distance / width;
        var end = Time + width;
        if (spreads == SpreadSlots)
        {
            // Out of slots (a flood of coalesced reports): fold this one into the last.
            var last = spreads - 1;
            var until = Math.Max(ends[last], end);
            rates[last] = (rates[last] * (ends[last] - Time) + rate * (end - Time)) / (until - Time);
            ends[last] = until;
            return;
        }
        rates[spreads] = rate;
        ends[spreads] = end;
        spreads++;
    }

    /// <summary>A touch drag moved the view to <paramref name="position"/> itself (Avalonia's presenter does that).</summary>
    public void Touch(double position, double now)
    {
        Advance(now);
        if (mode != Mode.Held)
        {
            sampleCount = 0;
            finger = Position;
        }
        mode = Mode.Held;
        staged = false;
        curve = null;
        AddSample(now, position - finger);
        finger = Position = Clamp(position);
    }

    /// <summary>The touch drag ended: the view coasts on from the release speed.</summary>
    public void Release(double now)
    {
        if (mode != Mode.Held) return;
        Advance(now);
        var speed = Math.Clamp(Speed(now), -MaxSpeed, MaxSpeed);
        mode = Mode.Lag;
        kind = Kind.Coast;
        tau = baseTau = MomentumDecay;
        input = total = Position + speed * tau; // x(t) = x0 + v tau (1 - e^(-t/tau)): it travels v tau, from speed v
        spreads = 0;
        landing = 0;
        Rest();
    }

    /// <summary>Animates to <paramref name="destination"/> along <paramref name="animation"/>, carrying any speed in. With
    /// <paramref name="runsOut"/> off the curve is followed exactly and ends at its duration, as the port's other programmatic
    /// animations do (ED-333: the chat timeline's, pinned by its tests, until ED-246 (4) is ruled on there).</summary>
    public void AnimateTo(double destination, Animation animation, double now, bool runsOut = true)
    {
        Advance(now);
        carry = mode == Mode.Idle ? 0 : Velocity;
        from = Position;
        to = destination;
        start = now;
        curve = animation;
        mode = Mode.Animate;
        staged = false;
        // A spring never quite arrives (SwiftUI's .smooth(0.25) is 0.8 % short at its settling duration): it is run on,
        // scaled to arrive exactly once it is within AnimationRest, so it ends on its destination with no snap.
        scale = 1;
        finish = animation.Duration;
        var distance = Math.Abs(to - from);
        if (runsOut && distance > 0 && Math.Abs(1 - animation.Progress(animation.Duration)) > 1e-9)
        {
            var stretched = 1 + AnimationRest / distance;
            var step = animation.Duration / 64;
            var overshoots = false;
            for (var t = step; t <= 4 * animation.Duration; t += step) overshoots |= animation.Progress(t) > 1 + 1e-9;
            if (!overshoots)
            {
                var t = animation.Duration;
                while (stretched * animation.Progress(t) < 1 && t < 8 * animation.Duration) t += step;
                var low = t - step;
                for (var i = 0; i < 50; i++)
                {
                    var mid = (low + t) / 2;
                    if (stretched * animation.Progress(mid) < 1) low = mid;
                    else t = mid;
                }
                scale = stretched;
                finish = t;
            }
        }
    }

    /// <summary>Ends a programmatic animation where it is; user motion is left alone.</summary>
    public void StopAnimation()
    {
        if (mode == Mode.Animate) Settle(Position);
    }

    /// <summary>Stops everything at <paramref name="position"/>.</summary>
    public void Jump(double position) => Settle(Clamp(position));

    /// <summary>Advances to <paramref name="now"/> (a frame); false once the motion has come to rest.</summary>
    public bool Step(double now)
    {
        Advance(now);
        return mode != Mode.Idle;
    }

    private void Advance(double now)
    {
        if (now <= Time) return;
        switch (mode)
        {
            case Mode.Lag:
                AdvanceLag(now);
                break;
            case Mode.Animate:
            {
                var t = now - start;
                if (t >= finish) Settle(Clamp(to));
                else Position = Clamp(AnimatedAt(t));
                break;
            }
        }
        Time = now;
    }

    // The curve, plus the carried speed: carry t (1 - t/T)^2 e^(-t/fade) leaves at the carried speed, dies out within
    // the curve and is exactly nothing, with no speed, at its end T.
    private double AnimatedAt(double t)
    {
        if (curve is null || t <= 0) return from;
        var fade = Math.Min(CarryDecay, finish / 8);
        var left = Math.Max(0, 1 - t / finish);
        return from + (to - from) * Math.Min(1, scale * curve.Progress(t)) + carry * t * left * left * Math.Exp(-t / fade);
    }

    // The lag, solved exactly from one breakpoint (a spread ending, braking onto an end, reaching the stop) to the next. The
    // wheel's glide (staged) has none but the last two: it is one exact step per frame.
    private void AdvanceLag(double now)
    {
        while (mode == Mode.Lag && Time < now)
        {
            // The input has all arrived: rest on its end; unless a report is arriving at this very moment (within a
            // microsecond of the last spread's end), which carries the stream straight on.
            if (spreads == 0 && !resting && !(holdRest && now - Time < 1e-6)) Rest();
            if (mode != Mode.Lag) break;
            var next = now;
            var slope = 0.0;
            for (var i = 0; i < spreads; i++)
            {
                next = Math.Min(next, ends[i]);
                slope += rates[i];
            }
            var h = next - Time;
            var a0 = resting ? aim : input;
            if (landing == 0 && Brakes(a0, slope, h, out var at, out var side, out var speed))
            {
                Integrate(a0, slope, at);
                Land(side, speed);
                continue;
            }
            if (resting && Arrives(h))
            {
                Settle(stop);
                return;
            }
            Integrate(a0, slope, h);
            if (spreads == 0) continue;
            var kept = 0;
            for (var i = 0; i < spreads; i++)
            {
                if (ends[i] <= Time + 1e-12) continue;
                rates[kept] = rates[i];
                ends[kept] = ends[i];
                kept++;
            }
            spreads = kept;
            if (spreads != 0) continue;
            input = total; // exactly where the reports added up to
        }
    }

    private void Integrate(double a0, double slope, double h)
    {
        var decay = Math.Exp(-h / tau);
        if (staged)
        {
            var lead = inner - a0;
            Position = Clamp(a0 + (Position - a0 + lead * h / tau) * decay);
            inner = a0 + lead * decay;
        }
        else Position = Clamp(a0 + slope * (h - tau) + (Position - a0 + slope * tau) * decay);
        if (spreads > 0) input += slope * h;
        Time += h;
    }

    // Where the view is u seconds on, following a0 (and the input path's slope), and its speed there. First order:
    // x(u) = a0 + slope (u - tau) + (x0 - a0 + slope tau) e^(-u/tau). The two stages, toward a0 (no slope: the glide's input
    // has all arrived): x(u) = a0 + (x0 - a0 + (z0 - a0) u/tau) e^(-u/tau), z the first stage.
    private double PathAt(double a0, double slope, double u) => staged
        ? a0 + (Position - a0 + (inner - a0) * u / tau) * Math.Exp(-u / tau)
        : a0 + slope * (u - tau) + (Position - a0 + slope * tau) * Math.Exp(-u / tau);

    private double SpeedAt(double a0, double slope, double u) => staged
        ? (inner - Position - (inner - a0) * u / tau) * Math.Exp(-u / tau) / tau
        : slope - (Position - a0 + slope * tau) / tau * Math.Exp(-u / tau);

    // The input has all arrived: rest on its end (the next whole row ahead, for the terminal), aiming a hair past it.
    private void Rest()
    {
        resting = true;
        settledInput = input;
        var direction = Math.Sign(input - Position);
        reach = Quantize(input, direction != 0 ? direction : Math.Sign(Velocity));
        stop = Clamp(reach);
        var side = Math.Sign(stop - Position);
        // Headed past an end, it keeps aiming where it was going (braking onto the end is Brakes' and Land's).
        var past = reach != stop;
        if (side == 0)
        {
            Settle(stop);
            return;
        }
        if (Quantum > 0 && Math.Abs(stop - input) > 1e-9)
        {
            // Stopped between rows: on to the next one, from the speed it has (no stop, then a creep).
            var speed = side * (input - Position) / tau;
            tau = speed > RestSpeed ? Math.Clamp(side * (stop - Position) / (speed - RestSpeed), tau, RowSettle) : RowSettle;
        }
        aim = (past ? reach : stop) + side * RestSpeed * tau;
    }

    // Whether the view reaches its stop within h, from the aim past it: x(u) = aim + (x0 - aim) e^(-u/tau). The two stages'
    // x(u) - aim is (a line) times e^(-u/tau), which has one turning point and dies out on the aim's side of the stop: from
    // before the stop it crosses it once, so where it is at h tells.
    private bool Arrives(double h)
    {
        var side = Math.Sign(aim - stop);
        if (side == 0 || side * (stop - Position) <= 0) return true;
        if (staged) return side * (PathAt(aim, 0, h) - stop) >= 0;
        var u = tau * Math.Log((aim - Position) / (aim - stop));
        return u <= h + 1e-12;
    }

    // Whether, within h, the view comes within LandingDecay (or its own lag, if shorter) of its speed from an end it is
    // headed past; then the time it does, which end, and its speed there.
    private bool Brakes(double a0, double slope, double h, out double at, out int side, out double speed)
    {
        at = 0;
        side = 0;
        speed = 0;
        var headed = resting ? reach : Pending();
        side = headed > max ? 1 : headed < min ? -1 : 0;
        if (side == 0) return false;
        var end = side > 0 ? max : min;
        var brake = Math.Min(LandingDecay, tau);
        var s = side;
        double Margin(double u) => s * (end - PathAt(a0, slope, u)) - brake * s * SpeedAt(a0, slope, u);
        if (Margin(0) <= 0)
        {
            speed = SpeedAt(a0, slope, 0);
            return true;
        }
        // Sampled every millisecond (frame-rate independent), then bisected.
        var count = Math.Max(1, (int)Math.Ceiling(h / 0.001));
        var low = 0.0;
        for (var k = 1; k <= count; k++)
        {
            var u = h * k / count;
            if (Margin(u) > 0)
            {
                low = u;
                continue;
            }
            var high = u;
            for (var i = 0; i < 60; i++)
            {
                var mid = (low + high) / 2;
                if (Margin(mid) > 0) low = mid;
                else high = mid;
            }
            at = high;
            speed = SpeedAt(a0, slope, high);
            return true;
        }
        return false;
    }

    // Braking onto an end from the speed the view has there, with no step in that speed.
    private void Land(int side, double speed)
    {
        var end = side > 0 ? max : min;
        landing = side;
        staged = false; // the landing is first order, from the speed the glide has here
        spreads = 0;
        resting = true;
        settledInput = input;
        reach = stop = end;
        var room = side * (end - Position);
        var toward = side * speed;
        tau = toward > RestSpeed ? Math.Max(1e-4, room / (toward - RestSpeed)) : Math.Min(LandingDecay, tau);
        aim = end + side * RestSpeed * tau;
    }

    private double Quantize(double value, int direction)
    {
        if (Quantum <= 0) return value;
        var rows = value / Quantum;
        var whole = Math.Round(rows);
        if (Math.Abs(rows - whole) < 1e-6) return whole * Quantum;
        return (direction >= 0 ? Math.Ceiling(rows) : Math.Floor(rows)) * Quantum;
    }

    // The touch speed over the recent window: everything after the oldest sample in it, over the time since that one.
    private double Speed(double now)
    {
        var oldest = double.NaN;
        var sum = 0.0;
        var latest = double.NaN;
        for (var i = 0; i < sampleCount; i++)
        {
            var index = (sampleNext - 1 - i + sampleTimes.Length) % sampleTimes.Length;
            var time = sampleTimes[index];
            if (now - time > VelocityWindow) break;
            if (double.IsNaN(latest)) latest = time;
            if (!double.IsNaN(oldest)) sum += sampleDistances[(index + 1) % sampleTimes.Length];
            oldest = time;
        }
        if (double.IsNaN(oldest) || latest - oldest < 0.004) return 0;
        return sum / (latest - oldest);
    }

    private void AddSample(double time, double distance)
    {
        sampleTimes[sampleNext] = time;
        sampleDistances[sampleNext] = distance;
        sampleNext = (sampleNext + 1) % sampleTimes.Length;
        sampleCount = Math.Min(sampleCount + 1, sampleTimes.Length);
    }

    private void Settle(double position)
    {
        mode = Mode.Idle;
        Position = inner = input = total = stop = aim = reach = settledInput = finger = position;
        staged = false;
        spreads = 0;
        resting = false;
        landing = 0;
        curve = null;
    }

    private double Clamp(double value) => Math.Clamp(value, min, max);
}

/// <summary>
/// PORT-ONLY. Drives two <see cref="ScrollPhysics"/> axes against something that scrolls (a ScrollViewer, the terminal's
/// history) on the frame clock. It owns no offset: it reads and writes the host's, and any change the host makes between
/// frames (anchoring, a clamp, an instant jump) carries the motion along with it.
/// </summary>
public sealed class ScrollDriver
{
    private readonly Func<Vector> read;
    private readonly Action<Vector> write;
    private readonly Func<Vector> limit;
    private readonly Func<bool> reduceMotion;
    private readonly ScrollPhysics x = new();
    private readonly ScrollPhysics y = new();
    private readonly Action<TimeSpan> frame;
    private IFrameSource? source;
    private bool outstanding;
    private bool running;
    private Vector written;

    /// <param name="read">The host's offset.</param>
    /// <param name="write">Sets the host's offset.</param>
    /// <param name="limit">The largest offset on each axis (the smallest is zero).</param>
    /// <param name="reduceMotion">\.accessibilityReduceMotion for the host.</param>
    public ScrollDriver(Func<Vector> read, Action<Vector> write, Func<Vector> limit, Func<bool> reduceMotion)
    {
        this.read = read;
        this.write = write;
        this.limit = limit;
        this.reduceMotion = reduceMotion;
        frame = OnFrame;
    }

    /// <summary>The phase changed (raised at the frame it changed at, and on input).</summary>
    public event Action<ScrollPhase>? PhaseChanged;

    /// <summary>The current phase.</summary>
    public ScrollPhase Phase { get; private set; }

    /// <summary>Whether a motion is under way (frames are being asked for).</summary>
    public bool IsMoving => running;

    /// <summary>The whole step the vertical axis rests on (the terminal's row); 0 for none.</summary>
    public double Quantum
    {
        get => y.Quantum;
        set => y.Quantum = value;
    }

    /// <summary>Where the motion will come to rest; the offset when nothing moves.</summary>
    public Vector Target => running ? new Vector(x.Target, y.Target) : read();

    /// <summary>The seconds the physics runs on: the Swift clock, which tests drive by hand.</summary>
    public static double Now => SwiftClock.Now.Ticks / (double)TimeSpan.TicksPerSecond;

    /// <summary>Whether the motion's target can still move along <paramref name="direction"/> (its signs only).</summary>
    public bool CanMove(Vector direction)
    {
        var target = Target;
        var most = limit();
        return (direction.Y > 0 && target.Y < most.Y - 0.01) || (direction.Y < 0 && target.Y > 0.01)
            || (direction.X > 0 && target.X < most.X - 0.01) || (direction.X < 0 && target.X > 0.01);
    }

    /// <summary>Whether a motion is under way along <paramref name="direction"/> (its signs only): more input that way is its.</summary>
    public bool IsHeading(Vector direction) => running
        && ((direction.Y != 0 && Math.Sign(y.Velocity) == Math.Sign(direction.Y)) || (direction.X != 0 && Math.Sign(x.Velocity) == Math.Sign(direction.X)));

    /// <summary>Wheel input of <paramref name="distance"/> points per axis, from a <paramref name="kind"/> device.</summary>
    public void Wheel(Vector distance, WheelKind kind)
    {
        var now = Begin();
        var instant = reduceMotion();
        if (distance.X != 0) Input(x, distance.X, kind, now, instant);
        if (distance.Y != 0) Input(y, distance.Y, kind, now, instant);
        Commit(now);

        static void Input(ScrollPhysics axis, double d, WheelKind kind, double now, bool instant)
        {
            if (kind == WheelKind.Touchpad) axis.Precise(d, now, instant);
            else axis.Notch(d, now, instant);
        }
    }

    /// <summary>Wheel input already given turns out to have been weighed wrong (see <see cref="ScrollPhysics.Amend"/>):
    /// where the glide is going moves by <paramref name="delta"/> points per axis.</summary>
    public void Amend(Vector delta)
    {
        if (!running) return;
        var now = Begin();
        x.Amend(delta.X, now);
        y.Amend(delta.Y, now);
        Commit(now);
    }

    /// <summary>The host was moved by a touch drag (Avalonia's presenter moves it itself).</summary>
    public void Touch()
    {
        // Not Begin: the presenter has already moved the offset, and that move is the touch, not someone else's.
        var now = Now;
        var at = read();
        if (!running)
        {
            x.Reset(at.X, now);
            y.Reset(at.Y, now);
        }
        Bound();
        x.Touch(at.X, now);
        y.Touch(at.Y, now);
        written = at;
        if (reduceMotion())
        {
            x.Jump(at.X);
            y.Jump(at.Y);
        }
        Commit(now);
    }

    /// <summary>The touch drag ended.</summary>
    public void Release()
    {
        if (!running) return;
        var now = Begin();
        x.Release(now);
        y.Release(now);
        if (reduceMotion())
        {
            x.Jump(x.Position);
            y.Jump(y.Position);
        }
        Commit(now);
    }

    /// <summary>
    /// Scrolls to <paramref name="destination"/> along <paramref name="animation"/>. With none (or reduce motion) the
    /// offset is set at once: a running programmatic animation ends, and a reader's own motion carries on from there.
    /// </summary>
    public void ScrollTo(Vector destination, Animation? animation, bool runsOut = true)
    {
        if (animation is null || reduceMotion())
        {
            StopAnimation();
            write(destination);
            if (!running) written = read();
            return;
        }
        var now = Begin();
        x.AnimateTo(destination.X, animation, now, runsOut);
        y.AnimateTo(destination.Y, animation, now, runsOut);
        Commit(now);
    }

    /// <summary>Ends a programmatic animation where it is.</summary>
    public void StopAnimation()
    {
        if (!running) return;
        x.StopAnimation();
        y.StopAnimation();
        if (!x.IsMoving && !y.IsMoving) Stop();
    }

    /// <summary>Stops everything at <paramref name="destination"/> at once.</summary>
    public void Jump(Vector destination)
    {
        Stop();
        write(destination);
        written = read();
    }

    /// <summary>Stops everything where it is and withdraws the frame request (the host left the screen).</summary>
    public void Stop()
    {
        running = false;
        if (outstanding)
        {
            outstanding = false;
            source?.CancelFrame(frame);
        }
        Report(ScrollPhase.Idle);
    }

    private double Begin()
    {
        var now = Now;
        if (!running)
        {
            var at = read();
            x.Reset(at.X, now);
            y.Reset(at.Y, now);
            written = at;
        }
        else Sync();
        Bound();
        return now;
    }

    // An offset the host changed since the last write moves the motion with it.
    private void Sync()
    {
        var at = read();
        if (at == written) return;
        x.Shift(at.X - written.X);
        y.Shift(at.Y - written.Y);
        written = at;
    }

    private void Bound()
    {
        var most = limit();
        x.SetBounds(0, Math.Max(0, most.X));
        y.SetBounds(0, Math.Max(0, most.Y));
    }

    private void Commit(double now)
    {
        if (x.IsMoving || y.IsMoving)
        {
            if (!running) source = FrameClock.Source.Current;
            running = true;
            Request();
        }
        else
        {
            running = false;
            Write();
        }
        Report(running ? Current() : ScrollPhase.Idle);
    }

    private void Request()
    {
        if (outstanding) return;
        outstanding = true;
        source!.RequestFrame(frame);
    }

    private void OnFrame(TimeSpan _)
    {
        outstanding = false;
        if (!running) return;
        var now = Now;
        Sync();
        Bound();
        var movingX = x.Step(now);
        var movingY = y.Step(now);
        Write();
        running = movingX || movingY;
        Report(running ? Current() : ScrollPhase.Idle);
        if (running) Request();
    }

    private void Write()
    {
        var next = new Vector(x.Position, y.Position);
        if (next != read())
        {
            write(next);
            WroteAt = Now;
        }
        written = read();
    }

    /// <summary>When this driver last moved its host (seconds); the scroll's last frame is reported after it stops.</summary>
    public double WroteAt { get; private set; } = double.NegativeInfinity;

    private ScrollPhase Current() => y.IsMoving ? y.Phase : x.Phase;

    private void Report(ScrollPhase phase)
    {
        if (phase == Phase) return;
        Phase = phase;
        PhaseChanged?.Invoke(phase);
    }
}

/// <summary>PORT-ONLY. A scroll view's phase changed (SwiftUI <c>.onScrollPhaseChange</c>).</summary>
public sealed class ScrollPhaseChangedEventArgs(ScrollPhase phase) : RoutedEventArgs(SoftScroll.PhaseChangedEvent)
{
    public ScrollPhase Phase { get; } = phase;
}

/// <summary>PORT-ONLY. The soft scrolling behaviour on every ScrollViewer (see the file note).</summary>
public static class SoftScroll
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScrollViewer, ScrollDriver> Drivers = new();
    private static readonly WheelClassifier Classifier = new();
    // What the current stream's wheel reports were given beyond a touchpad's weight, and the motion they went to: a stream
    // that turns out to be a touchpad's (its first reports can look like a fine wheel's) takes that back.
    private static Vector overweight;
    private static WeakReference<ScrollDriver>? overweighted;
    private static bool installed;
    // A touchpad stream stays with the scroll view it started on (as macOS latches a gesture), its inertia included.
    private static WeakReference<ScrollViewer>? latched;
    // The key being routed: whether something below the scroll view it has reached handled it, the offsets before
    // Avalonia's own ScrollViewer.OnKeyDown jumped them, and a scroll view waiting for its templated owner.
    private static KeyEventArgs? routedKey;
    private static bool handledBelow;
    private static ScrollViewer? waiting;
    private static readonly Dictionary<ScrollViewer, Vector> KeyOffsets = [];

    /// <summary>The curve keyboard scrolls take: <c>.smooth(duration: 0.25)</c> (see the file note).</summary>
    public static Animation KeyAnimation { get; } = Animation.Smooth(0.25);

    /// <summary>Whether the ScrollViewer scrolls softly, inherited by the scroll views inside it. True by default: every scroll
    /// view does (OD-0071, ED-NEW-softscroll-1); false is an opt-out, justified where it is set (see the file note).</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("SoftScrollIsEnabled", typeof(SoftScroll), defaultValue: true, inherits: true);

    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    /// <summary>A control that takes the wheel itself (the terminal): the scroll views around it leave its wheel to it, and to
    /// Avalonia's own handling after it (see the file note). Not inherited.</summary>
    public static readonly AttachedProperty<bool> OwnsWheelProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("SoftScrollOwnsWheel", typeof(SoftScroll));

    public static bool GetOwnsWheel(Control control) => control.GetValue(OwnsWheelProperty);

    public static void SetOwnsWheel(Control control, bool value) => control.SetValue(OwnsWheelProperty, value);

    // Whether the behaviour drives `viewer`: enabled, and not a logical scroller (its offset is in items, not points).
    private static bool Drives(ScrollViewer viewer) =>
        GetIsEnabled(viewer) && viewer.Presenter is not ScrollContentPresenter { Child: ILogicalScrollable { IsLogicalScrollEnabled: true } };

    /// <summary>Raised on a ScrollViewer when its phase changes.</summary>
    public static readonly RoutedEvent<ScrollPhaseChangedEventArgs> PhaseChangedEvent =
        RoutedEvent.Register<ScrollViewer, ScrollPhaseChangedEventArgs>("SoftScrollPhaseChanged", RoutingStrategies.Direct);

    /// <summary>Puts the behaviour on every ScrollViewer of the process that has not opted out of it (once; PortControlThemes.Register calls it).</summary>
    public static void Install()
    {
        if (installed) return;
        installed = true;
        // Tunnelling (InputElement.PointerWheelChangedEvent routes Tunnel | Bubble in Avalonia 12.1.2): the scroll view sees the
        // report before its ScrollContentPresenter's own wheel handling (bubbling, OnPointerWheelChanged) can jump the offset.
        InputElement.PointerWheelChangedEvent.AddClassHandler<ScrollViewer>(OnWheel, RoutingStrategies.Tunnel);
        InputElement.KeyDownEvent.AddClassHandler<InputElement>(BeforeKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.KeyDownEvent.AddClassHandler<InputElement>(AfterKey, RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.ScrollGestureEvent.AddClassHandler<ScrollViewer>((viewer, e) =>
        {
            if (NearestViewer(e.Source) == viewer && Drives(viewer)) For(viewer).Touch();
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        // A scroll bar's thumb taken by the pointer: the thumb moves the offset itself, so the motion stops where it is.
        Thumb.DragStartedEvent.AddClassHandler<ScrollViewer>((viewer, e) =>
        {
            if (e.Source is Visual thumb && thumb.FindAncestorOfType<ScrollBar>() is { } bar && bar.FindAncestorOfType<ScrollViewer>() == viewer
                && Drivers.TryGetValue(viewer, out var driver)) driver.Stop();
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.ScrollGestureEndedEvent.AddClassHandler<ScrollViewer>((viewer, e) =>
        {
            if (NearestViewer(e.Source) == viewer && Drivers.TryGetValue(viewer, out var driver)) driver.Release();
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>The driver of <paramref name="viewer"/>, made on first use.</summary>
    public static ScrollDriver For(ScrollViewer viewer) => Drivers.GetValue(viewer, Make);

    /// <summary>Scrolls <paramref name="viewer"/> to <paramref name="offset"/> along <paramref name="animation"/> (none: at once).</summary>
    public static void ScrollTo(ScrollViewer viewer, Vector offset, Animation? animation, bool runsOut = true) => For(viewer).ScrollTo(offset, animation, runsOut);

    /// <summary>Whether <paramref name="viewer"/> is in a scroll the behaviour drives.</summary>
    public static bool IsScrolling(ScrollViewer viewer) =>
        Drivers.TryGetValue(viewer, out var driver) && (driver.IsMoving || ScrollDriver.Now - driver.WroteAt < 0.05); // its settling frame too

    /// <summary>Ends a programmatic animation on <paramref name="viewer"/> where it is.</summary>
    public static void StopAnimation(ScrollViewer viewer)
    {
        if (Drivers.TryGetValue(viewer, out var driver)) driver.StopAnimation();
    }

    private static ScrollDriver Make(ScrollViewer viewer)
    {
        var driver = new ScrollDriver(
            () => viewer.Offset,
            offset => viewer.Offset = offset,
            () => new Vector(
                viewer.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled ? 0 : viewer.Extent.Width - viewer.Viewport.Width,
                viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled ? 0 : viewer.Extent.Height - viewer.Viewport.Height),
            // No frame clock (never in the app, which binds one at start-up): nothing could animate, so input steps at once.
            () => EnvironmentValues.GetAccessibilityReduceMotion(viewer) || !FrameClock.IsBound);
        driver.PhaseChanged += phase => viewer.RaiseEvent(new ScrollPhaseChangedEventArgs(phase));
        viewer.DetachedFromVisualTree += (_, _) => driver.Stop();
        return driver;
    }

    // ---- The wheel ----

    private static void OnWheel(ScrollViewer viewer, PointerWheelEventArgs e)
    {
        if (e.Handled) return;
        var delta = e.Delta;
        // Shift turns a vertical wheel sideways, as Avalonia's presenter (and macOS) do.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && delta.X == 0) delta = new Vector(delta.Y, 0);
        // The outermost scroll view on the route sees the report first (tunnelling): it is classified once, there.
        if (Outermost(e.Source) != viewer) return;
        var now = ScrollDriver.Now;
        var kind = Classifier.Classify(Math.Abs(delta.Y) >= Math.Abs(delta.X) ? delta.Y : delta.X, now);
        var owner = Owner(e.Source, delta, kind);
        if (owner is not null && !Drives(owner)) return; // a scroll view that opted out keeps Avalonia's own wheel
        // A control on the way that takes the wheel itself (the terminal's history) keeps it: nothing here handles it.
        for (var visual = e.Source as Visual; visual is not null && visual != owner; visual = visual.GetVisualParent())
            if (visual is Control control && GetOwnsWheel(control)) return;
        if (owner is null)
        {
            // Nobody can move that way. A view still gliding to that end keeps the report from its presenter (which
            // would jump the rest of the way at once); otherwise it goes on unhandled, as Avalonia's does.
            for (var visual = e.Source as Visual; visual is not null; visual = visual.GetVisualParent())
                if (visual is ScrollViewer moving && Drivers.TryGetValue(moving, out var driver) && driver.IsMoving && CanScrollAlong(moving, delta))
                    e.Handled = true;
            return;
        }
        e.Handled = true;
        // A wheel's notch is Windows' lines setting's distance (a page for "one screen at a time"); a touchpad's stream keeps
        // ED-246's 50 points per WHEEL_DELTA (ED-NEW-softscroll-2).
        var wheel = SystemWheel.Shared.Current;
        var horizontal = CanScroll(owner, horizontal: true);
        var vertical = CanScroll(owner, horizontal: false);
        var touchpad = new Vector(horizontal ? -delta.X * ScrollPhysics.WheelLine : 0, vertical ? -delta.Y * ScrollPhysics.WheelLine : 0);
        var distance = kind == WheelKind.Touchpad ? touchpad : new Vector(
            horizontal ? -delta.X * wheel.Notch(owner.Viewport.Width) : 0,
            vertical ? -delta.Y * wheel.Notch(owner.Viewport.Height) : 0);
        var glide = For(owner);
        if (Classifier.IsFresh || overweighted is null || !overweighted.TryGetTarget(out var weighed) || weighed != glide) overweight = default;
        if (kind == WheelKind.Touchpad)
        {
            if (overweight != default) glide.Amend(-overweight);
            overweight = default;
        }
        else
        {
            overweight += distance - touchpad;
            if (overweighted is null) overweighted = new WeakReference<ScrollDriver>(glide);
            else overweighted.SetTarget(glide);
        }
        glide.Wheel(distance, kind);
    }

    // The scroll view that takes a report: the innermost on its route that can still move that way, or is still moving
    // that way (a flick into its end keeps its notches, and lands with their speed). One at rest at its end hands it on
    // (Avalonia's presenter: handled only when the offset changes, ScrollViewer.IsScrollChainingEnabled), unless chaining
    // is off for it. A touchpad stream stays where it started.
    private static ScrollViewer? Owner(object? source, Vector delta, WheelKind kind)
    {
        var direction = new Vector(-delta.X, -delta.Y);
        if (kind == WheelKind.Touchpad && latched is not null && latched.TryGetTarget(out var held)
            && source is Visual start && (held == start || held.IsVisualAncestorOf(start)) && CanScrollAlong(held, delta))
            return held;
        ScrollViewer? owner = null;
        for (var visual = source as Visual; visual is not null && owner is null; visual = visual.GetVisualParent())
        {
            if (visual is not ScrollViewer viewer || !CanScrollAlong(viewer, delta)) continue;
            if (!Drives(viewer))
            {
                if (CanMoveNow(viewer, direction) || !ScrollViewer.GetIsScrollChainingEnabled(viewer)) owner = viewer;
                continue;
            }
            var driver = For(viewer);
            if (driver.CanMove(direction) || driver.IsHeading(direction) || !ScrollViewer.GetIsScrollChainingEnabled(viewer)) owner = viewer;
        }
        latched = kind == WheelKind.Touchpad && owner is not null && Drives(owner) ? new WeakReference<ScrollViewer>(owner) : null;
        return owner;
    }

    // Whether a scroll view Avalonia scrolls itself can still move along `direction` (its signs only).
    private static bool CanMoveNow(ScrollViewer viewer, Vector direction)
    {
        var most = new Vector(Math.Max(0, viewer.Extent.Width - viewer.Viewport.Width), Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height));
        var at = viewer.Offset;
        return (direction.Y > 0 && at.Y < most.Y - 0.01) || (direction.Y < 0 && at.Y > 0.01)
            || (direction.X > 0 && at.X < most.X - 0.01) || (direction.X < 0 && at.X > 0.01);
    }

    private static bool CanScrollAlong(ScrollViewer viewer, Vector delta) =>
        (delta.Y != 0 && CanScroll(viewer, horizontal: false)) || (delta.X != 0 && CanScroll(viewer, horizontal: true));

    private static ScrollViewer? Outermost(object? source)
    {
        ScrollViewer? outermost = null;
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
            if (visual is ScrollViewer viewer) outermost = viewer;
        return outermost;
    }

    // ---- Keys: the focused control first ----

    private static bool IsScrollKey(KeyEventArgs e) =>
        e.Key is Key.PageUp or Key.PageDown or Key.Home or Key.End && (e.KeyModifiers & ~(KeyModifiers.Control | KeyModifiers.Shift)) == 0;

    private static void Track(KeyEventArgs e)
    {
        if (ReferenceEquals(routedKey, e)) return;
        routedKey = e;
        handledBelow = false;
        waiting = null;
        KeyOffsets.Clear();
    }

    // Tunnelling: note the offsets of the scroll views on the key's way, and of those inside the focused control's own
    // template (a TextBox's PART_ScrollViewer), before anything handles it: Avalonia's ScrollViewer.OnKeyDown and
    // TextBox.OnKeyDown jump Page Up/Down before any class handler of ours there can step in.
    private static void BeforeKey(InputElement element, KeyEventArgs e)
    {
        if (!IsScrollKey(e)) return;
        Track(e);
        if (element is ScrollViewer viewer) KeyOffsets[viewer] = viewer.Offset;
        if (ReferenceEquals(element, e.Source) && element is TemplatedControl owner)
            foreach (var own in owner.GetVisualDescendants().OfType<ScrollViewer>())
                if (ReferenceEquals(own.TemplatedParent, owner)) KeyOffsets[own] = own.Offset;
    }

    // Bubbling, after each element's own handling: a scroll view acts only on a key nothing below it handled.
    private static void AfterKey(InputElement element, KeyEventArgs e)
    {
        if (!IsScrollKey(e)) return;
        Track(e);
        if (element is not ScrollViewer viewer)
        {
            // A control that paged a scroll view of its own template itself (Avalonia's TextBox calls
            // ScrollViewer.PageDown, a jump): the page is taken over and glides.
            if (e.Handled && e.Key is Key.PageUp or Key.PageDown)
                foreach (var (own, was) in KeyOffsets.ToArray())
                    if (Drives(own) && ReferenceEquals(own.TemplatedParent, element) && own.Offset != was)
                    {
                        own.Offset = was;
                        if (ReferenceEquals(waiting, own)) waiting = null;
                        Page(own, e);
                    }
            if (waiting is { } pending && ReferenceEquals(pending.TemplatedParent, element))
            {
                // The control that owns the waiting scroll view (a list, a text box) had its turn and left the key.
                waiting = null;
                if (!e.Handled && CanScroll(pending, horizontal: false)) Page(pending, e);
            }
            handledBelow = e.Handled;
            return;
        }
        if (!Drives(viewer))
        {
            handledBelow = e.Handled; // a scroll view that opted out pages itself, as Avalonia does
            return;
        }
        // Avalonia's own Page Up/Down jump (it runs only on a key that arrived unhandled) is undone: the key is ours.
        if (KeyOffsets.TryGetValue(viewer, out var before) && viewer.Offset != before) viewer.Offset = before;
        if (handledBelow) return;
        e.Handled = false;
        if (!CanScroll(viewer, horizontal: false)) return; // nothing to page: on to the scroll view around it
        if (viewer.TemplatedParent is InputElement owner && owner.IsVisualAncestorOf(viewer))
        {
            waiting = viewer; // its control decides first (a list's selection, a text box's caret)
            return;
        }
        Page(viewer, e);
    }

    private static void Page(ScrollViewer viewer, KeyEventArgs e)
    {
        e.Handled = true;
        handledBelow = true;
        var driver = For(viewer);
        var from = driver.Target;
        var most = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        var y = e.Key switch
        {
            Key.PageUp => from.Y - viewer.Viewport.Height,
            Key.PageDown => from.Y + viewer.Viewport.Height,
            Key.Home => 0,
            _ => most,
        };
        driver.ScrollTo(new Vector(viewer.Offset.X, Math.Clamp(y, 0, most)), KeyAnimation);
    }

    private static ScrollViewer? NearestViewer(object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
            if (visual is ScrollViewer viewer) return viewer;
        return null;
    }

    internal static bool CanScroll(ScrollViewer viewer, bool horizontal) => horizontal
        ? viewer.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled && viewer.Extent.Width - viewer.Viewport.Width > 0.5
        : viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled && viewer.Extent.Height - viewer.Viewport.Height > 0.5;
}
