using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.UI.Common;

// Source: DroppyCode/UI/Common/Genie.swift (B2 1fbf81e), :1-214. Partial port: the flight's keyframes (:155, :166-168,
// `keyframeAnimator(initialValue:trigger:)` with one `CubicKeyframe(end, duration: GenieAnimator.duration)`) are not
// written. The value a lone cubic keyframe takes between its ends is unsettled SwiftUI behaviour with no port type
// (STOP UI.Common.Genie #1), so a flight shows its starting progress until it is removed. The shader it calls,
// UI/Common/Genie.metal:16-54, is Port/ShaderLibrary.cs.

/// <summary>
/// Archived rows fly into the window's bottom-left corner with a genie effect.
///
/// The row is rendered once into a flat image, and that image is what flies: a small GPU
/// distortion over one layer for about half a second, with nothing left running afterwards.
/// </summary>
// :8-96
public sealed class GenieAnimator : MainActorModel
{
    private readonly IGeniePlatform platform;
    private ValueList<Flight> flights = ValueList<Flight>.Empty;
    private ValueList<Morph> morphs = ValueList<Morph>.Empty;

    /// <summary>The three AppKit reads (:64, :84, :85-88) come from <paramref name="platform"/>; Windows by default.</summary>
    public GenieAnimator(IGeniePlatform? platform = null)
    {
        this.platform = platform ?? new WindowsGeniePlatform();
    }

    // :11 static let shared; R-LIFE-2: an ambient, so a test can replace it for its own flow.
    public static Ambient<GenieAnimator> Shared { get; } = new(() => new GenieAnimator());

    // :12
    public const string CoordinateSpace = "droppy-code-window";

    // :13
    public const double Duration = 0.58;

    /// <summary>One ghost in the air.</summary>
    // :15-23
    public sealed record Flight(IImage Image, Rect Frame, Point? Target = null, bool IsArrival = false)
    {
        // :16 let id = UUID()
        public Guid Id { get; } = Guid.NewGuid();

        // :19-20 Where the flight lands, in the layer's coordinates; nil flies to the archive corner.
        public Point? Target { get; init; } = Target;

        // :21-22 Played backwards: the ghost comes out of the target and settles into `frame`.
        public bool IsArrival { get; init; } = IsArrival;
    }

    // :25 private(set) var flights
    public ValueList<Flight> Flights
    {
        get => Read(flights);
        private set => Set(ref flights, value);
    }

    /// <summary>
    /// A surface that changes shape on its way between two places: a rounded rectangle whose
    /// frame and corner radius run from one to the other, as one geometry. The Hydra panel
    /// grows out of its button this way and shrinks back into it.
    /// </summary>
    // :27-43
    public sealed record Morph(Rect From, double FromRadius, Rect To, double ToRadius, double FadeIn, double HoldsFor, Func<double, Control> Surface)
    {
        // :31 let id = UUID()
        public Guid Id { get; } = Guid.NewGuid();
    }

    // :45 private(set) var morphs
    public ValueList<Morph> Morphs
    {
        get => Read(morphs);
        private set => Set(ref morphs, value);
    }

    // :47-48 How long a morph takes to change shape, not counting its hold at the start.
    public const double MorphDuration = 0.5;

    // :49-50 The surface's crossfade at either end, with what it stands in for.
    public const double MorphCrossfade = 0.12;

    // :51
    public static Animation MorphAnimation => Animation.Smooth(duration: MorphDuration);

    /// <summary>
    /// Morphs a surface from one frame and corner radius to another, both in the layer's
    /// coordinates. Returns false when nothing moves (reduced motion, or nothing to draw),
    /// so the caller can show the real thing instead.
    /// </summary>
    // :53-70. Swift: morph(from:radius:to:radius:fadeIn:holdsFor:surface:); R-NAME-3 (the nested type Morph keeps the
    // name) and ED-095 (the surface, which has no default, moves before the defaulted timings).
    public bool GenieAnimatorMorph(Rect from, double fromRadius, Rect to, double toRadius, Func<double, Control> surface,
        double fadeIn = 0, double holdsFor = 0)
    {
        // :63-64
        if (!(from.Width > 1 && from.Height > 1 && to.Width > 1 && to.Height > 1 &&
              !platform.AccessibilityDisplayShouldReduceMotion)) return false;
        // :65-68
        Morphs = Morphs.Appending(new Morph(from, fromRadius, to, toRadius, fadeIn, holdsFor, surface));
        return true; // :69
    }

