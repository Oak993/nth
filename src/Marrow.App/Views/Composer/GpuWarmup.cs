using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Port.FirstRun;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Port;
using Marrow.UI.Port.ComposerLights;
using SkiaSharp;

namespace Marrow.App.Views.Composer;

// GR-1 (BUILD-PLAN 2.2; OD-0096 and the OD-0098 amendments; ED-252): the warm-up is a REGISTRY. Each entry is a GpuProgram: a
// program drawn on the render thread (GpuProgram.Draw: it is handed the window's GPU context and scaling, draws once offscreen
// into surfaces of its own and leaves nothing on screen) or a step of other work (GpuProgram.Work: awaited on the UI thread, in
// a first-run warm-up only, never in the idle fallback; the voice hardware check). Each names the first-run step it belongs to
// (FirstRunStep: id, W1's name, order); the registry keeps entries in step order, then registration order. The jewel knob's
// four programs, the composer light's three, the slider liquid's and the strike's are registered here (Builtins); a program or
// step another unit adds (the voice orbs, a new strike, the voice hardware check) registers from its own file with Register,
// and joins every later warm-up and FirstRunPreparation's plan without this file being edited. The cache entries a warm state
// needs (IsWarm, the marker) are counted over the families of programs that draw (ED-NEW-warmupsplit-1: a program split into
// frame-sized parts is one family); a work step makes none.
//
// OD-0090 / ED-252 (lead's rulings, 2026-10-03, under the owner's zero-stutter bar): on ANGLE (D3D11) the knob's programs
// compile on their first draw, so the first open of the effort popover would hitch. GpuWarmup prepares them ahead, as a
// reusable service:
//   WarmAsync(window): the one-time GPU preparation, awaitable, one program step a frame, each reported (the first-run
//     "Getting ready" screen, OD-0096, awaits it; the screen itself is designed separately). It completes when every program
//     is compiled and, where the GPU keeps them (GpuProgramCache, ANGLE's program binaries on disk), in the cache. It fails
//     (never completes as if prepared) when a step is not drawn within 10 s (the window hidden or minimised), when the window
//     closes meanwhile, or when the window is not shown. The idle runs going in the same window (its model chips') step
//     now too; the runs share the window's GPU context and step through the programs in one order, so a frame still makes
//     one program at most (a run behind another finds its programs made).
//   IsWarm: a cheap check (a marker file and DXGI) that this build's programs are in the cache, made on the graphics adapter
//     the app draws with, with the driver it has now, so later launches can skip that screen: they only load the programs'
//     binaries (about 4 to 17 ms a step, measured, no compile).
//   WhenIdle(host): the fallback the composer's model chip starts. It steps only in frames where nothing animates (no
//     frame-clock request waiting: the liquid, the knob, the port's transitions; no Avalonia transition or animation
//     running: the drag handles' grips and glows, the shortcuts page; nor any animation registered with AddActivity: the
//     composer's working dots), and once 3 s have passed since it began it steps anyway, one step a frame, so the popover
//     is ready. A step its window does not draw (minimised, the chip hidden) is given up after 10 s, compiles nothing when
//     drawn later, and the warm-up resumes, idle again, once its host is drawn again (in two frames in a row, not a single
//     one). It holds its host only while the host is in a window.
// Each step runs on the render thread in a 1 px composition visual that draws nothing on screen: it draws one of the knob's
// four programs (the noise, the fluid, the orb, the page with the shadows and the blit) offscreen into the knob's own
// surfaces on the window's GPU context, so the knob's first visible draw finds them made. A new scaling warms again,
// idle-gated. A new GPU context (a device lost, a display change) warms again too: idle-gated when no step waits, and when a
// step waits it is answered at once, compiling nothing on the new context, and its run starts again from its first step (the
// programs it made are on the old one). The CPU renderer has nothing to compile ahead: no steps, and its completed pass is kept as
// a CPU marker (GpuProgramCache.MarkWarmOnCpu: build and machine) so that IsWarm holds for it too (ED-288 (17)).

/// <summary>One step of the GPU warm-up: which program, how long it took on the render thread, and whether it waited for an
/// idle frame and was forced at the cap.</summary>
public sealed record GpuWarmupStep(int Index, int Count, string Program, double Milliseconds, bool Forced, string StepId = "");

/// <summary>What a drawn program is handed (the render thread): the window's GPU context (null: the CPU renderer, or a test
/// pretending a GPU) and the window's render scaling.</summary>
public readonly record struct GpuWarmContext(GRContext? Gpu, double Scaling);

/// <summary>
/// One entry of the warm-up's registry (see the file note): a program drawn on the render thread (<see cref="Draw"/>) or a step
/// of other work awaited on the UI thread (<see cref="Work"/>), and the first-run step it belongs to.
/// </summary>
public sealed class GpuProgram
{
    private readonly Action<GpuWarmContext>? drawing;
    private readonly Func<CancellationToken, Task>? working;

    private GpuProgram(string name, FirstRunStep step, Action<GpuWarmContext>? drawing, Func<CancellationToken, Task>? working, string? family = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(step);
        if (family is not null) ArgumentException.ThrowIfNullOrWhiteSpace(family);
        Name = name;
        Step = step;
        Family = family ?? name;
        this.drawing = drawing;
        this.working = working;
    }

    /// <summary>The program's name, as the warm-up's reports give it (unique in the registry).</summary>
    public string Name { get; }

    /// <summary>The first-run step it belongs to.</summary>
    public FirstRunStep Step { get; }

    /// <summary>
    /// The family it is a part of (ED-NEW-warmupsplit-1): a program too big for one frame is registered as several parts, each
    /// its own step, under one family (its own name when it is whole). The cache's proof of a warm machine counts families:
    /// each family that draws links at least one cache entry, as each whole program did before it was split.
    /// </summary>
    public string Family { get; }

    /// <summary>True for a step of other work, false for a program that draws (and so makes its cache entry).</summary>
    public bool IsWork => working is not null;

    internal Action<GpuWarmContext> Drawing => drawing ?? throw new InvalidOperationException("GPU warm-up: " + Name + " is a work step.");
    internal Func<CancellationToken, Task> Working => working ?? throw new InvalidOperationException("GPU warm-up: " + Name + " draws.");

    /// <summary>A program drawn on the render thread: <paramref name="draw"/> draws it once offscreen (a frame's step) and
    /// leaves nothing on screen. A throw faults the warm-up (it never completes as if prepared).</summary>
    public static GpuProgram Draw(string name, FirstRunStep step, Action<GpuWarmContext> draw, string? family = null)
    {
        ArgumentNullException.ThrowIfNull(draw);
        return new(name, step, draw, null, family);
    }

    /// <summary>A step of other work (the voice hardware check): awaited on the UI thread in a first-run warm-up, in its step's
    /// place; the idle fallback never runs it. It makes no cache entry.</summary>
    public static GpuProgram Work(string name, FirstRunStep step, Func<CancellationToken, Task> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new(name, step, null, run);
    }
}

/// <summary>Prepares the knob's GPU programs ahead of their first use (see the file note).</summary>
public static class GpuWarmup
{
    /// <summary>The programs that draw, in the order a warm-up draws them, one step each.</summary>
    public static IReadOnlyList<string> Programs => Registered.Where(p => !p.IsWork).Select(p => p.Name).ToArray();

    // the registry, kept in step order then registration order; a snapshot replaced whole under the lock, so a reader (a run
    // in the middle of a pass) never sees it change under it
    private static readonly List<GpuProgram> registry = [];
    private static GpuProgram[] registered = [];

    /// <summary>Every entry of the registry (programs that draw and work steps), in the order a warm-up runs them: by step
    /// order, then by registration.</summary>
    public static IReadOnlyList<GpuProgram> Registered => Volatile.Read(ref registered);

