// PORT-ONLY: Dextra AS-17 (DESIGN.md rev 5, 2.5 "An Astur's own mark", "Render budget", "Hover", "Reduce motion"; ED-NEW-dextra-as17-2). No Swift twin.
// AsturMark, the control every place that shows an Astur uses (its sidebar row, its org-chart node at 30 px, its messages, its decision cards): a fixed square slot that
// never changes size, drawn with Skia on the compositor's render thread like Armilla itself. The UI thread sends a new look only when the shape, the state, the size, the
// theme or the motion setting changes; the handler springs the mark (on the 60 Hz slots every ambient source of the compositor shares) and stops asking for frames when it
// has settled, which is nearly always. It draws nothing off screen, in a hidden window, or in a window that has been without focus for 30 s (it takes its new pose at once
// instead). The mark is out of the accessibility tree; its hover shows the current action, which every place also shows as visible text.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Marrow.Core.Port;
using SkiaSharp;

namespace Marrow.UI.Port.Armilla;

public sealed class AsturMark : Control
{
    public static readonly StyledProperty<double> MarkSizeProperty = AvaloniaProperty.Register<AsturMark, double>(nameof(MarkSize), 30);
    public static readonly StyledProperty<ArmillaShape> ShapeProperty = AvaloniaProperty.Register<AsturMark, ArmillaShape>(nameof(Shape));
    public static readonly StyledProperty<AsturMarkState> StateProperty = AvaloniaProperty.Register<AsturMark, AsturMarkState>(nameof(State));
    public static readonly StyledProperty<bool> CalmProperty = AvaloniaProperty.Register<AsturMark, bool>(nameof(Calm));
    public static readonly StyledProperty<string?> ActivityProperty = AvaloniaProperty.Register<AsturMark, string?>(nameof(Activity));

    private CompositionCustomVisual? visual;
    private AsturMarkHandler? handler;
    private AsturMarkLook? sent;
    private bool? sentVisible;
    private bool? sentSleep;
    private ArmillaWindowWatch? watch;
    private DextraViewWatch? availability;

    public AsturMark()
    {
        Focusable = false;
        ClipToBounds = false;
        // The mark is a picture: hidden from assistive technology, its meaning is the visible text beside it (2.5 "Hover").
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        AttachedToVisualTree += (_, _) => Install();
        DetachedFromVisualTree += (_, _) => Remove();
        ActualThemeVariantChanged += (_, _) => Send();
    }

    /// <summary>The square slot's side (px). It never changes with the state. The org chart's node uses 30.</summary>
    public double MarkSize { get => GetValue(MarkSizeProperty); set => SetValue(MarkSizeProperty, value); }

    /// <summary>The Astur's look: one of the six node shapes, each with its own tint.</summary>
    public ArmillaShape Shape { get => GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }

