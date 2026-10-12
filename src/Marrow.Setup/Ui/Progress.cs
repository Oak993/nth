using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Marrow.Setup.Motion;
using Marrow.Setup.Text;
using Marrow.Setup.Theme;
using SkiaSharp;

namespace Marrow.Setup.Ui;

/// <summary>A getting-ready row's state: the waiting ring, the tile-trace spinner, the disc with its tick.</summary>
public enum StepState
{
    Waiting,
    Working,
    Done,
}

/// <summary>One step: its label and, for a step that counts bytes, the longest form of its detail (its reserved width).</summary>
public sealed record StepSpec(string Label, string? LongestDetail = null);

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 and 4, the canvas's approved getting-ready rows <c>.steps</c>; unit U2,
/// ED-NEW-inst-u2-1). The steps of Installing and Update, 2 DIP apart, 420 DIP wide at most. Each row is at least 34 DIP
/// tall: a 16 DIP slot, 12 DIP, the label (15 DIP, ink 3 waiting, ink 1 working, ink 2 done, a 0.2 s change) and the
/// detail (monospace 12 DIP, ink 3, shown while working and done, keeping the width of its longest form so the label never
/// re-wraps mid-copy). A label that wraps at a large text size grows its row. The rows are a list to Narrator; a row's
/// name is its label and state.
/// </summary>
public sealed class StepList : StackPanel
{
    private readonly Func<bool> calm;
    private readonly List<StepRow> rows = [];
    private Animator? animator;

    public StepList(Func<bool> calm)
    {
        this.calm = calm ?? throw new ArgumentNullException(nameof(calm));
        Spacing = 2;
        Margin = new Thickness(0, 22, 0, 0);
        MaxWidth = 420;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetControlTypeOverride(this, Avalonia.Automation.Peers.AutomationControlType.List);
    }

    public IReadOnlyList<StepRow> Rows => rows;

    public void Attach(Animator frames)
    {
        animator = frames;
        foreach (var row in rows) row.Attach(frames);
    }

    /// <summary>Replaces the steps (all waiting). Done before the list is shown, or inside <see cref="Reveal.Now"/>.</summary>
    public void SetSteps(IReadOnlyList<StepSpec> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        Children.Clear();
        rows.Clear();
        foreach (var spec in steps)
        {
            var row = new StepRow(spec, calm);
            if (animator is not null) row.Attach(animator);
            rows.Add(row);
            Children.Add(row);
        }
    }

    /// <summary>Marks every step before <paramref name="index"/> done, <paramref name="index"/> working, the rest waiting (index = count: all done).</summary>
    public void Advance(int index)
    {
        for (var i = 0; i < rows.Count; i++) rows[i].State = i < index ? StepState.Done : i == index ? StepState.Working : StepState.Waiting;
    }
}

/// <summary>One getting-ready row.</summary>
public sealed class StepRow : Grid, IMotion
{
    public const double Fade = 0.2, DetailFade = 0.25;

    private readonly Func<bool> calm;
    private readonly StepSlot slot;
    private readonly TextBlock label;
    private readonly TextBlock detail;
    private readonly ReserveBox detailBox;
    private readonly Tween ink = new(0);
    private readonly Tween detailShown = new(0);
    private Animator? animator;
    private StepState state;
    private Color waiting, working, done;