    // :72-74
    public void FinishMorph(Guid id)
    {
        Morphs = new ValueList<Morph>(Morphs.Where(morph => morph.Id != id));
    }

    /// <summary>
    /// Flies a ghost of a row from <paramref name="frame"/>. The ghost draws exactly what <paramref name="ghost"/>
    /// returns, so pass the row as it looks, background included, and the row
    /// hands over to it without a visible change. Arriving, the ghost flies the other
    /// way: out of <paramref name="target"/> and into <paramref name="frame"/>. Returns false when nothing flies (reduced
    /// motion, or nothing to draw), so the caller can show the real thing instead.
    /// </summary>
    // :76-91. ColorScheme is ThemeVariant (AppTheme.cs); ED-095: the ghost, which has no default, moves before the
    // defaulted target and arriving.
    public bool Launch(Rect frame, ThemeVariant colorScheme, Func<Control> ghost, Point? target = null, bool arriving = false)
    {
        // :83-84
        if (!(frame.Width > 1 && frame.Height > 1 && !platform.AccessibilityDisplayShouldReduceMotion)) return false;
        // :85-86 GenieGhost(size: frame.size, content: ghost()).environment(\.colorScheme, colorScheme)
        var content = new ThemeVariantScope
        {
            RequestedThemeVariant = colorScheme,
            Child = new GenieGhost(frame.Size, ghost()),
        };
        var scale = platform.MainScreenBackingScaleFactor ?? 2; // :87
        if (platform.RenderImage(content, scale) is not { } image) return false; // :88
        Flights = Flights.Appending(new Flight(image, frame, target, arriving)); // :89
        return true; // :90
    }

    // :93-95
    public void Finish(Guid id)
    {
        Flights = new ValueList<Flight>(Flights.Where(flight => flight.Id != id));
    }
}

/// <summary>
/// The flat stand-in a row becomes for its flight. It adds no fill of its own:
/// the ghost already carries the row's background, so a transparent row stays
/// transparent instead of flashing opaque when archiving starts.
/// </summary>
// :98-109. Swift `private struct`; internal so the tests can reach it (R-NAME-6).
internal sealed class GenieGhost : Panel
{
    public GenieGhost(Size size, Control content)
    {
        GhostSize = size;
        Children.Add(content);
    }

    // :102
    public Size GhostSize { get; }

    // :106-107 content.frame(width: size.width, height: size.height, alignment: .topLeading)
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(GhostSize);
        return GhostSize;
    }

    // ENGINEERING DECISION: a SwiftUI frame proposes its size and a flexible view takes it all; a view of fixed size
    // keeps its own and sits top-leading. In Avalonia the flexible case is a stretching child with no size of its own.
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var width = child.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Stretch && double.IsNaN(child.Width)
                ? GhostSize.Width : child.DesiredSize.Width;
            var height = child.VerticalAlignment == Avalonia.Layout.VerticalAlignment.Stretch && double.IsNaN(child.Height)
                ? GhostSize.Height : child.DesiredSize.Height;
            child.Arrange(new Rect(0, 0, width, height));
        }
        return GhostSize;
    }
}

/// <summary>The window-wide layer the flights draw in. It never takes clicks.</summary>
// :111-128
public sealed class GenieLayer : Decorator
{
    private readonly Panel flightsPanel = new();
    private readonly Panel morphsPanel = new();
    private Size canvas;

