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
//     is compiled and, where the GPU keeps them (GpuProgramCache, ANGLE's program binaries on disk), in the cache. It fails
//     (never completes as if prepared) when a step is not drawn within 10 s (the window hidden or minimised), when the window
//     closes meanwhile, or when the window is not shown.
//   IsWarm: a cheap check (a marker file and DXGI) that this build's programs are in the cache, made on this machine's
//     graphics adapters with the drivers they have now, so later launches can skip that screen: they only load the programs'
//     binaries (about 4 to 17 ms a step, measured, no compile).
//   WhenIdle(host): the fallback the composer's model chip starts. It steps only in frames where nothing animates (no
//     frame-clock request waiting: the liquid, the knob, the port's transitions; nor any animation registered with
//     AddActivity: the composer's working dots), and once 3 s have passed since it began it steps anyway, one step a frame,
//     so the popover is ready. A step its window does not draw (minimised, the chip hidden) is given up after 10 s, compiles
//     nothing when drawn later, and the warm-up resumes, idle again, once its host is drawn again (in two frames in a row,
//     not a single one). It holds its host only while the host is in a window.
// Each step runs on the render thread in a 1 px composition visual that draws nothing on screen: it draws one of the knob's
// four programs (the noise, the fluid, the orb, the page with the shadows and the blit) offscreen into the knob's own
// surfaces on the window's GPU context, so the knob's first visible draw finds them made. A new GPU context or a new scaling
// warms again, idle-gated. The CPU renderer has nothing to compile ahead: no steps.

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

    /// <summary>How long a step waits for its window to draw it before the warm-up gives up on it.</summary>
    internal static TimeSpan StepTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Tests only: the CPU renderer answers each step as a GPU context would (the driver's whole flow without a
    /// GPU). Never set in the app.</summary>
    internal static bool PretendGpu { get; set; }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Visual, Driver> drivers = new();
    private static readonly List<Activity> activities = [];

    // the native record and tests: every step done in this process, with its time
    internal static int stepsDone, stepsAsked;
    internal static int StepsDone => Volatile.Read(ref stepsDone);
    internal static int StepsAsked => Volatile.Read(ref stepsAsked);
    internal static readonly System.Collections.Concurrent.ConcurrentQueue<GpuWarmupStep> Done = new();
    internal static long FirstStepTicks;

    /// <summary>Why the last automatic (idle) warm-up stopped short, for the native record (null: none did).</summary>
    internal static volatile string? LastGiveUp;

    /// <summary>
    /// Registers an animation the frame clock cannot see (a composition-thread one): while it answers true, the idle
    /// fallback does not step. Disposing the result removes that registration (once; a second Dispose does nothing).
    /// </summary>
    public static IDisposable AddActivity(Func<bool> isAnimating)
    {
        ArgumentNullException.ThrowIfNull(isAnimating);
        var activity = new Activity(isAnimating);
        lock (activities) activities.Add(activity);
        return activity;
    }

    // removed by identity: two registrations of equal delegates are two activities
    private sealed class Activity(Func<bool> isAnimating) : IDisposable
    {
        public bool IsAnimating() => isAnimating();
        public void Dispose() { lock (activities) activities.Remove(this); }
    }

    /// <summary>Registrations not yet disposed (tests).</summary>
    internal static int ActivityCount { get { lock (activities) return activities.Count; } }

    /// <summary>True when something on screen animates: a frame-clock request is waiting (on <paramref name="frames"/>, the
    /// frame clock's source by default), or a registered activity runs.</summary>
    internal static bool Animating(IFrameSource? frames = null)
    {
        if ((frames ?? FrameClock.Source.Current).Pending > 0) return true;
        lock (activities)
            foreach (var activity in activities)
                if (activity.IsAnimating()) return true;
        return false;
    }

    /// <summary>
    /// True when a warm-up of this build completed on this machine as it is now: the marker names this build, a GPU folder
    /// whose key names this build and this machine's graphics adapters with their current drivers (DXGI), and that folder
    /// holds an entry for each step. A driver update, a new GPU or a new build is not warm (OD-0096).
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
                if (lines.Length < 3 || lines[0] != GpuProgramCache.Build) return false;
                if (lines[1].Length == 0 || Path.GetFileName(lines[1]) != lines[1]) return false; // a folder's name, nothing else
                if (GpuProgramCache.Machine() is not { Length: > 0 } here || lines[2] != here) return false;
                var folder = Path.Combine(root, lines[1]);
                return Directory.Exists(folder) && GpuProgramCache.MadeHere(folder, here)
                    && Directory.EnumerateFiles(folder, "*.bin").Take(Steps).Count() == Steps;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    /// <summary>
    /// Warms the programs in <paramref name="window"/> now, one step a frame (no waiting for idle frames: the caller shows a
    /// screen meant for it), reporting each step; completes when they are compiled and cached (at once on the CPU renderer).
    /// Faults with a <see cref="TimeoutException"/> when a step is not drawn in time (the window minimised or hidden) and an
    /// <see cref="InvalidOperationException"/> when the window is not shown or closes meanwhile. Called while a warm-up of
    /// the window runs, it hurries that one and reports its remaining steps.
    /// </summary>
    public static Task WarmAsync(TopLevel window, IProgress<GpuWarmupStep>? progress = null, CancellationToken cancellation = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        var driver = drivers.GetValue(window, _ => new Driver(window, FrameClock.Source.Current));
        return driver.Run(idle: false, progress, cancellation);
    }

    /// <summary>The idle fallback (the composer's model chip): once its window has drawn its first frame, step only in
    /// frames where nothing animates, forced after the cap. Calling it again for the same host does nothing.</summary>
    public static void WhenIdle(Control host)
    {
        if (drivers.TryGetValue(host, out _)) return;
        var driver = new Driver(host, FrameClock.Source.Current); // the views' frame source, taken here (the steps run in other flows)
        drivers.Add(host, driver);
        host.AttachedToVisualTree += (_, _) => driver.Attached();
        host.DetachedFromVisualTree += (_, _) => driver.Detached();
        if (TopLevel.GetTopLevel(host) is not null) driver.Attached();
    }

    /// <summary>True when <paramref name="host"/> has its idle fallback scheduled (tests).</summary>
    internal static bool IsScheduled(Control host) => drivers.TryGetValue(host, out _);

    // UI thread: asks the render thread for one step at a time, waits for each, decides the next
    private sealed class Driver(Visual host, IFrameSource frames)
    {
        private CompositionCustomVisual? visual;
        private TopLevel? watched; // the window whose scaling it follows: its host's, while the host is in it
        private Task? running;
        private bool again, hurry;
        private IProgress<GpuWarmupStep>? progress;

        // WhenIdle's host is in a window: follow its scaling; warm after the window's first frame, then at idle (never in
        // startup's path or a frame being made)
        public void Attached()
        {
            if (TopLevel.GetTopLevel(host) is not { } top) return;
            Follow(top);
            top.RequestAnimationFrame(_ => Dispatcher.UIThread.Post(Start, DispatcherPriority.ApplicationIdle));
        }

        // it left its window: the window no longer holds it (a chip of a closed thread is collected)
        public void Detached() => Follow(null);

        private void Follow(TopLevel? top)
        {
            if (watched == top) return;
            if (watched is not null) watched.ScalingChanged -= OnScaling;
            watched = top;
            if (top is not null) top.ScalingChanged += OnScaling;
        }

        private void OnScaling(object? sender, EventArgs e) => Again();

        // a new scaling or a new GPU context: warm again, idle-gated (in the run going, or a new one)
        private void Again()
        {
            again = true;
            if (running is { IsCompleted: true }) Start();
        }

        // an automatic run: idle-gated, nobody awaits it (why it stopped short is kept for the native record)
        private void Start()
        {
            if (running is { IsCompleted: false }) return;
            Run(idle: true, null, default).ContinueWith(static run => LastGiveUp = run.Exception?.InnerException?.Message,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public Task Run(bool idle, IProgress<GpuWarmupStep>? progress, CancellationToken cancellation)
        {
            if (running is { IsCompleted: false })
            {
                if (!idle) { hurry = true; this.progress = progress ?? this.progress; } // WarmAsync joins it: no more idle waits
                return running.WaitAsync(cancellation);
            }
            if (TopLevel.GetTopLevel(host) is not { } top)
                return Task.FromException(new InvalidOperationException("GPU warm-up: its host is not in a window."));
            if (!top.IsVisible || top.PlatformImpl is null)
                return Task.FromException(new InvalidOperationException("GPU warm-up: its window is not shown."));
            if (visual is null)
            {
                if (ElementComposition.GetElementVisual(host) is not { } owner)
                    return Task.FromException(new InvalidOperationException("GPU warm-up: its window is not shown (no composition visual)."));
                visual = owner.Compositor.CreateCustomVisual(new Primer(() => Dispatcher.UIThread.Post(Again)));
                visual.Size = new Vector(1, 1);
                ElementComposition.SetElementChildVisual(host, visual);
                if (host == top) Follow(top); // WarmAsync's window: its own scaling
            }
            (hurry, this.progress) = (!idle, progress);
            return running = Steps(top, idle, cancellation);
        }

        // a step given up on (its host not drawn): the fallback starts again, idle-gated, once its host is drawn again (the
        // primer answers the withdrawn request then; nothing runs on the UI thread until it does)
        private void WhenDrawnAgain(StepRequest request) =>
            request.Answer.Task.ContinueWith(_ => Dispatcher.UIThread.Post(Start), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        private async Task Steps(TopLevel top, bool idle, CancellationToken cancellation)
        {
            // the SkSL compile and the machine's identity (DXGI), on a pool thread
            await Task.Run(() => { JewelKnobShader.Prepare(); GpuProgramCache.Prepare(); }, cancellation);
            do
            {
                again = false;
                var (size, _) = JewelOrb.RenderSize(EffortSlider.ThumbSize, top.RenderScaling);
                var entries = GpuProgramCache.Entries;
                var since = DateTime.UtcNow; // the cap counts from the warm-up's start: once it ran out, one step a frame
                for (var step = 0; step < Programs.Count; step++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var forced = idle && !hurry && await CapRanOut(top, since, cancellation);
                    if (TopLevel.GetTopLevel(host) != top || top.PlatformImpl is null)
                        throw new InvalidOperationException("GPU warm-up: its window closed (or its host left it) before the " + Programs[step] + " step.");
                    var request = new StepRequest(size, step);
                    Interlocked.Increment(ref stepsAsked);
                    visual!.SendHandlerMessage(request);
                    (double Ms, bool Gpu) answer;
                    try { answer = await request.Answer.Task.WaitAsync(StepTimeout, cancellation); }
                    catch (TimeoutException)
                    {
                        // a window that is not drawn (minimised, the host hidden) never answers: when it is drawn again the
                        // primer compiles nothing for this request, it only says so, and the fallback starts again from there
                        request.Withdrawn = true;
                        if (idle) WhenDrawnAgain(request);
                        throw new TimeoutException($"GPU warm-up: the {Programs[step]} step was not drawn within {StepTimeout.TotalSeconds:0.###} s (its window minimised or hidden, or its host hidden).");
                    }
                    catch (OperationCanceledException) { request.Withdrawn = true; throw; }
                    var (ms, gpu) = answer;
                    if (!gpu) return; // the CPU renderer: nothing to compile ahead
                    var report = new GpuWarmupStep(step, Programs.Count, Programs[step], ms, forced);
                    Done.Enqueue(report);
                    progress?.Report(report);
                }
                // warm only when the cache took (or gave back) an entry for every step: the programs are on disk (the marker and
                // the clean-up of other builds' folders on a pool thread, the UI thread's frames untouched)
                if (GpuProgramCache.Folder is { } cached && GpuProgramCache.Entries - entries >= Programs.Count)
                    await Task.Run(() => GpuProgramCache.MarkWarm(cached), CancellationToken.None);
            }
            while (again);
        }

        // waits for two frames in a row with nothing animating; true when the cap (since the warm-up began) ran out first
        private async Task<bool> CapRanOut(TopLevel top, DateTime since, CancellationToken cancellation)
        {
            var calm = 0;
            while (calm < 2)
            {
                if (hurry) return false; // WarmAsync joined: step now
                if (DateTime.UtcNow - since >= IdleCap) return true;
                var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                top.RequestAnimationFrame(_ => frame.TrySetResult());
                try { await frame.Task.WaitAsync(IdleCap, cancellation); }
                catch (TimeoutException) { return true; } // no frame at all (a closed or hidden window): the cap
                calm = Animating(frames) ? 0 : calm + 1;
                // a timer's turn between checks: where frames come back at once (nothing drawn, a headless test's job queue)
                // the loop leaves the dispatcher free instead of spinning it until the cap
                await Task.Delay(1, cancellation);
            }
            return false;
        }
    }

    // one step asked of the render thread; withdrawn when its driver stopped waiting for it
    private sealed class StepRequest(int size, int step)
    {
        public int Size { get; } = size;
        public int Step { get; } = step;
        public TaskCompletionSource<(double Ms, bool Gpu)> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool withdrawn;
        public bool Withdrawn { get => withdrawn; set => withdrawn = value; }
    }

    // render thread: one program per request, drawn offscreen in the frame the request arrives in
    private sealed class Primer(Action contextChanged) : CompositionCustomVisualHandler
    {
        private StepRequest? pending;
        private nint context;
        private long drawnOnce; // when a withdrawn request was drawn the first time (a timestamp; 0: not yet)

        public override void OnMessage(object message)
        {
            if (message is StepRequest request) { pending = request; drawnOnce = 0; Invalidate(); }
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
            if (gpu is not null) context = gpu.Handle;
            if (request.Withdrawn)
            {
                // the warm-up gave up on it, its host not drawn: it answers (compiling nothing) once the host is drawn in two
                // frames in a row, a window drawing again rather than a single frame; until then it asks to be drawn again
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (drawnOnce != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(drawnOnce, now) <= TimeSpan.FromMilliseconds(100))
                {
                    drawnOnce = 0;
                    request.Answer.TrySetResult((0, gpu is not null));
                    return;
                }
                drawnOnce = now;
                pending = request;
                Invalidate();
                return;
            }
            if (gpu is null && !PretendGpu) { request.Answer.TrySetResult((0, false)); return; }
            if (gpu is not null) GpuProgramCache.EnsureInstalled(gpu.Handle); // before the knob's programs are first linked: a later launch finds them made
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            Interlocked.CompareExchange(ref FirstStepTicks, Environment.TickCount64, 0);
            JewelKnobRenderer.For(gpu, request.Size).Warm(request.Step);
            gpu?.Flush(); // the compile happens here, in this frame (one with nothing animating, for the idle fallback)
            var ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Interlocked.Increment(ref stepsDone);
            request.Answer.TrySetResult((ms, true));
        }
    }
}
