// PORT-ONLY: Tactus pill v2, the pill's whole pose as a function of time (OD-0145, ED-NEW-pcctl-24; DESIGN.md 10.3, 10.6
// "Glass, motion and one clock", 10.11 "MotionDriver"). One immutable value per change holds every curve the pill moves
// on: the anchor point's travel, the width and height springs, Stop's key-cap fold, the lift while held, the arrive and
// leave scale (the owner's no-fades rule, ED-NEW-pcctl-21 item 3), the DPI scale, and Reduce Motion's opacity. The body
// drawing (on the render thread), the controls' key frames, the region at rest and the grab in flight all read this one
// value, so every part of the pill is at the same place at the same moment. A change keeps each curve's value and speed.
// Pure: no Avalonia, no Win32.
namespace Marrow.Core.Port.PcControl.Pill;

/// <summary>
/// The pill at one moment, in its band's DIP. <see cref="Anchor"/> is the point the pill hangs from: the top edge at the
/// anchor's side (left edge, centre or right edge, <see cref="Factor"/> 0, 0.5 or 1), so a width change grows about it.
/// </summary>
public readonly record struct PillPose(
    PillVector Anchor,
    double Factor,
    double Width,
    double Height,
    double Cap,
    double Lift,
    double Presence,
    double Dpi,
    double Opacity)
{
    /// <summary>The body's left edge relative to the anchor (the controls' coordinates).</summary>
    public double LocalLeft => -Factor * Width;

    /// <summary>The unscaled body in band DIP.</summary>
    public PillRect Body => new(Anchor.X - (Factor * Width), Anchor.Y, Width, Height);

    /// <summary>The whole scale: the lift times the arrive or leave scale times the DPI scale.</summary>
    public double Scale => Lift * Presence * Dpi;

    /// <summary>
    /// The translation that goes with <see cref="Scale"/> in the anchor's coordinates (x' = Scale x + ScaleOffset): the lift
    /// about the body's centre, then the presence and DPI scales about the anchor itself.
    /// </summary>
    public PillVector ScaleOffset => new PillVector((0.5 - Factor) * Width, Height / 2) * ((1 - Lift) * Presence * Dpi);

    /// <summary>The body as drawn (every scale applied), in band DIP.</summary>
    public PillRect Drawn
    {
        get
        {
            var s = Scale;
            var o = ScaleOffset;
            return new PillRect(Anchor.X + (s * LocalLeft) + o.X, Anchor.Y + o.Y, s * Width, s * Height);
        }
    }
}

/// <summary>The pill's pose over time: one immutable set of curves, continued (value and speed kept) by every change.</summary>
public sealed class PillMotion
{
    /// <summary>The lift while held (DESIGN.md 10.3 "While held, the pill lifts: scale 1.03").</summary>
    public const double LiftScale = 1.03;

    /// <summary>Arrive: the pill grows out of its anchor on a critically damped spring (as the pointer, ED-NEW-pcctl-21).</summary>
    public const double ArriveResponse = 0.30;

    /// <summary>Leave: it shrinks into its anchor on an accelerating ease.</summary>
    public const double LeaveSeconds = 0.18;

    /// <summary>Reduce Motion's arrive and leave: an opacity ease at full size (the one fade the owner's rule keeps).</summary>
    public const double ReducedSeconds = 0.15;

    /// <summary>The fold's excursion cap (a fraction of the cap's width).</summary>
    public const double FoldCap = 0.001;

    /// <summary>The longest any motion is sampled for (a spring is done well inside it).</summary>
    public const double LongestSeconds = 4;

    // The DIP one unit of a channel moves (so "within 0.1 DIP and under 5 DIP/s" means the same on every channel): the
    // cap's 88 DIP, and the widest pill's 440 DIP for the scales.
    private const double CapUnit = 88;
    private const double ScaleUnit = PillLayout.MaxWidth;

    private readonly PillTravel? travel;
    private readonly PillVector anchor;
    private readonly Channel width;
    private readonly Channel height;
    private readonly Channel cap;
    private readonly Channel lift;
    private readonly Channel presence;
    private readonly Channel dpi;
    private readonly Channel opacity;

    private PillMotion(double start, PillTravel? travel, PillVector anchor, double factor, Channel width, Channel height, Channel cap, Channel lift, Channel presence, Channel dpi, Channel opacity)
    {
        Start = start;
        this.travel = travel;
        this.anchor = anchor;
        Factor = factor;
        this.width = width;
        this.height = height;
        this.cap = cap;
        this.lift = lift;
        this.presence = presence;
        this.dpi = dpi;
        this.opacity = opacity;
        Shape = PillShape.Of(this);
    }

