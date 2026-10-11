using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.Composition;
using Marrow.Core.Port;

namespace Marrow.App.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerWorkingDots.swift (B3 52bc792), :5-11,152-190.
// PORT-ONLY: the render-server equivalent of the source's CALayer / CAReplicatorLayer animation.
// ED-NEW-lean-9: a frame update and a draw allocate nothing once warm. The field's state (the frame the UI thread sent) is drawn
// at the time the clock shows, in its own brush or, while the accent blends, in a brush kept until the colour changes; no sampled
// frame is made per update or draw (LastFrame makes one when read). The "stop when not drawn" guard of NthMotionHandler: the next
// update is asked for by a draw, so a field that is not drawn (its window not composed) stops asking until it is drawn again.
internal sealed class DotFieldCompositionHandler : CompositionCustomVisualHandler
{
    private DotFieldCompositionFrame? state;
    private DotFieldCompositionFrame? drawnState;
    private IImmutableBrush? drawnBrush;
    private long drawnAt;
    private IImmutableBrush? blendBrush;
    private Color blendColor;
    private int stopped;
    private bool requested, drawn = true;
    private int animating;

    /// <summary>The frame last drawn, as sampled at its draw (tests; made when read).</summary>
    public DotFieldCompositionFrame? LastFrame =>
        Volatile.Read(ref drawnState) is { } last ? last.At(new ContinuousInstant(Volatile.Read(ref drawnAt)), drawnBrush) : null;

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
        requested = false;
        if (IsStopped) return;
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
        drawnBrush = brush;
        Volatile.Write(ref drawnAt, now.Ticks);
        Volatile.Write(ref drawnState, current);
        Request(now);
    }

    // ED-NEW-lean-9: the blend's colour changes at every draw while it runs; a colour drawn again keeps its brush.
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
