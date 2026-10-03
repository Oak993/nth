using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

namespace Marrow.App.Views.Composer;

// OD-0090 / ED-252 (lead's ruling, 2026-10-03, under the owner's zero-stutter rule): on ANGLE (D3D11) the knob's programs
// compile on their first draw, 240 to 412 ms on the render thread (measured on a real window), so the first open of the effort
// popover would hitch. The composer's model chip primes them before the popover can be opened: at idle priority after its
// window's first frame, a 1 px composition visual that draws nothing on screen draws the knob's programs offscreen on the
// render thread, into the very surfaces the knob uses (JewelKnobRenderer's, for the window's render size), one program a
// frame (the fluid, then the glass and its blit), so no frame carries both compiles. The knob's renderer is kept per GPU
// context and size, so its later draws find the programs built. A new GPU context (a device lost or a display change: the
// window is drawn whole again on it, the primer with it, and finds the context new) or a change of the window's scaling (a
// monitor move: a new render size) primes them again. The CPU renderer has nothing to compile ahead: no-op.

/// <summary>Primes the Jewel knob's programs on the render thread before the effort popover can be opened.</summary>
public static class JewelWarmup
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, Handle> hosts = new();

    /// <summary>Warm-up steps drawn in this process (tests: one per program, per context and size).</summary>
    internal static int StepsDone => Volatile.Read(ref stepsDone);
    internal static int stepsDone;

    /// <summary>How long each warm-up step took on the render thread, ms (the native record).</summary>
    internal static readonly System.Collections.Concurrent.ConcurrentQueue<double> StepTimes = new();

    /// <summary>When the first warm-up step ran, in Environment.TickCount64 (the native record), or 0.</summary>
    internal static long FirstStepTicks;

    /// <summary>True when <paramref name="host"/> has scheduled the warm-up (tests).</summary>
    internal static bool IsScheduled(Control host) => hosts.TryGetValue(host, out _);

    /// <summary>
    /// Primes the programs from <paramref name="host"/> (the composer's model chip): once its window has drawn its first
    /// frame, at idle priority. Calling it again for the same host does nothing.
    /// </summary>
    public static void Schedule(Control host)
    {
        if (hosts.TryGetValue(host, out _)) return;
        var handle = new Handle();
        hosts.Add(host, handle);
        void Start()
        {
            if (TopLevel.GetTopLevel(host) is not { } top) return;
            // after the window's first frame, then at idle: never in startup's path or a frame being made
            top.RequestAnimationFrame(_ => Dispatcher.UIThread.Post(() => handle.Attach(host, top), DispatcherPriority.ApplicationIdle));
        }
        if (TopLevel.GetTopLevel(host) is not null) Start();
        else host.AttachedToVisualTree += (_, _) => Start();
    }

    private sealed class Handle
    {
        private CompositionCustomVisual? visual;
        private TopLevel? top;

        public void Attach(Control host, TopLevel window)
        {
            if (visual is not null || ElementComposition.GetElementVisual(host) is not { } owner) return;
            top = window;
            visual = owner.Compositor.CreateCustomVisual(new Primer());
            visual.Size = new Vector(1, 1);
            ElementComposition.SetElementChildVisual(host, visual);
            window.ScalingChanged += (_, _) => Rewarm();
            Rewarm();
        }

        public void Rewarm()
        {
            if (visual is null || top is null) return;
            var (size, _) = JewelOrb.RenderSize(EffortSlider.ThumbSize, top.RenderScaling);
            visual.SendHandlerMessage(size);
        }
    }

    // render thread: one program a frame, then idle until a message or a new context asks again
    private sealed class Primer : CompositionCustomVisualHandler
    {
        private int size, step;
        private nint context;

        public override void OnMessage(object message)
        {
            if (message is int px) { size = px; step = 0; context = 0; Invalidate(); }
        }

        public override void OnAnimationFrameUpdate() => Invalidate();

        public override Rect GetRenderBounds() => new(0, 0, 1, 1);

        public override void OnRender(ImmediateDrawingContext drawing)
        {
            if (size <= 0) return;
            using var lease = drawing.TryGetFeature<ISkiaSharpApiLeaseFeature>()?.Lease();
            if (lease?.GrContext is not { } gpu) return; // the CPU renderer: nothing to compile ahead
            if (gpu.Handle != context) { context = gpu.Handle; step = 0; }
            if (step >= 2) return;
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            if (Interlocked.CompareExchange(ref FirstStepTicks, Environment.TickCount64, 0) == 0) { }
            JewelKnobRenderer.For(gpu, size).Warm(step);
            gpu.Flush(); // the compile happens here, in this (otherwise unchanged) frame, not in the knob's first visible one
            StepTimes.Enqueue(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            Interlocked.Increment(ref stepsDone);
            step++;
            if (step < 2) RegisterForNextAnimationFrameUpdate();
        }
    }
}
