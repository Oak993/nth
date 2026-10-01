using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.UI.Chrome;

// PORT-ONLY (OD-0044, OD-0046): the Windows caption buttons in the app's design language, variant N "Turn pulse" as the
// owner picked it (porting/owner_decisions.md OD-0044; visual reference: the canvas's More.dc.html, variant N). They
// replace WindowChrome.swift's traffic lights: Windows places and orders them (minimise, maximise or restore, close at
// the top right) and performs them (the hit-test values below, WindowManager's caption hook); this control only draws.
//   At rest: the glyph alone, 12 pt at a 1.4 pt stroke in the label colour, centred in a 46 x 40 button.
//   Hovered: two rings (24 pt, 1.5 pt stroke; white at 65 % on dark, the label colour at 35 % on light; close: rose
//   rgb(255, 107, 139) at 90 %) leave the glyph, scale 0.45 -> 1.55 and fade to nothing over 1.3 s on
//   cubic-bezier(0.2, 0.6, 0.3, 1) (the comet's own ring curve, CometMotion.RingRadius), the second 0.45 s after the
//   first, repeating while hovered; the glyph ticks on the same 1.3 s beat: 1 -> 0.86 at 12 % -> 1.06 at 30 % -> 1.
// ENGINEERING DECISIONs (owner-visible only as small refinements of the picked variant, which shows no press, disabled
// or reduce-motion state): pressed, the glyph draws at 70 % opacity (Windows gives every caption button a press state);
// disabled (maximise on a window that cannot resize, as Windows greys it), the glyph draws at 35 % and nothing animates;
// under reduce motion a hovered button shows one still ring at full size and 45 % of its opacity instead of the pulse.
// The clock is the frame-clock seam (ED-117 (1)): frames are the only wake-ups, time is SwiftClock's.

/// <summary>One Windows caption button drawn as OD-0044's Turn pulse (see the file note).</summary>
public sealed class CaptionButton : Control
{
    public enum Kind { Minimize, Maximize, Close }

    public const double ButtonWidth = 46;
    public const double ButtonHeight = 40;
    public const double GlyphSize = 12;
    public const double GlyphStroke = 1.4;
    public const double RingDiameter = 24;
    public const double RingStroke = 1.5;
    public const double PulseSeconds = 1.3;
    public const double SecondRingDelay = 0.45;
    public const double PressedGlyphOpacity = 0.7;
    public const double DisabledGlyphOpacity = 0.35;

    private readonly Action<TimeSpan> tick;
    private IFrameSource? source;
    private bool waiting;
    private bool hovered;
    private bool pressed;
    private bool maximized;
    private ContinuousInstant hoverStart;

    public CaptionButton(Kind kind)
    {
        ButtonKind = kind;
        Width = ButtonWidth;
        Height = ButtonHeight;
        tick = OnFrame;
        DetachedFromVisualTree += (_, _) => Stop();
    }

    public Kind ButtonKind { get; }

    /// <summary>Seconds since the hover began, as of the last frame (0 when not hovered).</summary>
    public double PulseTime { get; private set; }

    /// <summary>The pointer is over the button (set by the window's caption hook from WM_NCMOUSEMOVE/WM_NCMOUSELEAVE).</summary>
    public bool IsHovered
    {
        get => hovered;
        set
        {
            if (hovered == value) return;
            hovered = value;
            PulseTime = 0;
            if (value && IsEffectivelyEnabled && !ReduceMotion)
            {
                hoverStart = SwiftClock.Now;
                Request();
            }
            else Stop();
            InvalidateVisual();
        }
    }

    /// <summary>The button is held down (set by the caption hook between WM_NCLBUTTONDOWN and the release).</summary>
    public bool IsPressed
    {
        get => pressed;
        set { if (pressed == value) return; pressed = value; InvalidateVisual(); }
    }

    /// <summary>The window is maximised: a maximise button shows the restore glyph.</summary>
    public bool IsMaximized
    {
        get => maximized;
        set { if (maximized == value) return; maximized = value; InvalidateVisual(); }
    }

    private static bool ReduceMotion => Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;

    private void Request()
    {
        if (waiting) return;
        source = FrameClock.Source.Current;
        waiting = true;
        source.RequestFrame(tick);
    }

