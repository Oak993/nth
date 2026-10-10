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
//   At rest: the glyph alone, 12 pt at a 1.4 pt stroke (colour: OD-0068, below), centred in a 46 x 40 button.
//   Hovered: two rings (24 pt, 1.5 pt stroke; white at 65 % on dark (light: OD-0068, below); close: rose
//   rgb(255, 107, 139) at 90 %) leave the glyph, scale 0.45 -> 1.55 and fade to nothing over 1.3 s on
//   cubic-bezier(0.2, 0.6, 0.3, 1) (the comet's own ring curve, CometMotion.RingRadius), the second 0.45 s after the
//   first, repeating while hovered; the glyph ticks on the same 1.3 s beat: 1 -> 0.86 at 12 % -> 1.06 at 30 % -> 1.
// ENGINEERING DECISIONs (owner-visible only as small refinements of the picked variant, which shows no press, disabled
// or reduce-motion state): pressed, the glyph draws at 70 % opacity (Windows gives every caption button a press state);
// disabled (maximise on a window that cannot resize, as Windows greys it), the glyph draws at 35 % and nothing animates;
// under reduce motion a hovered button shows one still ring at full size and 45 % of its opacity instead of the pulse.
// The clock is the frame-clock seam (ED-117 (1)): frames are the only wake-ups, time is SwiftClock's.
// OD-0068 (ED-244) amends the picked variant's colours only (motion, sizes and timing stay OD-0044's): the glyph and the
// rings take a colour that contrasts with the theme's own appearance, dark on a light theme and light on a dark one,
// rather than the label colour at 85 % (ED-051), which is 5:1 at best on a light surface and had no inactive state:
//   glyph at rest      light: rgb(24, 24, 27)   dark: white            (at least 4.5:1 over the window behind it)
//   window inactive    light: rgb(92, 92, 96)   dark: rgb(200, 200, 204) (at least 3:1; hovered or pressed, the
//                      glyph takes its active colour again, as Windows' own caption buttons do)
//   hover rings        light: the glyph colour at 65 %   dark: white at 65 % (OD-0044's)
//   close's rings      light: deep rose rgb(176, 18, 62) at 90 %   dark: rose rgb(255, 107, 139) at 90 % (OD-0044's)
// The appearance is the button's own ActualThemeVariant, which follows the window's (the theme's scheme, or Windows' for
// System); a change of it draws the button again (a theme switch used to leave the old appearance's glyph in place until
// the next hover, which is how the light gallery windows came to show white glyphs).

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

    /// <summary>OD-0068 (ED-244): the glyph on a light theme, at rest in an active window.</summary>
    public static readonly Color LightGlyph = Color.FromRgb(24, 24, 27);
    /// <summary>OD-0068 (ED-244): the glyph on a light theme while the window is inactive.</summary>
    public static readonly Color LightInactiveGlyph = Color.FromRgb(92, 92, 96);
    /// <summary>OD-0068 (ED-244): the glyph on a dark theme, at rest in an active window.</summary>
    public static readonly Color DarkGlyph = Color.FromRgb(255, 255, 255);
    /// <summary>OD-0068 (ED-244): the glyph on a dark theme while the window is inactive.</summary>
    public static readonly Color DarkInactiveGlyph = Color.FromRgb(200, 200, 204);
    /// <summary>OD-0044: the close button's rose rings on a dark theme, rgb(255, 107, 139) at 90 %.</summary>
    public static readonly Color DarkCloseRing = Color.FromArgb(230, 255, 107, 139);
    /// <summary>OD-0068 (ED-244): the deeper rose of the close button's rings on a light theme, at 90 %.</summary>
    public static readonly Color LightCloseRing = Color.FromArgb(230, 176, 18, 62);
    /// <summary>OD-0044 (dark), OD-0068 (light): the opacity of the other buttons' rings, 65 %.</summary>
    public const byte RingAlpha = 166;

    /// <summary>OD-0068 (ED-244): the glyph's colour on a dark (<paramref name="dark"/>) or light theme, in an active
    /// or an inactive window.</summary>
    public static Color GlyphColor(bool dark, bool windowActive) =>
        dark ? windowActive ? DarkGlyph : DarkInactiveGlyph : windowActive ? LightGlyph : LightInactiveGlyph;

    /// <summary>OD-0044, OD-0068 (ED-244): the hover rings' colour for <paramref name="kind"/> on a dark or light theme.</summary>
    public static Color RingColor(Kind kind, bool dark) => kind == Kind.Close
        ? dark ? DarkCloseRing : LightCloseRing
        : dark ? Color.FromArgb(RingAlpha, DarkGlyph.R, DarkGlyph.G, DarkGlyph.B) : Color.FromArgb(RingAlpha, LightGlyph.R, LightGlyph.G, LightGlyph.B);

    private readonly Action<TimeSpan> tick;
    private IFrameSource? source;
    private bool waiting;
    private bool hovered;
    private bool pressed;
    private bool maximized;
    private bool windowActive = true;
    private ContinuousInstant hoverStart;

    public CaptionButton(Kind kind)
    {
        ButtonKind = kind;
        Width = ButtonWidth;
        Height = ButtonHeight;
        tick = OnFrame;
        DetachedFromVisualTree += (_, _) => Stop();
        ActualThemeVariantChanged += (_, _) => OnAppearance(); // OD-0068: draw again in the new appearance
        drawnDark = PlainControls.IsDark(ActualThemeVariant);
    }

    // ED-254 (OD-0088, OD-0076): in a window that turns over (AppearanceTurn, the Settings window) the glyph and ring
    // colours ease 0.32 s from the old appearance's to the new one's with the window, 1 standing for dark and 0 for light.
    private bool drawnDark;
    private double? turn;
    private Action stopTurn = static () => { };

    /// <summary>ED-254: how dark the colours are drawn now, 0 (light) to 1 (dark), while the appearance turns over.</summary>
    public double Darkness => turn ?? (PlainControls.IsDark(ActualThemeVariant) ? 1 : 0);

    private void OnAppearance()
    {
        var dark = PlainControls.IsDark(ActualThemeVariant);
        var from = turn ?? (drawnDark ? 1 : 0);
        drawnDark = dark;
        stopTurn();
        stopTurn = static () => { };
        turn = null;
        var to = dark ? 1.0 : 0.0;
        if (from != to && AppearanceTurn.Eases(this))
        {
            stopTurn = AppearanceTurn.Run(this, progress =>
            {
                turn = progress >= 1 ? null : from + (to - from) * progress;
                InvalidateVisual();
            });
        }
        InvalidateVisual();
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

    /// <summary>OD-0068 (ED-244): the window is the active one (WindowChrome follows the window's IsActive); an inactive
    /// window's glyph at rest takes the dimmer inactive colour.</summary>
    public bool IsWindowActive
    {
        get => windowActive;
        set { if (windowActive == value) return; windowActive = value; InvalidateVisual(); }
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
        // OD-0044's entire 46 x 40 button is a native hit region, including between the glyph's strokes.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var darkness = Darkness;
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var enabled = IsEffectivelyEnabled;
        var active = windowActive || (enabled && (hovered || pressed));
        var ink = SwiftColor.PremultipliedMix(GlyphColor(false, active), GlyphColor(true, active), darkness);
        if (hovered && enabled)
        {
            var ring = SwiftColor.PremultipliedMix(RingColor(ButtonKind, false), RingColor(ButtonKind, true), darkness);
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