    public StepRow(StepSpec spec, Func<bool> calm)
    {
        ArgumentNullException.ThrowIfNull(spec);
        this.calm = calm;
        MinHeight = 34;
        ColumnDefinitions = new ColumnDefinitions("16,*,Auto");
        ColumnSpacing = 12;
        slot = new StepSlot(calm) { VerticalAlignment = VerticalAlignment.Center };
        label = SetupTheme.Text(new TextBlock { Text = spec.Label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }, TextRole.Step, null);
        detail = SetupTheme.Text(new TextBlock { TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Right, Opacity = 0 }, TextRole.Numbers, SetupTheme.Ink3);
        detailBox = Reveal.Reserve(detail, TextRole.Numbers, spec.LongestDetail is null ? [] : [spec.LongestDetail]);
        detailBox.VerticalAlignment = VerticalAlignment.Center;
        detailBox.IsVisible = spec.LongestDetail is not null;
        SetColumn(slot, 0);
        SetColumn(label, 1);
        SetColumn(detailBox, 2);
        Children.Add(slot);
        Children.Add(label);
        Children.Add(detailBox);
        Label = spec.Label;
        AutomationProperties.SetControlTypeOverride(this, Avalonia.Automation.Peers.AutomationControlType.ListItem);
        Announce();
        SetupTheme.BindColor(this, WaitingInkProperty, SetupTheme.Ink3);
        SetupTheme.BindColor(this, WorkingInkProperty, SetupTheme.Ink1);
        SetupTheme.BindColor(this, DoneInkProperty, SetupTheme.Ink2);
    }

    public static readonly StyledProperty<Color> WaitingInkProperty = AvaloniaProperty.Register<StepRow, Color>("WaitingInk");
    public static readonly StyledProperty<Color> WorkingInkProperty = AvaloniaProperty.Register<StepRow, Color>("WorkingInk");
    public static readonly StyledProperty<Color> DoneInkProperty = AvaloniaProperty.Register<StepRow, Color>("DoneInk");

    public string Label { get; private set; }

    public StepState State
    {
        get => state;
        set
        {
            if (state == value) return;
            state = value;
            slot.SetState(value);
            var instant = animator is null;
            ink.Retarget(InkIndex(value), instant ? 0 : Fade, CubicBezier.Ease);
            detailShown.Retarget(value == StepState.Waiting ? 0 : 1, instant ? 0 : DetailFade, CubicBezier.Ease);
            Announce();
            Paint();
            animator?.Start(this);
        }
    }

    public string? Detail => detail.Text;

    public double DetailOpacity => detail.Opacity;

    public TextBlock LabelBlock => label;

    public StepSlot Slot => slot;

    public void Attach(Animator frames)
    {
        animator = frames;
        slot.Attach(frames);
    }

    /// <summary>The detail beside the label ("120 of 420 MB"); its width stays that of the longest form.</summary>
    public void SetDetail(string text)
    {
        detail.Text = text;
        detailBox.IsVisible = true;
    }

    /// <summary>Changes the label (the Adding step follows the shortcut choice before the step starts).</summary>
    public void SetLabel(string text)
    {
        Label = text;
        label.Text = text;
        Announce();
    }

    bool IMotion.Advance(double seconds)
    {
        var moving = ink.Advance(seconds) | detailShown.Advance(seconds);
        Paint();
        return moving;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WaitingInkProperty || change.Property == WorkingInkProperty || change.Property == DoneInkProperty)
        {
            waiting = WaitingInk;
            working = WorkingInk;
            done = DoneInk;
            Paint();
        }
    }

    private Color WaitingInk => GetValue(WaitingInkProperty);

    private Color WorkingInk => GetValue(WorkingInkProperty);

    private Color DoneInk => GetValue(DoneInkProperty);

    private static double InkIndex(StepState value) => value switch { StepState.Working => 1, StepState.Done => 2, _ => 0 };

    private void Paint()
    {
        var v = ink.Value;
        var color = v <= 1 ? SetupPalette.Lerp(waiting, working, v) : SetupPalette.Lerp(working, done, v - 1);
        label.Foreground = new ImmutableSolidColorBrush(color);
        detail.Opacity = Math.Round(detailShown.Value, 3);
    }

    private void Announce()
    {
        var words = state switch { StepState.Working => "in progress", StepState.Done => "done", _ => "waiting" };
        AutomationProperties.SetName(this, Label + ", " + words);
    }
}

