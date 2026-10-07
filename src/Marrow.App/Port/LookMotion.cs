using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Port;

// PORT-ONLY (OD-0088, OD-0076; ED-250): the motion of the accent picker's Look card (LookCard.cs) as its locked board
// runs it: CSS transitions, each a value that moves from where it is to a new target along a named curve over a duration,
// after an optional delay (E:/Droppy/work/design/accent-build/SPEC.md section 6). They run on the frame clock (ED-117,
// ED-237), timed by SwiftClock, complete at the first frame within 1 ms of their end (ED-117 (2)); a value retargeted
// while it moves starts again from where it is, as a browser does. Reduce Motion (ED-084, read when a value is set) and a
// missing frame clock put every value at its target at once.

/// <summary>The board's curves (SPEC "Easing names").</summary>
internal static class LookCurve
{
    /// <summary><c>cubic-bezier(0.32, 0.72, 0, 1)</c>: slides and size changes.</summary>
    public static Animation Ez(double seconds) => Animation.TimingCurve(0.32, 0.72, 0, 1, seconds);

    /// <summary>CSS <c>ease</c>: fades, colours, hover, the fold.</summary>
    public static Animation Ease(double seconds) => Animation.TimingCurve(0.25, 0.1, 0.25, 1, seconds);

    /// <summary><c>cubic-bezier(0.2, 0.9, 0.25, 1)</c>: the theme menu growing in.</summary>
    public static Animation PopIn(double seconds) => Animation.TimingCurve(0.2, 0.9, 0.25, 1, seconds);
}

/// <summary>One frame driver for every value of a card: asks for frames only while a value moves.</summary>
internal sealed class LookMotion
{
    private readonly Control owner;
    private readonly List<LookValue> moving = [];
    private IFrameSource? source;
    private bool waiting;
    private readonly Action<TimeSpan> frame;

    public LookMotion(Control owner)
    {
        this.owner = owner;
        frame = Tick;
        owner.DetachedFromVisualTree += (_, _) => Settle();
    }

    /// <summary>Runs after the values moved, once per frame: the card draws what they now hold.</summary>
    public event Action? Frame;

    /// <summary>Whether transitions are stilled now (Reduce Motion, or no frame clock to run them).</summary>
    public bool Still => !FrameClock.IsBound || EnvironmentValues.GetAccessibilityReduceMotion(owner) || Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion; // the system setting too, as AnimatedScalar reads it (VT-12)

    /// <summary>Is anything moving.</summary>
    public bool IsMoving => moving.Count > 0;

    internal void Add(LookValue value)
    {
        if (!moving.Contains(value)) moving.Add(value);
        if (waiting) return;
        source = FrameClock.Source.Current;
        waiting = true;
        source.RequestFrame(frame);
    }

    internal void Remove(LookValue value) => moving.Remove(value);

    private void Tick(TimeSpan _)
    {
        waiting = false;
        var now = SwiftClock.Now;
        foreach (var value in moving.ToArray())
            if (value.Step(now)) moving.Remove(value);
        Frame?.Invoke();
        if (moving.Count == 0) return;
        waiting = true;
        source!.RequestFrame(frame);
    }

    /// <summary>Everything to its target and no frame left asked for (the card left the window).</summary>
    public void Settle()
    {
        foreach (var value in moving.ToArray()) value.Finish();
        moving.Clear();
        if (waiting) source?.CancelFrame(frame);
        waiting = false;
        Frame?.Invoke();
    }
}

/// <summary>A transitioned value (a CSS transition on one property).</summary>
internal abstract class LookValue(LookMotion motion)
{
    private ContinuousInstant start;
    private double delay;
    private Animation? curve;

    protected LookMotion Motion { get; } = motion;

    /// <summary>Is it moving.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>Runs whenever the value changes.</summary>
    public Action? Changed { get; set; }

    protected void Begin(Animation animation, double delaySeconds)
    {
        if (Motion.Still)
        {
            Finish();
            return;
        }
        curve = animation;
        delay = delaySeconds;
        start = SwiftClock.Now;
        IsMoving = true;
        Motion.Add(this);
    }

