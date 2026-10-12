// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.5 "The Avalonia port", "Render budget", "Hover", "Reduce motion"; ED-NEW-dextra-as8-1,
// ED-NEW-dextra-as8-2). No Swift twin.
// ArmillaMark, the one control every place that shows Dextra uses. It is a fixed square slot that never changes size; a
// composition custom visual draws it with Skia on the compositor's render thread, so the UI thread does no per-frame work. The
// control sends the visual a new look only when its state, glyph, Asturs, size, theme or motion setting changes; the visual's
// handler springs the mark on the shared clock (ArmillaHub), at the 60 Hz slots every ambient source shares, at 30 fps once it
// has rested, and stops asking for frames when nothing moves, when it is not drawn (off screen, a hidden or minimised window)
// or when its window has been without focus for 30 s. The mark is out of the accessibility tree; its hover shows the current
// action, which every place also shows as visible text.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Marrow.Core.Port;
using SkiaSharp;

namespace Marrow.UI.Port.Armilla;

public sealed class ArmillaMark : Control
{
    public static readonly StyledProperty<double> MarkSizeProperty = AvaloniaProperty.Register<ArmillaMark, double>(nameof(MarkSize), 24);
    public static readonly StyledProperty<ArmillaState> StateProperty = AvaloniaProperty.Register<ArmillaMark, ArmillaState>(nameof(State));
    public static readonly StyledProperty<ArmillaGlyph> GlyphProperty = AvaloniaProperty.Register<ArmillaMark, ArmillaGlyph>(nameof(Glyph));
    public static readonly StyledProperty<ArmillaKind> KindProperty = AvaloniaProperty.Register<ArmillaMark, ArmillaKind>(nameof(Kind), ArmillaKind.Row);
    public static readonly StyledProperty<IReadOnlyList<ArmillaNode>> NodesProperty =
        AvaloniaProperty.Register<ArmillaMark, IReadOnlyList<ArmillaNode>>(nameof(Nodes), []);
    public static readonly StyledProperty<bool> CalmProperty = AvaloniaProperty.Register<ArmillaMark, bool>(nameof(Calm));
    public static readonly StyledProperty<bool> MonoProperty = AvaloniaProperty.Register<ArmillaMark, bool>(nameof(Mono));
    public static readonly StyledProperty<string?> ActivityProperty = AvaloniaProperty.Register<ArmillaMark, string?>(nameof(Activity));

    private CompositionCustomVisual? visual;
    private ArmillaHandler? handler;
    private ArmillaLook? sent;
    private bool? sentVisible;
    private bool? sentSleep;
    private ArmillaWindowWatch? watch;
    private bool lastMoves;

    public ArmillaMark()
    {
        Focusable = false;
        ClipToBounds = false;
        // The mark is a picture: hidden from assistive technology, its meaning is the visible text beside it (2.5 "Hover").
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        AttachedToVisualTree += (_, _) => Install();
        DetachedFromVisualTree += (_, _) => Remove();
        ActualThemeVariantChanged += (_, _) => Send();
    }

    /// <summary>The square slot's side (px). It never changes with the state.</summary>
    public double MarkSize { get => GetValue(MarkSizeProperty); set => SetValue(MarkSizeProperty, value); }

