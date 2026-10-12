// PORT-ONLY: the Tactus mark, one swappable component (owner rulings 2026-10-11, OD-0146, ED-NEW-pcctl-11; DESIGN.md 4.5).
// Every Tactus surface draws the mark through this control (the pill slot, the collapsed pill, the 32 DIP fullscreen dot,
// the window tags, the approval badge, the chat strip, the Settings row, the composer chip) or through TactusMarkImage
// (the tray and notifications). The glyph's shape comes only from Core's TactusGlyph, so swapping the mark is a change to
// that one file; this control knows the states, not the shape.
//
// States, as the canvas (pc-control-ui/index.html, v4) draws the pill's 32 DIP slot: Ready (the mark alone), Working (an
// arc runs round the slot, one turn in 2.2 s), HandsOff (a track ring with a silver fill the caller drives through the
// gate time), Paused (the mark springs into its lift pose). The arc and the ring grow in from their heads and shrink back
// on a critically damped spring rather than fading. Each real action plays one press timed to the pointer's ripple, and a
// typing burst holds it (TactusMarkMotion). With Reduce motion there is no press, the arc stands still, and every change
// is immediate. Under 20 DIP the mark is the hand-hinted 16 unit drawing (Paused has its own), which never moves.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl.Mark;

namespace Marrow.UI.Port.PcControl;

/// <summary>What the mark shows.</summary>
public enum TactusMarkState
{
    Ready,
    Working,
    HandsOff,
    Paused,
}

/// <summary>PORT-ONLY. The Tactus mark (Cuspis), drawn at any size, with the pill slot's states.</summary>
public sealed class TactusMark : Control
{
    /// <summary>The pill slot and the fullscreen dot: 32 DIP, with the 24 DIP master inside.</summary>
    public const double SlotSize = 32;

    /// <summary>The working arc: one turn in this many seconds.</summary>
    public const double ArcTurnSeconds = 2.2;

    /// <summary>The working arc's width at the slot's edge.</summary>
    public const double ArcWidth = 1.6;

    /// <summary>The hands-off ring's width at the slot's edge.</summary>
    public const double RingWidth = 2.3;

    /// <summary>The working arc's tail (transparent), head (brightest) and end, in degrees clockwise from the top.</summary>
    public const double ArcTail = 170;
    public const double ArcHead = 340;

    /// <summary>The arc's peak opacity and the ring track's opacity.</summary>
    public const double ArcPeak = 0.9;
    public const double RingTrack = 0.16;

    /// <summary>The fullscreen dot's live dot: 6 DIP, 1 DIP in from the top right.</summary>
    public const double LiveDotSize = 6;

    /// <summary>Canvas --t1 and --silver-hi.</summary>
    public static readonly Color DefaultColor = Color.FromRgb(0xEC, 0xEC, 0xEE);
    public static readonly Color FillColor = Color.FromRgb(0xF6, 0xF6, 0xF8);

