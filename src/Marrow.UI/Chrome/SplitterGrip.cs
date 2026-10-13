using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.UI.Chrome;

// PORT-ONLY, no Swift twin: nth web surfaces, unit W1 (work/design/web-surfaces/DESIGN.md 4.2 and 4.3, canvas
// index.html `.spl .grip`). The one grip every splitter draws, replacing the three grip sizes the sidebar edge
// (10/22/2.5 with a glow), the terminal drawer (12/28/2.5) and the floating panels (none) each had (ED-NEW-web-w1-2).

/// <summary>The corner of a pane a corner grip sits on: it picks the diagonal cursor and the glyph's two edges.</summary>
public enum SplitterCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// The glass grip pill (4 x 28 DIP, 28 x 4 for a horizontal divider, growing to 36 while dragged) or, on a corner, a
/// small L glyph. At rest it is invisible; shown, it fades in and scales from 0.6 on a spring; dragged, it brightens and
/// grows; with keyboard focus it wears a 2 DIP ring. Every change moves only opacity and the render transform, stepped
/// on the frame clock (DESIGN 2.6); under Reduce Motion it only fades (0.15 s) and never scales.
/// </summary>
public sealed class SplitterGrip : Control
{
    public const double Thickness = 4;
    public const double RestLength = 28;
    public const double DragLength = 36;
    public const double RingWidth = 2;
    public const double CornerGlyph = 9;
    public const double CornerStroke = 2;
    public const double HiddenScale = 0.6;
    public const double FadeSeconds = 0.18;
    public const double ReducedFadeSeconds = 0.15;
    public const double BrightenSeconds = 0.2;

    private readonly Canvas host = new() { IsHitTestVisible = false };
    private readonly Panel figure = new() { IsHitTestVisible = false };
    private readonly Border ring;
    private readonly Border body;
    private readonly Border bright;
    private readonly Border glyph;
    private readonly Border glyphBright;

    private readonly Tween presence = new();
    private readonly Tween brightness = new();
    private readonly Tween ringPresence = new();
    private readonly SplitterSpring scale = new(HiddenScale, response: 0.38, damping: 0.72, epsilon: 0.0005);
    private readonly SplitterSpring length = new(0, response: 0.3, damping: 0.8, epsilon: 0.0005);
    private readonly SplitterSpring along = new(double.NaN, response: 0.25, damping: 1, epsilon: 0.05);

    private Action<TimeSpan>? pendingFrame;
    private readonly Action<TimeSpan> frameCallback;
    private IFrameSource? frameSource;
    private ContinuousInstant lastFrame;
    private bool shown;
    private bool dragging;
    private bool focusRing;
    private double? followAt;

