// PORT-ONLY: OD-0136 amendment 2 (ED-NEW-nth-motion). The one render-thread host every nth primitive draws through, built
// like AuroraLoader (OD-0113): the control sends an immutable frame (size, theme, calm, clock, its state) to a composition
// custom visual only when something changes; the visual's handler draws on the compositor's frame clock at the 60 Hz slots
// every ambient source shares (AmbientRefreshPacer, ED-NEW-frame-pacing), asks for frames only while its painter says
// something moves, and stops asking when it is not drawn (a hidden or scrolled-away primitive costs nothing until it is drawn
// again). Painters keep their paints and paths: nothing is allocated per frame. ED-NEW-lean-6: while the window cannot be seen
// (WindowPresence) the handler asks for no frames at all; seen again, its next frame is the painter at the current time (a
// painter is a pure function of the frame and the time, so every loop resumes in phase).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Marrow.Core.Port;
using SkiaSharp;

namespace Marrow.UI.Port.Nth;

/// <summary>Everything the render thread needs for one primitive, as one immutable value. Times are clock seconds.</summary>
public sealed record NthFrame(Size Size, bool Dark, bool Calm, TimeProvider Clock, double Started, object? State)
{
    public NthPalette Palette => NthPalette.For(Dark);
    public static double Seconds(TimeProvider clock) => clock.GetTimestamp() / (double)clock.TimestampFrequency;
}

/// <summary>Draws one kind of primitive on the render thread (or into any canvas, for tests and the GPU warm-up).</summary>
public abstract class NthPainter : IDisposable
{
    /// <summary>Whether the primitive still moves at <paramref name="now"/> (clock seconds): frames are asked for only then.</summary>
    public abstract bool Moving(NthFrame frame, double now);

    /// <summary>The area drawn, in the control's coordinates (a disc's overshoot may pass the control's edge).</summary>
    public virtual Rect Bounds(NthFrame frame) => new(frame.Size);

    internal abstract void Render(ImmediateDrawingContext context, NthFrame frame, double now);

    public virtual void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>A painter that draws with SkiaSharp through the compositor's lease.</summary>
public abstract class NthSkiaPainter : NthPainter
{
    private readonly SKPaint layer = new();

    /// <summary>Draw the primitive at <paramref name="now"/> (clock seconds) into <paramref name="canvas"/>, in the control's coordinates.</summary>
    public abstract void Draw(SKCanvas canvas, NthFrame frame, double now);

    internal sealed override void Render(ImmediateDrawingContext context, NthFrame frame, double now)
    {
        if (AuroraSkiaLease.TryBorrow(context, out var direct))
        {
            using (direct) Paint(direct.Canvas, frame, now, direct.Opacity);
        }
        else if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is { } feature)
        {
            using var lease = feature.Lease();
            Paint(lease.SkCanvas, frame, now, lease.CurrentOpacity);
        }
    }

    private void Paint(SKCanvas canvas, NthFrame frame, double now, double opacity)
    {
        if (opacity <= 0) return;
        if (opacity >= 1) { Draw(canvas, frame, now); return; }
        layer.Color = new SKColor(255, 255, 255, (byte)Math.Round(opacity * 255));
        var saved = canvas.SaveLayer(layer);
        try { Draw(canvas, frame, now); }
        finally { canvas.RestoreToCount(saved); }
    }

    public override void Dispose()
    {
        layer.Dispose();
        base.Dispose();
    }
}

/// <summary>The visual's handler: the painter on the compositor's frame clock, paced to the shared 60 Hz slots.</summary>
internal sealed class NthMotionHandler(NthPainter painter, AmbientRefreshPacer? pacer) : CompositionCustomVisualHandler
{
    internal sealed class Stop
    {
        internal static readonly Stop Instance = new();
    }

    /// <summary>ED-NEW-lean-6: whether the window can be seen (WindowPresence), sent when it changes.</summary>
    internal sealed class Presence
    {
        internal static readonly Presence Seen = new(true), Unseen = new(false);
        private Presence(bool seen) => IsSeen = seen;
        internal bool IsSeen { get; }
    }

    private NthFrame? frame;
    private bool requested, stopped, hidden, drawn = true;
    private int frames, requests;
    private double lastDrawn = double.NaN;

    /// <summary>Frames drawn (tests).</summary>
    internal int Frames => Volatile.Read(ref frames);