    private void Stop()
    {
        if (waiting) source?.CancelFrame(tick);
        waiting = false;
        PulseTime = 0;
    }

    private void OnFrame(TimeSpan _)
    {
        waiting = false;
        if (!hovered) return;
        PulseTime = (SwiftClock.Now - hoverStart).TotalSeconds;
        InvalidateVisual();
        Request();
    }

    /// <summary>The ring at <paramref name="index"/> (0 or 1) at time <paramref name="t"/> into the hover: its scale and
    /// opacity factor, or null before it first leaves.</summary>
    public static (double Scale, double Opacity)? Ring(int index, double t)
    {
        var local = t - index * SecondRingDelay;
        if (local < 0) return null;
        var phase = local % PulseSeconds / PulseSeconds;
        var eased = CometEasing.Bezier(phase, .2, .6, .3, 1).Value;
        return (0.45 + 1.1 * eased, 0.9 * (1 - eased));
    }

    /// <summary>The glyph's scale at time <paramref name="t"/> into the hover: 1 -> 0.86 at 12 % -> 1.06 at 30 % -> 1.</summary>
    public static double GlyphScale(double t)
    {
        if (t <= 0) return 1;
        var p = t % PulseSeconds / PulseSeconds;
        static double Ease(double x) => CometEasing.Bezier(x, .25, .1, .25, 1).Value; // CSS `ease`
        if (p < .12) return 1 + (0.86 - 1) * Ease(p / .12);
        if (p < .30) return 0.86 + (1.06 - 0.86) * Ease((p - .12) / .18);
        return 1.06 + (1 - 1.06) * Ease((p - .30) / .70);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var theme = ActualThemeVariant;
        var dark = PlainControls.IsDark(theme);
        var ink = Chrome.PrimaryText.Resolve(theme);
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var enabled = IsEffectivelyEnabled;
        if (hovered && enabled)
        {
            var ring = ButtonKind == Kind.Close ? Color.FromArgb(230, 255, 107, 139)
                : dark ? Color.FromArgb(166, 255, 255, 255) : Color.FromArgb(89, ink.R, ink.G, ink.B);
            if (ReduceMotion) DrawRing(context, centre, ring, 1, 0.45);
            else
                for (var index = 0; index < 2; index++)
                    if (Ring(index, PulseTime) is { } r) DrawRing(context, centre, ring, r.Scale, r.Opacity / 0.9);
        }
        var opacity = !enabled ? DisabledGlyphOpacity : pressed ? PressedGlyphOpacity : 1;
        var scale = hovered && enabled && !ReduceMotion ? GlyphScale(PulseTime) : 1;
        var pen = new Pen(new SolidColorBrush(ink, ink.A / 255.0 * opacity), GlyphStroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateTranslation(-6, -6) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(centre.X, centre.Y)))
            DrawGlyph(context, pen);
    }

    private static void DrawRing(DrawingContext context, Point centre, Color color, double scale, double opacity)
    {
        var radius = RingDiameter / 2 * scale - RingStroke / 2;
        if (radius <= 0 || opacity <= 0) return;
        context.DrawEllipse(null, new Pen(new SolidColorBrush(color, opacity), RingStroke), centre, radius, radius);
    }

    // The glyphs in a 12 x 12 box, as the reference variant draws them.
    private void DrawGlyph(DrawingContext context, Pen pen)
    {
        switch (ButtonKind)
        {
            case Kind.Minimize:
                context.DrawLine(pen, new Point(2, 6), new Point(10, 6));
                break;
            case Kind.Maximize when maximized:
                context.DrawRectangle(null, pen, new Rect(2, 4, 6, 6), 1.5, 1.5);
                context.DrawGeometry(null, pen, StreamGeometry.Parse("M4 3.5V3a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1h-.5"));
                break;
            case Kind.Maximize:
                context.DrawRectangle(null, pen, new Rect(2, 2, 8, 8), 2, 2);
                break;
            case Kind.Close:
                context.DrawLine(pen, new Point(2.5, 2.5), new Point(9.5, 9.5));
                context.DrawLine(pen, new Point(9.5, 2.5), new Point(2.5, 9.5));
                break;
        }
    }
}
