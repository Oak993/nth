// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 "Map: stops and routes on a built-in offline world basemap ... drawn natively. Pan and
// zoom by pointer and keyboard; labels for countries and the 300 largest cities"; 7.4 motion; 10 keyboard and Reduce Motion;
// owner bar: interruptible springs, no snaps; ED-NEW-web-w16c-1). No Swift counterpart. The control that draws a map: the ocean, the
// land with its borders, the lakes, an optional tile layer the reader added, the routes (great circles), the stops and the
// labels, in the thread's palette. The camera glides: a wheel notch, a key, a button or a new stop sends it to a new target by
// the nth spring (zoom keeps the place under the pointer fixed; a fit flies); a drag moves it 1:1 and takes it from any glide
// at once. Labels fade in and out as the zoom reaches them. The zoom wheel needs Ctrl, so the thread still scrolls under the
// pointer. Everything is drawn from the shared basemap; nothing is fetched.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.UI.NthUi.Map;

public sealed class NthUiMapCanvas : Control
{
    /// <summary>The padding kept around a fitted set of stops.</summary>
    public const double FitPadding = 56;

    /// <summary>The deepest zoom a fit reaches (a single stop would otherwise fill the view).</summary>
    public const double FitMaxZoom = 6;

    /// <summary>A wheel notch, a key and a button zoom by these.</summary>
    public const double WheelStep = .6;

    public const double KeyStep = 1;

    private const double Radius = 12;
    private const double StopRadius = 6.5;
    private const double CityRadius = 2.4;
    private const double ArrivalSeconds = .18;

    private enum Glide
    {
        Rest,
        Fly,
        Anchored,
    }

    private readonly NthUiMotion motion;
    private readonly Stepper stepper;
    private readonly Dictionary<(string Text, int Kind), FormattedText> texts = [];
    private readonly Dictionary<string, double> labelAlpha = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NthUiTween> arrivals = new(StringComparer.Ordinal);
    private NthUiMapModel model = new("", [], []);
    private IReadOnlyList<NthUiMapPlacedLabel> placed = [];
    private HashSet<string> plannedKeys = [];
    private NthUiTween routeFade = NthUiTween.At(0);
    private double centerX = .5;
    private double centerY = .45;
    private double zoom = 1;
    private Glide glide;
    private NthSpring cxSpring = NthSpring.At(.5);
    private NthSpring cySpring = NthSpring.At(.45);
    private NthSpring zoomSpring = NthSpring.At(1);
    private Point anchorScreen;
    private Point anchorUnit;
    private bool userMoved;
    private double? initialZoom;
    private bool dragging;
    private bool dragMoved;
    private Point dragFrom;
    private Point lastPointer;
    private int hovered = -1;
    private bool focusRing;
    private double lastStep = double.NaN;
    private bool framed;
    private Size framedFor;

    internal NthUiMapCanvas(NthUiMotion motion)
    {
        this.motion = motion;
        stepper = new Stepper(this);
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        // W10 (ED-NEW-web-w10-5): a street tile that arrives, or a tile source the reader adds or removes, draws the map again.
        AttachedToVisualTree += (_, _) => NthUiMapTiles.Changed += OnTilesChanged;
        DetachedFromVisualTree += (_, _) => NthUiMapTiles.Changed -= OnTilesChanged;
    }

