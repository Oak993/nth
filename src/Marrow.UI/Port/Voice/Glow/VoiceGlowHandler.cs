// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13.3): the glow's composition handler, after VoiceOrbHandler.
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;

namespace Marrow.UI.Port.Voice.Glow;

/// <summary>
/// The render-thread side of <see cref="VoiceGlowView"/>: steps the <see cref="VoiceGlowScene"/> on the compositor's frame clock and draws it with one
/// <see cref="VoiceGlowPainter"/> (one reused surface) inside the composer's rounded shape. Frames are asked for only while the glow runs (active or fading) and is drawn;
/// a view nobody draws (hidden, minimised, occluded) asks for none, and drawing it again resumes it. On the software rasterizer (no GPU) the glow costs about 30 ms a frame, so
/// it is drawn as one still, its fade taken at once (ED-336, as ED-251 for the composer light).
/// </summary>
internal sealed class VoiceGlowHandler : CompositionCustomVisualHandler
{
    private VoiceGlowSnapshot? snapshot;
    private readonly VoiceGlowScene scene = new();
    private readonly VoiceGlowPainter painter = new();
    private bool frameRequested;
    private bool drawnSinceRequest = true;
    private int stopped;
    private int animating;
    private int frames;
    private bool software;
    private bool backendKnown;
    private VoiceGlowSnapshot? manualInitial;
    private readonly List<VoiceGlowTick> pendingManualFrames = [];

    /// <summary>With no GPU (Skia's software rasterizer) the glow is one still; tests that need the motion on the headless renderer turn this on.</summary>
    internal static volatile bool AnimateWithoutGpu;

    public VoiceGlowScene Scene => scene;

    /// <summary>Frames drawn so far (tests).</summary>
    public int Frames => Volatile.Read(ref frames);

    /// <summary>True while the handler has asked the compositor for a frame.</summary>
    public bool IsAnimating => Volatile.Read(ref animating) != 0;

    public bool IsStopped => Volatile.Read(ref stopped) != 0;

    internal VoiceGlowPainter Painter => painter;

    private static double Seconds(TimeProvider clock) => clock.GetTimestamp() / (double)clock.TimestampFrequency;

    public override void OnMessage(object message)
    {
        if (message is VoiceGlowStop)
        {
            Interlocked.Exchange(ref stopped, 1);
            painter.Dispose();
            Volatile.Write(ref animating, 0);
            return;
        }
        if (message is VoiceGlowTick tick)
        {
            if (IsStopped) return;
            var input = tick.Input with { ReduceMotion = tick.Input.ReduceMotion || software };
            if (!backendKnown) pendingManualFrames.Add(tick);
            else scene.Advance(tick.At, input);
            Invalidate();
            return;
        }
        if (IsStopped || message is not VoiceGlowSnapshot next) return;
        snapshot = next;
        if (next.ManualOrigin is not null) manualInitial ??= next;
        Invalidate();
        if (drawnSinceRequest) Request();
    }

    public override void OnAnimationFrameUpdate()
    {
        frameRequested = false;
        Volatile.Write(ref animating, 0);
        if (IsStopped || snapshot is null) return;
        Invalidate();
        if (drawnSinceRequest) Request();
    }

    private void Request()
    {
        if (frameRequested || IsStopped || snapshot is null || software) return;
        if (!WantsFrames()) return;
        frameRequested = true;
        drawnSinceRequest = false;
        Volatile.Write(ref animating, 1);
        RegisterForNextAnimationFrameUpdate();
    }

    // an active or fading glow wants the next frame; one that is gone (or never was) does not, until a snapshot says otherwise
    private bool WantsFrames() => scene.NeedsFrames || (snapshot is { } s && s.Input.Active) || scene.Visible;

    public override Rect GetRenderBounds() => snapshot is { } s && s.Input.Width > 0 && s.Input.Height > 0 ? new Rect(0, 0, s.Input.Width, s.Input.Height) : default;

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (IsStopped || snapshot is not { } s) return;
        drawnSinceRequest = true;
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature is null) return;
        using var lease = feature.Lease();
        software = lease.GrContext is null && !AnimateWithoutGpu;
        var canvas = lease.SkCanvas;
        var m = canvas.TotalMatrix;
        var dpr = Math.Max(1, Math.Sqrt(m.ScaleX * m.ScaleX + m.SkewY * m.SkewY));
        var input = software && !s.Input.ReduceMotion ? s.Input with { ReduceMotion = true } : s.Input;
        painter.Prepare(lease.GrContext, input.Width, input.Height, dpr);       // V-13.5: the surface exists from the first layout, not from the first click
        if (s.ManualOrigin is null) scene.Advance(Seconds(s.Clock), input);
        else if (!backendKnown)
        {
            backendKnown = true;
            var initial = manualInitial ?? s;
            scene.Advance(initial.ManualOrigin!.Value, initial.Input with { ReduceMotion = initial.Input.ReduceMotion || software });
            foreach (var tick in pendingManualFrames)
                scene.Advance(tick.At, tick.Input with { ReduceMotion = tick.Input.ReduceMotion || software });
            pendingManualFrames.Clear();
        }
        if (scene.Visible && scene.Frame is { } frame && scene.Look is { } look)
        {
            painter.Draw(canvas, lease.GrContext, look, frame, input.Width, input.Height, dpr, scene.Opacity * lease.CurrentOpacity);
            Interlocked.Increment(ref frames);
        }
        Request();
    }
}