    public GenieLayer()
    {
        // :115 ZStack(alignment: .topLeading): each ForEach below, the flights first so the morphs draw above them.
        Stack = new Panel { Children = { flightsPanel, morphsPanel } };
        Child = Stack;
        // :116-118
        var flightRows = new ForEach<GenieAnimator.Flight, Guid>(flightsPanel, flight => flight.Id,
            flight => new GenieFlightView(flight, canvas), (row, flight) => ((GenieFlightView)row).Update(flight, canvas));
        // :119-121
        var morphRows = new ForEach<GenieAnimator.Morph, Guid>(morphsPanel, morph => morph.Id,
            morph => new MorphView(morph, canvas), (row, morph) => ((MorphView)row).Update(morph, canvas));
        this.Track(() => GenieAnimator.Shared.Current.Flights, flightRows.Apply);
        this.Track(() => GenieAnimator.Shared.Current.Morphs, morphRows.Apply);
        IsHitTestVisible = false; // :125 .allowsHitTesting(false)
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw); // :126 .accessibilityHidden(true)
    }

    internal Panel Stack { get; }

    // :114 GeometryReader: every child is handed the size the layer is given (proxy.size), and :123 frames the stack
    // to it, top-leading; the children stretch to it.
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (canvas != finalSize)
        {
            canvas = finalSize;
            foreach (var row in flightsPanel.Children.OfType<GenieFlightView>()) row.Update(row.Flight, canvas);
            foreach (var row in morphsPanel.Children.OfType<MorphView>()) row.Update(row.Morph, canvas);
        }
        return base.ArrangeOverride(finalSize);
    }

    // :126 .accessibilityHidden(true): neither the layer nor anything in it is exposed.
    protected override AutomationPeer OnCreateAutomationPeer() => new HiddenAutomationPeer(this);

    private sealed class HiddenAutomationPeer(Control owner) : ControlAutomationPeer(owner)
    {
        protected override bool IsContentElementCore() => false;

        protected override bool IsControlElementCore() => false;

        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => [];
    }
}

// :130-176. Swift `private struct`; internal so the tests can reach it (R-NAME-6).
internal sealed class GenieFlightView : Control
{
    private readonly SKImage image;

    public GenieFlightView(GenieAnimator.Flight flight, Size canvas)
    {
        Flight = flight;
        Canvas = canvas;
        image = ShaderLibrary.RasterImage(flight.Image);
        // :155 keyframeAnimator(initialValue: start, ...): the value before the keyframes run.
        Present(Start);
        // :169 .onAppear { hasStarted = true } is the keyframes' trigger (:155); the keyframes are unwritten, see the
        // file header (STOP UI.Common.Genie #1).
        // :170-174
        this.Task(() => 0, async _ =>
        {
            // Removed just before the keyframes finish, so the flight never springs back.
            await SwiftTask.TrySleep(SwiftTask.Seconds(GenieAnimator.Duration - 0.03));
            GenieAnimator.Shared.Current.Finish(Flight.Id);
        });
    }

    // :131
    public GenieAnimator.Flight Flight { get; private set; }

    // :132
    public Size Canvas { get; private set; }

    // :137
    public Point Target => Flight.Target ?? new Point(24, Canvas.Height - 12);

    // :139-141 An arrival is the same flight run backwards: all the way in at the start, and
    // out of the funnel into place by the end.
    public double Start => Flight.IsArrival ? 1.0 : 0.0;

    public double End => Flight.IsArrival ? 0.0 : 1.0;

    // :142-149 How far a destination pixel can reach for its source: the shader maps the
    // funnel's pixels back into the row's frame, so the row's size plus its distance
    // to the neck bounds it. The offscreen pass the effect rasterizes is padded by
    // this on every side; the whole canvas padded it by the whole window.
    public Size Reach => new(
        Math.Abs(Target.X - Flight.Frame.Center.X) + Flight.Frame.Width,
        Math.Abs(Target.Y - Flight.Frame.Center.Y) + Flight.Frame.Height);

    // The keyframes' current value (:155 `progress`).
    public double Progress { get; private set; }

    public void Update(GenieAnimator.Flight flight, Size canvas)
    {
        Flight = flight;
        if (Canvas == canvas) return;
        Canvas = canvas;
        InvalidateVisual();
    }