    private void OnTilesChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual, Avalonia.Threading.DispatcherPriority.Render);

    public NthUiMapModel Model => model;

    /// <summary>The initial zoom the line asked for (`zoom=`), or null to fit the stops.</summary>
    public double? InitialZoom
    {
        get => initialZoom;
        set
        {
            if (initialZoom == value) return;
            initialZoom = value;
            if (!userMoved) Frame(animated: false);
        }
    }

    /// <summary>True once the reader has moved the map; the map then stops following the stops that arrive.</summary>
    public bool UserMoved => userMoved;

    /// <summary>The labels placed at the last render (stops, countries, cities), in draw order.</summary>
    public IReadOnlyList<NthUiMapPlacedLabel> PlacedLabels => placed;

    /// <summary>How visible a placed label is now (0 to 1): labels fade in and out as the zoom reaches them (tests).</summary>
    public double LabelAlpha(string key) => labelAlpha.GetValueOrDefault(key, 0);

    /// <summary>The stop under the pointer, or -1.</summary>
    public int HoveredStop => hovered;

    public bool IsGliding => glide != Glide.Rest;

    /// <summary>The deepest zoom now: the tile source's, or the basemap's.</summary>
    public double MaxZoom => NthUiMapTiles.IsBound ? Math.Min(19, NthUiMapTiles.Source.Current.MaxZoom) : NthUiMapCamera.MaxZoom;

    private bool Calm => !motion.CanAnimate || NthUiCalm.For(this);

    /// <summary>The camera the next render uses.</summary>
    public NthUiMapCamera Camera => View(NthUiMotion.Now);

    /// <summary>Where the camera is gliding to (the zoom the reader or a fit asked for), else where it is.</summary>
    public double TargetZoom => glide == Glide.Rest ? zoom : zoomSpring.Target;

    public NthUiMapCamera TargetCamera
    {
        get
        {
            var cam = new NthUiMapCamera { Viewport = Bounds.Size.Width > 0 ? Bounds.Size : framedFor, CenterX = centerX, CenterY = centerY, Zoom = zoom };
            if (glide == Glide.Fly)
            {
                cam.CenterX = cxSpring.Target;
                cam.CenterY = cySpring.Target;
                cam.Zoom = zoomSpring.Target;
            }
            else if (glide == Glide.Anchored)
            {
                cam.Zoom = zoomSpring.Target;
                Anchor(cam);
            }
            cam.Clamp(MaxZoom);
            return cam;
        }
    }

    // ---- content -------------------------------------------------------------------------------------------------------

    /// <summary>Shows the map's stops and routes. New stops fade in; the camera follows them until the reader moves the map.</summary>
    public void Show(NthUiMapModel next)
    {
        var now = NthUiMotion.Now;
        var before = new HashSet<string>(model.Stops.Select(s => s.Key), StringComparer.Ordinal);
        model = next;
        foreach (var stop in next.Stops)
        {
            if (before.Contains(stop.Key) || arrivals.ContainsKey(stop.Key)) continue;
            arrivals[stop.Key] = motion.CanAnimate
                ? NthUiTween.At(0).Retarget(1, now, Calm ? NthUiTokens.CalmFadeSeconds : ArrivalSeconds, NthEase.Out)
                : NthUiTween.At(1);
        }
        var routeWanted = next.Paths.Count > 0 ? 1 : 0;
        routeFade = motion.CanAnimate ? routeFade.Retarget(routeWanted, now, Calm ? NthUiTokens.CalmFadeSeconds : .3, NthEase.Out) : NthUiTween.At(routeWanted);
        if (!userMoved) Frame(animated: framed && motion.CanAnimate);
        Wake();
        InvalidateVisual();
    }

    // ---- the camera ------------------------------------------------------------------------------------------------------

    private NthUiMapCamera View(double now)
    {
        var cam = new NthUiMapCamera { Viewport = Bounds.Size.Width > 0 ? Bounds.Size : framedFor, CenterX = centerX, CenterY = centerY, Zoom = zoom };
        if (glide == Glide.Fly)
        {
            cam.CenterX = cxSpring.Value(now);
            cam.CenterY = cySpring.Value(now);
            cam.Zoom = zoomSpring.Value(now);
        }
        else if (glide == Glide.Anchored)
        {
            cam.Zoom = zoomSpring.Value(now);
            Anchor(cam);
        }
        cam.Clamp(MaxZoom);
        return cam;
    }

    private void Anchor(NthUiMapCamera cam)
    {
        var world = cam.World;
        cam.CenterX = anchorUnit.X - (anchorScreen.X - cam.Viewport.Width / 2) / world;
        cam.CenterY = anchorUnit.Y - (anchorScreen.Y - cam.Viewport.Height / 2) / world;
    }

    /// <summary>Lets go of any glide at the place it has reached.</summary>
    private void Settle(double now)
    {
        if (glide == Glide.Rest) return;
        var cam = View(now);
        centerX = cam.CenterX;
        centerY = cam.CenterY;
        zoom = cam.Zoom;
        glide = Glide.Rest;
    }

    private void FlyTo(double x, double y, double z, bool animated)
    {
        var now = NthUiMotion.Now;
        var probe = new NthUiMapCamera { Viewport = Bounds.Size.Width > 0 ? Bounds.Size : framedFor, Zoom = z, CenterX = x, CenterY = y };
        probe.Clamp(MaxZoom);
        // Take the copy of the world nearest to where the view is, so a fit across the antimeridian turns the short way.
        var from = View(now);
        var nearX = probe.CenterX;
        while (nearX - from.CenterX > .5) nearX -= 1;
        while (from.CenterX - nearX > .5) nearX += 1;
        if (!animated || !motion.CanAnimate)
        {
            glide = Glide.Rest;
            centerX = nearX;
            centerY = probe.CenterY;
            zoom = probe.Zoom;
            InvalidateVisual();
            return;
        }
        var calm = Calm;
        if (glide == Glide.Fly)
        {
            cxSpring = cxSpring.Retarget(nearX, now, calm);
            cySpring = cySpring.Retarget(probe.CenterY, now, calm);
            zoomSpring = zoomSpring.Retarget(probe.Zoom, now, calm);
        }
        else
        {
            Settle(now);
            cxSpring = new NthSpring(centerX, 0, nearX, now, calm);
            cySpring = new NthSpring(centerY, 0, probe.CenterY, now, calm);
            zoomSpring = new NthSpring(zoom, 0, probe.Zoom, now, calm);
            glide = Glide.Fly;
        }
        Wake();
    }

    /// <summary>Frames the stops (or the `zoom` the line asked for) the way the map opens.</summary>
    private void Frame(bool animated)
    {
        var size = Bounds.Size.Width > 0 ? Bounds.Size : framedFor;
        if (size.Width <= 0 || size.Height <= 0) return;
        framedFor = size;
        var target = TargetFor(size);
        FlyTo(target.CenterX, target.CenterY, target.Zoom, animated);
        framed = true;
    }

    private NthUiMapCamera TargetFor(Size size)
    {
        if (model.Bounds() is not { } box)
        {
            var world = new NthUiMapCamera { Viewport = size, CenterX = .5, CenterY = .45, Zoom = initialZoom ?? Math.Log2(Math.Max(1, size.Width) / NthUiMapProjection.TileSize) };
            world.Clamp(MaxZoom);
            return world;
        }
        var fit = NthUiMapCamera.Fit(box, size, FitPadding, Math.Min(FitMaxZoom, MaxZoom));
        if (initialZoom is { } asked)
        {
            // The line named a zoom: that zoom, centred on the stops' box, but never so deep that a stop falls out of view (a single
            // stop has nothing else to keep in view, so it takes any zoom).
            var alone = box.Width < 1e-6 && box.Height < 1e-6;
            fit.Zoom = Math.Min(asked, alone ? MaxZoom : fit.Zoom);
            fit.Clamp(MaxZoom);
        }
        return fit;
    }

    /// <summary>Back to the framing of the stops; the map follows new stops again.</summary>
    public void FitAll()
    {
        userMoved = false;
        Frame(animated: true);
    }

    public void ZoomIn() => ZoomBy(KeyStep, null);

    public void ZoomOut() => ZoomBy(-KeyStep, null);

    /// <summary>Zooms by <paramref name="delta"/> (log2) about a point of the view (the middle by default); repeated calls add up.</summary>
    public void ZoomBy(double delta, Point? about)
    {
        var size = Bounds.Size;
        if (size.Width <= 0) return;
        userMoved = true;
        var now = NthUiMotion.Now;
        var anchor = about ?? new Point(size.Width / 2, size.Height / 2);
        var current = View(now);
        var wanted = Math.Clamp((glide == Glide.Rest ? current.Zoom : zoomSpring.Target) + delta, current.MinZoom, Math.Max(current.MinZoom, MaxZoom));
        if (!motion.CanAnimate)
        {
            Settle(now);
            var cam = View(now);
            cam.ZoomAbout(anchor, wanted, MaxZoom);
            centerX = cam.CenterX;
            centerY = cam.CenterY;
            zoom = cam.Zoom;
            InvalidateVisual();
            return;
        }
        var calm = Calm;
        if (glide == Glide.Anchored && Distance(anchor, anchorScreen) < 24)
        {
            zoomSpring = zoomSpring.Retarget(wanted, now, calm);
        }
        else
        {
            Settle(now);
            var cam = View(now);
            anchorScreen = anchor;
            anchorUnit = cam.ToUnit(anchor);
            zoomSpring = new NthSpring(cam.Zoom, 0, wanted, now, calm);
            glide = Glide.Anchored;
        }
        Wake();
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Moves the view by <paramref name="pixels"/> (the map follows the pointer) at once.</summary>
    public void PanBy(Vector pixels)
    {
        userMoved = true;
        var now = NthUiMotion.Now;
        Settle(now);
        var cam = View(now);
        cam.Pan(pixels);
        cam.Clamp(MaxZoom);
        centerX = cam.CenterX;
        centerY = cam.CenterY;
        zoom = cam.Zoom;
        InvalidateVisual();
    }

    /// <summary>Glides the map by <paramref name="pixels"/> as a drag would move it (the keyboard's arrows: Left looks left, so the map moves right).</summary>
    public void GlideBy(Vector pixels)
    {
        userMoved = true;
        var now = NthUiMotion.Now;
        var target = glide == Glide.Fly ? TargetCamera : View(now);
        if (glide == Glide.Anchored) target = TargetCamera;
        target.Pan(pixels);
        FlyTo(target.CenterX, target.CenterY, target.Zoom, animated: true);
    }

    // ---- layout ------------------------------------------------------------------------------------------------------

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 480 : availableSize.Width;
        return new Size(width, Math.Clamp(width * .56, 200, 360));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (finalSize != framedFor && finalSize.Width > 0)
        {
            var first = framedFor.Width <= 0;
            framedFor = finalSize;
            // A map that has not been moved follows its size 1:1 (a column being dragged wider); one the reader moved keeps its place.
            if (!userMoved) Frame(animated: false);
            else if (!first) Settle(NthUiMotion.Now);
        }
        return finalSize;
    }

    // ---- motion --------------------------------------------------------------------------------------------------------

    private void Wake() => motion.Start(stepper);

    private bool Advance(double now)
    {
        var dt = double.IsNaN(lastStep) ? 1 / 60.0 : Math.Clamp(now - lastStep, 0, .1);
        lastStep = now;
        var busy = false;
        if (glide != Glide.Rest)
        {
            var world = Math.Max(1, View(now).World);
            var done = glide == Glide.Fly
                ? PixelsSettled(cxSpring, world, now) && PixelsSettled(cySpring, world, now) && zoomSpring.Settled(now)
                : zoomSpring.Settled(now);
            if (done)
            {
                var target = TargetCamera;
                glide = Glide.Rest;
                centerX = target.CenterX;
                centerY = target.CenterY;
                zoom = target.Zoom;
            }
            else busy = true;
        }
        foreach (var key in arrivals.Keys.ToList())
        {
            if (!arrivals[key].Settled(now)) busy = true;
        }
        if (!routeFade.Settled(now)) busy = true;
        foreach (var key in labelAlpha.Keys.ToList())
        {
            var target = plannedKeys.Contains(key) ? 1.0 : 0.0;
            var alpha = labelAlpha[key];
            if (Math.Abs(alpha - target) < .02) alpha = target;
            else
            {
                alpha += (target - alpha) * (1 - Math.Exp(-dt * 14));
                busy = true;
            }
            if (alpha <= 0 && target == 0) labelAlpha.Remove(key);
            else labelAlpha[key] = alpha;
        }
        InvalidateVisual();
        return busy;
    }

    private static bool PixelsSettled(NthSpring spring, double world, double now) =>
        spring.Calm ? spring.Settled(now) : Math.Abs(spring.Value(now) - spring.Target) * world < .2 && Math.Abs(spring.VelocityAt(now)) * world < 2;

    private sealed class Stepper(NthUiMapCanvas owner) : INthUiAnimated
    {
        public bool Step(double now) => owner.Advance(now);
    }

    // ---- input ---------------------------------------------------------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        Focus(NavigationMethod.Pointer);
        focusRing = false;
        if (e.ClickCount == 2)
        {
            ZoomBy(KeyStep, point.Position);
            e.Handled = true;
            return;
        }
        dragging = true;
        dragMoved = false;
        dragFrom = lastPointer = point.Position;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (dragging)
        {
            if (!dragMoved && Distance(position, dragFrom) < 3) return;
            dragMoved = true;
            PanBy(position - lastPointer);
            lastPointer = position;
            e.Handled = true;
            return;
        }
        SetHover(HitStop(position));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!dragging) return;
        dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!dragging) SetHover(-1);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        dragging = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        // Without Ctrl the wheel belongs to the thread, which scrolls under the pointer.
        if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
        ZoomBy(e.Delta.Y * WheelStep, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        focusRing = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        focusRing = false;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? .6 : .2;
        var size = Bounds.Size;
        switch (e.Key)
        {
            case Key.Left:
                GlideBy(new Vector(size.Width * step, 0));
                break;
            case Key.Right:
                GlideBy(new Vector(-size.Width * step, 0));
                break;
            case Key.Up:
                GlideBy(new Vector(0, size.Height * step));
                break;
            case Key.Down:
                GlideBy(new Vector(0, -size.Height * step));
                break;
            case Key.OemPlus or Key.Add:
                ZoomIn();
                break;
            case Key.OemMinus or Key.Subtract:
                ZoomOut();
                break;
            case Key.Home or Key.D0 or Key.NumPad0:
                FitAll();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private int HitStop(Point position)
    {
        var cam = View(NthUiMotion.Now);
        for (var i = model.Stops.Count - 1; i >= 0; i--)
        {
            var unit = model.Stops[i].Unit;
            foreach (var k in Copies(cam, unit.X, unit.X))
            {
                if (Distance(cam.ToScreen(unit.X + k, unit.Y), position) <= StopRadius + 5) return i;
            }
        }
        return -1;
    }

    private void SetHover(int index)
    {
        if (hovered == index) return;
        hovered = index;
        Cursor = new Cursor(index >= 0 ? StandardCursorType.Hand : StandardCursorType.Arrow);
        InvalidateVisual();
    }

    /// <summary>Where a stop is on screen now (tests).</summary>
    public Point StopPosition(int index)
    {
        var cam = View(NthUiMotion.Now);
        var unit = model.Stops[index].Unit;
        var k = Copies(cam, unit.X, unit.X).FirstOrDefault();
        return cam.ToScreen(unit.X + k, unit.Y);
    }

    // ---- drawing -------------------------------------------------------------------------------------------------------

    /// <summary>The world offsets (whole turns) at which something spanning x from <paramref name="x0"/> to <paramref name="x1"/> shows.</summary>
    private static IEnumerable<int> Copies(NthUiMapCamera cam, double x0, double x1)
    {
        var visible = cam.VisibleUnits();
        var first = (int)Math.Floor(visible.Left - x1) + 1;
        var last = (int)Math.Ceiling(visible.Right - x0) - 1;
        for (var k = first; k <= last && k - first < 8; k++) yield return k;
    }

    private sealed record Palette(Color Ocean, Color Land, Color Border, Color Lake, Color City, Color Country, Color StopLabel, Color Pin, Color Halo, Color Route, Color Rim, Color Pill, Color Accent);

    private Palette PaletteFor()
    {
        var theme = ActualThemeVariant;
        var thread = NthUiTokens.Backdrop.Resolve(theme);
        var ocean = NthUiTokens.Over(NthUiTokens.Well.Resolve(theme), thread);
        var dark = theme == Avalonia.Styling.ThemeVariant.Dark;
        var land = dark ? NthUiTokens.Over(Color.FromArgb(20, 255, 255, 255), ocean) : Color.FromRgb(0xFF, 0xFF, 0xFF);
        var border = dark ? NthUiTokens.Over(Color.FromArgb(46, 255, 255, 255), land) : NthUiTokens.Over(Color.FromArgb(46, 0, 0, 0), land);
        return new Palette(ocean, land, border, ocean, NthUiTokens.Text2.Resolve(theme), NthUiTokens.Text3.Resolve(theme), NthUiTokens.Text1.Resolve(theme),
            NthUiTokens.Strong.Resolve(theme), thread, NthUiTokens.Text1.Resolve(theme), NthUiTokens.Over(NthUiTokens.CardRim.Resolve(theme), thread),
            Color.FromArgb(205, thread.R, thread.G, thread.B), NthUiTokens.Accent.Resolve(theme));
    }

    /// <summary>The colours the map draws with in the current theme (the ocean, the land, the label colours): tests check their contrast.</summary>
    public (Color Ocean, Color Land, Color City, Color Country, Color StopLabel, Color Pill) Colours()
    {
        var p = PaletteFor();
        return (p.Ocean, p.Land, p.City, p.Country, p.StopLabel, NthUiTokens.Over(p.Pill, p.Ocean));
    }

    private FormattedText Text(string text, NthUiMapLabelKind kind, Color color)
    {
        var key = (text, (int)kind);
        if (!texts.TryGetValue(key, out var formatted))
        {
            var family = NthUiTokens.Body.Resolve().FontFamily;
            var (size, weight) = kind switch
            {
                NthUiMapLabelKind.Stop => (12.5, FontWeight.SemiBold),
                NthUiMapLabelKind.Country => (10.5, FontWeight.Medium),
                _ => (11.0, FontWeight.Medium),
            };
            formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyle.Normal, weight), size, new ImmutableSolidColorBrush(color));
            if (texts.Count > 2000) texts.Clear();
            texts[key] = formatted;
        }
        formatted.SetForegroundBrush(new ImmutableSolidColorBrush(color));
        return formatted;
    }

    /// <summary>A stop's label: its name, and its detail under it in a smaller line.</summary>
    private Size MeasureStop(NthUiMapStop stop)
    {
        var name = Measure(stop.Label, NthUiMapLabelKind.Stop);
        if (stop.Detail is not { Length: > 0 } detail) return name;
        var small = Measure(detail, NthUiMapLabelKind.City);
        return new Size(Math.Max(name.Width, small.Width + 10), name.Height + small.Height - 3);
    }

    private Size Measure(string text, NthUiMapLabelKind kind)
    {
        var formatted = Text(text, kind, Colors.Black);
        return new Size(Math.Ceiling(formatted.WidthIncludingTrailingWhitespace) + (kind == NthUiMapLabelKind.Stop ? 10 : 0), Math.Ceiling(formatted.Height) + (kind == NthUiMapLabelKind.Stop ? 4 : 0));
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 1 || size.Height <= 1) return;
        var now = NthUiMotion.Now;
        var palette = PaletteFor();
        var cam = View(now);
        var shape = new RoundedRect(new Rect(size), Radius);
        using var clip = context.PushClip(shape);
        context.DrawRectangle(new ImmutableSolidColorBrush(palette.Ocean), null, new Rect(size));
        DrawBase(context, cam, palette);
        DrawTiles(context, cam, palette);
        DrawRoutes(context, cam, palette, now);
        Plan(cam);
        DrawLabels(context, palette);
        DrawStops(context, cam, palette, now);
        if (focusRing) context.DrawRectangle(null, new ImmutablePen(new ImmutableSolidColorBrush(palette.Accent), 2), new RoundedRect(new Rect(size).Deflate(1), Radius - 1));
        else context.DrawRectangle(null, new ImmutablePen(new ImmutableSolidColorBrush(palette.Rim), 1), new RoundedRect(new Rect(size).Deflate(.5), Radius - .5));
    }

    private void DrawBase(DrawingContext context, NthUiMapCamera cam, Palette palette)
    {
        var data = NthUiMapData.Shared;
        var level = Math.Clamp((int)Math.Floor(cam.Zoom), 0, NthUiMapPolygon.FullLevel);
        var scale = Math.Pow(2, cam.Zoom - level);
        var world = cam.World;
        var visible = cam.VisibleUnits().Inflate(.01);
        var land = new ImmutableSolidColorBrush(palette.Land);
        var lakes = new ImmutableSolidColorBrush(palette.Lake);
        var borderPen = new ImmutablePen(new ImmutableSolidColorBrush(palette.Border), .8 / scale, lineJoin: PenLineJoin.Round);
        foreach (var k in Copies(cam, 0, 1))
        {
            var origin = cam.ToScreen(k, 0);
            using var moved = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y));
            foreach (var country in data.Countries)
            {
                foreach (var polygon in country.Polygons)
                {
                    if (!Visible(polygon, k, visible, world)) continue;
                    context.DrawGeometry(land, borderPen, polygon.GeometryAt(level));
                }
            }
            foreach (var lake in data.Lakes)
            {
                if (!double.IsNaN(lake.MinZoom) && cam.Zoom + 1.5 < lake.MinZoom) continue;
                foreach (var polygon in lake.Polygons)
                {
                    if (!Visible(polygon, k, visible, world)) continue;
                    context.DrawGeometry(lakes, borderPen, polygon.GeometryAt(level));
                }
            }
        }
    }

    private static bool Visible(NthUiMapPolygon polygon, int copy, Rect visible, double world)
    {
        var b = polygon.Bounds;
        if (b.Width * world < .7 && b.Height * world < .7) return false;
        return b.X + copy < visible.Right && b.Right + copy > visible.Left && b.Y < visible.Bottom && b.Bottom > visible.Top;
    }

    private void DrawTiles(DrawingContext context, NthUiMapCamera cam, Palette palette)
    {
        if (!NthUiMapTiles.IsBound) return;
        var tiles = NthUiMapTiles.Source.Current;
        var z = Math.Clamp((int)Math.Round(cam.Zoom), 0, Math.Min(19, tiles.MaxZoom));
        var n = 1 << z;
        var tileSize = NthUiMapProjection.TileSize * Math.Pow(2, cam.Zoom - z);
        var visible = cam.VisibleUnits();
        var x0 = (int)Math.Floor(visible.Left * n);
        var x1 = (int)Math.Floor(visible.Right * n);
        var y0 = Math.Max(0, (int)Math.Floor(visible.Top * n));
        var y1 = Math.Min(n - 1, (int)Math.Floor(visible.Bottom * n));
        var drawn = false;
        for (var ty = y0; ty <= y1 && ty - y0 < 12; ty++)
        {
            for (var tx = x0; tx <= x1 && tx - x0 < 12; tx++)
            {
                if (tiles.Tile(z, ((tx % n) + n) % n, ty) is not { } image) continue;
                var topLeft = cam.ToScreen((double)tx / n, (double)ty / n);
                context.DrawImage(image, new Rect(image.Size), new Rect(topLeft, new Size(tileSize + .5, tileSize + .5)));
                drawn = true;
            }
        }
        if (drawn && tiles.Attribution.Length > 0)
        {
            var text = new FormattedText(tiles.Attribution, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(NthUiTokens.Body.Resolve().FontFamily), 9.5, new ImmutableSolidColorBrush(palette.StopLabel));
            var box = new Rect(4, Bounds.Height - text.Height - 6, text.Width + 8, text.Height + 2);
            context.DrawRectangle(new ImmutableSolidColorBrush(palette.Pill), null, new RoundedRect(box, 4));
            context.DrawText(text, new Point(box.X + 4, box.Y + 1));
        }
    }

    private void DrawRoutes(DrawingContext context, NthUiMapCamera cam, Palette palette, double now)
    {
        var fade = Math.Clamp(routeFade.Value(now), 0, 1);
        if (fade <= .01) return;
        foreach (var path in model.Paths)
        {
            if (path.Points.Count < 2) continue;
            var minX = path.Points.Min(p => p.X);
            var maxX = path.Points.Max(p => p.X);
            foreach (var k in Copies(cam, minX, maxX))
            {
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(cam.ToScreen(path.Points[0].X + k, path.Points[0].Y), false);
                    for (var i = 1; i < path.Points.Count; i++) g.LineTo(cam.ToScreen(path.Points[i].X + k, path.Points[i].Y));
                    g.EndFigure(false);
                }
                var halo = Color.FromArgb((byte)(130 * fade), palette.Halo.R, palette.Halo.G, palette.Halo.B);
                var ink = Color.FromArgb((byte)(225 * fade), palette.Route.R, palette.Route.G, palette.Route.B);
                context.DrawGeometry(null, new ImmutablePen(new ImmutableSolidColorBrush(halo), 5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
                context.DrawGeometry(null, new ImmutablePen(new ImmutableSolidColorBrush(ink), 2.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
            }
        }
    }

    // ---- labels ----------------------------------------------------------------------------------------------------------

    private void Plan(NthUiMapCamera cam)
    {
        var data = NthUiMapData.Shared;
        var candidates = new List<NthUiMapLabelCandidate>();
        var view = new Rect(Bounds.Size);
        for (var i = 0; i < model.Stops.Count; i++)
        {
            var stop = model.Stops[i];
            var unit = stop.Unit;
            foreach (var k in Copies(cam, unit.X, unit.X).Take(1))
            {
                var anchor = cam.ToScreen(unit.X + k, unit.Y);
                if (!view.Inflate(40).Contains(anchor)) continue;
                candidates.Add(new NthUiMapLabelCandidate("stop:" + stop.Key, NthUiMapLabelKind.Stop, stop.Label, anchor, MeasureStop(stop), 1000 - i, StopRadius));
            }
        }
        foreach (var country in data.Countries)
        {
            if (!NthUiMapLabelPlanner.CountryZoom(cam.Zoom, country.MinLabel, country.MaxLabel)) continue;
            var onScreen = country.Bounds.Width * cam.World;
            var text = Measure(country.Name, NthUiMapLabelKind.Country);
            if (onScreen < text.Width * 1.15 && country.LabelRank > 2) continue;
            foreach (var k in Copies(cam, country.LabelX, country.LabelX).Take(1))
            {
                var anchor = cam.ToScreen(country.LabelX + k, country.LabelY);
                if (!view.Contains(anchor)) continue;
                candidates.Add(new NthUiMapLabelCandidate("country:" + country.Name, NthUiMapLabelKind.Country, country.Name, anchor, text, 500 - country.LabelRank * 10 + Math.Log10(Math.Max(1, country.Population)), 0));
            }
        }
        foreach (var place in data.Places)
        {
            if (cam.Zoom < NthUiMapLabelPlanner.CityZoom(place.Rank)) continue;
            foreach (var k in Copies(cam, place.X, place.X).Take(1))
            {
                var anchor = cam.ToScreen(place.X + k, place.Y);
                if (!view.Contains(anchor)) continue;
                candidates.Add(new NthUiMapLabelCandidate("city:" + place.Rank.ToString(CultureInfo.InvariantCulture), NthUiMapLabelKind.City, place.Name, anchor, Measure(place.Name, NthUiMapLabelKind.City), 300 - place.Rank * .5, CityRadius));
            }
        }
        placed = NthUiMapLabelPlanner.Place(candidates, view.Deflate(2), RouteObstacles(cam));
        plannedKeys = [.. placed.Select(p => p.Key)];
        var fresh = false;
        foreach (var label in placed)
        {
            if (labelAlpha.ContainsKey(label.Key)) continue;
            labelAlpha[label.Key] = motion.CanAnimate ? 0 : 1;
            fresh = true;
        }
        if (!motion.CanAnimate)
        {
            foreach (var key in labelAlpha.Keys.ToList()) labelAlpha[key] = plannedKeys.Contains(key) ? 1 : 0;
        }
        else if (fresh || labelAlpha.Any(a => plannedKeys.Contains(a.Key) ? a.Value < 1 : a.Value > 0)) Wake();
    }

    /// <summary>Small boxes along every route's line on screen, which the labels keep off.</summary>
    private List<Rect> RouteObstacles(NthUiMapCamera cam)
    {
        var boxes = new List<Rect>();
        if (routeFade.Target < .5) return boxes;
        var view = new Rect(Bounds.Size).Inflate(30);
        foreach (var path in model.Paths)
        {
            if (path.Points.Count < 2) continue;
            var minX = path.Points.Min(p => p.X);
            var maxX = path.Points.Max(p => p.X);
            foreach (var k in Copies(cam, minX, maxX))
            {
                var previous = cam.ToScreen(path.Points[0].X + k, path.Points[0].Y);
                for (var i = 1; i < path.Points.Count && boxes.Count < 600; i++)
                {
                    var next = cam.ToScreen(path.Points[i].X + k, path.Points[i].Y);
                    var length = Distance(previous, next);
                    var steps = Math.Max(1, (int)Math.Ceiling(length / 7));
                    for (var s = 0; s <= steps; s++)
                    {
                        var at = new Point(previous.X + (next.X - previous.X) * s / steps, previous.Y + (next.Y - previous.Y) * s / steps);
                        if (view.Contains(at)) boxes.Add(new Rect(at.X - 3.5, at.Y - 3.5, 7, 7));
                    }
                    previous = next;
                }
            }
        }
        return boxes;
    }

    private void DrawLabels(DrawingContext context, Palette palette)
    {
        // Every label but the stops' (those draw with their pins, last).
        foreach (var label in placed)
        {
            if (label.Kind == NthUiMapLabelKind.Stop) continue;
            var alpha = labelAlpha.GetValueOrDefault(label.Key, 1);
            if (alpha <= .02) continue;
            var color = label.Kind == NthUiMapLabelKind.Country ? palette.Country : palette.City;
            var faded = Color.FromArgb((byte)Math.Round(color.A * alpha), color.R, color.G, color.B);
            if (label.Kind == NthUiMapLabelKind.City)
                context.DrawEllipse(new ImmutableSolidColorBrush(faded), null, label.Anchor, CityRadius, CityRadius);
            context.DrawText(Text(label.Text, label.Kind, faded), label.Box.TopLeft);
        }
    }

    private void DrawStops(DrawingContext context, NthUiMapCamera cam, Palette palette, double now)
    {
        // Stops draw last, over every label but their own pills.
        for (var i = 0; i < model.Stops.Count; i++)
        {
            var stop = model.Stops[i];
            var arrival = arrivals.TryGetValue(stop.Key, out var tween) ? Math.Clamp(tween.Value(now), 0, 1) : 1;
            if (arrival <= .01) continue;
            var unit = stop.Unit;
            foreach (var k in Copies(cam, unit.X, unit.X))
            {
                var at = cam.ToScreen(unit.X + k, unit.Y);
                if (!new Rect(Bounds.Size).Inflate(30).Contains(at)) continue;
                var r = StopRadius * arrival;
                if (hovered == i)
                    context.DrawEllipse(null, new ImmutablePen(new ImmutableSolidColorBrush(palette.Accent), 2), at, r + 4, r + 4);
                context.DrawEllipse(new ImmutableSolidColorBrush(palette.Pin), new ImmutablePen(new ImmutableSolidColorBrush(palette.Halo), 2.2), at, r, r);
            }
        }
        foreach (var label in placed)
        {
            if (label.Kind != NthUiMapLabelKind.Stop) continue;
            var key = label.Key["stop:".Length..];
            var arrival = arrivals.TryGetValue(key, out var tween) ? Math.Clamp(tween.Value(now), 0, 1) : 1;
            if (arrival <= .01) continue;
            var pill = Color.FromArgb((byte)Math.Round(palette.Pill.A * arrival), palette.Pill.R, palette.Pill.G, palette.Pill.B);
            var ink = Color.FromArgb((byte)Math.Round(palette.StopLabel.A * arrival), palette.StopLabel.R, palette.StopLabel.G, palette.StopLabel.B);
            context.DrawRectangle(new ImmutableSolidColorBrush(pill), null, new RoundedRect(label.Box, 6));
            var main = Text(label.Text, NthUiMapLabelKind.Stop, ink);
            context.DrawText(main, new Point(label.Box.X + 5, label.Box.Y + 2));
            if (model.Stops.FirstOrDefault(s => s.Key == key)?.Detail is { Length: > 0 } detail)
            {
                var second = Color.FromArgb((byte)Math.Round(palette.City.A * arrival), palette.City.R, palette.City.G, palette.City.B);
                context.DrawText(Text(detail, NthUiMapLabelKind.City, second), new Point(label.Box.X + 5, label.Box.Y + 2 + Math.Ceiling(main.Height) - 3));
            }
        }
    }
}