    public AsturMarkState State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }

    /// <summary>The motion budget stilled this mark's list: it shows its still pose (2.5 "Motion budget").</summary>
    public bool Calm { get => GetValue(CalmProperty); set => SetValue(CalmProperty, value); }

    /// <summary>What the Astur is doing now; the hover tip shows it (2.5 "Hover").</summary>
    public string? Activity { get => GetValue(ActivityProperty); set => SetValue(ActivityProperty, value); }

    /// <summary>An Astur's mark never moves, so it never counts against the motion budget (2.5 "Motion budget").</summary>
    public bool Moves => false;

    /// <summary>The look this mark shows now.</summary>
    public AsturMarkLook Look => new()
    {
        Size = MarkSize,
        Shape = Shape,
        State = State,
        Dark = PlainControls.IsDark(ActualThemeVariant),
        Reduce = Calm || EnvironmentValues.GetAccessibilityReduceMotion(this),
    };

    /// <summary>The look last sent to the render thread (tests).</summary>
    internal AsturMarkLook? SentLook => sent;

    /// <summary>Frames the render thread has drawn (tests).</summary>
    internal int FramesDrawn => handler?.Frames ?? 0;

    /// <summary>How far the last drawing reached, as a fraction of the half-box (tests).</summary>
    internal double LastExtent => handler?.Extent ?? 0;

    /// <summary>Whether the render thread still asks for frames (tests).</summary>
    internal bool Ticking => handler?.Ticking ?? false;

    protected override Size MeasureOverride(Size availableSize) => new(MarkSize, MarkSize);

    protected override Size ArrangeOverride(Size finalSize) => new(MarkSize, MarkSize);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkSizeProperty) InvalidateMeasure();
        if (change.Property == ActivityProperty) ToolTip.SetTip(this, string.IsNullOrWhiteSpace(Activity) ? null : Activity);
        if (change.Property == MarkSizeProperty || change.Property == ShapeProperty || change.Property == StateProperty || change.Property == CalmProperty
            || change.Property == EnvironmentValues.AccessibilityReduceMotionProperty)
            Send();
        if (change.Property == IsVisibleProperty) SendVisible(IsEffectivelyVisible);
    }

    private void Install()
    {
        availability = new DextraViewWatch(this);
        availability.Changed += OnWatch;
        watch = TopLevel.GetTopLevel(this) is { } top ? ArmillaWindowWatch.Acquire(top) : null;
        if (watch is not null) watch.Changed += OnWatch;
        if (visual is not null || ElementComposition.GetElementVisual(this) is not { } host) return;
        handler = new AsturMarkHandler(ArmillaHub.For(host.Compositor), AmbientRefreshPacer.For(host.Compositor), SwiftClock.Provider.Current);
        visual = host.Compositor.CreateCustomVisual(handler);
        ElementComposition.SetElementChildVisual(this, visual);
        sent = null; sentVisible = null; sentSleep = null;
        Send();
        SendVisible(IsEffectivelyVisible);
        OnWatch();
    }

    private void Remove()
    {
        if (availability is not null) { availability.Changed -= OnWatch; availability.Dispose(); availability = null; }
        if (watch is not null) { watch.Changed -= OnWatch; watch.Release(); }
        watch = null;
        visual?.SendHandlerMessage(AsturMarkHandler.Stop.Instance);
        ElementComposition.SetElementChildVisual(this, null);
        visual = null; handler = null; sent = null; sentVisible = null; sentSleep = null;
    }

    private void OnWatch()
    {
        SendVisible(IsEffectivelyVisible);
        var sleep = (watch?.Sleeping ?? false) || availability?.Available == false;
        if (visual is null || sentSleep == sleep) return;
        sentSleep = sleep;
        visual.SendHandlerMessage(sleep ? AsturMarkHandler.Sleep.On : AsturMarkHandler.Sleep.Off);
    }

    private void SendVisible(bool shown)
    {
        if (visual is null || sentVisible == shown) return;
        sentVisible = shown;
        visual.SendHandlerMessage(shown ? AsturMarkHandler.Shown.Yes : AsturMarkHandler.Shown.No);
    }

    private void Send()
    {
        if (visual is null) return;
        // Avalonia says nothing when an ancestor is hidden, so the mark asks whenever it has something new to show: a change that comes while an
        // ancestor hides it takes its pose at once, and showing the mark again plays no spring
        SendVisible(IsEffectivelyVisible);
        var look = Look;
        if (look.Equals(sent)) return;
        sent = look;
        visual.Size = new Vector(look.Size, look.Size);
        visual.SendHandlerMessage(look);
    }
}

/// <summary>The render-thread side of one Astur's mark: its engine on the shared clock, drawn with Skia.</summary>
internal sealed class AsturMarkHandler(ArmillaHub hub, AmbientRefreshPacer? pacer, TimeProvider clock) : CompositionCustomVisualHandler
{
    internal sealed class Stop
    {
        internal static readonly Stop Instance = new();
    }

    internal sealed class Sleep
    {
        internal static readonly Sleep On = new(), Off = new();
    }

