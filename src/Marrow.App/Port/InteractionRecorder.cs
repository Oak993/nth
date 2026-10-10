using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Support;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Port;

// PORT-ONLY (ED-NEW-stutterfix-1): a scripted interaction tour for the Release performance profile. It runs only when the
// marked copied profile also holds interaction-script.json; otherwise nothing here is touched (InteractionProbe stays off).
// Per interaction it reports render-tick intervals and drawn frames (with their merged dirty area) per window, UI-thread
// jobs over 16 ms with the named work inside them, Avalonia's own pass timings, GpuProgramCache, JIT and GC deltas.
// It never sends a message: the composer only receives typed letters and is given its text back; Return is never pressed.
internal static class InteractionRecorder
{
    private const string ScriptName = "interaction-script.json";
    private const string MeterName = "Avalonia.Diagnostic.Meter";
    private static readonly double TicksPerMs = Stopwatch.Frequency / 1000.0;

    private readonly record struct Tick(string Window, long Before, long Now);
    private readonly record struct Drawn(object Tracker, long At, long Area);
    private readonly record struct Pass(string Name, double Value, long At, int Thread);
    private readonly record struct Stall(long Posted, long Ran);

    private static readonly ConcurrentQueue<Tick> ticks = new();
    private static readonly ConcurrentQueue<Drawn> drawn = new();
    private static readonly ConcurrentQueue<Pass> passes = new();
    private static readonly ConcurrentQueue<Stall> stalls = new();
    private static readonly ConcurrentDictionary<object, string> trackers = new(ReferenceEqualityComparer.Instance);
    private static readonly List<TickProbe> probes = new();
    private static readonly HashSet<TopLevel> probed = new();
    private static volatile bool measuring;
    private static string current = "idle";
    private static int popups, uiThread;
    private static Popup? lastOpened;

    /// <summary>Whether this run records interactions: a marked copied profile that also holds the script.</summary>
    internal static bool Enabled { get; private set; }

    /// <summary>Before Avalonia is set up: Avalonia's diagnostic meter is switched on by an AppContext switch it reads once.</summary>
    internal static void BeginStartup(string root)
    {
        if (root.Length == 0 || !File.Exists(Path.Combine(root, ScriptName))) return;
        Enabled = true;
        AppContext.SetSwitch("Avalonia.Diagnostics.Diagnostic.IsEnabled", true);
        InteractionProbe.Enable();
    }

    private sealed record Script(double StartSeconds = 70, int Rounds = 3, string Search = "gpt", string Typed = "the quick brown fox jumps over the lazy dog", string[]? Steps = null, bool Allocations = false);

