using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.Composition;
using Marrow.Core.Port;

namespace Marrow.App.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerWorkingDots.swift (B3 52bc792), :5-11,152-190.
// PORT-ONLY: the render-server equivalent of the source's CALayer / CAReplicatorLayer animation.
// ED-NEW-lean-9: a frame update and a draw allocate nothing once warm. The field's state (the frame the UI thread sent) is drawn
// at the time the clock shows, in its own brush; no sampled frame is made per update or draw (LastFrame makes one when read). While
// the accent blends (0.32 s after a change) the colour changes at nearly every draw and each new colour is a new immutable brush
// (64 B a draw, ComposerWorkingDotsLeanBudgetTests); a colour drawn again keeps its brush. The "stop when not drawn" guard of
// NthMotionHandler: the next update is asked for by a draw, so a field that is not drawn (its window not composed) stops asking
// until it is drawn again or sent a new frame (ComposerWorkingDotsStopGuardTests). ED-NEW-lean-9 (pacing): decorative motion, so a
// frame update draws only on a 60 Hz slot of the compositor's shared pacer (ED-NEW-frame-pacing, NthMotionHandler's gate): every 4th
// refresh at 240 Hz; the other updates ask for the next one and draw nothing. The draw's time is still the clock's (SwiftClock), so a
// drawn picture is the unpaced field's picture at the same instant (ComposerWorkingDotsPacedPixelGuardTests).
internal sealed class DotFieldCompositionHandler(Func<long, double, bool>? pacer = null) : CompositionCustomVisualHandler
{
    private DotFieldCompositionFrame? state;
    // The last draw's frame, time and brush, written and read together (a reader on another thread sees one draw's three).
    private readonly Lock drawnGate = new();
    private DotFieldCompositionFrame? drawnState;
    private IImmutableBrush? drawnBrush;
    private long drawnAt;
    private IImmutableBrush? blendBrush;
    private Color blendColor;
    private int stopped;
    private bool requested, drawn = true;
    private int animating;
    private int updates;

    /// <summary>The frame last drawn, as sampled at its draw (tests; made when read).</summary>
    public DotFieldCompositionFrame? LastFrame
    {
        get
        {
            DotFieldCompositionFrame? last;
            long at;
            IImmutableBrush? brush;
            lock (drawnGate) { last = drawnState; at = drawnAt; brush = drawnBrush; }
            return last?.At(new ContinuousInstant(at), brush);
        }
    }

    /// <summary>Frame updates the field has had (tests: a field that is not drawn stops asking for them).</summary>
    internal int Updates => Volatile.Read(ref updates);

    public bool IsStopped => Volatile.Read(ref stopped) != 0;
    public bool IsAnimating => !IsStopped && Volatile.Read(ref animating) != 0;
    public void Stop() => Interlocked.Exchange(ref stopped, 1);

    public override void OnMessage(object message)
    {
        if (IsStopped || message is not DotFieldCompositionFrame next) return;
        state = next;
        Invalidate();
        Request(next.ClockNow());
    }

    public override void OnAnimationFrameUpdate()
    {
        Interlocked.Increment(ref updates);
        requested = false;
        if (IsStopped) return;
        // not a 60 Hz slot: ask for the next update and draw nothing; `drawn` is left as it is (the stop-when-not-drawn guard)
        if (pacer is not null && state is { } paced && !pacer(CompositionNow.Ticks, paced.ClockNow().Ticks / (double)TimeSpan.TicksPerSecond))
        {
            requested = true;
            RegisterForNextAnimationFrameUpdate();
            return;
        }
        Invalidate();
        // the draw that follows asks for the next update; one asked for before the last draw was drawn asks again here
        if (drawn && state is { } current) Request(current.ClockNow());
    }

    private void Request(ContinuousInstant now)
    {
        if (IsStopped || state is not { } current) { Volatile.Write(ref animating, 0); return; }
        // ED-253: a finished accent blend settles into its target, so later samples blend nothing.
        if (current.From is not null && !current.BlendingAt(now)) state = current = current.At(now);
        var needsFrame = current.NeedsFrameAt(now);
        Volatile.Write(ref animating, needsFrame ? 1 : 0);
        if (!needsFrame || requested) return;
        drawn = false;
        requested = true;
        RegisterForNextAnimationFrameUpdate();
    }

    public override Rect GetRenderBounds() => state?.RenderBounds ?? default;

    public override void OnRender(ImmediateDrawingContext context)
    {
        using var work = Marrow.UI.Port.AuroraBootTrace.Begin("Working dots: compositor draw");
        if (IsStopped || state is not { } current) return;
        drawn = true;
        var now = current.ClockNow();
        var brush = current.From is null ? current.Brush : BlendBrush(current.ShownAt(now));
        current.Draw(context, now, brush);
        lock (drawnGate) { drawnState = current; drawnAt = now.Ticks; drawnBrush = brush; }
        Request(now);
    }

    // ED-NEW-lean-9: the blend's colour changes at nearly every draw while it runs, and each new colour is a new brush (an
    // immutable brush is what the drawing context takes, and LastFrame hands the drawn one out); a colour drawn again keeps it.
    private IImmutableBrush BlendBrush(Color color)
    {
        if (blendBrush is null || blendColor != color)
        {
            blendBrush = new ImmutableSolidColorBrush(color);
            blendColor = color;
        }
        return blendBrush;
    }
}
