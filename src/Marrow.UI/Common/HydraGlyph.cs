using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.UI.Common;

// Source: DroppyCode/UI/Common/HydraGlyph.swift (B2 1fbf81e), :1-243.
// Forms (ED-095, ED-098): HydraGlyph, HydraMarkImage and HydraNameText are Decorators; HydraBreathRing
// (NSViewRepresentable, :72-85) and its NSView (HydraBreathRingView, :87-213) collapse into one Control,
// HydraBreathRingView, that draws and drives its own breath (there is no separate Avalonia host view to
// represent); HydraNameStyle is a static class, its one member the Text builder at :229-230.
// Chrome.success/Chrome.danger/Chrome.secondaryText are Marrow.UI.Chrome.Chrome (ED-062).
// The Core Animation timeline (:184-212) has no Avalonia twin; HydraBreathRingView reproduces the swell and
// fade as a pure function of elapsed time (Phase, an ENGINEERING DECISION so the breath is testable without
// a live render loop) driven each frame by the FrameClock seam (Port/FrameClock.cs, ED-117) exactly as
// LoadingMark.swift's port (Port/LoadingMark.cs) already drives its own animation. The shared clock origin
// (:207-209 `ring.convertTime(1, from: nil)`) has no Windows equivalent of CALayer's host time space; every
// ring instead phases off SwiftClock.Now from a fixed zero origin (ENGINEERING DECISION), so every ring in
// the app still breathes in step. `NSApplication.didBecomeActiveNotification` reinstall (:116, :155-158) is
// not reproduced: a frame-clock-driven render loop is not dropped when Windows deactivates the app, so there
// is nothing to reinstall (ENGINEERING DECISION).

/// <summary>
/// A head's mark: its own dragon, in its colour. The same glyph stands for the head everywhere it shows: the
/// panel, the popover, the tool row that sent it out and the report it hands back. The twenty-five heads are
/// template images, so one colour tints each whole.
/// </summary>
// :9-41 struct HydraGlyph: View
public sealed class HydraGlyph : Decorator
{
    /// <param name="persona">:10 <c>let persona: HydraPersona</c>.</param>
    /// <param name="size">:11 <c>var size: CGFloat = 20</c>.</param>
    /// <param name="isRunning">:13 <c>var isRunning = false</c>: a soft ring breathes around a head still at work.</param>
    /// <param name="status">:16 <c>var status: HydraHeadInfo.Status?</c>: a finished head wears its outcome on the
    /// glyph's bottom-right corner. Nil, or running, shows nothing.</param>
    // :20-41 body
    public HydraGlyph(HydraPersona persona, double size = 20, bool isRunning = false, HydraHeadInfo.Status? status = null)
    {
        Persona = persona;
        Size = size;
        IsRunning = isRunning;
        Status = status;

        var color = PersonaColor(persona);
        var stack = new Panel { Width = size, Height = size }; // :21 ZStack
        if (isRunning)
        {
            // :22-25 HydraBreathRing(color:, lineWidth: 1.5).frame(size).allowsHitTesting(false)
            var ring = new HydraBreathRingView
            {
                Width = size,
                Height = size,
                StrokeColor = color.Opacity(0.55),
                StrokeWidth = 1.5,
                IsHitTestVisible = false,
            };
            stack.Children.Add(ring);
        }
        // :27-32 Image(persona.asset).resizable().renderingMode(.template).aspectRatio(.fit)
        //        .foregroundStyle(persona.color).frame(size)
        var image = SwiftImage.Named(persona.Asset)
            .Resizable()
            .RenderingMode(ImageRenderingMode.Template)
            .AspectRatio(ContentMode.Fit)
            .ForegroundStyle(color);
        image.Width = size;
        image.Height = size;
        stack.Children.Add(image);

        var root = new Panel { Width = size, Height = size }; // :34 .frame(size) on the ZStack
        root.Children.Add(stack);
        if (status is { } finishedStatus && finishedStatus.IsFinished) // :36 if let status, status.isFinished
        {
            var badge = Badge(finishedStatus, size); // :37 badge(for: status)
            badge.HorizontalAlignment = HorizontalAlignment.Right; // :35 .overlay(alignment: .bottomTrailing)
            badge.VerticalAlignment = VerticalAlignment.Bottom;
            root.Children.Add(badge);
        }
        Child = root;

        // :40 .accessibilityLabel(Text(status.map { "\(persona.name), \($0.rawValue)" } ?? persona.name))
        AutomationProperties.SetName(this, status is { } labelled
            ? $"{persona.Name}, {SwiftEnum<HydraHeadInfo.Status>.RawValue(labelled)}"
            : persona.Name);
    }

