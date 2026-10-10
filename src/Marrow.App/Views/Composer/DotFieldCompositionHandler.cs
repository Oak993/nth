using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;

namespace Marrow.App.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerWorkingDots.swift (B3 52bc792), :5-11,152-190.
// PORT-ONLY: the render-server equivalent of the source's CALayer / CAReplicatorLayer animation.
internal sealed class DotFieldCompositionHandler : CompositionCustomVisualHandler
{
    private DotFieldCompositionFrame? state;
    private DotFieldCompositionFrame? lastFrame;
    private int stopped;
    private bool frameRequested;
    private int animating;

    public DotFieldCompositionFrame? LastFrame => Volatile.Read(ref lastFrame);
    public bool IsStopped => Volatile.Read(ref stopped) != 0;
    public bool IsAnimating => !IsStopped && Volatile.Read(ref animating) != 0;
    public void Stop() => Interlocked.Exchange(ref stopped, 1);

    public override void OnMessage(object message)
    {
        if (IsStopped || message is not DotFieldCompositionFrame next) return;
        state = next;
        Invalidate();
        RequestFrameIfNeeded();
    }

    public override void OnAnimationFrameUpdate()
    {
        frameRequested = false;
        if (IsStopped) return;
        Invalidate();
        RequestFrameIfNeeded();
    }

    private void RequestFrameIfNeeded()
    {
        var sample = IsStopped ? null : state?.Sample();
        // ED-253: a finished accent blend settles into its target, so later samples blend nothing.
        if (sample is not null && state?.From is not null && sample.From is null) state = sample;
        var needsFrame = sample?.NeedsFrame == true;
        Volatile.Write(ref animating, needsFrame ? 1 : 0);
        if (!needsFrame || frameRequested) return;
        frameRequested = true;
        RegisterForNextAnimationFrameUpdate();
    }

    public override Rect GetRenderBounds() => state?.RenderBounds ?? default;
    public override void OnRender(ImmediateDrawingContext context)
    {
        using var work = Marrow.UI.Port.AuroraBootTrace.Begin("Working dots: compositor draw");
        if (IsStopped || state is not { } current) return;
        var frame = current.Sample();
        frame.Draw(context);
        Volatile.Write(ref lastFrame, frame);
    }
}