/// <summary>
/// The row's 16 DIP slot: the waiting ring (8 DIP, a 1.5 DIP inner line in ink 3), spinner B (a 12 DIP rounded square,
/// radius 3.4, its track and a traced head: 1.6 s per lap, the head <c>cubic-bezier(.65,0,.35,1)</c> over the first 62 %,
/// the tail over the last 62 %, at least 4 % long, the whole drifting once in 4.5 s) and the disc (14 DIP, the strong ink,
/// a tick in the on ink); each cross-fades in 0.2 s. Calm: the spinner holds still at 30 % of its lap.
/// </summary>
public sealed class StepSlot : Control, IMotion
{
    public const double Lap = 1.6, Drift = 4.5;

    private static readonly CubicBezier HeadCurve = new(.65, 0, .35, 1);
    private readonly Func<bool> calm;
    private readonly Tween ring = new(1), spinner = new(0), disc = new(0);
    private ScreenTokens tokens = ScreenTokens.Dark;
    private Color ink3;
    private Animator? animator;
    private double clock;

    public StepSlot(Func<bool> calm)
    {
        this.calm = calm;
        Width = 16;
        Height = 16;
        IsHitTestVisible = false;
        ScreenTokens.Watch(this, value =>
        {
            tokens = value;
            InvalidateVisual();
        });
        SetupTheme.BindColor(this, InkProperty, SetupTheme.Ink3);
    }

    public static readonly StyledProperty<Color> InkProperty = AvaloniaProperty.Register<StepSlot, Color>("Ink");

    public StepState State { get; private set; }

    public (double Ring, double Spinner, double Disc) Opacities => (ring.Value, spinner.Value, disc.Value);

    /// <summary>The spinner's dash now: its start and length, as shares of the square's outline (0 to 100).</summary>
    public (double Start, double Length) Dash => DashAt(clock, calm());

    public void Attach(Animator frames) => animator = frames;

    public void SetState(StepState value)
    {
        State = value;
        var seconds = animator is null ? 0 : StepRow.Fade;
        ring.Retarget(value == StepState.Waiting ? 1 : 0, seconds, CubicBezier.Ease);
        spinner.Retarget(value == StepState.Working ? 1 : 0, seconds, CubicBezier.Ease);
        disc.Retarget(value == StepState.Done ? 1 : 0, seconds, CubicBezier.Ease);
        InvalidateVisual();
        animator?.Start(this);
    }

    public static (double Start, double Length) DashAt(double t, bool calm)
    {
        if (calm) return (0, 30);
        var u = t % Lap / Lap;
        var head = 100 * HeadCurve.Evaluate(Math.Min(1, u / .62));
        var tail = 100 * HeadCurve.Evaluate(Math.Max(0, (u - .38) / .62));
        var length = Math.Max(4, head - tail);
        var drift = 100 * (t % Drift / Drift);
        return ((tail + drift) % 100, length);
    }

    bool IMotion.Advance(double seconds)
    {
        clock += seconds;
        var moving = ring.Advance(seconds) | spinner.Advance(seconds) | disc.Advance(seconds);
        InvalidateVisual();
        return moving || (spinner.Value > 0 && !calm());
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == InkProperty)
        {
            ink3 = change.GetNewValue<Color>();
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var (start, length) = Dash;
        context.Custom(new SlotOperation(new Rect(0, 0, 16, 16), ring.Value, spinner.Value, disc.Value, start, length, ink3, tokens));
    }