    public HydraPersona Persona { get; }
    public double Size { get; }
    public bool IsRunning { get; }
    public HydraHeadInfo.Status? Status { get; }

    // :218-224 Color(red:green:blue:) defaults alpha to 1 (HydraPersona.Color, Core/Models/Hydra.swift:218-224);
    // Color at the Core boundary is the plain RGBA tuple (IMPLEMENTING 2.1), converted to the UI value here.
    internal static SwiftColor PersonaColor(HydraPersona persona)
    {
        var (red, green, blue, alpha) = persona.Color;
        return new SwiftColor(red, green, blue, alpha);
    }

    /// <summary>
    /// The outcome as a small disc riding the corner, cut out from the glyph by a ring in the surface's colour, the
    /// way a presence badge sits on an avatar.
    /// </summary>
    // :45-65 private func badge(for:)
    private static Control Badge(HydraHeadInfo.Status status, double size)
    {
        var diameter = Math.Max(8, size * 0.5); // :46
        var (symbolName, color) = status switch // :47-51
        {
            HydraHeadInfo.Status.Completed => ("checkmark", Chrome.Chrome.Success),
            HydraHeadInfo.Status.Failed => ("xmark", Chrome.Chrome.Danger),
            _ => ("stop.fill", Chrome.Chrome.SecondaryText),
        };

        var circle = new Ellipse { Width = diameter, Height = diameter }; // :53-54 Circle().fill(color)
        circle.Fill = color.Brush(circle);

        var symbol = SwiftImage.SystemName(symbolName).ForegroundStyle(SwiftColor.White); // :55-57
        SwiftImage.SetFont(symbol, SwiftFont.System(diameter * 0.55, SwiftFont.Weight.Heavy));

        var badge = new Panel { Width = diameter, Height = diameter }; // :52, :59
        badge.Children.Add(circle);
        badge.Children.Add(symbol);

        // :60-62 .overlay { Circle().strokeBorder(colorScheme == .dark ? black 0.55 : white 0.95, lineWidth:) }
        var strokeWidth = Math.Max(1, diameter * 0.12);
        var ring = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            StrokeThickness = strokeWidth,
            Stroke = SwiftColor.Dynamic(light: SwiftColor.White.Opacity(0.95), dark: SwiftColor.Black.Opacity(0.55)).Brush(badge),
        };
        var withRing = new Panel { Width = diameter, Height = diameter };
        withRing.Children.Add(badge);
        withRing.Children.Add(ring);

        // :63 .offset(x: diameter * 0.3, y: diameter * 0.3)
        withRing.RenderTransform = new TranslateTransform(diameter * 0.3, diameter * 0.3);
        // :64 .transition(.scale.combined(with: .opacity))
        return withRing.Transition(SwiftTransition.Scale().Combined(SwiftTransition.Opacity));
    }
}

/// <summary>
/// The ring around a working head: it swells and fades, over and over. Driven by the frame clock (ED-117), so
/// nothing above it re-renders while the ring breathes.
/// </summary>
// :72-85 struct HydraBreathRing: NSViewRepresentable, :87-213 final class HydraBreathRingView: NSView
public sealed class HydraBreathRingView : Control
{
    // :94 period = 1.6
    public const double Period = 1.6;