    internal sealed class Shown
    {
        internal static readonly Shown Yes = new(), No = new();
    }

    private readonly AsturSkiaCanvas canvas = new();
    private AsturMarkEngine? engine;
    private bool requested, stopped, sleeping, hidden, drawn = true, more = true;
    private double lastTime = double.NaN;
    private int frames;
    private long extentBits;

    internal int Frames => Volatile.Read(ref frames);

    internal double Extent => BitConverter.Int64BitsToDouble(Interlocked.Read(ref extentBits));

    internal bool Ticking => Volatile.Read(ref requested);

    private double Now => clock.GetTimestamp() / (double)clock.TimestampFrequency;

    // a mark nobody sees takes its pose at once: nothing springs behind another window or off screen
    private bool Unseen => hidden || sleeping;

    public override void OnMessage(object message)
    {
        switch (message)
        {
            case AsturMarkLook look:
                if (engine is null) engine = new AsturMarkEngine(look) { Visible = !Unseen };
                else
                {
                    engine.Visible = !Unseen;
                    engine.Set(look);
                }
                more = !engine.Settled;
                Invalidate();
                Request();
                break;
            case Shown shown:
                hidden = shown == Shown.No;
                if (engine is not null) engine.Visible = !Unseen;
                if (!hidden) { Invalidate(); Request(); }
                break;
            case Sleep sleep:
                sleeping = sleep == Sleep.On;
                if (engine is not null) engine.Visible = !Unseen;
                if (!sleeping) { Invalidate(); Request(); }
                break;
            case Stop:
                stopped = true;
                canvas.Dispose();
                break;
        }
    }

    public override Rect GetRenderBounds() => engine is { } e ? new Rect(0, 0, e.Look.Size, e.Look.Size) : default;

    public override void OnAnimationFrameUpdate()
    {
        Volatile.Write(ref requested, false);
        if (stopped || Unseen || engine is not { } mark) return;
        var now = Now;
        // the 60 Hz slots every ambient source of this compositor shares (ED-NEW-frame-pacing): drawing is capped at 60 fps
        if (pacer is not null && !pacer.Beat(CompositionNow.Ticks, now)) { Register(); return; }
        var t = hub.Time(CompositionNow.Ticks, now);
        var dt = double.IsNaN(lastTime) ? 0 : Math.Clamp(t - lastTime, 0, ArmillaHub.MaxStep);
        lastTime = t;
        more = mark.Tick(dt);
        Invalidate();
        if (drawn && more) Request();
    }

    // Always registers: a frame update asked for while the visual was hidden by an ancestor is never delivered, so a flag that says "already asked" would
    // leave the mark without frames when it is shown again (registering twice for one frame is harmless).
    private void Request()
    {
        if (stopped || Unseen) return;
        drawn = false;
        Register();
    }

    private void Register()
    {
        Volatile.Write(ref requested, true);
        RegisterForNextAnimationFrameUpdate();
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (stopped || sleeping || hidden || engine is not { } mark) return;
        drawn = true;
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
        using var lease = feature.Lease();
        var sk = lease.SkCanvas;
        var half = (float)(mark.Look.Size / 2);
        var saved = sk.Save();
        try
        {
            sk.Translate(half, half);
            canvas.Begin(sk);
            if (lease.CurrentOpacity < 1)
            {
                using var layer = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Round(Math.Clamp(lease.CurrentOpacity, 0, 1) * 255)) };
                sk.SaveLayer(layer);
            }
            mark.Draw(canvas);
        }
        finally { sk.RestoreToCount(saved); }
        Interlocked.Exchange(ref extentBits, BitConverter.DoubleToInt64Bits(mark.MaxExtent));
        Interlocked.Increment(ref frames);
        // a drawn frame with nothing left to move is the end of the run: whatever update was still asked for is a no-op
        if (more) Request();
        else Volatile.Write(ref requested, false);
    }
}