    private sealed class SlotOperation(Rect bounds, double ring, double spinner, double disc, double start, double length, Color ink3, ScreenTokens tokens) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point point) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var opacity = Math.Clamp(lease.CurrentOpacity, 0, 1);
            var canvas = lease.SkCanvas;
            using var paint = new SKPaint { IsAntialias = true };
            if (ring > 0.002)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1.5f;
                paint.Color = Sk(ink3, ring * opacity);
                canvas.DrawCircle(8, 8, 3.25f, paint);
            }
            if (spinner > 0.002)
            {
                using var outline = Square();
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1.8f;
                paint.Color = Sk(tokens.Track, spinner * opacity);
                canvas.DrawPath(outline, paint);
                paint.StrokeCap = SKStrokeCap.Round;
                paint.Color = Sk(tokens.SpinnerHead, spinner * opacity);
                using var measure = new SKPathMeasure(outline);
                var k = measure.Length / 100f;
                var from = (float)start * k;
                var to = (float)(start + length) * k;
                using var part = new SKPath();
                measure.GetSegment(from, Math.Min(to, measure.Length), part, true);
                if (to > measure.Length) measure.GetSegment(0, to - measure.Length, part, true);
                canvas.DrawPath(part, paint);
                paint.StrokeCap = SKStrokeCap.Butt;
            }
            if (disc > 0.002)
            {
                paint.Style = SKPaintStyle.Fill;
                paint.Color = Sk(tokens.Strong, disc * opacity);
                canvas.DrawCircle(8, 8, 7, paint);
                // the tick: M2.2 5.3 l1.9 1.9 3.8-4.2 in a 10 unit box drawn at 9 DIP, centred
                var saved = canvas.Save();
                canvas.Translate(3.5f, 3.5f);
                canvas.Scale(.9f, .9f);
                using var tick = new SKPath();
                tick.MoveTo(2.2f, 5.3f);
                tick.RLineTo(1.9f, 1.9f);
                tick.RLineTo(3.8f, -4.2f);
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1.7f;
                paint.StrokeCap = SKStrokeCap.Round;
                paint.StrokeJoin = SKStrokeJoin.Round;
                paint.Color = Sk(tokens.On, disc * opacity);
                canvas.DrawPath(tick, paint);
                canvas.RestoreToCount(saved);
            }
        }

        /// <summary>The SVG rect (x 1, y 1, 12 × 12, rx 3.4) in the 14 DIP box at the slot's centre, from its top left corner's end, clockwise.</summary>
        private static SKPath Square()
        {
            const float x = 2, y = 2, s = 12, r = 3.4f;
            var path = new SKPath();
            path.MoveTo(x + r, y);
            path.LineTo(x + s - r, y);
            path.ArcTo(new SKRect(x + s - 2 * r, y, x + s, y + 2 * r), -90, 90, false);
            path.LineTo(x + s, y + s - r);
            path.ArcTo(new SKRect(x + s - 2 * r, y + s - 2 * r, x + s, y + s), 0, 90, false);
            path.LineTo(x + r, y + s);
            path.ArcTo(new SKRect(x, y + s - 2 * r, x + 2 * r, y + s), 90, 90, false);
            path.LineTo(x, y + r);
            path.ArcTo(new SKRect(x, y, x + 2 * r, y + 2 * r), 180, 90, false);
            path.Close();
            return path;
        }
    }

    internal static SKColor Sk(Color c, double opacity) => new(c.R, c.G, c.B, (byte)Math.Round(c.A * Math.Clamp(opacity, 0, 1)));
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 4, the canvas's progress bar B <c>.pb</c>; unit U2, ED-NEW-inst-u2-1). Liquid silver:
/// a 4 DIP track (radius 2), its fill a vertical gradient with a light that runs inside it (at most 120 DIP or 60 % of the
/// fill, once every 2.6 s, crossing in the first 72 %), and the real percentage beside it (monospace 12 DIP, ink 2, 38 DIP
/// wide, right-aligned, 12 DIP away). The fill follows its target on a critically damped spring (stiffness 110) and never
/// goes back. Calm: the fill is set at once. Indeterminate (Removing): no percentage; a segment runs across (1.8 s, head and
/// tail on <c>cubic-bezier(.4,0,.2,1)</c>), in Calm it holds in the middle (35 % to 65 %) and only pulses its opacity.
/// To Narrator it is a progress bar with its value.
/// </summary>
public sealed class ProgressBarB : Panel, IMotion
{
    public const double Stiffness = 110, LightCycle = 2.6, SegmentCycle = 1.8;