    // :95 restingScale = 1.05
    public const double RestingScale = 1.05;

    // :96 swollenScale = 1.5
    public const double SwollenScale = 1.5;

    // :97 restingOpacity: Float = 0.9
    public const double RestingOpacity = 0.9;

    // ED-NEW-lean-8 review: the swell's ease-out over one period, made once (an immutable timing curve; a new one each frame
    // was the frame's only allocation of its own).
    private static readonly Marrow.UI.Port.Animation BreathCurve = Marrow.UI.Port.Animation.EaseOut(Period);

    private readonly Action<TimeSpan> frame;
    private IFrameSource? source;
    private bool waiting;
    private double scale = RestingScale;
    private double opacity = RestingOpacity;
    private DecorationPace pace;

    // ED-NEW-lean-8: the stroke, made once per colour and width rather than once per frame (a new brush per frame also bound
    // itself to the control's theme changes, one subscription per frame). It is the pen each frame made before, of the brush
    // StrokeColor.Brush(this) makes: that brush follows this view's theme and the system accent by itself (BrushBinding), so
    // the resting ring under Reduce Motion, which asks for no frames, still takes a theme or accent change at once.
    private Pen? pen;
    private SwiftColor? penColor;
    private double penWidth;

    public HydraBreathRingView()
    {
        frame = OnFrame;
        Focusable = false;
        // :128 override func hitTest(_:) -> NSView? { nil }
        IsHitTestVisible = false;
        AttachedToVisualTree += (_, _) => // :141-148, :184 the resting frame, then the breath, from the shared origin
        {
            source = FrameClock.Source.Current;
            Update();
            InvalidateVisual();
            Request();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (waiting) source!.CancelFrame(frame);
            waiting = false;
            source = null;
        };
        // :192 under Reduce Motion the ring rests and asks for no frames (ED-NEW-lean-8); turned off again, it breathes.
        PropertyChanged += (_, e) =>
        {
            if (e.Property != EnvironmentValues.AccessibilityReduceMotionProperty || source is null) return;
            Update();
            InvalidateVisual();
            if (!Reduced) Request();
            else if (waiting)
            {
                source.CancelFrame(frame);
                waiting = false;
            }
        };
    }

    /// <summary>:73, :135 <c>let color: CGColor</c> / <c>ring.strokeColor</c>.</summary>
    public SwiftColor StrokeColor { get; set; } = SwiftColor.White;

    /// <summary>:74, :136 <c>let lineWidth: CGFloat</c> / <c>ring.lineWidth</c>.</summary>
    public double StrokeWidth { get; set; } = 1.5;

    private bool Reduced => EnvironmentValues.GetAccessibilityReduceMotion(this);

    public double CurrentScale => scale;

    public double CurrentOpacity => opacity;

    /// <summary>
    /// :189-211 installAnimation as a pure function of elapsed time: the resting frame at <paramref name="elapsed"/>
    /// zero (also what Reduce Motion shows forever, :192), then an ease-out swell from <see cref="RestingScale"/> to
    /// <see cref="SwollenScale"/> and fade from <see cref="RestingOpacity"/> to 0, repeating every
    /// <see cref="Period"/> seconds without end (:193-206).
    /// </summary>
    public static (double Scale, double Opacity) Phase(double elapsedSeconds, bool reduceMotion)
    {
        if (reduceMotion) return (RestingScale, RestingOpacity);
        var period = Period;
        var phase = elapsedSeconds % period;
        if (phase < 0) phase += period;
        var progress = BreathCurve.Progress(phase);
        return (RestingScale + (SwollenScale - RestingScale) * progress, RestingOpacity * (1 - progress));
    }

    private void Request()
    {
        if (waiting || source is null || Reduced) return;
        pace.Reset(); // a new run of frames repaints at its first
        waiting = true;
        source.RequestFrame(frame);
    }

    /// <summary>ED-NEW-lean-8 (tests): how many frames have repainted the ring.</summary>
    internal int Invalidations { get; private set; }