    public SplitterGrip(SplitterOrientation orientation, SplitterCorner corner = SplitterCorner.BottomRight)
    {
        frameCallback = OnFrame;
        Orientation = orientation;
        Corner = corner;
        IsHitTestVisible = false;
        ClipToBounds = false;

        var (w, h) = PillSize;
        ring = new Border
        {
            Width = w + 2 * RingWidth,
            Height = h + 2 * RingWidth,
            Margin = new Thickness(-RingWidth),
            BorderThickness = new Thickness(RingWidth),
            CornerRadius = new CornerRadius(3 + RingWidth),
            Opacity = 0,
        };
        body = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(3) };
        bright = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(3), Opacity = 0 };
        glyph = new Border { Width = CornerGlyph, Height = CornerGlyph };
        glyphBright = new Border { Width = CornerGlyph, Height = CornerGlyph, Opacity = 0 };
        if (orientation == SplitterOrientation.Both)
        {
            figure.Children.Add(glyph);
            figure.Children.Add(glyphBright);
            figure.Width = figure.Height = CornerGlyph;
        }
        else
        {
            figure.Children.Add(body);
            figure.Children.Add(bright);
            figure.Children.Add(ring);
            figure.Width = w;
            figure.Height = h;
        }
        figure.RenderTransformOrigin = RelativePoint.TopLeft;
        figure.Opacity = 0;
        host.Children.Add(figure);
        LogicalChildren.Add(host);
        VisualChildren.Add(host);
        ApplyCorner();
        ApplyColors();
        ActualThemeVariantChanged += (_, _) => ApplyColors();
        DetachedFromVisualTree += (_, _) => StopFrames(settle: true);
    }

    public SplitterOrientation Orientation { get; }

    public SplitterCorner Corner { get; private set; }

    /// <summary>The pill's size at rest along and across the divider.</summary>
    public (double Width, double Height) PillSize => Orientation switch
    {
        SplitterOrientation.Vertical => (Thickness, RestLength),
        SplitterOrientation.Horizontal => (RestLength, Thickness),
        _ => (CornerGlyph, CornerGlyph),
    };

    public bool IsShown => shown;

    public bool IsDragging => dragging;

    public bool HasFocusRing => focusRing;

    /// <summary>The opacity drawn now (0 hidden, 1 shown).</summary>
    public double Presence => figure.Opacity;

    /// <summary>The scale drawn now across the whole grip (0.6 hidden, 1 shown; always 1 under Reduce Motion).</summary>
    public double Scale => scale.Value;

    /// <summary>The pill's length drawn now: 28 at rest, 36 while dragged.</summary>
    public double Length => RestLength + (DragLength - RestLength) * length.Value;

    /// <summary>The bright layer's opacity drawn now (1 while dragged).</summary>
    public double Brightness => Orientation == SplitterOrientation.Both ? glyphBright.Opacity : bright.Opacity;

    public double RingOpacity => ring.Opacity;

    /// <summary>The pill's centre along the divider drawn now (its centre when not following the pointer).</summary>
    public double Along => double.IsNaN(along.Value) ? AlongCentre : along.Value;

    internal Border Body => Orientation == SplitterOrientation.Both ? glyph : body;

    internal Border Bright => Orientation == SplitterOrientation.Both ? glyphBright : bright;

    internal Border Ring => ring;

    internal Panel Figure => figure;

    /// <summary>Whether the frame clock still has a step of this grip waiting.</summary>
    public bool IsEasing => pendingFrame is not null;

    private bool ReduceMotion =>
        EnvironmentValues.GetAccessibilityReduceMotion(this) || Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;

    private double AlongCentre => Orientation == SplitterOrientation.Horizontal ? Bounds.Width / 2 : Bounds.Height / 2;

    public void SetCorner(SplitterCorner corner)
    {
        if (corner == Corner) return;
        Corner = corner;
        ApplyCorner();
        ApplyColors();
    }

    /// <summary>Shows or hides the grip (hover after 150 ms, a press, keyboard focus).</summary>
    public void SetShown(bool value)
    {
        if (value == shown) return;
        shown = value;
        var reduce = ReduceMotion;
        presence.Start(figure.Opacity, value ? 1 : 0, reduce ? ReducedFadeSeconds : FadeSeconds, Now);
        if (reduce) scale.Snap(1);
        else scale.SetTarget(value ? 1 : HiddenScale);
        if (!value && !dragging && followAt is null) along.Snap(double.NaN);
        Kick();
    }

    /// <summary>Dragged: the pill brightens and grows to 36 DIP.</summary>
    public void SetDragging(bool value)
    {
        if (value == dragging) return;
        dragging = value;
        brightness.Start(Brightness, value ? 1 : 0, BrightenSeconds, Now);
        if (ReduceMotion) length.Snap(value ? 1 : 0);
        else length.SetTarget(value ? 1 : 0);
        Kick();
    }

    /// <summary>Keyboard focus: a 2 DIP ring around the pill.</summary>
    public void SetFocusRing(bool value)
    {
        if (value == focusRing) return;
        focusRing = value;
        ringPresence.Start(ring.Opacity, value ? 1 : 0, ReduceMotion ? ReducedFadeSeconds : FadeSeconds, Now);
        Kick();
    }

    /// <summary>
    /// Follows the pointer along a long divider: <paramref name="position"/> along it, held 40 DIP from either end, with a
    /// spring; null returns to the centre of the visible span.
    /// </summary>
    public void Follow(double? position)
    {
        followAt = position;
        var span = Orientation == SplitterOrientation.Horizontal ? Bounds.Width : Bounds.Height;
        var target = position is { } p ? SplitterMath.Clamp(p, 40, Math.Max(40, span - 40)) : AlongCentre;
        if (double.IsNaN(along.Value) || !shown || ReduceMotion) along.Snap(target);
        else along.SetTarget(target);
        Kick();
    }

    /// <summary>Lands every animation on its target now (pointer-down settling of the grip itself).</summary>
    public void Settle()
    {
        StopFrames(settle: true);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EnvironmentValues.AccessibilityHighContrastProperty) ApplyColors();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        host.Measure(availableSize);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        host.Arrange(new Rect(finalSize));
        if (followAt is null && !double.IsNaN(along.Value) && !along.IsMoving) along.Snap(AlongCentre);
        Apply();
        return finalSize;
    }

    private static ContinuousInstant Now => SwiftClock.Now;

    private void Kick()
    {
        if (!FrameClock.IsBound || TopLevel.GetTopLevel(this) is null)
        {
            StopFrames(settle: true);
            return;
        }
        if (pendingFrame is not null)
        {
            Apply();
            return;
        }
        lastFrame = Now;
        frameSource = FrameClock.Source.Current;
        pendingFrame = frameCallback;
        frameSource.RequestFrame(pendingFrame);
        Apply();
    }

    private void OnFrame(TimeSpan _)
    {
        pendingFrame = null;
        var now = Now;
        var dt = Math.Min(0.034, Math.Max(0, (now - lastFrame).TotalSeconds));
        lastFrame = now;
        scale.Step(dt);
        length.Step(dt);
        along.Step(dt);
        Apply();
        if (Moving(now))
        {
            pendingFrame = frameCallback;
            frameSource?.RequestFrame(pendingFrame);
        }
    }

    private bool Moving(ContinuousInstant now) =>
        presence.IsRunning(now) || brightness.IsRunning(now) || ringPresence.IsRunning(now) || scale.IsMoving || length.IsMoving || along.IsMoving;

    private void StopFrames(bool settle)
    {
        if (pendingFrame is { } frame) frameSource?.CancelFrame(frame);
        pendingFrame = null;
        if (!settle) return;
        presence.Finish();
        brightness.Finish();
        ringPresence.Finish();
        scale.Settle();
        length.Settle();
        along.Settle();
        Apply();
    }

    private void Apply()
    {
        var now = Now;
        figure.Opacity = presence.Value(now);
        Bright.Opacity = brightness.Value(now);
        ring.Opacity = ringPresence.Value(now);
        var s = ReduceMotion ? 1 : scale.Value;
        var grow = Length / RestLength;
        var (w, h) = (figure.Width, figure.Height);
        double cx, cy;
        switch (Orientation)
        {
            case SplitterOrientation.Vertical:
                cx = Bounds.Width / 2;
                cy = Along;
                break;
            case SplitterOrientation.Horizontal:
                cx = Along;
                cy = Bounds.Height / 2;
                break;
            default:
                cx = Bounds.Width / 2;
                cy = Bounds.Height / 2;
                break;
        }
        var sx = s * (Orientation == SplitterOrientation.Horizontal ? grow : 1);
        var sy = s * (Orientation == SplitterOrientation.Vertical ? grow : 1);
        figure.RenderTransform = new MatrixTransform(
            Matrix.CreateTranslation(-w / 2, -h / 2) * Matrix.CreateScale(sx, sy) * Matrix.CreateTranslation(cx, cy));
    }

    private void ApplyCorner()
    {
        var stroke = CornerStroke;
        var (thickness, radius) = Corner switch
        {
            SplitterCorner.TopLeft => (new Thickness(stroke, stroke, 0, 0), new CornerRadius(3, 0, 0, 0)),
            SplitterCorner.TopRight => (new Thickness(0, stroke, stroke, 0), new CornerRadius(0, 3, 0, 0)),
            SplitterCorner.BottomLeft => (new Thickness(stroke, 0, 0, stroke), new CornerRadius(0, 0, 0, 3)),
            _ => (new Thickness(0, 0, stroke, stroke), new CornerRadius(0, 0, 3, 0)),
        };
        glyph.BorderThickness = glyphBright.BorderThickness = thickness;
        glyph.CornerRadius = glyphBright.CornerRadius = radius;
    }

    // Canvas `.spl .grip` (dark) and `.dev .spl .grip` (the light variant): a translucent fill with a top highlight, a
    // dark hairline and a soft shadow; brighter while dragged. The ring is white on dark and near-black on light.
    private void ApplyColors()
    {
        // W20 (DESIGN 10, ED-NEW-web-w20-1): under a Windows contrast theme the pill is the system's WindowText, solid, with no
        // shadow; dragged, the Highlight; the focus ring is the Highlight too.
        if (EnvironmentValues.GetAccessibilityHighContrast(this) is { } palette)
        {
            body.Background = palette.Brush(ContrastRole.Text);
            bright.Background = palette.Brush(ContrastRole.Highlight);
            body.BoxShadow = default;
            ring.BorderBrush = palette.Brush(ContrastRole.Highlight);
            glyph.BorderBrush = palette.Brush(ContrastRole.Text);
            glyphBright.BorderBrush = palette.Brush(ContrastRole.Highlight);
            return;
        }
        var dark = PlainControls.IsDark(ActualThemeVariant);
        body.Background = new SolidColorBrush(dark ? Color.FromArgb(0x57, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x99, 0x28, 0x28, 0x2E));
        bright.Background = new SolidColorBrush(dark ? Color.FromArgb(0xDB, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xE6, 0x1E, 0x1E, 0x24));
        body.BoxShadow = BoxShadows.Parse(dark
            ? "inset 0 0.5 0 0 #99FFFFFF, 0 0 0 0.5 #73000000, 0 2 8 0 #80000000"
            : "inset 0 0.5 0 0 #40FFFFFF, 0 0 0 0.5 #40FFFFFF, 0 2 8 0 #33000000");
        ring.BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xE6, 0x1E, 0x1E, 0x24));
        glyph.BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x99, 0x28, 0x28, 0x2E));
        glyphBright.BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xE6, 0x1E, 0x1E, 0x24));
    }

    /// <summary>A timed run from one value to another on the CSS <c>ease</c> curve, read at any instant.</summary>
    private sealed class Tween
    {
        private double from;
        private double to;
        private double seconds;
        private ContinuousInstant start;

        public void Start(double fromValue, double toValue, double duration, ContinuousInstant at)
        {
            from = fromValue;
            to = toValue;
            seconds = duration;
            start = at;
        }

        public double Value(ContinuousInstant now)
        {
            if (seconds <= 0) return to;
            var progress = Math.Min(1, Math.Max(0, (now - start).TotalSeconds / seconds));
            return from + (to - from) * SplitterMath.CubicBezier(0.25, 0.1, 0.25, 1, progress);
        }

        public bool IsRunning(ContinuousInstant now) => seconds > 0 && (now - start).TotalSeconds < seconds;

        public void Finish() => seconds = 0;
    }
}