    // :155-165 the keyframes' content closure for one value of progress.
    public void Present(double progress)
    {
        Progress = progress;
        // :165 .opacity(progress > 0.9 ? max(0, (1 - progress) / 0.1) : 1); R-NUM-4 max.
        var fading = (1 - progress) / 0.1;
        Opacity = progress > 0.9 ? (fading >= 0 ? fading : 0) : 1;
        InvalidateVisual();
    }

    // :150-164 the image at the frame's size, centred on the frame, inside a canvas-sized top-leading frame, distorted.
    // The distortion draws over the frame padded by `reach` on every side (maxSampleOffset, :163), within the canvas.
    public override void Render(DrawingContext context)
    {
        var frame = Flight.Frame;
        var reach = Reach;
        var padded = new Rect(frame.X - reach.Width, frame.Y - reach.Height,
            frame.Width + 2 * reach.Width, frame.Height + 2 * reach.Height);
        var bounds = padded.Intersect(new Rect(Canvas));
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        context.Custom(new GenieDistortionOperation(bounds, image, Progress, frame, Target));
    }
}

/// <summary>
/// One surface on its way between two shapes: its frame and corner radius animate as one,
/// so it reads as a single thing growing or shrinking rather than two views swapped. It
/// fades in over what it replaces, holds while that finishes, changes shape, and fades
/// out over the last stretch so the real thing can take over underneath.
/// </summary>
// :178-214. Swift `private struct`; internal so the tests can reach it (R-NAME-6). The outer control is
// `.opacity(hasStarted || fadeIn <= 0 ? 1 : 0)` (:200), FadeLayer is `.opacity(isFading ? 0 : 1)` (:198), and inside
// it the surface is framed and positioned in the canvas (:193-196).
internal sealed class MorphView : Decorator
{
    private readonly ViewState state = new();
    private readonly Placement placement = new();
    private readonly PresentedValue landing;
    private readonly PresentedValue fading;
    private readonly PresentedValue starting;
    private readonly List<IDisposable> changes = [];
    private double? drawnRadius;

    public MorphView(GenieAnimator.Morph morph, Size canvas)
    {
        Morph = morph;
        Canvas = canvas;
        FadeLayer = new Decorator { Child = placement };
        Child = FadeLayer;
        landing = new PresentedValue(0, Land);
        fading = new PresentedValue(1, value => FadeLayer.Opacity = value);
        starting = new PresentedValue(StartOpacity, value => Opacity = value);
        AttachedToVisualTree += (_, _) => Appear();
        DetachedFromVisualTree += (_, _) => Disappear();
        // :206-212
        this.Task(() => 0, async _ =>
        {
            var lands = Morph.HoldsFor + GenieAnimator.MorphDuration;
            await SwiftTask.TrySleep(SwiftTask.Seconds(lands - GenieAnimator.MorphCrossfade));
            state.IsFading = true;
            await SwiftTask.TrySleep(SwiftTask.Seconds(GenieAnimator.MorphCrossfade + 0.05));
            GenieAnimator.Shared.Current.FinishMorph(Morph.Id);
        });
    }

    // :183
    public GenieAnimator.Morph Morph { get; private set; }

    // :184
    public Size Canvas { get; private set; }

    public Decorator FadeLayer { get; }

    // :191 the frame the surface has now: `from` until it lands, `to` after, animated between.
    public Rect PresentedFrame { get; private set; }

    // :192 the corner radius the surface has now.
    public double PresentedRadius { get; private set; }

    // :193 the surface as last drawn.
    public Control? Surface { get; private set; }

    // :200
    private double StartOpacity => state.HasStarted || Morph.FadeIn <= 0 ? 1 : 0;

    public void Update(GenieAnimator.Morph morph, Size canvas)
    {
        Morph = morph;
        Canvas = canvas; // :196 the canvas-sized frame: this control stretches to the layer's size.
    }