    // Optional (Allocations): the runtime's allocation ticks (one per ~100 KB allocated) by type and by whether the UI thread
    // allocated, to find what allocates. It perturbs timing, so a run that records it is not used for timing.
    private static readonly ConcurrentDictionary<(string Type, bool Ui), long> allocations = new();
    private sealed class AllocationListener : System.Diagnostics.Tracing.EventListener
    {
        protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
                EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)0x1);
        }

        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
        {
            if (!measuring || e.EventName is not { } name || !name.StartsWith("GCAllocationTick", StringComparison.Ordinal) || e.PayloadNames is not { } names || e.Payload is not { } payload) return;
            var type = names.IndexOf("TypeName") is >= 0 and var t ? payload[t] as string ?? "?" : "?";
            var amount = names.IndexOf("AllocationAmount64") is >= 0 and var a ? Convert.ToInt64(payload[a], System.Globalization.CultureInfo.InvariantCulture) : names.IndexOf("AllocationAmount") is >= 0 and var b ? Convert.ToInt64(payload[b], System.Globalization.CultureInfo.InvariantCulture) : 0;
            allocations.AddOrUpdate((type, Environment.CurrentManagedThreadId == uiThread), amount, (_, sum) => sum + amount);
        }
    }
    private static AllocationListener? allocationListener;

    internal static void Start(string root)
    {
        if (!Enabled) return;
        Script script;
        try { script = JsonSerializer.Deserialize<Script>(File.ReadAllText(Path.Combine(root, ScriptName)), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Script(); }
        catch (JsonException) { script = new Script(); }
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == MeterName) l.EnableMeasurementEvents(instrument); },
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
        { if (measuring) passes.Enqueue(new Pass(instrument.Name, value, Stopwatch.GetTimestamp(), Environment.CurrentManagedThreadId)); });
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        { if (measuring) passes.Enqueue(new Pass(instrument.Name, value, Stopwatch.GetTimestamp(), Environment.CurrentManagedThreadId)); });
        listener.Start();
        GC.KeepAlive(listener);
        keptListener = listener;
        if (script.Allocations) allocationListener = new AllocationListener();
        InteractionProbe.FrameDrawn = (tracker, rect, _) =>
        {
            if (!measuring) return;
            var area = (long)Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
            drawn.Enqueue(new Drawn(tracker, Stopwatch.GetTimestamp(), area));
        };
        AvaloniaWindow.WindowOpenedEvent.AddClassHandler<AvaloniaWindow>((window, _) => Probe(window, window.GetType().Name));
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup, _) =>
        {
            if (!popup.IsOpen) return;
            lastOpened = popup;
            var name = $"popup{++popups}({current})";
            Dispatcher.UIThread.Post(() => { if (popup.Child is { } child && TopLevel.GetTopLevel(child) is { } top) Probe(top, name); }, DispatcherPriority.Loaded);
        });
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(script.StartSeconds)).ConfigureAwait(false);
            var stallThread = new Thread(WatchStalls) { IsBackground = true, Name = "Interaction stall watch" };
            var results = new List<object>();
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() => Tour(script, results, stallThread));
            }
            catch (Exception e) { results.Add(new { error = e.ToString() }); }
            measuring = false;
            await using var file = new FileStream(Path.Combine(root, "interactions.json"), FileMode.Create, FileAccess.Write, FileShare.Read, 16 * 1024, useAsync: true);
            await JsonSerializer.SerializeAsync(file, new { frequency = Stopwatch.Frequency, script, interactions = results }, new JsonSerializerOptions { WriteIndented = true }).ConfigureAwait(false);
        });
    }

    private static MeterListener? keptListener;

    // ---- probes ----------------------------------------------------------------------------------------------------

    private sealed class TickProbe : CompositionCustomVisualHandler
    {
        private readonly string window;
        private long last;
        private bool observed, running;
        public TickProbe(string window) => this.window = window;
        internal CompositionCustomVisual? Visual { get; set; }

        public override void OnMessage(object message)
        {
            if (message is not true || running) return;
            running = true; last = 0; RegisterForNextAnimationFrameUpdate();
        }

        public override void OnAnimationFrameUpdate()
        {
            if (!measuring) { running = false; last = 0; return; }
            var now = Stopwatch.GetTimestamp();
            if (!observed)
            {
                observed = true;
                InteractionProbe.ObserveFrames(this);
                if (InteractionProbe.TrackerOf(this) is { } tracker) trackers[tracker] = window;
            }
            if (last != 0) ticks.Enqueue(new Tick(window, last, now));
            last = now;
            RegisterForNextAnimationFrameUpdate(); // no Invalidate: the 1 px probe never adds to a frame's dirty rect
        }

        public override void OnRender(ImmediateDrawingContext context) { }
    }

    private static void Probe(TopLevel top, string name)
    {
        if (!probed.Add(top)) return;
        if (ElementComposition.GetElementChildVisual(top) is not null || ElementComposition.GetElementVisual(top) is not { } element) return;
        var handler = new TickProbe(name);
        var visual = element.Compositor.CreateCustomVisual(handler);
        visual.Size = new(1, 1);
        handler.Visual = visual;
        ElementComposition.SetElementChildVisual(top, visual);
        probes.Add(handler);
        if (top is AvaloniaWindow window) window.Closed += (_, _) => { probes.Remove(handler); probed.Remove(top); };
        if (measuring) visual.SendHandlerMessage(true);
    }

    // A UI-thread job longer than 16 ms delays a Send-priority ping posted while it runs by about its remaining length.
    // Pings are posted one at a time, 2 ms apart on a high-resolution waitable timer (no change to the system timer).
    private static void WatchStalls()
    {
        var timer = CreateWaitableTimerExW(0, null, 2, 0x1F0003);
        using var answered = new ManualResetEventSlim();
        long posted = 0;
        var ping = new Action(() =>
        {
            var now = Stopwatch.GetTimestamp();
            var at = Volatile.Read(ref posted);
            if (now - at > 16 * TicksPerMs) stalls.Enqueue(new Stall(at, now));
            answered.Set();
        });
        while (measuring)
        {
            answered.Reset();
            Volatile.Write(ref posted, Stopwatch.GetTimestamp());
            Dispatcher.UIThread.Post(ping, DispatcherPriority.Send);
            answered.Wait();
            if (timer != 0)
            {
                long due = -20_000; // 2 ms, relative, in 100 ns units
                if (SetWaitableTimer(timer, ref due, 0, 0, 0, false)) WaitForSingleObject(timer, 100);
            }
            else Thread.Sleep(1);
        }
        if (timer != 0) CloseHandle(timer);
    }

    // ---- the tour --------------------------------------------------------------------------------------------------

    private static async Task Tour(Script script, List<object> results, Thread stallThread)
    {
        uiThread = Environment.CurrentManagedThreadId;
        var manager = Marrow.App.Window.WindowManager.Shared;
        if (manager.MainWindow is not { } main) { results.Add(new { error = "no main window" }); return; }
        Probe(main, "main");
        measuring = true;
        foreach (var probe in probes) probe.Visual?.SendHandlerMessage(true);
        stallThread.Start();
        await Task.Delay(1000);
        var steps = script.Steps ?? ["effort-open", "effort-close", "picker-open", "picker-scroll", "picker-search", "picker-close", "settings-open", "settings-close", "hydra-open", "hydra-close", "composer-typing"];
        for (var round = 1; round <= script.Rounds; round++)
        {
            foreach (var step in steps)
            {
                var mark = Mark.Begin(step);
                string? note = null;
                try { note = await Run(step, script, main); }
                catch (Exception e) { note = "failed: " + e.GetType().Name + ": " + e.Message; }
                results.Add(mark.End(round, note));
                current = "idle";
                await Task.Delay(400);
                Drain();
            }
        }
    }

    private static async Task<string?> Run(string step, Script script, AvaloniaWindow main)
    {
        switch (step)
        {
            case "effort-open":
            case "effort-close":
                if (Find<ModelEffortButton>(main)?.Child is not Button chip) return "no effort chip";
                chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(1200);
                return null;
            case "picker-open":
                if (Find<ModelEffortButton>(main)?.Child is not Button opener) return "no effort chip";
                opener.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(1000);
                Restart(); // the interaction is the title tap, not the effort popover's open
                if (OpenContent() is not { } panel || Find<Button>(panel, b => ToolTip.GetTip(b) is "Choose a model") is not { } title) return "no title button";
                title.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(1200);
                return null;
            case "picker-scroll":
            {
                if (OpenContent() is not { } content) return "no open popover";
                var viewer = Descendants(content).OfType<ScrollViewer>().MaxBy(v => v.Extent.Height - v.Viewport.Height);
                if (viewer is null || TopLevel.GetTopLevel(viewer) is not { } top) return "no scroll viewer";
                var range = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
                await Animate(top, 1.2, t => viewer.Offset = new Vector(viewer.Offset.X, range * t));
                await Animate(top, 1.2, t => viewer.Offset = new Vector(viewer.Offset.X, range * (1 - t)));
                await Task.Delay(300);
                return $"range {range:F0}";
            }
            case "picker-search":
            {
                if (OpenContent() is not { } content || Find<TextBox>(content, b => AutomationProperties.GetName(b) == "Search models") is not { } field) return "no search field";
                foreach (var letter in script.Search)
                {
                    field.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = letter.ToString() });
                    await Task.Delay(150);
                }
                await Task.Delay(900);
                field.Text = "";
                await Task.Delay(900);
                return null;
            }
            case "picker-close":
                if (Find<ModelEffortButton>(main)?.Child is not Button closer) return "no effort chip";
                closer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(1000);
                return null;
            case "settings-open":
                Marrow.App.Window.WindowManager.Shared.ShowSettings();
                await Task.Delay(1800);
                if (Marrow.App.Window.WindowManager.Shared.SettingsWindow is { } settings) Probe(settings, "settings");
                return null;
            case "settings-close":
                Marrow.App.Window.WindowManager.Shared.SettingsWindow?.Close();
                await Task.Delay(1000);
                main.Activate();
                return null;
            case "hydra-open":
            case "hydra-close":
                if (Find<HydraButton>(main) is not { } hydra || Find<Button>(hydra) is not { } hydraButton) return "no hydra button";
                hydraButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(1300);
                return null;
            case "composer-typing":
            {
                if (Find<ComposerNSTextView>(main) is not { } composer) return "no composer";
                var before = composer.Text;
                foreach (var letter in script.Typed)
                {
                    composer.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = letter.ToString() });
                    await Task.Delay(70);
                }
                await Task.Delay(400);
                composer.Text = before;
                await Task.Delay(400);
                return null;
            }
            default:
                return "unknown step";
        }
    }

    private static Control? OpenContent() => lastOpened is { IsOpen: true, Child: { } child } ? child : null;

    private static IEnumerable<Visual> Descendants(Visual root) => root.GetVisualDescendants();

    private static T? Find<T>(Visual root, Func<T, bool>? match = null) where T : Control =>
        Descendants(root).OfType<T>().FirstOrDefault(c => c.IsEffectivelyVisible && (match is null || match(c)));

    // Drives a value from 0 to 1 over the given seconds, one step per frame of the window's own clock.
    private static Task Animate(TopLevel top, double seconds, Action<double> apply)
    {
        var done = new TaskCompletionSource();
        TimeSpan? start = null;
        void Frame(TimeSpan now)
        {
            start ??= now;
            var t = Math.Clamp((now - start.Value).TotalSeconds / seconds, 0, 1);
            apply(t);
            if (t < 1) top.RequestAnimationFrame(Frame); else done.SetResult();
        }
        top.RequestAnimationFrame(Frame);
        return done.Task;
    }

    // ---- per-interaction accounting --------------------------------------------------------------------------------

    private static Mark? open;

    private static void Restart()
    {
        if (open is { } mark) open = Mark.Begin(mark.Name);
    }

    private static void Drain()
    {
        while (ticks.TryDequeue(out _)) { }
        while (drawn.TryDequeue(out _)) { }
        while (passes.TryDequeue(out _)) { }
        while (stalls.TryDequeue(out _)) { }
        InteractionProbe.DrainSpans();
    }

    private sealed class Mark
    {
        internal string Name { get; private init; } = "";
        private long start;
        private int writes, hits, misses, gen0, gen1, gen2;
        private long jitMethods, allocated;
        private TimeSpan jitTime, pause;
        private IReadOnlyDictionary<string, (long Count, long Ticks, long Longest)> totals = new Dictionary<string, (long, long, long)>();

        internal static Mark Begin(string name)
        {
            Drain();
            allocations.Clear();
            current = name;
            var mark = new Mark
            {
                Name = name,
                writes = GpuProgramCache.Writes, hits = GpuProgramCache.Hits, misses = GpuProgramCache.Misses,
                gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2),
                jitMethods = System.Runtime.JitInfo.GetCompiledMethodCount(), jitTime = System.Runtime.JitInfo.GetCompilationTime(),
                pause = GC.GetTotalPauseDuration(), allocated = GC.GetTotalAllocatedBytes(),
                totals = InteractionProbe.Totals(),
            };
            mark.start = Stopwatch.GetTimestamp();
            open = mark;
            return mark;
        }

        internal object End(int round, string? note)
        {
            var self = open ?? this;
            open = null;
            return self.Finish(round, note);
        }

        private object Finish(int round, string? note)
        {
            var end = Stopwatch.GetTimestamp();
            var tickList = Take(ticks).Where(t => t.Now >= start && t.Now <= end).ToList();
            var drawnList = Take(drawn).Where(d => d.At >= start && d.At <= end).ToList();
            var passList = Take(passes).Where(p => p.At >= start && p.At <= end).ToList();
            var stallList = Take(stalls).Where(s => s.Ran >= start && s.Posted <= end).ToList();
            var spanList = InteractionProbe.DrainSpans().Where(s => s.End >= start && s.Start <= end).ToList();
            var after = InteractionProbe.Totals();
            double Ms(long ticksValue) => Math.Round(ticksValue / TicksPerMs, 2);
            var tickReport = tickList.GroupBy(t => t.Window).ToDictionary(g => g.Key, g =>
            {
                var gaps = g.Select(t => (t.Now - t.Before) / TicksPerMs).ToList();
                return new { count = gaps.Count, maxMs = Math.Round(gaps.Max(), 2), over20 = gaps.Count(x => x > 20), over33 = gaps.Count(x => x > 33) };
            });
            var frameReport = drawnList.GroupBy(d => trackers.TryGetValue(d.Tracker, out var w) ? w : "unprobed").ToDictionary(g => g.Key, g =>
            {
                var list = g.OrderBy(d => d.At).ToList();
                var gap = 0.0;
                for (var i = 1; i < list.Count; i++) gap = Math.Max(gap, (list[i].At - list[i - 1].At) / TicksPerMs);
                return new
                {
                    frames = list.Count, maxGapMs = Math.Round(gap, 2),
                    meanAreaPx = list.Count == 0 ? 0 : list.Sum(d => d.Area) / list.Count, maxAreaPx = list.Max(d => d.Area), totalAreaPx = list.Sum(d => d.Area),
                };
            });
            var stallReport = stallList.Select(s => new
            {
                atMs = Ms(s.Posted - start), ms = Ms(s.Ran - s.Posted),
                work = spanList.Where(x => x.Thread == uiThread && x.End >= s.Posted && x.Start <= s.Ran)
                    .Select(x => (name: x.Name, ms: Ms(x.End - x.Start)))
                    .Concat(passList.Where(p => p.Thread == uiThread && p.Name.EndsWith(".time", StringComparison.Ordinal) && p.At >= s.Posted && p.At - (long)(p.Value * TicksPerMs) <= s.Ran)
                        .Select(p => (name: p.Name, ms: Math.Round(p.Value, 2))))
                    .OrderByDescending(x => x.ms).Take(8).Select(x => $"{x.name} {x.ms:F1} ms").ToList(),
            }).ToList();
            var meterReport = passList.GroupBy(p => p.Name).ToDictionary(g => g.Key, g => new { count = g.Count(), sum = Math.Round(g.Sum(p => p.Value), 2), max = Math.Round(g.Max(p => p.Value), 2) });
            var spanReport = after.Where(p => !totals.TryGetValue(p.Key, out var b) || b.Count != p.Value.Count).ToDictionary(p => p.Key, p =>
            {
                totals.TryGetValue(p.Key, out var b);
                return new { count = p.Value.Count - b.Count, ms = Ms(p.Value.Ticks - b.Ticks), longestKeptMs = spanList.Where(x => x.Name == p.Key).Select(x => Ms(x.End - x.Start)).DefaultIfEmpty(0).Max() };
            });
            return new
            {
                round, name = Name, note, ms = Ms(end - start),
                longestStallMs = stallReport.Select(s => s.ms).DefaultIfEmpty(0).Max(),
                stalls = stallReport,
                ticks = tickReport,
                frames = frameReport,
                meter = meterReport,
                spans = spanReport,
                allocationTypes = allocations.OrderByDescending(p => p.Value).Take(12).Select(p => $"{(p.Key.Ui ? "ui" : "other")} {p.Key.Type} {p.Value / 1024} KB").ToList(),
                gpu = new { writes = GpuProgramCache.Writes - writes, hits = GpuProgramCache.Hits - hits, misses = GpuProgramCache.Misses - misses },
                jit = new { methods = System.Runtime.JitInfo.GetCompiledMethodCount() - jitMethods, ms = Math.Round((System.Runtime.JitInfo.GetCompilationTime() - jitTime).TotalMilliseconds, 2) },
                gc = new
                {
                    gen0 = GC.CollectionCount(0) - gen0, gen1 = GC.CollectionCount(1) - gen1, gen2 = GC.CollectionCount(2) - gen2,
                    pauseMs = Math.Round((GC.GetTotalPauseDuration() - pause).TotalMilliseconds, 2),
                    allocatedKb = (GC.GetTotalAllocatedBytes() - allocated) / 1024,
                },
            };
        }

        private static List<T> Take<T>(ConcurrentQueue<T> queue)
        {
            var list = new List<T>();
            while (queue.TryDequeue(out var item)) list.Add(item);
            return list;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(nint timer, ref long due, int period, nint completion, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
