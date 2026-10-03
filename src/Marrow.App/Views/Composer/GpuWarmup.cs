using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Avalonia.Threading;
using Marrow.Core.Support;
using Marrow.UI.Port;

namespace Marrow.App.Views.Composer;

// OD-0090 / ED-252 (lead's rulings, 2026-10-03, under the owner's zero-stutter bar): on ANGLE (D3D11) the knob's programs
// compile on their first draw, so the first open of the effort popover would hitch. GpuWarmup prepares them ahead, as a
// reusable service:
//   WarmAsync(window): the one-time GPU preparation, awaitable, one program step a frame, each reported (the first-run
//     "Getting ready" screen, OD-0096, awaits it; the screen itself is designed separately). It completes when every program
//     is compiled and, where the GPU keeps them (GpuProgramCache, ANGLE's program binaries on disk), in the cache.
//   IsWarm: a cheap check (a marker file) that this build's programs are already in the cache, so later launches can skip
//     that screen: they only load the programs' binaries (about 10 to 18 ms a step, measured, no compile).
//   WhenIdle(host): the fallback the composer's model chip starts. It steps only in frames where nothing animates (no
//     frame-clock request waiting: the liquid, the knob, the port's transitions; nor any animation registered with
//     AddActivity), and once 3 s have passed since it began it steps anyway, one step a frame, so the popover is ready.
// Each step runs on the render thread in a 1 px composition visual that draws nothing on screen: it draws one of the knob's
// four programs (the noise, the fluid, the orb, the page with the shadows and the blit) offscreen into the knob's own
// surfaces on the window's GPU context, so the knob's first visible draw finds them made. A new GPU context or a new scaling
// warms again. The CPU renderer has nothing to compile ahead: no steps.

/// <summary>One step of the GPU warm-up: which program, how long it took on the render thread, and whether it waited for an
/// idle frame and was forced at the cap.</summary>
public sealed record GpuWarmupStep(int Index, int Count, string Program, double Milliseconds, bool Forced);

/// <summary>Prepares the knob's GPU programs ahead of their first use (see the file note).</summary>
public static class GpuWarmup
{
    /// <summary>The programs, one step each.</summary>
    public static IReadOnlyList<string> Programs { get; } = ["noise", "fluid", "orb", "page"];

    /// <summary>How many steps a warm-up takes.</summary>
    public static int Steps => Programs.Count;

    /// <summary>How long the idle fallback waits for a quiet frame before it steps anyway (tests shorten it).</summary>
    public static TimeSpan IdleCap { get; internal set; } = TimeSpan.FromSeconds(3);

    /// <summary>How long a step waits for its window to draw it before the warm-up gives up (unmarked).</summary>
    internal static TimeSpan StepTimeout { get; set; } = TimeSpan.FromSeconds(10);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Visual, Driver> drivers = new();
    private static readonly List<Func<bool>> activities = [];

    // the native record and tests: every step done in this process, with its time
    internal static int stepsDone, stepsAsked;
    internal static int StepsDone => Volatile.Read(ref stepsDone);
    internal static int StepsAsked => Volatile.Read(ref stepsAsked);
    internal static readonly System.Collections.Concurrent.ConcurrentQueue<GpuWarmupStep> Done = new();
    internal static long FirstStepTicks;

    /// <summary>
    /// Registers an animation the frame clock cannot see (a composition-thread one): while it answers true, the idle
    /// fallback does not step. Disposing the result removes it.
    /// </summary>
    public static IDisposable AddActivity(Func<bool> isAnimating)
    {
        ArgumentNullException.ThrowIfNull(isAnimating);
        lock (activities) activities.Add(isAnimating);
        return new Activity(isAnimating);
    }

    private sealed class Activity(Func<bool> isAnimating) : IDisposable
    {
        public void Dispose() { lock (activities) activities.Remove(isAnimating); }
    }