    /// <summary>
    /// Adds a program or step to the registry; it joins the warm-ups that begin after it (a pass takes the registry as it is
    /// when it begins) and FirstRunPreparation's plan. Disposing the result removes it (once). Refused (ArgumentException):
    /// a name already registered, a step id given another name or order than its first registration gave it, and an order
    /// that another step already has (a step's programs stay together).
    /// </summary>
    public static IDisposable Register(GpuProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        lock (registry)
        {
            if (registry.Any(p => p.Name == program.Name))
                throw new ArgumentException("GPU warm-up: a program named " + program.Name + " is already registered.", nameof(program));
            if (registry.Any(p => p.Step.Id == program.Step.Id && p.Step != program.Step))
                throw new ArgumentException("GPU warm-up: the step " + program.Step.Id + " is already registered with another name or order.", nameof(program));
            if (registry.FirstOrDefault(p => p.Step.Order == program.Step.Order && p.Step.Id != program.Step.Id) is { } sharing)
                throw new ArgumentException("GPU warm-up: the order " + program.Step.Order + " belongs to the step " + sharing.Step.Id + ".", nameof(program));
            registry.Insert(registry.FindLastIndex(p => p.Step.Order <= program.Step.Order) + 1, program);
            Volatile.Write(ref registered, registry.ToArray());
        }
        return new Registration(program);
    }