    public ArmillaState State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }

    public ArmillaGlyph Glyph { get => GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }

    public ArmillaKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    /// <summary>Dextra's Asturs on the orbit (the live presence at 48 px and up; the busy band below).</summary>
    public IReadOnlyList<ArmillaNode> Nodes { get => GetValue(NodesProperty); set => SetValue(NodesProperty, value); }

    /// <summary>The motion budget stilled this mark's list: it shows its still pose (2.5 "Motion budget").</summary>
    public bool Calm { get => GetValue(CalmProperty); set => SetValue(CalmProperty, value); }

    public bool Mono { get => GetValue(MonoProperty); set => SetValue(MonoProperty, value); }

    /// <summary>What the mark's work is doing now; the hover tip shows it (2.5 "Hover").</summary>
    public string? Activity { get => GetValue(ActivityProperty); set => SetValue(ActivityProperty, value); }

    /// <summary>Whether this mark moves now, by its state and size (what the motion budget counts), whatever Calm says.</summary>
    public bool Moves => ArmillaMotionBudget.Moves(Look with { Reduce = false }, Kind);

    /// <summary>Raised when <see cref="Moves"/> changes, so the motion budget can be given out again.</summary>
    public event Action<ArmillaMark>? MovesChanged;

    /// <summary>The look this mark shows now.</summary>
    public ArmillaLook Look => new()
    {
        Size = MarkSize,
        State = State,
        Glyph = Glyph,
        Nodes = Kind == ArmillaKind.Live ? Nodes : [],
        Dark = PlainControls.IsDark(ActualThemeVariant),
        Mono = Mono,
        Rest = Kind != ArmillaKind.Live,
        Reduce = Calm || Kind == ArmillaKind.Still || EnvironmentValues.GetAccessibilityReduceMotion(this),
    };

    /// <summary>The look last sent to the render thread (tests).</summary>
    internal ArmillaLook? SentLook => sent;

    /// <summary>Frames the render thread has drawn (tests).</summary>
    internal int FramesDrawn => handler?.Frames ?? 0;

    /// <summary>How far the last drawing reached, as a fraction of the half-box (tests).</summary>
    internal double LastExtent => handler?.Extent ?? 0;

    /// <summary>Whether the render thread still asks for frames (tests).</summary>
    internal bool Ticking => handler?.Ticking ?? false;

    /// <summary>The voice level 0..1 (60 ms attack, 240 ms release on the mark), fed while listening or speaking.</summary>
    public void SetLevel(double level) => visual?.SendHandlerMessage(new ArmillaHandler.Level(level));

    protected override Size MeasureOverride(Size availableSize) => new(MarkSize, MarkSize);

    protected override Size ArrangeOverride(Size finalSize) => new(MarkSize, MarkSize);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkSizeProperty) InvalidateMeasure();
        if (change.Property == ActivityProperty) ToolTip.SetTip(this, string.IsNullOrWhiteSpace(Activity) ? null : Activity);
        if (change.Property == MarkSizeProperty || change.Property == StateProperty || change.Property == GlyphProperty || change.Property == KindProperty
            || change.Property == NodesProperty || change.Property == CalmProperty || change.Property == MonoProperty
            || change.Property == EnvironmentValues.AccessibilityReduceMotionProperty || change.Property == BoundsProperty)
        {
            Send();
            var moves = Moves;
            if (moves != lastMoves) { lastMoves = moves; MovesChanged?.Invoke(this); }
        }
        // a hidden ancestor, a clipped scroll position or an off-screen place is caught by the handler: a visual the compositor
        // does not draw stops asking for frames, and drawing it again restarts them
        if (change.Property == IsVisibleProperty) SendVisible(IsVisible);
    }

    private void Install()
    {
        lastMoves = Moves;
        watch = TopLevel.GetTopLevel(this) is { } top ? ArmillaWindowWatch.For(top) : null;
        if (watch is not null) watch.Changed += OnWatch;
        if (visual is not null || ElementComposition.GetElementVisual(this) is not { } host) return;
        handler = new ArmillaHandler(ArmillaHub.For(host.Compositor), AmbientRefreshPacer.For(host.Compositor), SwiftClock.Provider.Current);
        visual = host.Compositor.CreateCustomVisual(handler);
        ElementComposition.SetElementChildVisual(this, visual);
        sent = null; sentVisible = null; sentSleep = null;
        Send();
        SendVisible(IsVisible);
        OnWatch();
    }

    private void Remove()
    {
        if (watch is not null) watch.Changed -= OnWatch;
        watch = null;
        visual?.SendHandlerMessage(ArmillaHandler.Stop.Instance);
        ElementComposition.SetElementChildVisual(this, null);
        visual = null; handler = null; sent = null; sentVisible = null; sentSleep = null;
    }

    private void OnWatch()
    {
        var sleep = watch?.Sleeping ?? false;
        if (visual is null || sentSleep == sleep) return;
        sentSleep = sleep;
        visual.SendHandlerMessage(sleep ? ArmillaHandler.Sleep.On : ArmillaHandler.Sleep.Off);
    }

    private void SendVisible(bool shown)
    {
        if (visual is null || sentVisible == shown) return;
        sentVisible = shown;
        visual.SendHandlerMessage(shown ? ArmillaHandler.Shown.Yes : ArmillaHandler.Shown.No);
    }

    private void Send()
    {
        if (visual is null) return;
        var look = Look;
        if (look.Equals(sent)) return;
        sent = look;
        visual.Size = new Vector(look.Size, look.Size);
        visual.SendHandlerMessage(look);
    }
}

/// <summary>The render-thread side of one mark: its engine on the shared clock, drawn with Skia.</summary>
internal sealed class ArmillaHandler(ArmillaHub hub, AmbientRefreshPacer? pacer, TimeProvider clock) : CompositionCustomVisualHandler
{
    internal sealed class Stop
    {
        internal static readonly Stop Instance = new();
    }

    internal sealed record Level(double Value);

    internal sealed class Sleep
    {
        internal static readonly Sleep On = new(), Off = new();
    }

    internal sealed class Shown
    {
        internal static readonly Shown Yes = new(), No = new();
    }

