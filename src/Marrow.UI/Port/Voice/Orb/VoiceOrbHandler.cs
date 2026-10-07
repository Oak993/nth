// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-8, voice-SPEC V-11): the orb's composition handler (V-11.3), after ComposerLightHandler.
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;

namespace Marrow.UI.Port.Voice.Orb;

/// <summary>
/// The render-thread side of <see cref="VoiceOrbView"/>: steps the <see cref="OrbScene"/> on the compositor's frame clock and draws it with one
/// <see cref="OrbRenderer"/> (one reused surface). Frames are asked for only while the view is drawn and something moves: the orb always moves at rest, so a drawn
/// orb asks for a frame after every frame; a view nobody draws (hidden, minimised, occluded) asks for none, and drawing it again resumes it. Under reduce motion, and
/// on the software rasterizer (no GPU), it draws one still and asks for no frames (ED-251's rule for the composer light).
/// </summary>
internal sealed class VoiceOrbHandler : CompositionCustomVisualHandler
{
    private VoiceOrbSnapshot? snapshot;
    private OrbScene? scene;
    private readonly OrbRenderer renderer = new();
    private bool frameRequested;
    private bool drawnSinceRequest = true;
    private int stopped;
    private int animating;
    private int frames;
    private bool software;
    private bool backendKnown;
    private VoiceOrbSnapshot? manualInitial;
    private readonly List<VoiceOrbTick> pendingManualFrames = [];

    /// <summary>With no GPU (Skia's software rasterizer) the orb is one still; tests that need the motion on the headless renderer turn this on.</summary>
    internal static volatile bool AnimateWithoutGpu;

    public OrbScene? Scene => Volatile.Read(ref scene);

    public OrbRenderer Renderer => renderer;

    /// <summary>Frames drawn so far (tests).</summary>
    public int Frames => Volatile.Read(ref frames);

    /// <summary>True while the handler has asked the compositor for a frame.</summary>
    public bool IsAnimating => Volatile.Read(ref animating) != 0;

    public bool IsStopped => Volatile.Read(ref stopped) != 0;

    private static double Seconds(TimeProvider clock) => clock.GetTimestamp() / (double)clock.TimestampFrequency;

    public override void OnMessage(object message)
    {
        if (message is VoiceOrbStop)
        {
            Interlocked.Exchange(ref stopped, 1);
            renderer.Dispose();
            Volatile.Write(ref animating, 0);
            return;
        }
        if (message is VoiceOrbTick tick)
        {
            if (IsStopped || scene is null) return;
            if (!backendKnown) pendingManualFrames.Add(tick);
            else scene.Advance(tick.At, tick.Input with { ReduceMotion = tick.Input.ReduceMotion || software }, tick.Dpr);
            Invalidate();
            return;
        }
        if (IsStopped || message is not VoiceOrbSnapshot next) return;
        snapshot = next;
        if (next.ManualOrigin is not null) manualInitial ??= next;
        scene ??= new OrbScene(next.Input.Kind, next.Input.SlotDiameter, next.Input.Dark);
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
        if (frameRequested || IsStopped || snapshot is null || snapshot.Input.ReduceMotion || software) return;
        frameRequested = true;
        drawnSinceRequest = false;
        Volatile.Write(ref animating, 1);
        RegisterForNextAnimationFrameUpdate();
    }

    public override Rect GetRenderBounds() => new(0, 0, snapshot?.Input.ViewSize ?? OrbScene.ViewPx, snapshot?.Input.ViewSize ?? OrbScene.ViewPx);

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (IsStopped || snapshot is not { } s || scene is null) return;
        drawnSinceRequest = true;
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature is null) return;
        using var lease = feature.Lease();
        software = lease.GrContext is null && !AnimateWithoutGpu;
        var canvas = lease.SkCanvas;
        var m = canvas.TotalMatrix;
        var dpr = Math.Max(1, Math.Sqrt(m.ScaleX * m.ScaleX + m.SkewY * m.SkewY));
        var input = s.Input with { ReduceMotion = s.Input.ReduceMotion || software };
        if (s.ManualOrigin is null) scene.Advance(Seconds(s.Clock), input, dpr);
        else if (!backendKnown)
        {
            backendKnown = true;
            var initial = manualInitial ?? s;
            scene.Advance(initial.ManualOrigin!.Value, initial.Input with { ReduceMotion = initial.Input.ReduceMotion || software }, dpr);
            foreach (var tick in pendingManualFrames)
                scene.Advance(tick.At, tick.Input with { ReduceMotion = tick.Input.ReduceMotion || software }, tick.Dpr);
            pendingManualFrames.Clear();
        }
        for (var i = 0; i < 2; i++)
        {
            var kind = (Marrow.Core.Port.Voice.VoiceOrbKind)i;
            var runtime = scene.Runtime(kind);
            var opacity = scene.Opacity(kind);
            if (runtime is null || opacity <= 0) continue;
            renderer.Draw(canvas, lease.GrContext, runtime.Values, runtime.Mrw, runtime.DevicePx, runtime.RenderPx, (float)input.ViewSize, (float)(opacity * lease.CurrentOpacity));
        }
        Interlocked.Increment(ref frames);
        Request();
    }
}