    /// <summary>Animation frames asked of the compositor (tests: ED-NEW-lean-6's budget).</summary>
    internal int Requests => Volatile.Read(ref requests);

    /// <summary>The time the last frame was drawn at, clock seconds (tests).</summary>
    internal double LastDrawn => Volatile.Read(ref lastDrawn);

    public override void OnMessage(object message)
    {
        if (message is NthFrame next) { frame = next; Invalidate(); Request(); }
        else if (message is Stop) { stopped = true; painter.Dispose(); }
        else if (message is Presence presence && hidden == presence.IsSeen)
        {
            hidden = !presence.IsSeen;
            // seen again: draw the next frame at the current time, and ask for frames again while it moves
            if (!hidden && frame is not null) { Invalidate(); Request(); }
        }
    }

    public override Rect GetRenderBounds() => frame is { } value ? painter.Bounds(value) : default;

    public override void OnAnimationFrameUpdate()
    {
        requested = false;
        if (stopped || hidden || frame is not { } current) return;
        var now = NthFrame.Seconds(current.Clock);
        // ED-NEW-frame-pacing: decorative motion advances once per 1/60 s slot, on the same refresh as every other source
        if (pacer is not null && !pacer.Beat(CompositionNow.Ticks, now)) { Register(); return; }
        Invalidate();
        if (drawn && painter.Moving(current, now)) Request();
    }

    private void Request()
    {
        if (requested || stopped || hidden) return;
        drawn = false;
        Register();
    }

    private void Register()
    {
        requested = true;
        Interlocked.Increment(ref requests);
        RegisterForNextAnimationFrameUpdate();
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (stopped || frame is not { } current) return;
        drawn = true;
        var now = NthFrame.Seconds(current.Clock);
        painter.Render(context, current, now);
        Volatile.Write(ref lastDrawn, now);
        Interlocked.Increment(ref frames);
        if (painter.Moving(current, now)) Request();
    }
}

/// <summary>
/// The composition plumbing of one primitive on its owner control: one custom visual as the owner's child visual, sent a new
/// <see cref="NthFrame"/> when the owner's size, theme, calm or state change. Used by <see cref="NthMotionControl"/> and by
/// the text primitives that are TextBlocks themselves.
/// </summary>
public sealed class NthVisualHost
{
    private static readonly HashSet<NthVisualHost> mounted = [];
    private readonly Control owner;
    private readonly Func<NthPainter> create;
    private readonly Func<object?> state;
    private CompositionCustomVisual? visual;
    private NthMotionHandler? handler;
    private Func<NthFrame, bool>? probe; // the painter's Moving: a pure function of the frame and the time
    private TimeProvider? clock;
    private double? started;
    private NthFrame? sent;
    private WindowPresence? presence;
    private readonly Action presenceChanged;

    public NthVisualHost(Control owner, Func<NthPainter> create, Func<object?> state)
    {
        this.owner = owner; this.create = create; this.state = state;
        presenceChanged = PresenceChanged;
        owner.AttachedToVisualTree += (_, _) => Install();
        owner.DetachedFromVisualTree += (_, _) => Remove();
        owner.ActualThemeVariantChanged += (_, _) => Send();
        owner.PropertyChanged += (_, change) =>
        {
            if (change.Property == Visual.BoundsProperty || change.Property == EnvironmentValues.AccessibilityReduceMotionProperty) Send();
        };
    }

    /// <summary>The clock every time of this primitive is read on (SwiftClock's provider when it was first shown).</summary>
    public TimeProvider Clock => clock ??= SwiftClock.Provider.Current;

    /// <summary>Now on <see cref="Clock"/>, in seconds.</summary>
    public double Now => NthFrame.Seconds(Clock);

    /// <summary>When the primitive started (its enter fade and loops count from here), clock seconds.</summary>
    public double Started => started ??= Now;

    public bool Calm => EnvironmentValues.GetAccessibilityReduceMotion(owner);

    public bool Dark => PlainControls.IsDark(owner.ActualThemeVariant);

    /// <summary>The frame last sent to the render thread (tests).</summary>
    public NthFrame? Frame => sent;

    /// <summary>Frames the render thread has drawn (tests).</summary>
    public int FramesDrawn => handler?.Frames ?? 0;