    /// <summary>When this value was made (seconds on the pill's clock): its curves start at or before it.</summary>
    public double Start { get; }

    /// <summary>The anchor's side: 0 left, 0.5 centre, 1 right.</summary>
    public double Factor { get; }

    /// <summary>The shape that takes input, the same curves (the overlays' region and the grab in flight read it).</summary>
    public PillShape Shape { get; }

    /// <summary>True when the anchor travels.</summary>
    public bool Travels => travel is not null;

    /// <summary>A pill at rest in <paramref name="pose"/>.</summary>
    public static PillMotion Still(PillPose pose, double now) =>
        new(now, null, pose.Anchor, pose.Factor, Channel.Fixed(pose.Width, 1), Channel.Fixed(pose.Height, 1), Channel.Fixed(pose.Cap, CapUnit),
            Channel.Fixed(pose.Lift, ScaleUnit), Channel.Fixed(pose.Presence, ScaleUnit), Channel.Fixed(pose.Dpi, ScaleUnit), Channel.Fixed(pose.Opacity, 1));

    /// <summary>A hidden pill (presence 0) at <paramref name="anchor"/>, ready to arrive.</summary>
    public static PillMotion Hidden(PillVector anchor, double factor, double width, double height, double now) =>
        Still(new PillPose(anchor, factor, width, height, 0, 1, 0, 1, 1), now);

    /// <summary>True when nothing changes with time.</summary>
    public bool AtRest => travel is null && width.Still && height.Still && cap.Still && lift.Still && presence.Still && dpi.Still && opacity.Still;

    public PillPose At(double now) => new(
        travel?.Presented(now, 1) ?? anchor,
        Factor,
        width.Value(now),
        height.Value(now),
        cap.Value(now),
        lift.Value(now),
        presence.Value(now),
        dpi.Value(now),
        opacity.Value(now) * (travel?.Opacity(now) ?? 1));

    /// <summary>Where every curve ends.</summary>
    public PillPose Final => new(travel?.Target ?? anchor, Factor, width.Final, height.Final, cap.Final, lift.Final, presence.Final, dpi.Final, opacity.Final);

    /// <summary>DESIGN's "visually settled": the travel within 1 DIP and under 20 DIP/s, every other curve settled.</summary>
    public bool Settled(double now) =>
        (travel?.VisuallySettled(now) ?? true) && width.Settled(now) && height.Settled(now) && cap.Settled(now) && lift.Settled(now) && presence.Settled(now) && dpi.Settled(now) && opacity.Settled(now);

    /// <summary>Every curve at its end (a spring within 0.1 and under 5 per second): the final pose may be shown exactly.</summary>
    public bool Done(double now) =>
        (travel is null || TravelDone(travel, now)) && width.Done(now) && height.Done(now) && cap.Done(now) && lift.Done(now) && presence.Done(now) && dpi.Done(now) && opacity.Done(now);

    /// <summary>The first 1 ms step at or after <see cref="Start"/> where the motion is done (capped at 4 s).</summary>
    public double DoneAt()
    {
        if (AtRest) return Start;
        for (var i = 0; i <= LongestSeconds * 1000; i++)
        {
            var t = Start + (i / 1000.0);
            if (Done(t)) return t;
        }
        return Start + LongestSeconds;
    }

    /// <summary>The arrive or leave phase.</summary>
    public PillPresence Presence => presence.Final >= 1 ? (presence.Still ? PillPresence.Shown : PillPresence.Arriving) : presence.Still && presence.Final <= 0 ? PillPresence.Gone : PillPresence.Leaving;

    /// <summary>The anchor's travel to <paramref name="target"/>, keeping its place and speed (a flick keeps its release velocity).</summary>
    public PillMotion TravelTo(double now, PillVector target, bool reduceMotion, PillVector? velocity = null)
    {
        var from = travel?.Value(now) ?? anchor;
        var speed = velocity ?? travel?.Velocity(now) ?? PillVector.Zero;
        if ((target - from).Length < 1e-9 && speed.Length < 1e-9) return With(now, travel: null, anchorAt: target);
        return With(now, travel: PillTravel.Start(now, from, target, speed, reduceMotion), anchorAt: from);
    }