    private void Appear()
    {
        // R-VIEW-4: each `.animation(a, value: v)` is an OnChange on v applying with a.
        // :197 the frame and radius, on one curve: morphAnimation delayed by the hold.
        changes.Add(new OnChange<bool>(() => state.HasLanded,
            (_, landed) => landing.Set(landed ? 1 : 0, GenieAnimator.MorphAnimation.Delay(Morph.HoldsFor))));
        // :198-199
        changes.Add(new OnChange<bool>(() => state.IsFading,
            (_, isFading) => fading.Set(isFading ? 0 : 1, Animation.EaseOut(duration: GenieAnimator.MorphCrossfade))));
        // :200-201
        changes.Add(new OnChange<double>(() => StartOpacity,
            (_, opacity) => starting.Set(opacity, Animation.EaseOut(duration: Morph.FadeIn))));
        // :202-205
        state.HasStarted = true;
        state.HasLanded = true;
    }

    private void Disappear()
    {
        foreach (var change in changes) change.Dispose();
        changes.Clear();
        landing.Stop();
        fading.Stop();
        starting.Stop();
    }

    // :191-196 for a landing progress p: frame and radius from the same p, and the surface drawn for that radius.
    private void Land(double p)
    {
        var from = Morph.From;
        var to = Morph.To;
        var width = from.Width + (to.Width - from.Width) * p;
        var height = from.Height + (to.Height - from.Height) * p;
        var midX = from.Center.X + (to.Center.X - from.Center.X) * p;
        var midY = from.Center.Y + (to.Center.Y - from.Center.Y) * p;
        PresentedFrame = new Rect(midX - width / 2, midY - height / 2, width, height);
        PresentedRadius = Morph.FromRadius + (Morph.ToRadius - Morph.FromRadius) * p;
        if (drawnRadius != PresentedRadius)
        {
            drawnRadius = PresentedRadius;
            Surface = Morph.Surface(PresentedRadius);
            placement.Children.Clear();
            placement.Children.Add(Surface);
        }
        placement.Place(PresentedFrame);
    }

    private sealed class ViewState : MainActorModel
    {
        private bool hasStarted;
        private bool hasLanded;
        private bool isFading;

        // :186
        public bool HasStarted { get => Read(hasStarted); set => Set(ref hasStarted, value); }

        // :187
        public bool HasLanded { get => Read(hasLanded); set => Set(ref hasLanded, value); }

        // :188
        public bool IsFading { get => Read(isFading); set => Set(ref isFading, value); }
    }

    // :194-195 .frame(width:height:).position(x: frame.midX, y: frame.midY): the surface is proposed the frame's size
    // and centred on the frame's centre.
    private sealed class Placement : Panel
    {
        private Rect frame;

        // The surface sits at the animated frame exactly, as SwiftUI places it; Avalonia's layout rounding (inherited
        // by the surface) would snap each frame of the animation to whole pixels instead.
        public Placement() => UseLayoutRounding = false;

        public void Place(Rect frame)
        {
            this.frame = frame;
            InvalidateMeasure();
            InvalidateArrange();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var child in Children) child.Measure(frame.Size);
            return default;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (var child in Children) child.Arrange(frame);
            return finalSize;
        }
    }

    // The presentation of one animated value (R-VIEW-4): it moves from what is shown now to a new value on the
    // frame clock, reading SwiftClock, and without an animation it jumps.
    private sealed class PresentedValue
    {
        private readonly Action<double> apply;
        private IFrameSource? source;
        private Action<TimeSpan>? pending;

        public PresentedValue(double value, Action<double> apply)
        {
            Value = value;
            this.apply = apply;
            apply(value);
        }

        public double Value { get; private set; }

        public void Set(double target, Animation? animation)
        {
            Stop();
            if (animation is null)
            {
                Value = target;
                apply(target);
                return;
            }
            var from = Value;
            var start = SwiftClock.Now;
            var frames = FrameClock.Source.Current;
            source = frames;
            Action<TimeSpan>? frame = null;
            frame = _ =>
            {
                var seconds = (SwiftClock.Now - start).TotalSeconds;
                var complete = seconds >= animation.Duration;
                Value = complete ? target : from + (target - from) * animation.Progress(seconds);
                apply(Value);
                if (complete) pending = null;
                else frames.RequestFrame(frame!);
            };
            pending = frame;
            frames.RequestFrame(frame);
        }

        public void Stop()
        {
            if (pending is not null) source?.CancelFrame(pending);
            pending = null;
        }
    }
}