    /// <summary>True when something on screen animates: a frame-clock request is waiting (on <paramref name="frames"/>, the
    /// frame clock's source by default), or a registered activity runs.</summary>
    internal static bool Animating(IFrameSource? frames = null)
    {
        if ((frames ?? FrameClock.Source.Current).Pending > 0) return true;
        lock (activities) return activities.Any(active => active());
    }

    /// <summary>
    /// True when this build's programs are in the GPU cache (written by a completed warm-up on this machine): a later launch
    /// only loads them. A changed GPU or driver is found on the render thread and simply compiled again there.
    /// </summary>
    public static bool IsWarm
    {
        get
        {
            if (GpuProgramCache.Root() is not { } root) return false;
            try
            {
                var marker = Path.Combine(root, "warm.txt");
                if (!File.Exists(marker)) return false;
                var lines = File.ReadAllLines(marker);
                return lines.Length >= 2 && lines[0] == GpuProgramCache.Build && Directory.Exists(Path.Combine(root, lines[1]))
                    && Directory.EnumerateFiles(Path.Combine(root, lines[1]), "*.bin").Any();
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    /// <summary>
    /// Warms the programs in <paramref name="window"/> now, one step a frame (no waiting for idle frames: the caller shows a
    /// screen meant for it), reporting each step; completes when they are compiled and cached (at once on the CPU renderer).
    /// </summary>
    public static Task WarmAsync(TopLevel window, IProgress<GpuWarmupStep>? progress = null, CancellationToken cancellation = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        var driver = drivers.GetValue(window, _ => new Driver(FrameClock.Source.Current));
        return driver.Run(window, window, idle: false, progress, cancellation);
    }

    /// <summary>The idle fallback (the composer's model chip): once its window has drawn its first frame, step only in
    /// frames where nothing animates, forced after the cap. Calling it again for the same host does nothing.</summary>
    public static void WhenIdle(Control host)
    {
        if (drivers.TryGetValue(host, out _)) return;
        var driver = new Driver(FrameClock.Source.Current); // the views' frame source, taken here (the steps run in other flows)
        drivers.Add(host, driver);
        void Start()
        {
            if (TopLevel.GetTopLevel(host) is not { } top) return;
            // after the window's first frame, then at idle: never in startup's path or a frame being made
            top.RequestAnimationFrame(frameTime => Dispatcher.UIThread.Post(() => _ = driver.Run(host, top, idle: true, null, default), DispatcherPriority.ApplicationIdle));
        }
        if (TopLevel.GetTopLevel(host) is not null) Start();
        else host.AttachedToVisualTree += (_, _) => Start();
    }

    /// <summary>True when <paramref name="host"/> has its idle fallback scheduled (tests).</summary>
    internal static bool IsScheduled(Control host) => drivers.TryGetValue(host, out _);

    // UI thread: asks the render thread for one step at a time, waits for each, decides the next
    private sealed class Driver(IFrameSource frames)
    {
        private CompositionCustomVisual? visual;
        private Task? running;
        private bool again;

        public Task Run(Visual host, TopLevel top, bool idle, IProgress<GpuWarmupStep>? progress, CancellationToken cancellation)
        {
            if (running is { IsCompleted: false }) return running;
            if (visual is null)
            {
                if (ElementComposition.GetElementVisual(host) is not { } owner) return Task.CompletedTask;
                visual = owner.Compositor.CreateCustomVisual(new Primer(() => Dispatcher.UIThread.Post(() =>
                {
                    // a new GPU context (a device lost, a display change): its programs are not made yet
                    again = true;
                    if (running is { IsCompleted: true }) _ = Run(host, top, idle, null, default);
                })));
                visual.Size = new Vector(1, 1);
                ElementComposition.SetElementChildVisual(host, visual);
                top.ScalingChanged += (_, _) => { again = true; if (running is { IsCompleted: true }) _ = Run(host, top, idle, null, default); };
            }
            return running = Steps(host, top, idle, progress, cancellation);
        }

        private async Task Steps(Visual host, TopLevel top, bool idle, IProgress<GpuWarmupStep>? progress, CancellationToken cancellation)
        {
            await Task.Run(JewelKnobShader.Prepare, cancellation); // the SkSL compile, on a pool thread
            do
            {
                again = false;
                var (size, _) = JewelOrb.RenderSize(EffortSlider.ThumbSize, top.RenderScaling);
                var since = DateTime.UtcNow; // the cap counts from the warm-up's start: once it ran out, one step a frame
                for (var step = 0; step < Programs.Count; step++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var forced = idle && !await Quiet(top, since, cancellation);
                    if (TopLevel.GetTopLevel(host) != top || top.PlatformImpl is null) return; // its window closed meanwhile
                    var done = new TaskCompletionSource<(double Ms, bool Gpu)>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Interlocked.Increment(ref stepsAsked);
                    visual!.SendHandlerMessage(new StepRequest(size, step, result => done.TrySetResult(result)));
                    (double Ms, bool Gpu) answer;
                    // a window that is not drawn (hidden, minimized) never answers: give up, unmarked (a later call warms)
                    try { answer = await done.Task.WaitAsync(StepTimeout, cancellation); }
                    catch (TimeoutException) { return; }
                    var (ms, gpu) = answer;
                    if (!gpu) return; // the CPU renderer: nothing to compile ahead
                    var report = new GpuWarmupStep(step, Programs.Count, Programs[step], ms, forced);
                    Done.Enqueue(report);
                    progress?.Report(report);
                }
                if (GpuProgramCache.Folder is { } cached) GpuProgramCache.MarkWarm(cached);
            }
            while (again);
        }

        // waits for two frames in a row with nothing animating; false when the cap (since the warm-up began) ran out first
        private async Task<bool> Quiet(TopLevel top, DateTime since, CancellationToken cancellation)
        {
            var calm = 0;
            while (calm < 2)
            {
                if (DateTime.UtcNow - since >= IdleCap) return false;
                var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                top.RequestAnimationFrame(_ => frame.TrySetResult());
                try { await frame.Task.WaitAsync(IdleCap, cancellation); }
                catch (TimeoutException) { return false; } // no frame at all (a closed or hidden window): the cap
                calm = Animating(frames) ? 0 : calm + 1;
                // a timer's turn between checks: where frames come back at once (nothing drawn, a headless test's job queue)
                // the loop leaves the dispatcher free instead of spinning it until the cap
                await Task.Delay(1, cancellation);
            }
            return true;
        }
    }

    private sealed record StepRequest(int Size, int Step, Action<(double Ms, bool Gpu)> Done);

    // render thread: one program per request, drawn offscreen in the frame the request arrives in
    private sealed class Primer(Action contextChanged) : CompositionCustomVisualHandler
    {
        private StepRequest? pending;
        private nint context;

        public override void OnMessage(object message)
        {
            if (message is StepRequest request) { pending = request; Invalidate(); }
        }

        public override Rect GetRenderBounds() => new(0, 0, 1, 1);

        public override void OnRender(ImmediateDrawingContext drawing)
        {
            using var lease = drawing.TryGetFeature<ISkiaSharpApiLeaseFeature>()?.Lease();
            var gpu = lease?.GrContext;
            // the window drawn whole on another GPU context: warm again (the primer is drawn with it)
            if (gpu is not null && context != 0 && gpu.Handle != context && pending is null) { context = gpu.Handle; contextChanged(); return; }
            if (pending is not { } request) return;
            pending = null;
            if (gpu is null) { Dispatcher.UIThread.Post(() => request.Done((0, false))); return; }
            context = gpu.Handle;
            GpuProgramCache.EnsureInstalled(); // before the knob's programs are first linked: a later launch finds them made
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            Interlocked.CompareExchange(ref FirstStepTicks, Environment.TickCount64, 0);
            JewelKnobRenderer.For(gpu, request.Size).Warm(request.Step);
            gpu.Flush(); // the compile happens here, in this frame (one with nothing animating, for the idle fallback)
            var ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Interlocked.Increment(ref stepsDone);
            Dispatcher.UIThread.Post(() => request.Done((ms, true)));
        }
    }
}