    /// <summary>The anchor held at <paramref name="at"/> (a drag frame: no curve, the pointer's place).</summary>
    public PillMotion Held(double now, PillVector at) => With(now, travel: null, anchorAt: at);

    /// <summary>A new anchor side with the body held where it is drawn now (an anchor change before a travel).</summary>
    public PillMotion Reanchored(double now, double factor)
    {
        var pose = At(now);
        var body = pose.Body;
        var at = new PillVector(body.X + (factor * body.Width), body.Y);
        return new PillMotion(now, null, at, factor, width.From(now), height.From(now), cap.From(now), lift.From(now), presence.From(now), dpi.From(now), opacity.From(now));
    }

    /// <summary>The width springs to <paramref name="to"/> (DESIGN.md 10.3's width spring; Reduce Motion its ease).</summary>
    public PillMotion Widen(double now, double to, bool reduceMotion) => To(now, w: width.Toward(now, to, PillCurve.WidthResponse, reduceMotion));

    /// <summary>The height springs to <paramref name="to"/> with <paramref name="response"/> (the card's expand or collapse).</summary>
    public PillMotion Heighten(double now, double to, double response, bool reduceMotion) => To(now, h: height.Toward(now, to, response, reduceMotion));

    /// <summary>Stop's key cap folds (1) or unfolds (0) on the width spring.</summary>
    public PillMotion Fold(double now, bool folded, bool reduceMotion) => To(now, c: cap.Toward(now, folded ? 1 : 0, PillCurve.WidthResponse, reduceMotion, FoldCap));

    /// <summary>The lift while held (1.03) or its return (1), spring response 0.2 s; Reduce Motion: no lift.</summary>
    public PillMotion Lifted(double now, bool lifted, bool reduceMotion) =>
        To(now, l: reduceMotion ? lift.Set(1) : lift.Toward(now, lifted ? LiftScale : 1, PillCurve.LiftResponse, false));

    /// <summary>Arrive: grow out of the anchor from the current scale and speed; Reduce Motion: an opacity ease at full size.</summary>
    public PillMotion Arrive(double now, bool reduceMotion)
    {
        if (reduceMotion)
        {
            // from hidden the ease starts at 0; from a part-way leave it starts where the opacity is
            var from = presence.Value(now) <= 0 ? opacity.Set(0) : opacity;
            return To(now, p: presence.Set(1), o: from.Eased(now, 1, ReducedSeconds));
        }
        return To(now, p: presence.Toward(now, 1, ArriveResponse, false, 0.001), o: opacity.Set(1));
    }

    /// <summary>Leave: shrink into the anchor, accelerating (180 ms); Reduce Motion: an opacity ease at full size.</summary>
    public PillMotion Leave(double now, bool reduceMotion)
    {
        if (reduceMotion) return To(now, p: presence.Set(presence.Value(now) > 0 ? 1 : 0), o: opacity.Eased(now, 0, ReducedSeconds));
        var value = presence.Value(now);
        var seconds = LeaveSeconds * Math.Clamp(value, 0, 1);
        return To(now, p: seconds <= 0 ? presence.Set(0) : presence.With(PillCurve.EaseIn(value, 0, now, seconds)));
    }

    /// <summary>A DPI change in place: the scale springs from old over new (the same physical size) to 1; Reduce Motion: at once.</summary>
    public PillMotion Rescaled(double now, double oldOverNew, bool reduceMotion) =>
        To(now, d: reduceMotion || Math.Abs(oldOverNew - 1) < 1e-9 ? dpi.Set(1) : dpi.With(PillCurve.Spring(oldOverNew * dpi.Value(now), 1, 0, PillCurve.DpiResponse, now, cap: 0.001)));

    /// <summary>This motion as one value at <paramref name="now"/> with every curve ended where it is (a hold).</summary>
    public PillMotion Frozen(double now) => Still(At(now), now);

    private PillMotion With(double now, PillTravel? travel, PillVector anchorAt) =>
        new(now, travel, anchorAt, Factor, width.Keep(now), height.Keep(now), cap.Keep(now), lift.Keep(now), presence.Keep(now), dpi.Keep(now), opacity.Keep(now));

    private PillMotion To(double now, Channel? w = null, Channel? h = null, Channel? c = null, Channel? l = null, Channel? p = null, Channel? d = null, Channel? o = null) =>
        new(now, Keep(travel, now), travel is null || TravelDone(travel, now) ? travel?.Target ?? anchor : anchor, Factor,
            w ?? width.Keep(now), h ?? height.Keep(now), c ?? cap.Keep(now), l ?? lift.Keep(now), p ?? presence.Keep(now), d ?? dpi.Keep(now), o ?? opacity.Keep(now));