    private static readonly CubicBezier SegmentCurve = new(.4, 0, .2, 1);
    private readonly Func<bool> calm;
    private readonly BarTrack track;
    private readonly TextBlock percent;
    private Animator? animator;
    private double target, velocity, clock;

    public ProgressBarB(Func<bool> calm, bool indeterminate = false)
    {
        this.calm = calm ?? throw new ArgumentNullException(nameof(calm));
        Indeterminate = indeterminate;
        Margin = new Thickness(0, 18, 0, 0);
        MaxWidth = 420;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        track = new BarTrack(this);
        percent = SetupTheme.Text(new TextBlock { TextAlignment = TextAlignment.Right, Text = SetupStrings.ProgressPercent(0) }, TextRole.Numbers, SetupTheme.Ink2);
        Children.Add(track);
        if (!indeterminate) Children.Add(percent);
        AutomationProperties.SetControlTypeOverride(this, Avalonia.Automation.Peers.AutomationControlType.ProgressBar);
        AutomationProperties.SetName(this, indeterminate ? "Progress" : SetupStrings.ProgressPercent(0));
    }

    public bool Indeterminate { get; }

    /// <summary>The value drawn now (0 to 100).</summary>
    public double Shown { get; private set; }

    public double Target => target;

    /// <summary>The whole percentage beside the bar.</summary>
    public int Percent => (int)Math.Floor(Shown);

    public string PercentText => percent.Text ?? "";

    internal double Clock => clock;

    internal bool IsCalm => calm();

    public void Attach(Animator frames) => animator = frames;

    /// <summary>Moves the target to <paramref name="value"/> (0 to 100); a lower value is ignored, the bar never goes back.</summary>
    public void SetTarget(double value)
    {
        value = Math.Clamp(value, 0, 100);
        if (value <= target && !(value == 0 && target == 0)) return;
        target = value;
        if (calm() || animator is null)
        {
            Shown = target;
            velocity = 0;
            Paint();
        }
        animator?.Start(this);
    }

    /// <summary>Back to 0 at once (a new run).</summary>
    public void Reset()
    {
        target = 0;
        Shown = 0;
        velocity = 0;
        Paint();
    }

    /// <summary>Starts the light and the indeterminate segment (they run while the bar is in the window).</summary>
    public void Run() => animator?.Start(this);