    private readonly ArmillaSkiaCanvas canvas = new();
    private ArmillaEngine? engine;
    private bool requested, stopped, sleeping, hidden, drawn = true, more = true;
    private double lastTime = double.NaN;
    private int frames;
    private long extentBits;

    internal int Frames => Volatile.Read(ref frames);

    internal double Extent => BitConverter.Int64BitsToDouble(Interlocked.Read(ref extentBits));

    internal bool Ticking => Volatile.Read(ref requested);

    private double Now => clock.GetTimestamp() / (double)clock.TimestampFrequency;

    public override void OnMessage(object message)
    {
        switch (message)
        {
            case ArmillaLook look:
                if (engine is null) engine = new ArmillaEngine(look) { Visible = !hidden };
                else engine.Set(look);
                more = true;
                Invalidate();
                Request();
                break;
            case Level level:
                engine?.SetLevel(level.Value);
                more = true;
                Request();
                break;
            case Shown shown:
                hidden = shown == Shown.No;
                if (engine is not null) engine.Visible = !hidden;
                if (!hidden) { more = true; Invalidate(); Request(); }
                break;
            case Sleep sleep:
                sleeping = sleep == Sleep.On;
                if (!sleeping) { more = true; Request(); }
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
        if (stopped || sleeping || hidden || engine is not { } mark) return;
        var now = Now;
        // the 60 Hz slots every ambient source of this compositor shares (ED-NEW-frame-pacing): drawing is capped at 60 fps
        if (pacer is not null && !pacer.Beat(CompositionNow.Ticks, now)) { Register(); return; }
        var t = hub.Time(CompositionNow.Ticks, now);
        var dt = double.IsNaN(lastTime) ? 0 : Math.Clamp(t - lastTime, 0, ArmillaHub.MaxStep);
        lastTime = t;
        // a mark that has rested 1.2 s in Idle, Paused or Offline steps at 30 fps
        var step = mark.RestStep(dt);
        if (step == 0 && dt > 0) { Register(); return; }
        more = mark.Tick(step, t);
        Invalidate();
        if (drawn && more) Request();
    }

    private void Request()
    {
        if (Volatile.Read(ref requested) || stopped || sleeping || hidden) return;
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
        if (stopped || engine is not { } mark) return;
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
        if (more) Request();
    }
}

/// <summary>
/// Whether the marks of one window sleep (2.5 "Render budget"): after 30 s without focus every mark sleeps, and it wakes on focus;
/// a minimised or hidden window draws nothing. One per window; its timer runs on SwiftClock's provider.
/// </summary>
internal sealed class ArmillaWindowWatch
{
    /// <summary>How long a window may be without focus before its marks sleep.</summary>
    public static readonly TimeSpan SleepAfter = TimeSpan.FromSeconds(30);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TopLevel, ArmillaWindowWatch> watches = new();
    private readonly TopLevel top;
    private ITimer? timer;
    private bool unfocused, asleep, minimised;

    private ArmillaWindowWatch(TopLevel top)
    {
        this.top = top;
        if (top is Window window)
        {
            // Window.IsActive is the focus the rule names (AmbientWindowState's "available" is immediate and also reads cover
            // and foreground for the 20 s ambient rule; Armilla's rule is 30 s without focus, then sleep)
            window.PropertyChanged += (_, e) =>
            {
                if (e.Property == Window.IsActiveProperty) Focused(window.IsActive);
                else if (e.Property == Window.WindowStateProperty || e.Property == Visual.IsVisibleProperty) Away(window);
            };
            Away(window);
            if (!window.IsActive) Focused(false);
        }
    }

    private void Away(Window window)
    {
        var away = window.WindowState == WindowState.Minimized || !window.IsVisible;
        if (away == minimised) return;
        minimised = away;
        Raise();
    }

    public static ArmillaWindowWatch For(TopLevel top) => watches.GetValue(top, static t => new ArmillaWindowWatch(t));

    /// <summary>The marks of this window draw nothing now.</summary>
    public bool Sleeping => asleep || minimised;

    public event Action? Changed;

    /// <summary>The window gained or lost focus (Window.IsActive; tests call it).</summary>
    internal void Focused(bool focused)
    {
        timer?.Dispose();
        timer = null;
        unfocused = !focused;
        if (focused)
        {
            if (asleep) { asleep = false; Raise(); }
            return;
        }
        timer = SwiftClock.Provider.Current.CreateTimer(_ => Avalonia.Threading.Dispatcher.UIThread.Post(Doze), null, SleepAfter, Timeout.InfiniteTimeSpan);
    }

    private void Doze()
    {
        if (!unfocused || asleep) return;
        asleep = true;
        Raise();
    }

    private void Raise() => Changed?.Invoke();

    internal TopLevel Top => top;
}