    /// <summary>Moves the value to frame time <paramref name="now"/>; true when it is done.</summary>
    internal bool Step(ContinuousInstant now)
    {
        if (!IsMoving) return true;
        var elapsed = now - start;
        var span = TimeSpan.FromSeconds(delay + curve!.Duration);
        if (elapsed.Ticks + TimeSpan.TicksPerMillisecond >= span.Ticks)
        {
            Finish();
            return true;
        }
        var seconds = elapsed.TotalSeconds - delay;
        Blend(seconds <= 0 ? 0 : curve.Progress(seconds));
        Changed?.Invoke();
        return false;
    }

    /// <summary>At the target now.</summary>
    internal void Finish()
    {
        IsMoving = false;
        Motion.Remove(this);
        Arrive();
        Changed?.Invoke();
    }

    protected abstract void Blend(double progress);

    protected abstract void Arrive();
}

/// <summary>A number in transition.</summary>
internal sealed class LookNumber(LookMotion motion, double initial) : LookValue(motion)
{
    private double from;

    public double Value { get; private set; } = initial;

    public double Target { get; private set; } = initial;

    /// <summary>Moves to <paramref name="target"/> along <paramref name="animation"/> after <paramref name="delay"/> seconds.</summary>
    public void To(double target, Animation animation, double delay = 0)
    {
        if (target == Target && (IsMoving || Value == target)) return;
        Target = target;
        from = Value;
        Begin(animation, delay);
    }

    /// <summary>At <paramref name="value"/> at once (a placement, not a move).</summary>
    public void Jump(double value)
    {
        Target = value;
        Finish();
    }

    protected override void Blend(double progress) => Value = from + (Target - from) * progress;

    protected override void Arrive() => Value = Target;
}

/// <summary>A colour in transition, mixed per channel in sRGB premultiplied with alpha (SPEC "Colours interpolate").</summary>
internal sealed class LookColor(LookMotion motion, Color initial) : LookValue(motion)
{
    private Color from;

    public Color Value { get; private set; } = initial;

    public Color Target { get; private set; } = initial;

    public void To(Color target, Animation animation, double delay = 0)
    {
        if (target == Target && (IsMoving || Value == target)) return;
        Target = target;
        from = Value;
        Begin(animation, delay);
    }

    public void Jump(Color value)
    {
        Target = value;
        Finish();
    }

    protected override void Blend(double progress) => Value = SwiftColor.PremultipliedMix(from, Target, progress);

    protected override void Arrive() => Value = Target;
}

/// <summary>Drawing helpers shared by the card's parts.</summary>
internal static class LookDraw
{
    public static IBrush Brush(Color color) => new Avalonia.Media.Immutable.ImmutableSolidColorBrush(color);

    public static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Clamp(Math.Round(color.A * alpha, MidpointRounding.AwayFromZero), 0, 255), color.R, color.G, color.B);

    public static Typeface Face(SwiftFont font)
    {
        var resolved = font.Resolve();
        return new Typeface(resolved.FontFamily, resolved.FontStyle, resolved.FontWeight);
    }

    /// <summary>The width a label takes, rounded up (the board's canvas measure).</summary>
    public static double Width(string text, SwiftFont font)
    {
        var resolved = font.Resolve();
        using var layout = new Avalonia.Media.TextFormatting.TextLayout(text, Face(font), resolved.FontSize, null);
        return Math.Ceiling(layout.WidthIncludingTrailingWhitespace);
    }

    /// <summary>Draws <paramref name="text"/> centred on <paramref name="centre"/>.</summary>
    public static void Centred(DrawingContext context, string text, SwiftFont font, Color color, Point centre)
    {
        if (color.A == 0) return;
        var resolved = font.Resolve();
        using var layout = new Avalonia.Media.TextFormatting.TextLayout(text, Face(font), resolved.FontSize, Brush(color));
        layout.Draw(context, new Point(centre.X - layout.WidthIncludingTrailingWhitespace / 2, centre.Y - layout.Height / 2));
    }

    /// <summary>A 1.3 stroke chevron of the board (7 x 5, M1 1.2 3.5 3.7 6 1.2), drawn with its top-left at the origin.</summary>
    public static Geometry Chevron { get; } = StreamGeometry.Parse("M1,1.2 L3.5,3.7 L6,1.2");

    public static Pen Pen(Color color, double thickness) =>
        new(Brush(color), thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    public static bool IsDark(ThemeVariant? variant)
    {
        for (var v = variant; v is not null; v = v.InheritVariant)
            if (v == ThemeVariant.Dark) return true;
        return false;
    }
}