    /// <summary>Animation frames the render thread has asked for (tests).</summary>
    internal int FrameRequests => handler?.Requests ?? 0;

    /// <summary>The time the render thread last drew at, clock seconds (tests).</summary>
    internal double LastDrawnAt => handler?.LastDrawn ?? double.NaN;

    /// <summary>Whether any nth primitive on screen is moving (the idle GPU warm-up waits for none, as for AuroraLoader).</summary>
    public static bool AnyAnimating()
    {
        foreach (var host in mounted)
            if (host.IsMoving) return true;
        return false;
    }

    /// <summary>Whether this primitive is on screen and moving now (its painter still asks for frames).</summary>
    public bool IsMoving => owner.IsEffectivelyVisible && sent is { } frame && probe is { } moving && moving(frame);

    /// <summary>Start the loops again from now (a new wait).</summary>
    public void Restart() { started = Now; Send(); }

    /// <summary>A frame for tests and pictures, as the owner would send it now.</summary>
    public NthFrame Snapshot() => new(owner.Bounds.Size, Dark, Calm, Clock, Started, state());

    private void Install()
    {
        mounted.Add(this);
        _ = Started;
        if (visual is not null || ElementComposition.GetElementVisual(owner) is not { } host) return;
        var painter = create();
        probe = frame => painter.Moving(frame, NthFrame.Seconds(frame.Clock));
        handler = new NthMotionHandler(painter, AmbientRefreshPacer.For(host.Compositor));
        visual = host.Compositor.CreateCustomVisual(handler);
        ElementComposition.SetElementChildVisual(owner, visual);
        presence = WindowPresence.For(owner);
        if (presence is not null)
        {
            presence.Changed += presenceChanged;
            if (!presence.Seen) visual.SendHandlerMessage(NthMotionHandler.Presence.Unseen);
        }
        sent = null;
        Send();
    }

    private void PresenceChanged()
    {
        if (presence is { } current) visual?.SendHandlerMessage(current.Seen ? NthMotionHandler.Presence.Seen : NthMotionHandler.Presence.Unseen);
    }

    /// <summary>Send the current frame to the render thread (call when the state changes).</summary>
    public void Send()
    {
        if (visual is null || owner.Bounds.Width <= 0 || owner.Bounds.Height <= 0) return;
        var frame = new NthFrame(owner.Bounds.Size, Dark, Calm, Clock, Started, state());
        if (frame == sent) return;
        sent = frame;
        visual.Size = new Vector(owner.Bounds.Width, owner.Bounds.Height);
        visual.SendHandlerMessage(frame);
    }

    private void Remove()
    {
        mounted.Remove(this);
        if (presence is not null) { presence.Changed -= presenceChanged; presence = null; }
        visual?.SendHandlerMessage(NthMotionHandler.Stop.Instance);
        ElementComposition.SetElementChildVisual(owner, null);
        visual = null; handler = null; sent = null; probe = null;
    }
}

/// <summary>
/// The base of every drawn nth primitive: a control that hosts one composition visual (<see cref="NthVisualHost"/>). Not
/// hit-testable, not focusable, hidden from assistive technology; the site's own text carries the meaning.
/// </summary>
public abstract class NthMotionControl : Control
{
    private readonly NthVisualHost host;

    protected NthMotionControl()
    {
        IsHitTestVisible = false;
        Focusable = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        host = new NthVisualHost(this, CreatePainter, () => State);
    }

    protected TimeProvider Clock => host.Clock;
    protected double Now => host.Now;
    public double Started => host.Started;
    public bool Calm => host.Calm;
    public bool Dark => host.Dark;
    public NthFrame? Frame => host.Frame;
    public int FramesDrawn => host.FramesDrawn;
    internal int FrameRequests => host.FrameRequests;
    internal double LastDrawnAt => host.LastDrawnAt;
    public bool IsMoving => host.IsMoving;
    public NthFrame Snapshot() => host.Snapshot();

    /// <summary>Start the loops again from now (a new wait).</summary>
    protected void Restart() => host.Restart();

    /// <summary>Send the current frame to the render thread (call when the state changes).</summary>
    protected void Send() => host.Send();

    /// <summary>The painter the render thread draws this primitive with (one per visual).</summary>
    protected abstract NthPainter CreatePainter();

    /// <summary>The primitive's own state, an immutable value (null when it has none).</summary>
    protected abstract object? State { get; }
}