    bool IMotion.Advance(double seconds)
    {
        clock += seconds;
        if (calm()) Shown = target;
        else
        {
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Min(seconds, .05) * 240));
            var h = Math.Min(seconds, .05) / steps;
            var c = 2 * Math.Sqrt(Stiffness);
            for (var i = 0; i < steps; i++)
            {
                var a = Stiffness * (target - Shown) - c * velocity;
                velocity += a * h;
                Shown += velocity * h;
                if (Shown > target && velocity > 0) Shown = Math.Min(Shown, target + .001);
            }
            Shown = Math.Clamp(Shown, 0, 100);
            if (Math.Abs(Shown - target) < .01 && Math.Abs(velocity) < .05)
            {
                Shown = target;
                velocity = 0;
            }
        }
        Paint();
        // the spring settles wherever the bar is; the light and the segment run only while it is in a window
        var settling = Shown != target || velocity != 0;
        return settling || (IsVisible && TopLevel.GetTopLevel(this) is not null);
    }

    private void Paint()
    {
        var whole = (int)Math.Floor(Shown >= 99.6 && target >= 100 ? 100 : Shown);
        var words = SetupStrings.ProgressPercent(whole);
        if (percent.Text != words)
        {
            percent.Text = words;
            if (!Indeterminate) AutomationProperties.SetName(this, words);
        }
        track.InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 4, side = 0;
        if (!Indeterminate)
        {
            percent.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            height = Math.Max(height, percent.DesiredSize.Height);
            side = Math.Max(38, percent.DesiredSize.Width) + 12;
        }
        track.Measure(new Size(Math.Max(0, availableSize.Width - side), 4));
        return new Size(double.IsInfinity(availableSize.Width) ? 420 : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var side = 0.0;
        if (!Indeterminate)
        {
            var w = Math.Max(38, percent.DesiredSize.Width);
            side = w + 12;
            percent.Arrange(new Rect(finalSize.Width - w, (finalSize.Height - percent.DesiredSize.Height) / 2, w, percent.DesiredSize.Height));
        }
        track.Arrange(new Rect(0, (finalSize.Height - 4) / 2, Math.Max(0, finalSize.Width - side), 4));
        return finalSize;
    }

    /// <summary>The track, the fill and its light (or the indeterminate segment).</summary>
    private sealed class BarTrack : Control
    {
        private readonly ProgressBarB owner;
        private ScreenTokens tokens = ScreenTokens.Dark;

        public BarTrack(ProgressBarB owner)
        {
            this.owner = owner;
            Height = 4;
            IsHitTestVisible = false;
            ScreenTokens.Watch(this, value =>
            {
                tokens = value;
                InvalidateVisual();
            });
        }

        public override void Render(DrawingContext context)
        {
            var w = Bounds.Width;
            var track = new RoundedRect(new Rect(0, 0, w, 4), 2);
            context.DrawRectangle(new ImmutableSolidColorBrush(tokens.Track), tokens.Contrast ? new ImmutablePen(new ImmutableSolidColorBrush(tokens.TrackEdge), 1) : null, track);
            var fill = Brushes(tokens);
            using (context.PushClip(track))
            {
                if (owner.Indeterminate)
                {
                    var (left, width, opacity) = Segment(owner.Clock, owner.IsCalm);
                    if (width > 0)
                        using (context.PushOpacity(opacity))
                            context.DrawRectangle(fill, null, new RoundedRect(new Rect(left * w, 0, width * w, 4), 2));
                    return;
                }
                var fw = w * owner.Shown / 100;
                if (fw <= 0.01) return;
                var fillRect = new RoundedRect(new Rect(0, 0, fw, 4), 2);
                context.DrawRectangle(fill, null, fillRect);
                if (tokens.Contrast || owner.IsCalm) return;
                // the light: min(120, 60 %) wide, left −60 % → 110 % of the fill over the first 72 % of 2.6 s (ease)
                var u = owner.Clock % LightCycle / LightCycle;
                if (u >= .72) return;
                var lw = Math.Min(120, .6 * fw);
                var left2 = (-.6 + 1.7 * CubicBezier.Ease.Evaluate(u / .72)) * fw;
                using (context.PushClip(fillRect))
                {
                    var light = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(Transparent(tokens.Shine), 0), new GradientStop(tokens.Shine, .5), new GradientStop(Transparent(tokens.Shine), 1) },
                    };
                    context.DrawRectangle(light, null, new Rect(left2, 0, lw, 4));
                }
            }
        }

        private static Color Transparent(Color c) => Color.FromArgb(0, c.R, c.G, c.B);

        private static IBrush Brushes(ScreenTokens tokens) => tokens.Contrast
            ? new ImmutableSolidColorBrush(tokens.FillTop)
            : new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(tokens.FillTop, 0), new GradientStop(tokens.FillBottom, 1) },
            };
    }

    /// <summary>The indeterminate segment at <paramref name="t"/>: its left and width as shares of the track, and its opacity.</summary>
    public static (double Left, double Width, double Opacity) Segment(double t, bool calm)
    {
        if (calm) return (.35, .30, .55 + .45 * (.5 + .5 * Math.Sin(t * 2.6)));
        var v = t % SegmentCycle / SegmentCycle;
        var head = SegmentCurve.Evaluate(Math.Min(1, v / .66));
        var tail = SegmentCurve.Evaluate(Math.Max(0, (v - .34) / .66));
        return (tail, Math.Max(0, head - tail), 1);
    }
}