    // ED-NEW-lean-8: the breath repaints at the 60 Hz cap (DecorationPace); the frames between only ask again.
    private void OnFrame(TimeSpan _)
    {
        waiting = false;
        if (source is null) return;
        if (Reduced)
        {
            Update(); // the resting frame, and no more frames
            InvalidateVisual();
            return;
        }
        if (pace.Due(SwiftClock.Now))
        {
            Update();
            Invalidations++;
            InvalidateVisual();
        }
        waiting = true;
        source.RequestFrame(frame);
    }

    private void Update()
    {
        var reduceMotion = EnvironmentValues.GetAccessibilityReduceMotion(this);
        var elapsed = (SwiftClock.Now - default(ContinuousInstant)).TotalSeconds; // shared origin (see file header)
        (scale, opacity) = Phase(elapsed, reduceMotion);
    }

    // :103-104 layer?.masksToBounds = false: nothing here clips the ring, which swells past the glyph's frame.
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var inset = StrokeWidth / 2; // :180 bounds.insetBy(dx: lineWidth / 2, dy: lineWidth / 2)
        var rect = new Rect(inset, inset, Math.Max(0, Bounds.Width - StrokeWidth), Math.Max(0, Bounds.Height - StrokeWidth));
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        if (pen is null || !ReferenceEquals(StrokeColor, penColor) || StrokeWidth != penWidth)
        {
            pen = new Pen(StrokeColor.Brush(this), StrokeWidth);
            penColor = StrokeColor;
            penWidth = StrokeWidth;
        }
        using (context.PushOpacity(opacity))
        using (context.PushTransform(Matrix.CreateTranslation(-centre.X, -centre.Y) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(centre.X, centre.Y)))
        {
            context.DrawEllipse(null, pen, rect.Center, rect.Width / 2, rect.Height / 2);
        }
    }
}

/// <summary>The Hydra mark: one clean dragon head, a template that takes whatever colour it is given. Sized by its
/// frame.</summary>
// :217-224 struct HydraMarkImage: View
public sealed class HydraMarkImage : Decorator
{
    public HydraMarkImage()
    {
        // :219-222 Image("hydra-mark").resizable().renderingMode(.template).aspectRatio(.fit)
        Child = SwiftImage.Named("hydra-mark")
            .Resizable()
            .RenderingMode(ImageRenderingMode.Template)
            .AspectRatio(ContentMode.Fit);
    }
}

// :226-232 enum HydraNameStyle
public static class HydraNameStyle
{
    /// <summary>
    /// The head's name as Text, all in one colour: "Otto 2" reads as a single name, its round number never picked
    /// out in the head's own colour the glyph beside it wears.
    /// </summary>
    // :229-230 static func text(_:base:)
    public static StyledText Text(HydraPersona persona, SwiftColor @base) =>
        new StyledText(persona.Name).ForegroundColor(@base); // Text(verbatim: persona.name).foregroundColor(base)
}

// :234-242 struct HydraNameText: View
public sealed class HydraNameText : Decorator
{
    /// <param name="persona">:235 <c>let persona: HydraPersona</c>.</param>
    /// <param name="size">:236 <c>var size: CGFloat = 13</c>.</param>
    /// <param name="weight">:237 <c>var weight: Font.Weight = .medium</c>.</param>
    /// <param name="color">:238 <c>var color: Color = Chrome.primaryText.opacity(0.92)</c>.</param>
    // :239-241 body: HydraNameStyle.text(persona, base: color).font(.system(size: size, weight: weight))
    public HydraNameText(HydraPersona persona, double size = 13, SwiftFont.Weight weight = SwiftFont.Weight.Medium, SwiftColor? color = null)
    {
        var text = HydraNameStyle.Text(persona, color ?? Chrome.Chrome.PrimaryText.Opacity(0.92))
            .Font(SwiftFont.System(size, weight));
        var block = new TextBlock();
        text.Render(block);
        Child = block;
    }
}