    private sealed class Registration(GpuProgram program) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lock (registry)
            {
                registry.Remove(program);
                Volatile.Write(ref registered, registry.ToArray());
                workDone.Remove(program);
            }
        }
    }

    // GR-1 review round 2 (ED-288 (14)): the work steps done in this process, in a first-run warm-up (never the idle
    // fallback's). The marker (ED-252) says the machine is prepared, and a registered work step (the voice hardware check) is part
    // of being prepared: a pass writes the marker only when every step of other work registered at that moment has been done,
    // so an idle fallback after a first run that failed or was cancelled before its work step cannot make the next launch skip
    // the first run. Guarded by the registry's lock.
    private static readonly HashSet<GpuProgram> workDone = [];

    private static void WorkWasDone(GpuProgram program)
    {
        lock (registry) workDone.Add(program);
    }

    /// <summary>True when every step of other work registered now has been done in this process (none registered: true).</summary>
    private static bool WorkIsDone()
    {
        lock (registry) return registry.All(p => !p.IsWork || workDone.Contains(p));
    }

    static GpuWarmup()
    {
        foreach (var program in Builtins()) Register(program);
    }

    /// <summary>How many steps a warm-up takes: the programs that draw (a work step is no program).</summary>
    public static int Steps => Registered.Count(p => !p.IsWork);

    /// <summary>How many families the programs that draw make (ED-NEW-warmupsplit-1): the cache entries a warm state needs.</summary>
    internal static int Families => FamiliesOf(Registered);

    private static int FamiliesOf(IEnumerable<GpuProgram> programs) => programs.Where(p => !p.IsWork).Select(p => p.Family).Distinct(StringComparer.Ordinal).Count();

    /// <summary>How long the idle fallback waits for a quiet frame before it steps anyway (tests shorten it).</summary>
    public static TimeSpan IdleCap { get; internal set; } = TimeSpan.FromSeconds(3);

    /// <summary>How long a step waits for its window to draw it before the warm-up gives up on it.</summary>
    internal static TimeSpan StepTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// True when the app draws on the CPU renderer: Avalonia's Win32 platform bound no graphics platform (ANGLE/D3D11 and WGL
    /// both could not be made, so it fell back to software), and no test pretends a GPU. Known as soon as the platform is up, so
    /// before any window is drawn (the window RunAsync is given draws with that platform). Where the platform cannot be read it is
    /// false: the CPU marker then counts for nothing, and the GPU's marker is the only prepared state. GR-1 review round 4,
    /// ED-288 (18).
    /// </summary>
    internal static bool DrawsOnCpu => !PretendGpu && PlatformDrawsOnGpu() == false;

    // Avalonia's locator holds the graphics platform the Win32 platform made (IPlatformGraphics: ANGLE, else WGL) and holds none
    // for the software fallback. The locator's members are not in the reference assembly (PrivateApi), so it is read by
    // reflection, as the clock above is (a test pins that the pinned Avalonia is read). null: it could not be read.
    private static readonly System.Reflection.PropertyInfo? locatorCurrent = typeof(Avalonia.Animation.Animatable).Assembly
        .GetType("Avalonia.AvaloniaLocator")?.GetProperty("Current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

    internal static bool? PlatformDrawsOnGpu()
    {
        try
        {
            if (locatorCurrent?.GetValue(null) is not { } locator) return null;
            var service = locator.GetType().GetMethod("GetService", [typeof(Type)]);
            return service is null ? null : service.Invoke(locator, [typeof(IPlatformGraphics)]) is not null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Tests only: the CPU renderer answers each step as a GPU context would (the driver's whole flow without a
    /// GPU). Never set in the app.</summary>
    internal static bool PretendGpu { get; set; }

    /// <summary>Tests only, with <see cref="PretendGpu"/>: the GPU context the CPU renderer pretends to draw with (0: none
    /// told apart); a change is a new GPU context. Never set in the app.</summary>
    internal static nint PretendContext { get => Volatile.Read(ref pretendContext); set => Volatile.Write(ref pretendContext, value); }
    private static nint pretendContext;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Visual, Driver> drivers = new();
    private static readonly List<Activity> activities = [];

    // the native record and tests: every step done in this process, with its time (the last DoneKept kept: a whole pass, its
    // families drawn in frame-sized parts (ED-NEW-warmupsplit-1), and a model chip's warm-up adds four, and one runs for every
    // composer made in a session)
    internal static int stepsDone, stepsAsked;
    internal static int StepsDone => Volatile.Read(ref stepsDone);
    internal static int StepsAsked => Volatile.Read(ref stepsAsked);
    internal static readonly System.Collections.Concurrent.ConcurrentQueue<GpuWarmupStep> Done = new();
    internal const int DoneKept = 256;
    internal static long FirstStepTicks;

    /// <summary>Keeps a step done in <see cref="Done"/>, the oldest let go beyond <see cref="DoneKept"/>.</summary>
    internal static void Record(GpuWarmupStep step)
    {
        Done.Enqueue(step);
        while (Done.Count > DoneKept) Done.TryDequeue(out _);
    }

    /// <summary>Why the last automatic (idle) warm-up stopped short, for the native record (null: none did).</summary>
    internal static volatile string? LastGiveUp;

    /// <summary>
    /// Registers an animation the frame clock cannot see (a composition-thread one): while it answers true, the idle
    /// fallback does not step. Disposing the result removes that registration (once; a second Dispose does nothing).
    /// </summary>
    public static IDisposable AddActivity(Func<bool> isAnimating)
    {
        ArgumentNullException.ThrowIfNull(isAnimating);
        var activity = new Activity(isAnimating, activities);
        lock (activities) activities.Add(activity);
        return activity;
    }

    // removed by identity: two registrations of equal delegates are two activities
    private sealed class Activity(Func<bool> isAnimating, List<Activity> list) : IDisposable
    {
        public bool IsAnimating() => isAnimating();
        public void Dispose() { lock (list) list.Remove(this); }
    }

    /// <summary>Registrations not yet disposed (tests).</summary>
    internal static int ActivityCount { get { lock (activities) return activities.Count; } }

    private static readonly List<Activity> holds = [];

    /// <summary>
    /// ED-NEW-launch-fix (6): while <paramref name="holding"/> answers true (read on the UI thread), the idle fallback does not
    /// step at all, not even once its cap ran out: Boot B holds it until it finishes, so the render thread compiles nothing of
    /// the idle warm-ups while Boot B draws (its own awaited preparation, WarmAsync, is not held). Disposing the result lets go
    /// (once); a hold whose window has gone answers false, so a hold never left behind can stop the fallback for good.
    /// </summary>
    internal static IDisposable HoldIdle(Func<bool> holding)
    {
        ArgumentNullException.ThrowIfNull(holding);
        var hold = new Activity(holding, holds);
        lock (holds) holds.Add(hold);
        return hold;
    }

    /// <summary>True while a hold (<see cref="HoldIdle"/>) answers true.</summary>
    internal static bool Held()
    {
        lock (holds)
            foreach (var hold in holds)
                if (hold.IsAnimating()) return true;
        return false;
    }

    /// <summary>
    /// ED-NEW-warmupsplit-2: how long after the user's last input (a key down or up, text, the pointer pressed, released or
    /// turning its wheel, in any of the app's windows or popups) the idle fallback waits before it asks a step,
    /// cap or not. A popup that opens or closes, or a window that repaints, makes the UI thread wait for the render thread's
    /// next frame (Avalonia 12.1.2, decompiled: CompositingRenderer.Paint and Dispose wait on MediaContext's synchronous
    /// commit), so a step drawn just then would hold the key's handling for as long as it takes. Tests shorten it.
    /// </summary>
    internal static TimeSpan InputQuiet { get; set; } = TimeSpan.FromMilliseconds(300);

    private static long lastInput; // the timestamp of the last input, on lastInputClock (null: none yet)
    private static TimeProvider? lastInputClock;
    private static int watchingInput;

    /// <summary>Notes input now (the class handlers below; tests).</summary>
    internal static void NoteInput()
    {
        var clock = SwiftClock.Provider.Current;
        Volatile.Write(ref lastInput, clock.GetTimestamp());
        Volatile.Write(ref lastInputClock, clock);
    }

    /// <summary>True when input came less than <see cref="InputQuiet"/> ago (read on <paramref name="clock"/>, the clock the
    /// input was noted on; input noted on another clock, a test's earlier one, counts as long ago).</summary>
    internal static bool InputRecent(TimeProvider clock)
    {
        return ReferenceEquals(Volatile.Read(ref lastInputClock), clock) && clock.GetElapsedTime(Volatile.Read(ref lastInput)) < InputQuiet;
    }

    // class handlers on every TopLevel (windows and popups alike), handled events too: they see every key, text,
    // press, release and wheel first (what opens and closes popups and repaints), cost a timestamp each, and are added once a
    // process; a pointer that only moves does not hold the warm-up
    internal static void WatchInput()
    {
        if (Interlocked.Exchange(ref watchingInput, 1) != 0) return;
        // every route an event can take to its window (a key goes Direct and Bubble, a press Tunnel and Bubble): a handler may
        // see an event twice, which only notes the same input again
        const Avalonia.Interactivity.RoutingStrategies routes = Avalonia.Interactivity.RoutingStrategies.Direct
            | Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble;
        Avalonia.Input.InputElement.KeyDownEvent.AddClassHandler<TopLevel>((_, _) => NoteInput(), routes, handledEventsToo: true);
        Avalonia.Input.InputElement.KeyUpEvent.AddClassHandler<TopLevel>((_, _) => NoteInput(), routes, handledEventsToo: true);
        Avalonia.Input.InputElement.TextInputEvent.AddClassHandler<TopLevel>((_, _) => NoteInput(), routes, handledEventsToo: true);
        Avalonia.Input.InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, _) => NoteInput(), routes, handledEventsToo: true);
        Avalonia.Input.InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>((_, _) => NoteInput(), routes, handledEventsToo: true);
        Avalonia.Input.InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>((_, _) => NoteInput(), routes, handledEventsToo: true);
    }

    /// <summary>True when something on screen animates: a frame-clock request is waiting (on <paramref name="frames"/>, the
    /// frame clock's source by default), an Avalonia transition or animation runs, or a registered activity runs.</summary>
    internal static bool Animating(IFrameSource? frames = null)
    {
        if ((frames ?? FrameClock.Source.Current).Pending > 0) return true;
        if (AvaloniaAnimates()) return true;
        lock (activities)
            foreach (var activity in activities)
                if (activity.IsAnimating()) return true;
        return false;
    }

    // Avalonia's own animations: a transition (DoubleTransition and the others: the drag handles' grips and glows, the
    // shortcuts page's reset buttons and key caps) or a keyframe animation runs on Avalonia's global clock, which the frame
    // clock does not see. Each subscribes to that clock while it runs and lets go a frame after it ends (Avalonia 12.1.2,
    // decompiled: TransitionInstance's TransitionClock and AnimationInstance subscribe to the IGlobalClock that Application
    // binds to MediaContext.Instance.Clock, a MediaContextClock holding its subscribers in _observers; all internal, so read
    // by reflection, once per MediaContext). The UI thread's to read; elsewhere, or where it cannot be read, false.
    private static readonly System.Reflection.PropertyInfo? mediaContextInstance = typeof(Avalonia.Animation.Animatable).Assembly
        .GetType("Avalonia.Media.MediaContext")?.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    private static object? clockOwner;
    private static System.Collections.ICollection? clockSubscribers;

    /// <summary>True when an Avalonia transition or animation runs: its global clock has subscribers (the UI thread).</summary>
    internal static bool AvaloniaAnimates() => AvaloniaClockSubscribers() is { Count: > 0 };

    /// <summary>The subscribers of Avalonia's global clock (null where they cannot be read: off the UI thread, or an
    /// Avalonia that keeps them elsewhere; tests pin that the pinned Avalonia is read).</summary>
    internal static System.Collections.ICollection? AvaloniaClockSubscribers()
    {
        if (!Dispatcher.UIThread.CheckAccess()) return null;
        try
        {
            if (mediaContextInstance?.GetValue(null) is not { } context) return null;
            if (!ReferenceEquals(context, clockOwner))
            {
                var clock = context.GetType().GetProperty("Clock", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.GetValue(context);
                clockSubscribers = clock?.GetType().GetField("_observers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(clock) as System.Collections.ICollection;
                clockOwner = context;
            }
            return clockSubscribers;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// True when a warm-up of this build completed on this machine as it is now: the marker names this build, a GPU folder
    /// and the graphics adapter the app draws with and its current driver (DXGI), that folder's key names the same build and
    /// adapter, and the folder holds an entry for each family of programs. An update of that adapter's driver, a move of the app to another
    /// adapter or a new build is not warm (OD-0096); a change to an adapter the app does not draw with leaves it warm.
    /// </summary>
    public static bool IsWarm
    {
        get
        {
            if (GpuProgramCache.Root() is not { } root) return false;
            // the CPU renderer keeps nothing: its marker (GR-1 review round 3, ED-288 (17)) names this build and machine, and
            // counts only while the app draws on the CPU renderer (round 4, (18)): the machine's name reads the same whether ANGLE
            // came up or Avalonia fell back to software, and a launch that draws on the GPU needs the GPU's marker
            if (DrawsOnCpu && GpuProgramCache.Machine() is { Length: > 0 } drawn && GpuProgramCache.WarmedOnCpu(root, drawn)) return true;
            try
            {
                var marker = Path.Combine(root, "warm.txt");
                if (!File.Exists(marker)) return false;
                var lines = File.ReadAllLines(marker);
                if (lines.Length < 3 || lines[0] != GpuProgramCache.Build) return false;
                if (lines[1].Length == 0 || Path.GetFileName(lines[1]) != lines[1]) return false; // a folder's name, nothing else
                if (GpuProgramCache.Machine() is not { Length: > 0 } here || lines[2] != here) return false;
                var folder = Path.Combine(root, lines[1]);
                var steps = Families; // an entry for each family that draws (ED-NEW-warmupsplit-1: a split program's parts are one family)
                return Directory.Exists(folder) && GpuProgramCache.MadeHere(folder, here)
                    && Directory.EnumerateFiles(folder, "*.bin").Take(steps).Count() == steps;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    /// <summary>
    /// The first-run step "Graphics card" (FirstRunPreparation): reads the identity of the graphics adapter the app draws with and
    /// its driver (DXGI) on a pool thread, ahead of the render thread's install of the program cache. A warm-up's pass does it
    /// again as it begins (it is cheap), so a pass after a new GPU context names the adapter as it is then.
    /// </summary>
    internal static Task PrepareGraphicsAsync(CancellationToken cancellation = default) => Task.Run(GpuProgramCache.Prepare, cancellation);

    /// <summary>
    /// Warms the programs in <paramref name="window"/> now, one step a frame (no waiting for idle frames: the caller shows a
    /// screen meant for it), reporting each step; completes when they are compiled and cached (at once on the CPU renderer).
    /// Faults with a <see cref="TimeoutException"/> when a step is not drawn in time (the window minimised or hidden) and an
    /// <see cref="InvalidOperationException"/> when the window is not shown or closes meanwhile. Called while a warm-up of
    /// the window runs, it hurries that one and reports its remaining steps; called while an idle-gated one of the window runs
    /// (a new scaling or GPU context began it: its items leave the work steps out), it hurries that one to its end and then
    /// runs its own pass, which reports every program and step and runs the work steps. The idle runs going in the window (its
    /// hosts') are hurried too, so none of them waits to compile after the screen meant for it.
    /// </summary>
    public static Task WarmAsync(TopLevel window, IProgress<GpuWarmupStep>? progress = null, CancellationToken cancellation = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        WatchInput();
        var driver = drivers.GetValue(window, _ => new Driver(window, FrameClock.Source.Current, SwiftClock.Provider.Current));
        foreach (var (host, other) in drivers)
            if (other != driver && TopLevel.GetTopLevel(host) == window) other.Hurry();
        return driver.Run(idle: false, progress, cancellation);
    }

    /// <summary>The idle fallback (the composer's model chip): once its window has drawn its first frame, step only in
    /// frames where nothing animates, forced after the cap. Calling it again for the same host does nothing.</summary>
    public static void WhenIdle(Control host)
    {
        if (drivers.TryGetValue(host, out _)) return;
        WatchInput();
        var driver = new Driver(host, FrameClock.Source.Current, SwiftClock.Provider.Current); // the views' frame source and clock, taken here (the steps run in other flows)
        drivers.Add(host, driver);
        host.AttachedToVisualTree += (_, _) => driver.Attached();
        host.DetachedFromVisualTree += (_, _) => driver.Detached();
        if (TopLevel.GetTopLevel(host) is not null) driver.Attached();
    }

    /// <summary>
    /// ED-NEW-lightv2-3 (OD-0139): warms <paramref name="programs"/>, which are not in the registry (a Composer Light v2 light: the one
    /// selected at launch, one previewed or picked), in <paramref name="host"/>'s window, gated exactly as the idle fallback is (two
    /// calm frames, never within InputQuiet of input or while held, forced one step a frame after IdleCap), each program compiled first
    /// on the worker's own GPU context (ED-NEW-warmupsplit-2) so the render thread's step loads its binary. It writes no marker and
    /// adds no family (IsWarm is the registry's), waits while a registry pass runs in that window, and runs once a process per program
    /// and scaling (a run that failed may run again). <paramref name="host"/> gets the warm-up's 1 px visual as its child visual, so it
    /// must be a control of its own (not one that draws through a child visual). Completes when the steps are drawn.
    /// </summary>
    public static Task WarmWhenIdle(Control host, IReadOnlyList<GpuProgram> programs)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (programs.Count == 0 || programs.Any(p => p.IsWork)) throw new ArgumentException("GPU warm-up: on-demand programs draw.", nameof(programs));
        if (TopLevel.GetTopLevel(host) is not { } top) return Task.FromException(new InvalidOperationException("GPU warm-up: its host is not in a window."));
        var key = string.Join("|", programs.Select(p => p.Name)) + "@" + top.RenderScaling.ToString(System.Globalization.CultureInfo.InvariantCulture);
        lock (onDemand)
        {
            if (onDemand.TryGetValue(key, out var going) && !going.IsFaulted && !going.IsCanceled) return going;
            WatchInput();
            var driver = onDemandDrivers.GetValue(host, _ => new Driver(host, FrameClock.Source.Current, SwiftClock.Provider.Current));
            var run = driver.RunOnly(programs.ToArray());
            onDemand[key] = run;
            return run;
        }
    }

    private static readonly Dictionary<string, Task> onDemand = [];
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Visual, Driver> onDemandDrivers = new();
    private static readonly Dictionary<(string, double), Task> onDemandWorker = [];
    private static int registryPasses; // registry passes under way (an on-demand pass waits for them: their marker counts cache entries)

    /// <summary>True when <paramref name="host"/> has its idle fallback scheduled (tests).</summary>
    internal static bool IsScheduled(Control host) => drivers.TryGetValue(host, out _);

    // UI thread: asks the render thread for one step at a time, waits for each, decides the next
    // the idle cap is read on the port's one clock (ED-068): TimeProvider.System in the app, a test's manual clock in a test
    private sealed class Driver(Visual host, IFrameSource frames, TimeProvider clock)
    {
        private CompositionCustomVisual? visual;
        private TopLevel? watched; // the window whose scaling it follows: its host's, while the host is in it
        private Task? running;
        private bool again, hurry, runningIdle;
        private int quiet; // calm frames in a row, kept from step to step in a pass (ED-NEW-warmupsplit-1)
        private GpuProgram[]? only; // ED-NEW-lightv2-3: an on-demand pass's programs (not the registry's)
        private bool onDemandDriver; // ED-NEW-lightv2-3: this driver only ever runs on-demand passes (a new scaling or context does not start a registry pass in it)
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
            if (onDemandDriver) return;
            again = true;
            if (running is { IsCompleted: true }) Start();
        }

        // WarmAsync in its window: the run going steps now, no more idle waits
        public void Hurry()
        {
            if (running is { IsCompleted: false }) hurry = true;
        }

        // an automatic run: idle-gated, nobody awaits it (why it stopped short is kept for the native record)
        private void Start()
        {
            if (running is { IsCompleted: false }) return;
            Run(idle: true, null, default).ContinueWith(static run => LastGiveUp = run.Exception?.InnerException?.Message,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        /// <summary>ED-NEW-lightv2-3: an idle-gated pass over <paramref name="programs"/> alone (after the one going, if any).</summary>
        public async Task RunOnly(GpuProgram[] programs)
        {
            onDemandDriver = true;
            if (running is { IsCompleted: false } going) { try { await going; } catch (Exception) { } }
            only = programs;
            try { await Run(idle: true, null, CancellationToken.None); }
            finally { only = null; }
        }

        public Task Run(bool idle, IProgress<GpuWarmupStep>? progress, CancellationToken cancellation)
        {
            if (running is { IsCompleted: false } going)
            {
                if (idle) return going.WaitAsync(cancellation);
                hurry = true; // WarmAsync joins it: no more idle waits
                if (runningIdle) return AfterIdle(going, progress, cancellation); // its items leave the work steps out
                this.progress = progress ?? this.progress;
                return going.WaitAsync(cancellation);
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
            runningIdle = idle;
            return running = Steps(top, idle, cancellation);
        }

        // WarmAsync found an idle-gated run going (begun by a new scaling or GPU context): that run took its items with the work
        // steps left out (a first run's), so it is hurried to its end, silently, and the first run's own pass follows it, reporting
        // every program and step (the programs it made are found made) and running the work steps. The idle run's own failure is
        // not this caller's: its pass tells.
        private async Task AfterIdle(Task idleRun, IProgress<GpuWarmupStep>? progress, CancellationToken cancellation)
        {
            try { await idleRun.WaitAsync(cancellation); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception) { }
            await Run(idle: false, progress, cancellation);
        }

        // a step given up on (its host not drawn): the fallback starts again, idle-gated, once its host is drawn again (the
        // primer answers the withdrawn request then; nothing runs on the UI thread until it does)
        private void WhenDrawnAgain(StepRequest request) =>
            request.Answer.Task.ContinueWith(_ => Dispatcher.UIThread.Post(Start), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        private async Task Steps(TopLevel top, bool idle, CancellationToken cancellation)
        {
            var mine = only;
            if (mine is not null)
            {
                // ED-NEW-lightv2-3: a registry pass counts the cache's new entries for its marker, so an on-demand pass never runs beside one
                while (Volatile.Read(ref registryPasses) > 0) await Task.Delay(50, cancellation);
            }
            else Interlocked.Increment(ref registryPasses);
            try { await StepsOf(top, idle, mine, cancellation); }
            finally { if (mine is null) Interlocked.Decrement(ref registryPasses); }
        }

        private async Task StepsOf(TopLevel top, bool idle, GpuProgram[]? mine, CancellationToken cancellation)
        {
            var worked = new HashSet<GpuProgram>(); // work steps done in this run: a pass started again does not repeat them
            do
            {
                again = false;
                // the SkSL compile and the identity of the adapter drawn with (DXGI), on a pool thread, each pass (a pass
                // after a new GPU context installs the cache on it naming the adapter as it is then)
                await Task.Run(() => { JewelKnobShader.Prepare(); ComposerLightRenderer.Prepare(); GpuProgramCache.Prepare(); }, cancellation);
                // GR-1: the registry as it is as the pass begins; the idle fallback draws only (a work step is a first run's)
                var items = mine ?? Registered.Where(p => !idle || !p.IsWork).ToArray();
                var drawn = FamiliesOf(items);
                var scaling = top.RenderScaling;
                var since = clock.GetTimestamp(); // the cap counts from the warm-up's start: once it ran out, one step a frame
                if (!idle && top is Avalonia.Controls.Window { Content: AuroraBootStage })
                {
                    await GpuBootPreparation.Run(items, scaling, cancellation);
                    since = clock.GetTimestamp(); // (Boot B holds the steps while it draws: its cap from here, as before)
                }
                else if (idle && mine is not null)
                    await OnDemandWorkerPass(mine, scaling, cancellation); // ED-NEW-lightv2-3: its own worker pass, not the registry's
                else if (idle)
                    await WorkerPass(items, scaling, cancellation); // ED-NEW-warmupsplit-2: compiles and first runs off the render thread, inside the cap
                var entries = GpuProgramCache.Entries;
                quiet = 0;
                var restarted = false;
                var gpuLess = false;
                string? passFolder = null;
                for (var step = 0; step < items.Length; step++)
                {
                    var item = items[step];
                    cancellation.ThrowIfCancellationRequested();
                    if (item.IsWork)
                    {
                        if (!worked.Add(item)) continue;
                        var begun = System.Diagnostics.Stopwatch.GetTimestamp();
                        await Worked(item, cancellation); // a throw, a timeout or a cancellation ends the run: never complete as if prepared
                        WorkWasDone(item);
                        var made = new GpuWarmupStep(step, items.Length, item.Name, System.Diagnostics.Stopwatch.GetElapsedTime(begun).TotalMilliseconds, false, item.Step.Id);
                        Record(made);
                        progress?.Report(made);
                        continue;
                    }
                    if (gpuLess) continue; // the CPU renderer: nothing to compile ahead (the work steps still run)
                    var forced = idle && !hurry && await CapRanOut(top, since, cancellation);
                    if (TopLevel.GetTopLevel(host) != top || top.PlatformImpl is null)
                        throw new InvalidOperationException("GPU warm-up: its window closed (or its host left it) before the " + item.Name + " step.");
                    var request = new StepRequest(item, scaling);
                    Interlocked.Increment(ref stepsAsked);
                    visual!.SendHandlerMessage(request);
                    StepAnswer answer;
                    try { answer = await request.Answer.Task.WaitAsync(StepTimeout, cancellation); }
                    catch (TimeoutException)
                    {
                        // a window that is not drawn (minimised, the host hidden) never answers: when it is drawn again the
                        // primer compiles nothing for this request, it only says so, and the fallback starts again from there
                        request.Withdrawn = true;
                        if (idle) WhenDrawnAgain(request);
                        throw new TimeoutException($"GPU warm-up: the {item.Name} step was not drawn within {StepTimeout.TotalSeconds:0.###} s (its window minimised or hidden, or its host hidden).");
                    }
                    catch (OperationCanceledException) { request.Withdrawn = true; throw; }
                    // drawn on a new GPU context: the programs made so far are on the old one, so the run starts again
                    if (answer.NewContext) { restarted = again = true; break; }
                    if (!answer.Gpu) { gpuLess = true; continue; }
                    passFolder = answer.CacheFolder ?? (PretendGpu ? GpuProgramCache.Folder : null);
                    var report = new GpuWarmupStep(step, items.Length, item.Name, answer.Ms, forced, item.Step.Id);
                    Record(report);
                    progress?.Report(report);
                }
                if (gpuLess)
                {
                    // the CPU renderer has nothing to compile or keep, so there is no GPU marker to write: the machine is prepared
                    // once every registered work step is done (the same bar as below), and the next launch skips the first run
                    if (WorkIsDone()) await Task.Run(GpuProgramCache.MarkWarmOnCpu, CancellationToken.None);
                    return;
                }
                // warm only when the cache took (or gave back) an entry for every program that draws: the programs are on disk
                // (the marker and the clean-up of other builds' folders on a pool thread, the UI thread's frames untouched), and
                // every registered step of other work has been done (an idle pass never runs one: it leaves the machine unprepared
                // for a first run to do it)
                if (mine is null && !restarted && passFolder is { } cached)
                {
                    await GpuProgramCache.Drain(cached); // persistence completes before Entries is assessed, also in idle/non-Boot passes
                    if (GpuProgramCache.Entries - entries >= drawn && WorkIsDone())
                        await Task.Run(() => GpuProgramCache.MarkWarm(cached), CancellationToken.None);
                }
            }
            while (again);
        }

        // a work step, bounded like a draw step (StepTimeout) and ended by the cancellation even when it ignores the token (GR-1 review
        // round 4, ED-288 (19)): one that never answers (an audio driver that hangs) must not hold the first run, and the
        // Getting ready sheet over the app, for ever. On a timeout it is not done (WorkWasDone is never reached), so the machine is
        // not marked prepared and the next launch tries again; what it throws later, unobserved by anyone, is observed here.
        private static async Task Worked(GpuProgram item, CancellationToken cancellation)
        {
            var working = item.Working(cancellation);
            try { await working.WaitAsync(StepTimeout, cancellation); }
            catch (TimeoutException)
            {
                Observe(working);
                throw new TimeoutException($"GPU warm-up: the {item.Name} step did not finish within {StepTimeout.TotalSeconds:0.###} s.");
            }
            catch (OperationCanceledException)
            {
                Observe(working);
                throw;
            }
        }

        private static void Observe(Task abandoned) =>
            abandoned.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        // waits for two frames in a row with nothing animating and no input for InputQuiet; once they came, the steps follow
        // one a frame while it stays so (ED-NEW-warmupsplit-1: a pass is many frame-sized steps; anything that animates or input
        // waits for two calm frames again). Once the cap (since the warm-up began) has run out it steps without waiting for
        // calm frames, but still never while held or right after input (ED-NEW-warmupsplit-2). The answer is the step's Forced
        // label (ED-NEW-warmupsplit-3): true only when, as the cap lets it step, something animates, so a step the cap let
        // through in a calm moment is not called forced.
        private async Task<bool> CapRanOut(TopLevel top, long since, CancellationToken cancellation)
        {
            while (true)
            {
                if (hurry) return false; // WarmAsync joined: step now
                var held = Held(); // Boot B draws (ED-NEW-launch-fix (6)): no step, not even at the cap
                var waiting = held || InputRecent(clock); // input just now: a sync paint may wait on the render thread (ED-NEW-warmupsplit-2)
                if (waiting || Animating(frames)) quiet = 0;
                else if (quiet >= 2) return false; // still calm since the last step's frame: the next one
                if (!waiting && clock.GetElapsedTime(since) >= IdleCap) return Animating(frames);
                var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                top.RequestAnimationFrame(_ => frame.TrySetResult());
                try { await frame.Task.WaitAsync(IdleCap, clock, cancellation); }
                catch (TimeoutException)
                {
                    // no frame at all (a closed or hidden window): the cap, but never while held or right after input (a timer's
                    // turn before waiting again, so a short cap does not spin the UI thread)
                    if (!Held() && !InputRecent(clock)) return Animating(frames);
                    await Task.Delay(1, cancellation);
                    continue;
                }
                quiet = held || InputRecent(clock) || Animating(frames) ? 0 : quiet + 1;
                // a timer's turn between checks: where frames come back at once (nothing drawn, a headless test's job queue)
                // the loop leaves the dispatcher free instead of spinning it until the cap
                await Task.Delay(1, cancellation);
            }
        }
    }

    // ---------- the programs registered here (GR-1) ----------
    // Each draws once, offscreen, through its owner's own entry point, so the owner's first visible draw finds the programs made
    // (and, on ANGLE, their binaries kept by GpuProgramCache). ENGINEERING DECISION (UNVERIFIED on a GPU until the supervised
    // native session): what a warm-up draws is a representative frame of each, so a program is made for each shader the owner
    // draws with: the composer light's three shader families, the liquid's passes with Grok's oil and its bubbles, and the
    // lightning with the lit glass. The target is an offscreen RGBA surface made by the window's GPU context (the knob's own way),
    // not the window's framebuffer; the liquid and the strike draw into it through a WarmTarget, because a RenderTargetBitmap is a
    // CPU raster whatever the window draws with (Avalonia 12.1.2) and would compile nothing.

    private static IEnumerable<GpuProgram> Builtins()
    {
        // the jewel knob's four programs (OD-0090, ED-252): the noise, the fluid, the orb, the page with the shadows and the blit
        string[] knob = ["noise", "fluid", "orb", "page"];
        for (var k = 0; k < knob.Length; k++)
        {
            var step = k;
            yield return GpuProgram.Draw(knob[k], FirstRunSteps.Knob,
                context => JewelKnobRenderer.For(context.Gpu, JewelOrb.RenderSize(EffortSlider.ThumbSize, context.Scaling).Size).Warm(step));
        }
        // the composer light (ED-251): Border beam's rotating beam (Large, the default: its blob bake and its shader), its moving
        // beam (Line, and the two pulses) and the Uiverse light; ED-NEW-warmupsplit-1: each light one GPU draw a step (its
        // bakes' blurred rectangles, the blob bake's passes, each slot's shader), so a step makes one program at most
        foreach (var kind in Enum.GetValues<LightKind>())
            foreach (var part in Parts(LightFamily(kind), FirstRunSteps.Lights, LightSites(kind), (context, k, n) => DrawLight(context, kind, k, n)))
                yield return part;
        // the slider liquid (OD-0051, OD-0075): every pass of the tube, Grok's oil and the bubbles, one pass a step
        foreach (var part in Parts("liquid", FirstRunSteps.Liquid, LiquidSites, (context, k, n) => DrawLiquid(context, strike: false, k, n)))
            yield return part;
        // the fast mode's lightning (OD-0073): the strike over the liquid and the glass that lights with it, and every Mix effect
        foreach (var part in StrikeWarmup.Programs) yield return part;
    }

    /// <summary>
    /// ED-NEW-warmupsplit-1: a frame drawn in <paramref name="count"/> parts, one numbered GPU draw site each (GpuWarmSites), as
    /// one family: part k draws only site k, the last part every site from its own on (a site added later is still warmed).
    /// Named "family[k]" (the family's own name when it is whole).
    /// </summary>
    internal static IEnumerable<GpuProgram> Parts(string family, FirstRunStep step, int count, Action<GpuWarmContext, int, int> draw)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        for (var k = 0; k < count; k++)
        {
            var part = k;
            yield return GpuProgram.Draw(count == 1 ? family : family + "[" + part + "]", step, context => draw(context, part, count), family);
        }
    }

    /// <summary>A part's scope: only site <paramref name="part"/> of <paramref name="parts"/> draws (the last, every later one too).</summary>
    internal static GpuWarmSites.Scope Site(int part, int parts) => GpuWarmSites.Only(part, andAfter: part == parts - 1);

    /// <summary>The family a light kind's parts make (the registry's names before the split).</summary>
    internal static string LightFamily(LightKind kind) => kind switch { LightKind.Moving => "light-moving", LightKind.Aura => "light-aura", _ => "light-rotate" };

    /// <summary>How many GPU draws (sites) a warm-up's frame of each light makes, so how many parts it is drawn in; a test
    /// counts them on the CPU and pins these.</summary>
    internal static int LightSites(LightKind kind) => kind switch { LightKind.Moving => 3, LightKind.Aura => 3, _ => 5 };

    /// <summary>How many GPU draws (sites) the liquid's warm-up frame makes (a test counts them and pins this).</summary>
    internal const int LiquidSites = 8;

    // ED-NEW-warmupsplit-2: before an idle pass's first step, once a process and scaling, every program it draws is drawn on
    // GpuBootPreparation's own GPU context, on its worker thread (Boot B's independent ANGLE D3D11 context: its own EGL display,
    // only the program binaries cross, through GpuProgramCache). A program not kept yet (no first run, or one that did not
    // finish) is compiled and kept there, so the render thread's step only loads it; and the drawing code's first run (its
    // JIT, its statics, its scene) is paid there too, so a step on the render thread is its GPU draw and little else. The
    // worker draws on the GPU as the steps do: a frame drawn on the CPU first was measured to change what 10 of the 43
    // programs compile to (Skia's caches are shared by both), so no part of the warm-up is drawn on the CPU. Shared by every
    // idle run; a failure (no ANGLE, no cache folder) only means the steps do it all as before.
    private static readonly Dictionary<double, Task> workerPasses = [];

    // ED-NEW-lightv2-3: an on-demand pass's worker pass, once a process per program and scaling (a failed one may run again)
    private static async Task OnDemandWorkerPass(GpuProgram[] items, double scaling, CancellationToken cancellation)
    {
        var passes = new List<Task>();
        lock (onDemandWorker)
            foreach (var item in items)
            {
                if (!onDemandWorker.TryGetValue((item.Name, scaling), out var pass) || pass.IsFaulted || pass.IsCanceled)
                    onDemandWorker[(item.Name, scaling)] = pass = GpuBootPreparation.Run([item], scaling, CancellationToken.None);
                passes.Add(pass);
            }
        try { await Task.WhenAll(passes).WaitAsync(cancellation); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception) { }
    }

    private static async Task WorkerPass(GpuProgram[] items, double scaling, CancellationToken cancellation)
    {
        Task? pass;
        lock (workerPasses)
        {
            if (!workerPasses.TryGetValue(scaling, out pass) || pass.IsFaulted || pass.IsCanceled)
                workerPasses[scaling] = pass = GpuBootPreparation.Run(items, scaling, CancellationToken.None);
        }
        try { await pass.WaitAsync(cancellation); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception) { }
    }

    /// <summary>The composer lights a warm-up draws (tests read what each draws).</summary>
    internal enum LightKind { Rotate, Moving, Aura }

    private const double LightWidth = 164, LightHeight = 52;

    /// <summary>The light a warm-up draws for one shader family: its default settings, at rest, in a composer-sized box.</summary>
    internal static ComposerLightAnimator LightAnimator(LightKind kind)
    {
        var settings = kind switch
        {
            LightKind.Moving => ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default with { Type = BeamType.Line } },
            LightKind.Aura => ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura },
            _ => ComposerLightSettings.Default,
        };
        return new ComposerLightAnimator(settings, ComposerLightPhase.Rest, dark: true, LightWidth, LightHeight, reduceMotion: false);
    }

    // the composer light's own renderer into a small offscreen surface: its first draw bakes (the blurred rectangles, the blob
    // fields) and runs the family's shader; a part draws only its site of that frame (ED-NEW-warmupsplit-1)
    internal static void DrawLight(GpuWarmContext context, LightKind kind, int part, int parts)
    {
        var light = LightAnimator(kind);
        using var renderer = new ComposerLightRenderer();
        var info = new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = (context.Gpu is { } gpu ? SKSurface.Create(gpu, false, info) : null) ?? SKSurface.Create(info);
        using (Site(part, parts)) renderer.Draw(surface.Canvas, context.Gpu, light, (float)Math.Max(1, context.Scaling));
    }

    private const int WarmSeed = 1;
    private static readonly Size LiquidSize = new(160, EffortSlider.TrackHeight);

    private sealed record LiquidScene(Size Size, double Time, LiquidSpec Spec, SKColor[] Gradient, BubbleLanes Bubbles, LiquidFlow Flow, IReadOnlyList<StrikeOp>? Strike, double Ink);

    // a tube of Grok's (the oil sheen) at a time a bubble is rising; for the strike, the fast look with the lightning 35 ms into
    // its first strike (the reduce-motion still)
    private static LiquidScene Scene(bool strike, double scale)
    {
        var brand = strike ? EffortBrand.Claude : EffortBrand.Grok;
        var spec = LiquidSpec.For(new TrackLook(strike ? TrackLook.Kind.Fast : TrackLook.Kind.Plain, brand));
        var size = LiquidSize;
        var bubbles = new BubbleLanes(WarmSeed, size.Width, size.Height);
        var time = 0.0;
        while (time < 30 && !Enumerable.Range(0, bubbles.Count).Any(i => bubbles.At(i, time) is not null)) time += 0.25;
        IReadOnlyList<StrikeOp>? lightning = null;
        var ink = 0.0;
        if (strike)
        {
            var tint = StrikeTint.For(brand);
            var geometry = new StrikeGeometry(size.Width, size.Height, size.Width - EffortSlider.ThumbSize / 2, EffortSlider.ThumbSize / 2);
            lightning = new StrikeChannels(WarmSeed).Scene(geometry, Strike.StillTime(WarmSeed), tint);
            ink = tint.Ink;
        }
        return new LiquidScene(size, time, spec, LiquidTrack.GradientFor(spec, (int)Math.Ceiling(size.Width * scale), ThemeVariant.Dark), bubbles,
            LiquidFlow.At(time, strike), lightning, ink);
    }

    /// <summary>The frame a warm-up draws for the liquid (<paramref name="strike"/> false) or the strike (tests read what it holds).</summary>
    internal static LiquidTrack.Frame LiquidFrame(bool strike, double scale = 1)
    {
        var scene = Scene(strike, scale);
        return new LiquidTrack.Frame(scene.Size, scene.Size.Width, scene.Time, scene.Gradient, scene.Spec, scene.Bubbles, false,
            scene.Spec.Segments.Select(segment => segment.Strand.Resolve(ThemeVariant.Dark)).ToArray(), scene.Flow, scene.Strike, scene.Ink);
    }

    // the liquid's own public entry point, LiquidTrack.Draw, into a target on the window's GPU context (WarmTarget): its custom
    // operation runs at once, here on the render thread, and makes its surfaces and compiles its shaders on that context
    internal static void DrawLiquid(GpuWarmContext context, bool strike) => DrawLiquid(context, strike, 0, 1);

    /// <summary>Part <paramref name="part"/> of <paramref name="parts"/> of the liquid's (or the strike's) warm-up frame: only its
    /// site draws (ED-NEW-warmupsplit-1).</summary>
    internal static void DrawLiquid(GpuWarmContext context, bool strike, int part, int parts)
    {
        var scale = Math.Max(1, context.Scaling);
        var scene = Scene(strike, scale);
        var info = new SKImageInfo((int)Math.Ceiling(scene.Size.Width * scale), (int)Math.Ceiling(scene.Size.Height * scale), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var target = WarmTarget.Open(context.Gpu, info, scale);
        using var site = Site(part, parts); // the custom operation runs at once, inside this part's scope
        LiquidTrack.Draw(target.Context, scene.Size, scene.Size.Width, scene.Time, scene.Spec, scene.Gradient, scene.Bubbles, false, ThemeVariant.Dark, scene.Flow, scene.Strike, scene.Ink);
    }

    /// <summary>
    /// An offscreen surface and the drawing context a program draws into it with, for a program that draws through its owner's
    /// public entry point taking a DrawingContext (LiquidTrack.Draw). With a GPU context the surface is made by it and the drawing
    /// context carries it (Avalonia.Skia's DrawingContextImpl.CreateInfo.GrContext), so what the entry point's custom operation reads off its lease
    /// (ISkiaSharpApiLeaseFeature: GrContext, SkSurface, the canvas's scaling) is the GPU's and it makes its own surfaces, and
    /// compiles its shaders, on that context. RenderTargetBitmap cannot do that: in Avalonia 12.1.2 it is always a CPU raster
    /// (Avalonia.Skia, decompiled: PlatformRenderInterface.CreateRenderTargetBitmap returns RenderTargetBitmapImpl, a
    /// WriteableBitmapImpl drawn through a FramebufferRenderTarget, no GRContext). A GPU context that makes no surface fails the
    /// target: a draw on the CPU would be reported as the GPU's.
    /// </summary>
    internal sealed class WarmTarget : IDisposable
    {
        /// <summary>The surface a GPU context makes for a target (tests substitute it: no GPU can be made there).</summary>
        internal static Func<GRContext, SKImageInfo, SKSurface?> GpuSurface { get; set; } = (gpu, info) => SKSurface.Create(gpu, true, info);

        /// <summary>Tests only: sees the surface as the target is disposed, after the drawing.</summary>
        internal static Action<SKSurface>? Inspect { get; set; }

        // Avalonia 12.1.2 (decompiled) keeps what makes such a context internal: Avalonia.Skia's DrawingContextImpl (an impl over a
        // surface, naming the GPU context: its CreateInfo's Surface, GrContext, Dpi and ScaleDrawingToDpi, public fields of a nested
        // struct) and Avalonia.Base's PlatformDrawingContext (the DrawingContext over an impl, as DrawingContextHelper.RenderAsync
        // makes it). Read once, by reflection (the way this file reads Avalonia's clock); where one is not found (another Avalonia)
        // the target fails, it does not draw somewhere else, and a test pins that the pinned Avalonia has them.
        private const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        private static readonly Type? implType = typeof(SkiaOptions).Assembly.GetType("Avalonia.Skia.DrawingContextImpl");
        private static readonly Type? infoType = implType?.GetNestedType("CreateInfo");
        private static readonly System.Reflection.ConstructorInfo? implConstructor = infoType is null ? null : implType!.GetConstructor(Any, [infoType, typeof(IDisposable[])]);
        private static readonly System.Reflection.ConstructorInfo? contextConstructor = typeof(DrawingContext).Assembly.GetType("Avalonia.Media.PlatformDrawingContext")
            ?.GetConstructor(Any, [typeof(Avalonia.Platform.IDrawingContextImpl), typeof(bool)]);

        /// <summary>True when the pinned Avalonia has the types a target is made of (tests pin it).</summary>
        internal static bool Available
        {
            get
            {
                var info = infoType;
                return info is not null && implConstructor is not null && contextConstructor is not null
                    && new[] { "Surface", "GrContext", "Dpi", "ScaleDrawingToDpi" }.All(name => info.GetField(name) is not null);
            }
        }

        private readonly IDisposable impl;

        private WarmTarget(SKSurface surface, IDisposable impl, DrawingContext context)
        {
            Surface = surface;
            this.impl = impl;
            Context = context;
        }

        /// <summary>The surface drawn into.</summary>
        public SKSurface Surface { get; }

        /// <summary>The drawing context over it, at the scaling given to <see cref="Open"/>.</summary>
        public DrawingContext Context { get; }

        /// <summary>The first exception a custom operation drawn through <see cref="Context"/> threw on this thread (null: none).
        /// Avalonia's PlatformDrawingContext.Custom (12.1.2, decompiled) catches what an operation's Render throws and only logs
        /// it, under the area "Visual" at Error, so the draw would return as if it had made its programs; the target hears that
        /// log, keeps the exception, and <see cref="Dispose"/> throws it, so the program faults the warm-up
        /// (ED-288 (7) and (15)).</summary>
        public Exception? Failure { get; private set; }

        private WarmTarget? enclosing; // the target this thread was watching for before this one
        private bool disposed;

        // The sink: installed over the app's own while any target is open (the app's still hears every event it enabled), and
        // taken off, restoring the app's, when the last closes. A thread watches the target it opened last; the log of a throw
        // comes from the thread that called Custom, synchronously, so it finds that target. (Avalonia.Logging.Logger.Sink is
        // global; where something else replaced it while a target was open, it is left as that set it.)
        private static readonly object watching = new();
        private static int open;
        private static ErrorSink? installed;
        [ThreadStatic] private static WarmTarget? current;

        private sealed class ErrorSink(Avalonia.Logging.ILogSink? inner) : Avalonia.Logging.ILogSink
        {
            public Avalonia.Logging.ILogSink? Inner { get; } = inner;

            private static bool Watches(Avalonia.Logging.LogEventLevel level, string area, object? source) =>
                current is not null && level == Avalonia.Logging.LogEventLevel.Error && area == Avalonia.Logging.LogArea.Visual && source is Avalonia.Rendering.SceneGraph.ICustomDrawOperation;

            public bool IsEnabled(Avalonia.Logging.LogEventLevel level, string area) =>
                (current is not null && level == Avalonia.Logging.LogEventLevel.Error && area == Avalonia.Logging.LogArea.Visual) || (Inner?.IsEnabled(level, area) ?? false);

            public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate)
            {
                if (Inner?.IsEnabled(level, area) == true) Inner.Log(level, area, source, messageTemplate);
            }

            public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
            {
                if (Watches(level, area, source) && propertyValues.OfType<Exception>().FirstOrDefault() is { } error) current!.Failure ??= error;
                if (Inner?.IsEnabled(level, area) == true) Inner.Log(level, area, source, messageTemplate, propertyValues);
            }
        }

        private void Watch()
        {
            lock (watching)
            {
                if (open++ == 0)
                {
                    installed = new ErrorSink(Avalonia.Logging.Logger.Sink);
                    Avalonia.Logging.Logger.Sink = installed;
                }
            }
            enclosing = current;
            current = this;
        }

        private void Unwatch()
        {
            if (current == this) current = enclosing;
            lock (watching)
            {
                if (--open != 0) return;
                if (ReferenceEquals(Avalonia.Logging.Logger.Sink, installed)) Avalonia.Logging.Logger.Sink = installed!.Inner;
                installed = null;
            }
        }

        /// <summary>A target of <paramref name="info"/>'s size (device pixels) on <paramref name="gpu"/> (null: the CPU, a test or
        /// the renderer without a GPU), drawn at <paramref name="scale"/>.</summary>
        public static WarmTarget Open(GRContext? gpu, SKImageInfo info, double scale)
        {
            var surface = (gpu is null ? SKSurface.Create(info) : GpuSurface(gpu, info))
                ?? throw new InvalidOperationException(gpu is null ? "GPU warm-up: no surface could be made for the target." : "GPU warm-up: the GPU context made no surface for the target.");
            try
            {
                if (!Available) throw new InvalidOperationException("GPU warm-up: this Avalonia has not the types a target's drawing context is made of.");
                surface.Canvas.Clear(SKColors.Transparent);
                // ScaleDrawingToDpi: the impl sets the canvas to the scaling itself (as a RenderTargetBitmap's does), which the custom
                // operation reads off the canvas as its scale
                var createInfo = Activator.CreateInstance(infoType!)!;
                infoType!.GetField("Surface")!.SetValue(createInfo, surface);
                infoType.GetField("GrContext")!.SetValue(createInfo, gpu);
                infoType.GetField("Dpi")!.SetValue(createInfo, new Vector(96 * scale, 96 * scale));
                infoType.GetField("ScaleDrawingToDpi")!.SetValue(createInfo, true);
                var impl = (IDisposable)implConstructor!.Invoke([createInfo, Array.Empty<IDisposable>()]);
                try
                {
                    var target = new WarmTarget(surface, impl, (DrawingContext)contextConstructor!.Invoke([impl, false]));
                    target.Watch();
                    return target;
                }
                catch { impl.Dispose(); throw; }
            }
            catch
            {
                surface.Dispose();
                throw;
            }
        }

        /// <summary>Lets the target go, then throws the exception a custom operation drawn through it threw
        /// (<see cref="Failure"/>), with its own stack.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Unwatch();
            try
            {
                Context.Dispose();
                impl.Dispose(); // lets go of the GPU context it holds
                Inspect?.Invoke(Surface);
            }
            finally { Surface.Dispose(); }
            if (Failure is { } failure) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    // a step's answer: its time on the render thread, whether a GPU context drew it, and whether that context was new (the
    // step then compiled nothing and its run starts again)
    private readonly record struct StepAnswer(double Ms, bool Gpu, bool NewContext = false, string? CacheFolder = null);

    // one step asked of the render thread; withdrawn when its driver stopped waiting for it
    private sealed class StepRequest(GpuProgram program, double scaling)
    {
        public GpuProgram Program { get; } = program;
        public double Scaling { get; } = scaling;
        public TaskCompletionSource<StepAnswer> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
            var handle = gpu?.Handle ?? (PretendGpu ? PretendContext : 0);
            // the window drawn whole on another GPU context (the primer is drawn with it): seen first, whether a step waits
            // or not. A step that waits is answered at once, compiling nothing on the new context, and its run starts again
            // from its first step (the programs it made are on the old one); with none waiting, warm again, idle-gated.
            if (handle != 0 && context != 0 && handle != context)
            {
                context = handle;
                if (pending is { Withdrawn: false } waiting)
                {
                    pending = null;
                    waiting.Answer.TrySetResult(new StepAnswer(0, true, NewContext: true));
                    return;
                }
                contextChanged();
                if (pending is null) return;
            }
            if (pending is not { } request) return;
            pending = null;
            if (handle != 0) context = handle;
            if (request.Withdrawn)
            {
                // the warm-up gave up on it, its host not drawn: it answers (compiling nothing) once the host is drawn in two
                // frames in a row, a window drawing again rather than a single frame; until then it asks to be drawn again
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (drawnOnce != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(drawnOnce, now) <= TimeSpan.FromMilliseconds(100))
                {
                    drawnOnce = 0;
                    request.Answer.TrySetResult(new StepAnswer(0, gpu is not null));
                    return;
                }
                drawnOnce = now;
                pending = request;
                Invalidate();
                return;
            }
            if (gpu is null && !PretendGpu) { request.Answer.TrySetResult(new StepAnswer(0, false)); return; }
            using var whole = AuroraBootTrace.Begin("warmup: " + request.Program.Name);
            var binding = gpu is not null ? GpuProgramCache.ForContext(gpu) : null; // before the knob's programs are first linked: a later launch finds them made
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            Interlocked.CompareExchange(ref FirstStepTicks, Environment.TickCount64, 0);
            try
            {
                using (AuroraBootTrace.Begin("draw: " + request.Program.Name)) request.Program.Drawing(new GpuWarmContext(gpu, request.Scaling));
                using (AuroraBootTrace.Begin("GPU flush: " + request.Program.Name)) gpu?.Flush(); // the compile happens here, in this frame (one with nothing animating, for the idle fallback)
            }
            catch (Exception error)
            {
                // a program that did not draw faults the warm-up (never complete as if prepared); the render thread goes on
                request.Answer.TrySetException(error);
                return;
            }
            var ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Interlocked.Increment(ref stepsDone);
            request.Answer.TrySetResult(new StepAnswer(ms, true, CacheFolder: binding?.Folder));
        }
    }
}