    private static PillTravel? Keep(PillTravel? travel, double now) => travel is not null && !TravelDone(travel, now) ? travel : null;

    private static bool TravelDone(PillTravel travel, double now) =>
        travel.Spring is { } spring ? spring.Ended(now, 1) : travel.X!.Done(now) && travel.Y!.Done(now);

    /// <summary>
    /// One scalar: a curve, or a fixed value. <c>unit</c> is the DIP one unit moves, so a spring's settle (within 1 DIP,
    /// under 20 DIP/s) and end (within 0.1 DIP, under 5 DIP/s) read the same on a width, a fold fraction and a scale.
    /// </summary>
    private readonly struct Channel(PillCurve? curve, double value, double unit)
    {
        public bool Still => curve is null;

        public double Final => curve?.To ?? value;

        public static Channel Fixed(double value, double unit) => new(null, value, unit);

        public Channel Set(double to) => new(null, to, unit);

        public Channel With(PillCurve next) => new(next, next.To, unit);

        public double Value(double now) => curve is { } c ? (Done(now) ? c.To : c.Value(now)) : value;

        public double Speed(double now)
        {
            if (curve is not { } c || Done(now)) return 0;
            if (c.Kind == PillCurveKind.Spring) return c.Velocity(now);
            if (c.Kind == PillCurveKind.FadeThrough) return 0;
            const double h = 0.0005;
            return (c.Value(now + h) - c.Value(now - h)) / (2 * h);
        }

        public bool Settled(double now) => curve is not { } c || (c.Kind == PillCurveKind.Spring
            ? Near(c, now, PillSpring.LandingDistance, PillSpring.SettledSpeed)
            : c.Settled(now));

        public bool Done(double now) => curve is not { } c || (c.Kind == PillCurveKind.Spring
            ? Near(c, now, PillSpring.EndPixels, PillSpring.EndSpeed)
            : c.Done(now));

        private bool Near(PillCurve c, double now, double distance, double speed)
        {
            if (c.From == c.To && c.Velocity0 == 0) return true;
            return now > c.Start && Math.Abs(c.Value(now) - c.To) * unit < distance && Math.Abs(c.Velocity(now)) * unit < speed;
        }

        /// <summary>The same channel, dropped to a fixed value once its curve has ended.</summary>
        public Channel Keep(double now) => curve is { } c && Done(now) ? Set(c.To) : this;

        /// <summary>A fixed value where the channel is now (the curve dropped).</summary>
        public Channel From(double now) => Set(Value(now));

        public Channel Toward(double now, double to, double response, bool reduceMotion, double cap = 0.5)
        {
            var from = Value(now);
            var speed = Speed(now);
            if (Math.Abs(from - to) < 1e-9 && Math.Abs(speed) < 1e-9) return Set(to);
            return With(PillCurve.Size(from, to, reduceMotion ? 0 : speed, response, now, reduceMotion, cap));
        }

        public Channel Eased(double now, double to, double seconds)
        {
            var from = Value(now);
            return Math.Abs(from - to) < 1e-9 ? Set(to) : With(PillCurve.Ease(from, to, now, seconds));
        }
    }
}

/// <summary>Where the pill is in its arrive and leave.</summary>
public enum PillPresence
{
    Gone,
    Arriving,
    Shown,
    Leaving,
}

/// <summary>What the content's places depend on, measured once per state (DIP).</summary>
public readonly record struct PillParts(double GroupWidth, double StopWidth, double CapWidth, bool ShowsStop);