    /// <summary>Canvas --v2-tint-d and --v2-rim2: the fullscreen dot's glass.</summary>
    public static readonly Color DotTint = Color.FromArgb(0xD6, 0x16, 0x16, 0x1A);
    public static readonly Color DotRim = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF);

    /// <summary>The arc and ring grow in and out on this spring.</summary>
    public static readonly TactusSpring PresenceSpring = TactusMarkMotion.PoseSpring;

    public static readonly StyledProperty<double> GlyphSizeProperty =
        AvaloniaProperty.Register<TactusMark, double>(nameof(GlyphSize), TactusGlyph.MasterUnits);

    public static readonly StyledProperty<bool> InSlotProperty =
        AvaloniaProperty.Register<TactusMark, bool>(nameof(InSlot));

    public static readonly StyledProperty<bool> IsDotProperty =
        AvaloniaProperty.Register<TactusMark, bool>(nameof(IsDot));

    public static readonly StyledProperty<TactusMarkState> StateProperty =
        AvaloniaProperty.Register<TactusMark, TactusMarkState>(nameof(State));

    public static readonly StyledProperty<double> RingFillProperty =
        AvaloniaProperty.Register<TactusMark, double>(nameof(RingFill), coerce: (_, value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0);

    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<TactusMark, Color>(nameof(Color), DefaultColor);

    private readonly TactusMarkMotion motion = new();
    private readonly Action<TimeSpan> frame;
    private readonly ConicGradientBrush arcBrush = new() { Center = RelativePoint.Center };
    private readonly GradientStop[] arcStops = [new(), new(), new(), new(), new()];
    private IFrameSource? source;
    private bool waiting;
    private long lastFrame;
    private double arc;
    private double arcSpeed;
    private double ring;
    private double ringSpeed;
    private StreamGeometry? cached;

    static TactusMark()
    {
        AffectsMeasure<TactusMark>(GlyphSizeProperty, InSlotProperty, IsDotProperty);
        AffectsRender<TactusMark>(StateProperty, RingFillProperty, ColorProperty);
        AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<TactusMark>(AccessibilityView.Raw);
    }

    public TactusMark()
    {
        frame = OnFrame;
        Focusable = false;
        IsHitTestVisible = false;
        AttachedToVisualTree += (_, _) =>
        {
            source = FrameClock.Source.Current;
            lastFrame = Now();
            motion.ReduceMotion = ReduceMotion;
            motion.Step(lastFrame);
            Settle();
            Request();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (waiting) source?.CancelFrame(frame);
            waiting = false;
            source = null;
        };
    }

    /// <summary>The glyph's size in DIP: 24 shows the master; under 20 the hinted 16 unit drawing.</summary>
    public double GlyphSize
    {
        get => GetValue(GlyphSizeProperty);
        set => SetValue(GlyphSizeProperty, value);
    }

    /// <summary>Drawn in the pill's 32 DIP slot (with the working arc and the hands-off ring), the glyph centred.</summary>
    public bool InSlot
    {
        get => GetValue(InSlotProperty);
        set => SetValue(InSlotProperty, value);
    }

    /// <summary>The 32 DIP fullscreen dot: the slot on a glass disc, with the live dot at its top right.</summary>
    public bool IsDot
    {
        get => GetValue(IsDotProperty);
        set => SetValue(IsDotProperty, value);
    }

    public TactusMarkState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>The hands-off ring's fill, 0 to 1; the caller moves it through the gate time and drains it.</summary>
    public double RingFill
    {
        get => GetValue(RingFillProperty);
        set => SetValue(RingFillProperty, value);
    }

    /// <summary>The mark's one colour (currentColor).</summary>
    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>The motion behind the master glyph (for tests and the surfaces' frame checks).</summary>
    public TactusMarkMotion Motion => motion;

    /// <summary>How far the working arc and the hands-off ring have grown in, 0 to 1.</summary>
    public double ArcPresence => arc;

    public double RingPresence => ring;

    /// <summary>The working arc's turn now, in degrees clockwise from the top (0 with Reduce motion).</summary>
    public double ArcAngle => ReduceMotion ? 0 : Turn(Now());

    /// <summary>True when this mark draws the hinted 16 unit drawing.</summary>
    public bool IsSmall => TactusGlyph.UsesSmallDrawing(GlyphSize);

    /// <summary>
    /// One press for a real action whose contact (the pointer's ripple) comes <paramref name="contactInMs"/> from now.
    /// False when skipped (Reduce motion, a held press, another press under 450 ms ago).
    /// </summary>
    public bool Press(int contactInMs = TactusMarkMotion.WindLeadMs)
    {
        Sync();
        var pressed = motion.Press(Now(), contactInMs);
        Request();
        return pressed;
    }

    /// <summary>A typing burst starts or ends: the press is held through it.</summary>
    public void Hold(bool on)
    {
        Sync();
        motion.Hold(Now(), on);
        Request();
    }

    /// <summary>The mark arrives from its tip.</summary>
    public void Enter()
    {
        Sync();
        motion.Enter(Now());
        Request();
        InvalidateVisual();
    }

    /// <summary>The mark leaves into its tip.</summary>
    public void Leave()
    {
        Sync();
        motion.Leave(Now());
        Request();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = InSlot || IsDot ? SlotSize * GlyphSize / TactusGlyph.MasterUnits : GlyphSize;
        return new Size(side, side);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty)
        {
            Sync();
            motion.SetPaused(Now(), State == TactusMarkState.Paused);
            Request();
        }
        else if (change.Property == EnvironmentValues.AccessibilityReduceMotionProperty)
        {
            motion.ReduceMotion = ReduceMotion;
            if (ReduceMotion) Settle();
            InvalidateVisual();
            Request();
        }
        else if (change.Property == InSlotProperty || change.Property == IsDotProperty)
        {
            Request();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        var framed = InSlot || IsDot;
        var unit = GlyphSize / TactusGlyph.MasterUnits;
        var side = framed ? SlotSize * unit : GlyphSize;
        var origin = new Point((size.Width - side) / 2, (size.Height - side) / 2);
        var colour = Color;

        if (IsDot) DrawDot(context, origin, side, unit);
        if (framed)
        {
            DrawRing(context, origin, side, unit, colour);
            DrawArc(context, origin, side, unit, colour);
        }
        var glyphOrigin = origin + new Point((side - GlyphSize) / 2, (side - GlyphSize) / 2);
        DrawGlyph(context, glyphOrigin, colour);
    }

    /// <summary>A Core outline as Avalonia geometry, scaled by <paramref name="scale"/> (radii included).</summary>
    public static StreamGeometry Geometry(TactusPath path, double scale = 1)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.SetFillRule(FillRule.NonZero);
        var open = false;
        foreach (var s in path.Segments)
        {
            var point = new Point(s.X * scale, s.Y * scale);
            switch (s.Kind)
            {
                case TactusSegmentKind.Move:
                    if (open) context.EndFigure(true);
                    context.BeginFigure(point, true);
                    open = true;
                    break;
                case TactusSegmentKind.Line:
                    context.LineTo(point);
                    break;
                case TactusSegmentKind.Arc:
                    context.ArcTo(point, new Size(s.Radius * scale, s.Radius * scale), 0, false,
                        s.Clockwise ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
                    break;
                default:
                    if (open) context.EndFigure(true);
                    open = false;
                    break;
            }
        }
        if (open) context.EndFigure(true);
        return geometry;
    }

    private bool ReduceMotion => EnvironmentValues.GetAccessibilityReduceMotion(this);

    private bool Framed => InSlot || IsDot;

    private static long Now() => SwiftClock.Now.Ticks / TimeSpan.TicksPerMillisecond;

    private static double Turn(long nowMs) => nowMs % (long)(ArcTurnSeconds * 1000) / (ArcTurnSeconds * 1000) * 360;

    private double ArcGoal => Framed && State == TactusMarkState.Working ? 1 : 0;

    private double RingGoal => Framed && State == TactusMarkState.HandsOff ? 1 : 0;

    /// <summary>With Reduce motion, the arc and ring stand at their goals and the glyph at its base pose.</summary>
    private void Settle()
    {
        if (!ReduceMotion) return;
        arc = ArcGoal;
        ring = RingGoal;
        arcSpeed = ringSpeed = 0;
        motion.Still();
        cached = null;
    }

    /// <summary>
    /// Brings the springs up to now before a change, so a change mid-flight starts where the mark is; a still mark only
    /// restarts its frame time (no frame was owed, so nothing jumps).
    /// </summary>
    private void Sync()
    {
        motion.ReduceMotion = ReduceMotion;
        if (ReduceMotion) Settle();
        else if (waiting) Advance(Now());
        else
        {
            lastFrame = Now();
            motion.Step(lastFrame);
        }
    }

    private void Advance(long now)
    {
        var seconds = (now - lastFrame) / 1000.0;
        lastFrame = Math.Max(lastFrame, now);
        if (ReduceMotion)
        {
            Settle();
            return;
        }
        motion.Step(now);
        PresenceSpring.Advance(ref arc, ref arcSpeed, ArcGoal, seconds);
        PresenceSpring.Advance(ref ring, ref ringSpeed, RingGoal, seconds);
    }

    private bool NeedsFrames =>
        !ReduceMotion && ((motion.Moving && !IsSmall) || arc != ArcGoal || arcSpeed != 0 || ring != RingGoal || ringSpeed != 0 || arc > 0);

    private void Request()
    {
        cached = null;
        InvalidateVisual();
        if (waiting || source is null || !NeedsFrames) return;
        waiting = true;
        source.RequestFrame(frame);
    }

    private void OnFrame(TimeSpan _)
    {
        waiting = false;
        if (source is null) return;
        Advance(Now());
        Request();
    }

    private void DrawGlyph(DrawingContext context, Point origin, Color colour)
    {
        var brush = new ImmutableSolidColorBrush(colour);
        using var transform = context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y));
        if (IsSmall)
        {
            var scale = GlyphSize / TactusGlyph.SmallUnits;
            var drawing = TactusGlyph.SmallDrawing(State == TactusMarkState.Paused ? TactusSmallState.Paused : TactusSmallState.Ready);
            var mark = Geometry(drawing.Mark, scale);
            if (drawing.KnockOut is { } knock)
            {
                var whole = new RectangleGeometry(new Rect(0, 0, GlyphSize, GlyphSize));
                var hole = new RectangleGeometry(new Rect(knock.X * scale, knock.Y * scale, knock.Width * scale, knock.Height * scale));
                using (context.PushGeometryClip(new CombinedGeometry(GeometryCombineMode.Exclude, whole, hole)))
                {
                    context.DrawGeometry(brush, null, mark);
                }
            }
            else
            {
                context.DrawGeometry(brush, null, mark);
            }
            if (drawing.Extra is { } extra) context.DrawGeometry(brush, null, Geometry(extra, scale));
            return;
        }
        cached ??= Geometry(motion.Path, GlyphSize / TactusGlyph.MasterUnits);
        context.DrawGeometry(brush, null, cached);
    }

    private void DrawArc(DrawingContext context, Point origin, double side, double unit, Color colour)
    {
        if (arc <= 1e-3) return;
        var p = Math.Min(1, arc);
        var tail = (ArcHead - ((ArcHead - ArcTail) * p)) / 360;
        var head = ArcHead / 360;
        var end = (ArcHead + ((360 - ArcHead) * p)) / 360;
        var clear = Color.FromArgb(0, colour.R, colour.G, colour.B);
        if (arcBrush.GradientStops.Count == 0) arcBrush.GradientStops.AddRange(arcStops);
        (double Offset, Color Colour)[] stops =
        [
            (0, clear),
            (tail, clear),
            (head, Color.FromArgb((byte)Math.Round(255 * ArcPeak), colour.R, colour.G, colour.B)),
            (end, clear),
            (1, clear),
        ];
        for (var i = 0; i < stops.Length; i++)
        {
            arcStops[i].Offset = stops[i].Offset;
            arcStops[i].Color = stops[i].Colour;
        }
        arcBrush.Angle = ReduceMotion ? 0 : Turn(Now());
        var width = ArcWidth * unit;
        var centre = origin + new Point(side / 2, side / 2);
        var radius = (side / 2) - (width / 2);
        context.DrawEllipse(null, new Pen(arcBrush, width), centre, radius, radius);
    }

    private void DrawRing(DrawingContext context, Point origin, double side, double unit, Color colour)
    {
        if (ring <= 1e-3) return;
        var width = RingWidth * unit;
        var centre = origin + new Point(side / 2, side / 2);
        var radius = (side / 2) - (width / 2);
        var track = new Pen(new ImmutableSolidColorBrush(Color.FromArgb((byte)Math.Round(255 * RingTrack), colour.R, colour.G, colour.B)), width);
        DrawSweep(context, track, centre, radius, Math.Min(1, ring));
        var fill = Math.Min(1, ring) * RingFill;
        if (fill > 1e-3) DrawSweep(context, new Pen(new ImmutableSolidColorBrush(FillColor), width), centre, radius, fill);
    }

    private void DrawDot(DrawingContext context, Point origin, double side, double unit)
    {
        var centre = origin + new Point(side / 2, side / 2);
        var rim = 0.5 * unit;
        context.DrawEllipse(new ImmutableSolidColorBrush(DotTint), new Pen(new ImmutableSolidColorBrush(DotRim), rim), centre, (side / 2) - (rim / 2), (side / 2) - (rim / 2));
        var live = LiveDotSize * unit;
        var liveCentre = origin + new Point(side - unit - (live / 2), unit + (live / 2));
        context.DrawEllipse(new ImmutableSolidColorBrush(FillColor), null, liveCentre, live / 2, live / 2);
    }

    /// <summary>A stroke from the top, clockwise, over <paramref name="fraction"/> of the circle.</summary>
    private static void DrawSweep(DrawingContext context, Pen pen, Point centre, double radius, double fraction)
    {
        if (fraction >= 1 - 1e-6)
        {
            context.DrawEllipse(null, pen, centre, radius, radius);
            return;
        }
        var angle = fraction * 2 * Math.PI;
        var start = new Point(centre.X, centre.Y - radius);
        var end = new Point(centre.X + (radius * Math.Sin(angle)), centre.Y - (radius * Math.Cos(angle)));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(start, false);
            g.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }
}