/// <summary>
/// The content's places in the anchor's coordinates for one pose (canvas `frame()`): the row from the body's left edge,
/// the state's group and Stop riding the right edge, Stop's visible width as its cap folds, and the text column's clip.
/// </summary>
public readonly record struct PillPlaces(double BodyLeft, double BodyRight, double RowLeft, double GroupLeft, double StopLeft, double StopVisible, double TextClip)
{
    /// <summary>The text column starts this far right of the body's left edge (8 + the 50 DIP mark and glyph + 8).</summary>
    public const double TextStart = 66;

    /// <summary>
    /// The places at <paramref name="pose"/>. Stop's capsule keeps its width at the right edge even when it does not show (so
    /// it can scale away where it was), but takes room from the text and the group only while it shows.
    /// </summary>
    public static PillPlaces Of(PillPose pose, PillParts parts)
    {
        var left = pose.LocalLeft;
        var right = left + pose.Width;
        var visible = parts.StopWidth > 0 ? parts.StopWidth - (Math.Clamp(pose.Cap, 0, 1) * parts.CapWidth) : 0;
        var taken = parts.ShowsStop ? visible : 0;
        var stopLeft = right - PillLayout.Gap - visible;
        var groupLeft = right - PillLayout.Gap - (taken > 0 ? taken + PillLayout.Gap : 0) - parts.GroupWidth;
        var text = pose.Width - PillLayout.Chrome - (parts.GroupWidth > 0 ? parts.GroupWidth + PillLayout.Gap : 0) - (taken > 0 ? taken + PillLayout.Gap : 0);
        return new PillPlaces(left, right, left + 8, groupLeft, stopLeft, visible, Math.Max(0, text));
    }
}

/// <summary>One property's 1 ms key frames (DESIGN.md 10.6's fallback; 10.11: at most 0.15 DIP from the curve to 2,000 DIP).</summary>
public sealed class PillTrack(double start, double duration, float[] keys, double[] values)
{
    /// <summary>The spacing (DESIGN.md 10.6: 1 ms; the withdrawn 8 ms spacing erred by up to 2.4 DIP at 728 DIP).</summary>
    public const double Step = 0.001;

    public double Start { get; } = start;

    public double Duration { get; } = duration;

    /// <summary>Normalised times, 0 to 1.</summary>
    public IReadOnlyList<float> Keys { get; } = keys;

    public IReadOnlyList<double> Values { get; } = values;

    /// <summary>True when the value never changes (no animation is needed; the end value is set directly).</summary>
    public bool Constant
    {
        get
        {
            for (var i = 1; i < values.Length; i++)
                if (Math.Abs(values[i] - values[0]) > 1e-6) return false;
            return true;
        }
    }

    public double Last => values[^1];

    /// <summary>The value an animation of these key frames shows at <paramref name="now"/> (linear between keys).</summary>
    public double Interpolate(double now)
    {
        if (Duration <= 0 || now >= Start + Duration) return values[^1];
        if (now <= Start) return values[0];
        var p = (now - Start) / Duration;
        var hi = 1;
        while (hi < keys.Length - 1 && keys[hi] < p) hi++;
        var lo = hi - 1;
        var span = keys[hi] - keys[lo];
        var k = span <= 0 ? 1 : (p - keys[lo]) / span;
        return values[lo] + ((values[hi] - values[lo]) * k);
    }

    /// <summary>Samples <paramref name="value"/> (of the time) every 1 ms from <paramref name="start"/> to <paramref name="end"/>, the last key exactly at the end.</summary>
    public static PillTrack Of(double start, double end, Func<double, double> value)
    {
        end = Math.Max(start, end);
        var count = Math.Max(2, (int)Math.Ceiling(((end - start) / Step) - 1e-9) + 1);
        var keys = new float[count];
        var values = new double[count];
        var duration = end - start;
        for (var i = 0; i < count; i++)
        {
            var t = i == count - 1 ? end : start + (i * Step);
            keys[i] = duration <= 0 ? i / (float)(count - 1) : (float)Math.Clamp((t - start) / duration, 0, 1);
            values[i] = value(t);
        }
        keys[^1] = 1;
        return new PillTrack(start, duration, keys, values);
    }

    /// <summary>
    /// Samples <paramref name="value"/> of <paramref name="motion"/> every 1 ms from <paramref name="from"/> (its start when
    /// null) to the motion's end; the last key is the final pose's value, exactly.
    /// </summary>
    public static PillTrack Sample(PillMotion motion, Func<PillPose, double> value, double? from = null, double? until = null)
    {
        var start = from ?? motion.Start;
        var end = Math.Max(start, until ?? motion.DoneAt());
        var count = Math.Max(2, (int)Math.Ceiling(((end - start) / Step) - 1e-9) + 1);
        var keys = new float[count];
        var values = new double[count];
        var duration = end - start;
        for (var i = 0; i < count; i++)
        {
            var t = i == count - 1 ? end : start + (i * Step);
            keys[i] = duration <= 0 ? i / (float)(count - 1) : (float)Math.Clamp((t - start) / duration, 0, 1);
            values[i] = value(motion.At(t));
        }
        keys[^1] = 1;
        values[^1] = value(motion.Final);
        return new PillTrack(start, duration, keys, values);
    }
}
