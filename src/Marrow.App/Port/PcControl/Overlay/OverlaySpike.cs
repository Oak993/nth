// PORT-ONLY: Tactus's overlay spike (unit P25a, debug builds only, never shipped; ED-NEW-pcctl-22; DESIGN.md 10.6, 10.13
// NP-15 and NP-16). Probes the lead runs by hand in a quiet window, never a test: `Marrow.exe --tactus-overlay-spike
// <probe> [--warm] [--straddle] [--lwa]`, steps in `scripts/pc-control-overlay-spike.ps1`. It sends no input, captures
// nothing and drives no other window. Each probe opens Tactus-style band windows at the top of the work area, logs what
// it measured to the console and to %TEMP%\tactus-spike-<probe>.log, writes a RESULT line and exits after RunSeconds.
// - np15-region: the region hit test. A non-layered input band whose window region is the resting pill (inflated 2 DIP)
//   and a click-through shadow band; while the pill travels the region is empty and the shadow band draws the pill.
//   A low-level mouse hook's reader thread tests every button-down against the moving shape at the record's time (the
//   grab in flight); presses on the input band are logged with their pointer type (mouse, pen, touch). Every 2 s it
//   checks that no other topmost window covers the pill and puts the band on top again if one does.
// - np15-pixel: per-pixel alpha. One layered, not click-through band draws the pill; a press logged outside the pill
//   means Avalonia's composited window does not hit-test by alpha.
// - np15-flip: the style flip. One layered, click-through band; the hook's reader thread clears WS_EX_TRANSPARENT while
//   a record is inside the (moving) pill and sets it again outside, and times each flip from the record (target 4 ms).
// - np16: the render-thread driver. A composition custom visual evaluates one closed-form travel at the frame time and
//   draws the pill (clip, offset and opacity in one draw, so one commit); it timestamps every frame. A card expands at
//   5 s, folds 10 s later and expands again every 20 s; --warm draws the card once at 1 % first. --straddle opens one band per monitor on one
//   clock, the travel crossing the seam. The RESULT gives frame pacing against the monitor's refresh rate, whether the
//   render timer follows it, and the longest frame after each expand.
// - np18-focus: the card's keyboard (ED-NEW-pcctl-26 item 8). An input band shows a card with the reply field; CardFocus
//   runs on the real calls (CardFocusWin32): a click in the field saves the window the keyboard comes from, Enter hands it
//   back, a click elsewhere while typing only takes it away. It logs each save, each 100 ms check and each hand-back.
// - np11-rdp: the separate workspace's Remote Desktop control (unit P18a, ED-NEW-pcctl-33, NP-11), in
//   Workspace/RdpHostSpike.cs: hosted in a native child window, connected to a child session when they are on, and its
//   drawing and the session's heartbeat measured while visible, cloaked and minimized.
#if DEBUG
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Avalonia.Threading;
using Marrow.Core.Port.PcControl.Pill;
using SkiaSharp;

namespace Marrow.App.Port.PcControl.Overlay;

/// <summary>The spike's entry: the probe name, its options, and the app it runs in.</summary>
public static class OverlaySpike
{
    public const string Command = "--tactus-overlay-spike";

    public const double PillWidth = 440;
    public const double PillHeight = 48;
    public const double PillTop = 16;
    public const double BandHeight = 120;

    /// <summary>How long a probe runs before it writes its RESULT and exits.</summary>
    public const double RunSeconds = 90;

    public static IReadOnlyList<string> Probes { get; } = ["np15-region", "np15-pixel", "np15-flip", "np16", "np18-focus", Workspace.RdpProbe.Probe];

    public static int Run(string[] args)
    {
        var probe = args.Length > 1 ? args[1] : "";
        if (!Probes.Contains(probe, StringComparer.Ordinal))
        {
            Console.Error.WriteLine($"usage: {Command} {string.Join('|', Probes)} [--warm] [--straddle] [--lwa]");
            return 2;
        }
        using var log = new SpikeLog(Path.Combine(Path.GetTempPath(), $"tactus-spike-{probe}.log"));
        var options = new SpikeOptions(args.Contains("--warm", StringComparer.Ordinal), args.Contains("--straddle", StringComparer.Ordinal),
            args.Contains("--lwa", StringComparer.Ordinal));
        var lifetime = new ClassicDesktopStyleApplicationLifetime { Args = args, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithLifetime(lifetime);
        lifetime.Startup += (_, _) =>
        {
            SpikeProbe running = probe switch
            {
                "np15-region" => new RegionProbe(log, options),
                "np15-pixel" => new PixelProbe(log, options),
                "np15-flip" => new FlipProbe(log, options),
                "np18-focus" => new FocusProbe(log, options),
                Workspace.RdpProbe.Probe => new Workspace.RdpProbe(log, options),
                _ => new MotionProbe(log, options),
            };
            log.Line($"probe {probe} warm={options.Warm} straddle={options.Straddle} lwa={options.Lwa} refresh={SpikeNative.RefreshHz()} Hz");
            running.Open();
            DispatcherTimer.RunOnce(() =>
            {
                running.Finish();
                lifetime.Shutdown();
            }, TimeSpan.FromSeconds(RunSeconds));
        };
        return lifetime.Start(args);
    }
}

internal sealed record SpikeOptions(bool Warm, bool Straddle, bool Lwa);

/// <summary>Lines with the time since start, to the console and the log file.</summary>
internal sealed class SpikeLog : IDisposable
{
    private readonly StreamWriter file;
    private readonly long start = Stopwatch.GetTimestamp();
    private readonly object gate = new();

    public SpikeLog(string path)
    {
        file = new StreamWriter(path, append: false) { AutoFlush = true };
        Console.WriteLine($"log: {path}");
    }

    public void Line(string text)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"{Stopwatch.GetElapsedTime(start).TotalMilliseconds,10:F1} ms  {text}");
        lock (gate)
        {
            Console.WriteLine(line);
            file.WriteLine(line);
        }
    }

    public void Dispose() => file.Dispose();
}

/// <summary>A band across the top of one monitor's work area.</summary>
internal sealed record SpikeBand(PixelRect Work, double Scale)
{
    public double Width => Work.Width / Scale;

    public static IReadOnlyList<SpikeBand> All(Avalonia.Controls.Window probe, bool every)
    {
        IReadOnlyList<Screen> screens = every ? probe.Screens.All : [probe.Screens.Primary ?? probe.Screens.All[0]];
        return screens.Select(screen => new SpikeBand(screen.WorkingArea, screen.Scaling)).ToArray();
    }

    public void Place(Avalonia.Controls.Window window, double height)
    {
        window.Position = new PixelPoint(Work.X, Work.Y);
        window.Width = Width;
        window.Height = height;
    }

    /// <summary>A rect in this band's DIP as screen pixels, rounded outward.</summary>
    public PixelRect Pixels(PillRect rect) => new(
        Work.X + (int)Math.Floor(rect.X * Scale), Work.Y + (int)Math.Floor(rect.Y * Scale),
        (int)Math.Ceiling(rect.Width * Scale) + 1, (int)Math.Ceiling(rect.Height * Scale) + 1);
}

/// <summary>The pill resting 3 s at a quarter of the band, then travelling to three quarters, and back (closed form).</summary>
internal sealed class SpikeShuttle
{
    public const double RestSeconds = 3;

    private readonly SpikeBand band;
    private volatile State state;

    public SpikeShuttle(SpikeBand band)
    {
        this.band = band;
        state = new State(null, new PillVector((band.Width * 0.25) - (OverlaySpike.PillWidth / 2), OverlaySpike.PillTop), Clock() + RestSeconds);
    }

    public static double Clock() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>Advances at most one phase and says where the pill is and whether it moves.</summary>
    public (PillVector At, bool Moving) Tick(double now)
    {
        var current = state;
        if (current.Travel is { } travel)
        {
            if (!travel.VisuallySettled(now)) return (travel.Value(now), true);
            state = new State(null, travel.Target, now + RestSeconds);
            return (travel.Target, false);
        }
        if (now < current.RestUntil) return (current.At, false);
        var left = new PillVector((band.Width * 0.25) - (OverlaySpike.PillWidth / 2), OverlaySpike.PillTop);
        var right = new PillVector((band.Width * 0.75) - (OverlaySpike.PillWidth / 2), OverlaySpike.PillTop);
        state = new State(PillTravel.Start(now, current.At, current.At == left ? right : left, PillVector.Zero, reduceMotion: false), current.At, 0);
        return (current.At, true);
    }

    /// <summary>The pill's rect in band DIP at a time (any thread).</summary>
    public PillRect Shape(double now)
    {
        var current = state;
        var at = current.Travel?.Value(now) ?? current.At;
        return new PillRect(at.X, at.Y, OverlaySpike.PillWidth, OverlaySpike.PillHeight);
    }

    public bool Moving => state.Travel is not null;

    private sealed record State(PillTravel? Travel, PillVector At, double RestUntil);
}

internal abstract class SpikeProbe(SpikeLog log, SpikeOptions options)
{
    protected SpikeLog Log { get; } = log;

    protected SpikeOptions Options { get; } = options;

    public abstract void Open();

    public abstract void Finish();

    protected static Border Pill(string text) => new()
    {
        Width = OverlaySpike.PillWidth,
        Height = OverlaySpike.PillHeight,
        CornerRadius = new CornerRadius(OverlaySpike.PillHeight / 2),
        Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
        BorderThickness = new Thickness(1),
        Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 13, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(20, 0) },
    };

    protected static nint Handle(Avalonia.Controls.Window window) => window.TryGetPlatformHandle()?.Handle ?? 0;

    protected void Opened(TactusOverlayWindow window, string name)
    {
        var handle = Handle(window);
        if (Options.Lwa && (SpikeNative.ExtendedStyle(handle) & OverlayStyles.Layered) != 0) SpikeNative.LayeredOpaque(handle);
        Log.Line($"{name}: hwnd 0x{handle:x} exstyle 0x{SpikeNative.ExtendedStyle(handle):x8} (layered {Bit(handle, OverlayStyles.Layered)}, transparent {Bit(handle, OverlayStyles.Transparent)}, no-redirection {Bit(handle, SpikeNative.NoRedirectionBitmap)}) hidden-from-capture {window.HiddenFromCapture} scaling {window.RenderScaling}");
    }

    protected void LogPresses(Avalonia.Controls.Window window, string name, Func<Point, bool> inside) =>
        window.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            var point = e.GetPosition(window);
            Log.Line($"{name}: press {e.Pointer.Type} at ({point.X:F0}, {point.Y:F0}) DIP, inside the pill {inside(point)}");
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

    protected static bool Inside(PillRect rect, double x, double y) => x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height;

    private static bool Bit(nint handle, uint bit) => (SpikeNative.ExtendedStyle(handle) & bit) != 0;
}

/// <summary>NP-18: the card's keyboard. A card with the reply field in an input band; CardFocus on the real calls.</summary>
internal sealed class FocusProbe(SpikeLog log, SpikeOptions options) : SpikeProbe(log, options)
{
    private readonly Dictionary<CardHandBack, int> backs = [];
    private TactusOverlayWindow? window;
    private int activations;
    private int failed;

    public override void Open()
    {
        var band = new TactusOverlayWindow(OverlayRole.Input);
        window = band;
        var place = SpikeBand.All(band, every: false)[0];
        place.Place(band, 260);
        var composer = new Marrow.UI.Port.PcControl.MiniComposer("Type, then Enter: the keyboard goes back");
        var card = new Border
        {
            Width = OverlaySpike.PillWidth, Padding = new Thickness(16), CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "NP-18: click the field, type, press Enter", Foreground = Brushes.White, FontSize = 13 },
                    composer,
                },
            },
        };
        Canvas.SetLeft(card, (place.Width - OverlaySpike.PillWidth) / 2);
        Canvas.SetTop(card, OverlaySpike.PillTop);
        band.Content = new Canvas { Children = { card } };
        var focus = new CardFocus(new CardFocusWin32(), () => Handle(band), _ => false, TimeProvider.System,
            (seconds, run) => DispatcherTimer.RunOnce(run, TimeSpan.FromSeconds(seconds)));
        focus.ActivationFailed += () =>
        {
            failed++;
            Log.Line("100 ms check: the overlay is not in front (the card would say Type your answer in the chat)");
        };
        band.Activating += () =>
        {
            activations++;
            focus.Activating();
            Log.Line(focus.Saved is { } saved ? $"activating: saved 0x{saved.Window:x} process {saved.ProcessId} class {saved.ClassName}" : "activating: nothing saved");
        };
        band.Activated += (_, _) =>
        {
            focus.Activated();
            Log.Line("the overlay has the keyboard");
        };
        band.Deactivated += (_, _) =>
        {
            focus.Deactivated();
            Log.Line("the overlay lost the keyboard (a later hand-back does nothing)");
        };
        composer.Field.KeyDown += (_, _) => focus.Typed();
        composer.KeyPressed += e =>
        {
            if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
            var back = focus.HandBack();
            backs[back] = backs.GetValueOrDefault(back) + 1;
            composer.Field.Text = "";
            Log.Line($"Enter: hand-back {back}");
            e.Handled = true;
        };
        band.Opened += (_, _) => Opened(band, "card band");
        band.Show();
    }

    public override void Finish()
    {
        Log.Line($"RESULT np18-focus: activations {activations}, activation failed {failed}, hand-backs {string.Join(", ", backs.Select(b => $"{b.Key} {b.Value}"))}. Check by eye: after Enter the keyboard is back in Notepad, Word and a browser's address bar with the caret where it was; a click on another app while typing leaves that app in front and Enter later activates nothing; the overlay took the keyboard only from a click in the field.");
        window?.Close();
    }
}

/// <summary>NP-15, implementation 1: the window region at rest, empty while moving, with a click-through shadow band.</summary>
internal sealed class RegionProbe(SpikeLog log, SpikeOptions options) : SpikeProbe(log, options)
{
    private readonly ConcurrentQueue<double> grabLatencies = new();
    private SpikeBand band = null!;
    private SpikeShuttle shuttle = null!;
    private TactusOverlayWindow input = null!;
    private TactusOverlayWindow shadow = null!;
    private Border inputPill = null!;
    private Border shadowPill = null!;
    private Border shade = null!;
    private SpikeMouseHook? hook;
    private DispatcherTimer? topmost;
    private bool moving;
    private int presses;
    private int grabs;
    private int covered;

    public override void Open()
    {
        shadow = new TactusOverlayWindow(OverlayRole.Shadow);
        band = SpikeBand.All(shadow, every: false)[0];
        shuttle = new SpikeShuttle(band);
        input = new TactusOverlayWindow(OverlayRole.Input);
        band.Place(shadow, OverlaySpike.BandHeight);
        band.Place(input, OverlaySpike.BandHeight);
        shade = new Border { Width = OverlaySpike.PillWidth, Height = OverlaySpike.PillHeight, CornerRadius = new CornerRadius(OverlaySpike.PillHeight / 2), BoxShadow = BoxShadows.Parse("0 8 28 0 #70000000") };
        shadowPill = Pill("Moving: drawn by the click-through band");
        shadowPill.IsVisible = false;
        shadow.Content = new Canvas { Children = { shade, shadowPill } };
        inputPill = Pill("Region pill: click, pen-tap and touch me");
        input.Content = new Canvas { Children = { inputPill } };
        LogPresses(input, "input band", point =>
        {
            var inside = Inside(shuttle.Shape(SpikeShuttle.Clock()), point.X, point.Y);
            if (inside) Interlocked.Increment(ref presses);
            return inside;
        });
        shadow.Opened += (_, _) => Opened(shadow, "shadow band");
        input.Opened += (_, _) =>
        {
            Opened(input, "input band");
            Rest(shuttle.Tick(SpikeShuttle.Clock()).At);
            input.RequestAnimationFrame(Frame);
        };
        shadow.Show();
        input.Show();
        hook = new SpikeMouseHook(Record);
        topmost = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => CheckTopmost());
        topmost.Start();
    }

    public override void Finish()
    {
        topmost?.Stop();
        hook?.Dispose();
        var latencies = grabLatencies.ToArray();
        Log.Line($"RESULT np15-region: presses on the resting pill {presses}; grabs in flight seen {grabs} (record to test: {SpikeStats.Describe(latencies)}); covered by another topmost window {covered} times. Check by eye: clicks beside the pill and through the moving pill reached the app beneath; pen and touch logged above; no flicker when the pill hands over between bands at the start and end of each travel.");
        input.Close();
        shadow.Close();
    }

    private void Frame(TimeSpan _)
    {
        var (at, isMoving) = shuttle.Tick(SpikeShuttle.Clock());
        if (isMoving && !moving)
        {
            moving = true;
            SpikeNative.SetRegion(Handle(input), null);
            shadowPill.IsVisible = true;
            inputPill.IsVisible = false;
            Log.Line("travel starts: region empty, the shadow band draws the pill");
        }
        Canvas.SetLeft(shade, at.X);
        Canvas.SetTop(shade, at.Y);
        Canvas.SetLeft(shadowPill, at.X);
        Canvas.SetTop(shadowPill, at.Y);
        if (!isMoving && moving)
        {
            moving = false;
            Rest(at);
        }
        input.RequestAnimationFrame(Frame);
    }

    private void Rest(PillVector at)
    {
        Canvas.SetLeft(inputPill, at.X);
        Canvas.SetTop(inputPill, at.Y);
        inputPill.IsVisible = true;
        shadowPill.IsVisible = false;
        var rect = new PillRect(at.X, at.Y, OverlaySpike.PillWidth, OverlaySpike.PillHeight).Inflate(2);
        var scale = band.Scale;
        SpikeNative.SetRegion(Handle(input), new PixelRect((int)Math.Floor(rect.X * scale), (int)Math.Floor(rect.Y * scale),
            (int)Math.Ceiling(rect.Width * scale), (int)Math.Ceiling(rect.Height * scale)), (int)Math.Round((OverlaySpike.PillHeight / 2 + 2) * 2 * scale));
        Log.Line($"at rest: region set to the pill at ({at.X:F0}, {at.Y:F0}) DIP");
    }

    // The reader thread: a button-down inside the moving pill at the record's time is a grab in flight.
    private void Record(SpikeRecord record)
    {
        if (!record.ButtonDown || !shuttle.Moving) return;
        var shape = shuttle.Shape(record.At);
        var x = (record.X - band.Work.X) / band.Scale;
        var y = (record.Y - band.Work.Y) / band.Scale;
        if (!Inside(shape, x, y)) return;
        Interlocked.Increment(ref grabs);
        grabLatencies.Enqueue(Stopwatch.GetElapsedTime(record.Stamp).TotalMilliseconds);
        Log.Line($"grab in flight: {record.Describe()} inside the moving pill (would be swallowed and dragged from hook records)");
    }

    private void CheckTopmost()
    {
        var shape = shuttle.Shape(SpikeShuttle.Clock());
        var centre = band.Pixels(shape).Center;
        var under = SpikeNative.RootAt(centre.X, centre.Y);
        if (moving || under == 0 || under == Handle(input) || under == Handle(shadow)) return;
        covered++;
        Log.Line($"covered: 0x{under:x} is over the pill; putting the bands on top again");
        shadow.Reassert();
        input.Reassert();
    }
}

/// <summary>NP-15, implementation 2: per-pixel alpha hit testing on one layered band that is not click-through.</summary>
internal sealed class PixelProbe(SpikeLog log, SpikeOptions options) : SpikeProbe(log, options)
{
    private TactusOverlayWindow window = null!;
    private int inside;
    private int outside;

    public override void Open()
    {
        window = new TactusOverlayWindow(OverlayRole.Input);
        var band = SpikeBand.All(window, every: false)[0];
        band.Place(window, OverlaySpike.BandHeight);
        var pill = Pill("Per-pixel pill: click me, then click beside me");
        var rect = new PillRect((band.Width - OverlaySpike.PillWidth) / 2, OverlaySpike.PillTop, OverlaySpike.PillWidth, OverlaySpike.PillHeight);
        Canvas.SetLeft(pill, rect.X);
        Canvas.SetTop(pill, rect.Y);
        window.Content = new Canvas { Children = { pill } };
        LogPresses(window, "pixel band", point =>
        {
            var hit = Inside(rect, point.X, point.Y);
            if (hit) Interlocked.Increment(ref inside);
            else Interlocked.Increment(ref outside);
            return hit;
        });
        window.Opened += (_, _) =>
        {
            var handle = Handle(window);
            SpikeNative.SetExtendedStyle(handle, SpikeNative.ExtendedStyle(handle) | OverlayStyles.Layered);
            Opened(window, "pixel band");
        };
        window.Show();
    }

    public override void Finish()
    {
        Log.Line($"RESULT np15-pixel: presses on the pill {inside}, presses on transparent pixels {outside}. Per-pixel hit testing works only if the pill took its presses, every press beside it reached the app beneath and the second count is 0.");
        window.Close();
    }
}

/// <summary>NP-15, implementation 3: the style flip from the hook's reader thread, timed from the record.</summary>
internal sealed class FlipProbe(SpikeLog log, SpikeOptions options) : SpikeProbe(log, options)
{
    private readonly ConcurrentQueue<double> latencies = new();
    private SpikeBand band = null!;
    private SpikeShuttle shuttle = null!;
    private TactusOverlayWindow window = null!;
    private Border pill = null!;
    private SpikeMouseHook? hook;
    private nint handle;
    private bool takesInput;
    private int presses;

    public override void Open()
    {
        window = new TactusOverlayWindow(OverlayRole.Shadow);
        band = SpikeBand.All(window, every: false)[0];
        shuttle = new SpikeShuttle(band);
        band.Place(window, OverlaySpike.BandHeight);
        pill = Pill("Flip pill: click me resting and moving");
        window.Content = new Canvas { Children = { pill } };
        LogPresses(window, "flip band", point =>
        {
            Interlocked.Increment(ref presses);
            return Inside(shuttle.Shape(SpikeShuttle.Clock()), point.X, point.Y);
        });
        window.Opened += (_, _) =>
        {
            handle = Handle(window);
            Opened(window, "flip band");
            hook = new SpikeMouseHook(Record);
            window.RequestAnimationFrame(Frame);
        };
        window.Show();
    }

    public override void Finish()
    {
        hook?.Dispose();
        var all = latencies.ToArray();
        Log.Line($"RESULT np15-flip: presses taken {presses}; flips {all.Length}, record to hit-testable {SpikeStats.Describe(all)}; over 4 ms {all.Count(ms => ms > 4)}. Check by eye: a click just inside the pill's edge right after moving onto it is taken; a click beside it reaches the app beneath.");
        window.Close();
    }

    private void Frame(TimeSpan _)
    {
        var at = shuttle.Tick(SpikeShuttle.Clock()).At;
        Canvas.SetLeft(pill, at.X);
        Canvas.SetTop(pill, at.Y);
        window.RequestAnimationFrame(Frame);
    }

    // The reader thread, which does only this: the shape at the record's time decides the style.
    private void Record(SpikeRecord record)
    {
        if (handle == 0) return;
        var shape = shuttle.Shape(record.At);
        var inside = Inside(shape, (record.X - band.Work.X) / band.Scale, (record.Y - band.Work.Y) / band.Scale);
        if (inside == takesInput) return;
        takesInput = inside;
        var style = SpikeNative.ExtendedStyle(handle);
        SpikeNative.SetExtendedStyle(handle, inside ? style & ~OverlayStyles.Transparent : style | OverlayStyles.Transparent);
        latencies.Enqueue(Stopwatch.GetElapsedTime(record.Stamp).TotalMilliseconds);
    }
}

/// <summary>NP-16: the render-thread driver's frame pacing, the first expand with and without the warm-up, the seam.</summary>
internal sealed class MotionProbe(SpikeLog log, SpikeOptions options) : SpikeProbe(log, options)
{
    private readonly List<(TactusOverlayWindow Window, SpikeBand Band, SpikeDriverHandler Handler)> bands = [];
    private readonly List<long> expands = [];
    private DispatcherTimer? expander;
    private Border? card;

    public override void Open()
    {
        var probe = new TactusOverlayWindow(OverlayRole.Shadow);
        var all = SpikeBand.All(probe, Options.Straddle).OrderBy(band => band.Work.X).ToArray();
        probe.Close();
        var primary = all[0];
        var left = primary.Work.X + (int)(primary.Work.Width * 0.1);
        var schedule = new SpikeSchedule(left, left + (int)(728 * primary.Scale),
            all.Length > 1 ? all[1].Work.X + (all[1].Work.Width / 2) : primary.Work.Right - (int)((OverlaySpike.PillWidth + 40) * primary.Scale), SpikeShuttle.Clock());
        foreach (var band in all)
        {
            var window = new TactusOverlayWindow(OverlayRole.Shadow);
            band.Place(window, OverlaySpike.BandHeight + 280);
            var host = new SpikeDriverHost(band, schedule);
            var canvas = new Canvas { Children = { host } };
            window.Content = canvas;
            var index = bands.Count;
            window.Opened += (_, _) =>
            {
                Opened(window, $"band {index}");
                if (index == 0 && Options.Warm) Warm(canvas);
            };
            window.Show();
            bands.Add((window, band, host.Handler));
        }
        expander = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Normal, (_, _) => Expand());
        DispatcherTimer.RunOnce(() =>
        {
            Expand();
            expander.Start();
        }, TimeSpan.FromSeconds(5));
    }

    public override void Finish()
    {
        expander?.Stop();
        var refresh = SpikeNative.RefreshHz();
        var expected = refresh > 0 ? 1000.0 / refresh : 1000.0 / 60;
        foreach (var (window, band, handler) in bands)
        {
            var stamps = handler.Stamps();
            var intervals = new double[Math.Max(0, stamps.Length - 1)];
            for (var i = 1; i < stamps.Length; i++) intervals[i - 1] = Stopwatch.GetElapsedTime(stamps[i - 1], stamps[i]).TotalMilliseconds;
            var median = SpikeStats.Percentile(intervals, 0.5);
            var stalls = expands.Select(at => MaxAfter(stamps, at)).ToArray();
            Log.Line($"RESULT np16 band at {band.Work.X},{band.Work.Y} scale {band.Scale}: frames {stamps.Length}, interval {SpikeStats.Describe(intervals)}, over 1.5 frames {intervals.Count(ms => ms > expected * 1.5)}; primary refresh {refresh} Hz, the render timer follows it {Math.Abs(median - expected) < expected * 0.1}; longest frame after each expand (first is the first expand{(Options.Warm ? ", warmed" : ", not warmed")}): {string.Join(", ", stalls.Select(ms => ms.ToString("F1", CultureInfo.InvariantCulture)))} ms. Check by eye: no judder, no seam or jump where the pill crosses monitors{(Options.Straddle ? "" : " (run with --straddle)")}.");
            window.Close();
        }
    }

    private static double MaxAfter(long[] stamps, long at)
    {
        var end = at + (long)(0.3 * Stopwatch.Frequency);
        var max = 0.0;
        for (var i = 1; i < stamps.Length; i++)
            if (stamps[i] >= at && stamps[i - 1] <= end) max = Math.Max(max, Stopwatch.GetElapsedTime(stamps[i - 1], stamps[i]).TotalMilliseconds);
        return max;
    }

    private void Expand()
    {
        var canvas = (Canvas)bands[0].Window.Content!;
        if (card is not null)
        {
            canvas.Children.Remove(card);
            card = null;
            return;
        }
        card = Card();
        Canvas.SetLeft(card, 40);
        Canvas.SetTop(card, OverlaySpike.PillTop + OverlaySpike.PillHeight + 16);
        expands.Add(Stopwatch.GetTimestamp());
        canvas.Children.Add(card);
        Log.Line("card expands");
    }

    // The off-screen warm-up stand-in: the card drawn once at 1 % opacity, then removed two frames later.
    private void Warm(Canvas canvas)
    {
        var warm = Card();
        warm.Opacity = 0.01;
        canvas.Children.Add(warm);
        var top = bands[0].Window;
        top.RequestAnimationFrame(first => top.RequestAnimationFrame(second =>
        {
            canvas.Children.Remove(warm);
            Log.Line("warm-up drawn");
        }));
    }

    private static Border Card() => new()
    {
        Width = OverlaySpike.PillWidth,
        Height = 240,
        CornerRadius = new CornerRadius(24),
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0xE6, 0x30, 0x30, 0x38), 0), new GradientStop(Color.FromArgb(0xE6, 0x1C, 0x1C, 0x22), 1) },
        },
        BoxShadow = BoxShadows.Parse("0 12 40 0 #80000000, 0 0 0 1 #30FFFFFF"),
        Child = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Needs your OK. Outlook", Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeight.SemiBold },
                new Border { Height = 60, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), Effect = new BlurEffect { Radius = 12 } },
                new TextBlock { Text = "Send the reply to Dana", Foreground = Brushes.LightGray, FontSize = 13 },
            },
        },
    };
}

/// <summary>One travel schedule on one clock, in screen pixels: A to B (728 DIP), B to A, A to C (wide or across), C to A.</summary>
internal sealed class SpikeSchedule
{
    public const double SegmentSeconds = 1.6;

    private readonly PillTravel[] travels;

    public SpikeSchedule(int a, int b, int c, double start)
    {
        Start = start;
        var points = new[] { a, b, a, c, a };
        travels = new PillTravel[4];
        for (var i = 0; i < 4; i++)
            travels[i] = PillTravel.Start(0, new PillVector(points[i], 0), new PillVector(points[i + 1], 0), PillVector.Zero, reduceMotion: false);
    }

    public double Start { get; }

    /// <summary>The pill's left edge in screen pixels at a time (no allocation).</summary>
    public double X(double now)
    {
        var t = Math.Max(0, now - Start);
        var segment = (long)Math.Floor(t / SegmentSeconds);
        return travels[(int)(segment % 4)].Value(t - (segment * SegmentSeconds)).X;
    }
}

/// <summary>The control that owns one band's driver visual.</summary>
internal sealed class SpikeDriverHost : Control
{
    private readonly SpikeBand band;
    private readonly SpikeSchedule schedule;
    private CompositionCustomVisual? visual;

    public SpikeDriverHost(SpikeBand band, SpikeSchedule schedule)
    {
        this.band = band;
        this.schedule = schedule;
        Width = band.Width;
        Height = OverlaySpike.BandHeight;
        IsHitTestVisible = false;
    }

    public SpikeDriverHandler Handler { get; } = new();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (visual is not null || ElementComposition.GetElementVisual(this) is not { } host) return;
        visual = host.Compositor.CreateCustomVisual(Handler);
        visual.Size = new Vector(band.Width, OverlaySpike.BandHeight);
        ElementComposition.SetElementChildVisual(this, visual);
        visual.SendHandlerMessage(new SpikeDriverStart(band, schedule));
    }
}

internal sealed record SpikeDriverStart(SpikeBand Band, SpikeSchedule Schedule);

/// <summary>The render-thread driver: the travel evaluated at the frame time, drawn with clip, offset and opacity in one draw.</summary>
internal sealed class SpikeDriverHandler : CompositionCustomVisualHandler
{
    private const int Capacity = 1 << 16;

    private readonly long[] stamps = new long[Capacity];
    private readonly SKPaint fill = new() { Color = new SKColor(0x2A, 0x2A, 0x30), IsAntialias = true };
    private readonly SKPaint edge = new() { Color = new SKColor(0xFF, 0xFF, 0xFF, 0x40), IsAntialias = true, IsStroke = true, StrokeWidth = 1 };
    private SpikeDriverStart? start;
    private int count;

    public long[] Stamps()
    {
        var n = Math.Min(Volatile.Read(ref count), Capacity);
        return stamps[..n];
    }

    public override void OnMessage(object message)
    {
        if (message is not SpikeDriverStart next) return;
        start = next;
        Invalidate();
        RegisterForNextAnimationFrameUpdate();
    }

    public override void OnAnimationFrameUpdate()
    {
        Invalidate();
        RegisterForNextAnimationFrameUpdate();
    }

    public override Rect GetRenderBounds() => start is { } s ? new Rect(0, 0, s.Band.Width, OverlaySpike.BandHeight) : default;

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (start is not { } s || context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
        var now = Stopwatch.GetTimestamp();
        if (count < Capacity) stamps[count] = now;
        Volatile.Write(ref count, count + 1);
        var x = (s.Schedule.X(now / (double)Stopwatch.Frequency) - s.Band.Work.X) / s.Band.Scale;
        using var lease = feature.Lease();
        var canvas = lease.SkCanvas;
        var saved = canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, (float)s.Band.Width, (float)OverlaySpike.BandHeight));
        canvas.Translate((float)x, (float)OverlaySpike.PillTop);
        fill.Color = fill.Color.WithAlpha((byte)Math.Round(255 * lease.CurrentOpacity));
        var radius = (float)(OverlaySpike.PillHeight / 2);
        canvas.DrawRoundRect(0, 0, (float)OverlaySpike.PillWidth, (float)OverlaySpike.PillHeight, radius, radius, fill);
        canvas.DrawRoundRect(0.5f, 0.5f, (float)OverlaySpike.PillWidth - 1, (float)OverlaySpike.PillHeight - 1, radius, radius, edge);
        canvas.RestoreToCount(saved);
    }
}

internal static class SpikeStats
{
    public static double Percentile(double[] values, double p)
    {
        if (values.Length == 0) return 0;
        var sorted = values.Order().ToArray();
        return sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
    }

    public static string Describe(double[] values) => values.Length == 0
        ? "none"
        : string.Create(CultureInfo.InvariantCulture, $"n {values.Length}, p50 {Percentile(values, 0.5):F2} ms, p95 {Percentile(values, 0.95):F2} ms, p99 {Percentile(values, 0.99):F2} ms, max {values.Max():F2} ms");
}

/// <summary>One low-level mouse record, stamped in the hook callback.</summary>
internal readonly record struct SpikeRecord(long Stamp, int Message, int X, int Y, uint Flags, nint Extra)
{
    private const int LeftDown = 0x0201;
    private const int RightDown = 0x0204;
    private const int MiddleDown = 0x0207;

    public double At => Stamp / (double)Stopwatch.Frequency;

    public bool ButtonDown => Message is LeftDown or RightDown or MiddleDown;

    /// <summary>Pen and touch promote to mouse records carrying the 0xFF515700 signature.</summary>
    public string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"0x{Message:x} at ({X}, {Y}) px, {((Extra & unchecked((nint)0xFFFFFF00)) == unchecked((nint)0xFF515700) ? ((Extra & 0x80) != 0 ? "touch" : "pen") : "mouse")}{((Flags & 1) != 0 ? ", injected" : "")}");
}

/// <summary>A low-level mouse hook on its own thread whose callback only stamps and queues; a reader thread handles records.</summary>
internal sealed class SpikeMouseHook : IDisposable
{
    private readonly BlockingCollection<SpikeRecord> records = new(8192);
    private readonly SpikeNative.HookProc callback;
    private readonly ManualResetEventSlim ready = new();
    private uint threadId;

    public SpikeMouseHook(Action<SpikeRecord> onRecord)
    {
        callback = Callback;
        new Thread(() =>
        {
            foreach (var record in records.GetConsumingEnumerable()) onRecord(record);
        }) { IsBackground = true, Name = "Tactus spike reader", Priority = ThreadPriority.AboveNormal }.Start();
        new Thread(Loop) { IsBackground = true, Name = "Tactus spike hook", Priority = ThreadPriority.Highest }.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        if (threadId != 0) SpikeNative.PostThreadMessage(threadId, 0x0012, 0, 0);
        records.CompleteAdding();
        ready.Dispose();
    }

    private void Loop()
    {
        threadId = SpikeNative.GetCurrentThreadId();
        var hook = SpikeNative.SetWindowsHookEx(14, callback, SpikeNative.GetModuleHandle(null), 0);
        ready.Set();
        while (SpikeNative.GetMessage(out _, 0, 0, 0) > 0)
        {
        }
        if (hook != 0) SpikeNative.UnhookWindowsHookEx(hook);
    }

    private nint Callback(int code, nint message, nint data)
    {
        if (code >= 0 && !records.IsAddingCompleted)
        {
            var stamp = Stopwatch.GetTimestamp();
            var record = Marshal.PtrToStructure<SpikeNative.MouseRecord>(data);
            try
            {
                records.TryAdd(new SpikeRecord(stamp, unchecked((int)message), record.X, record.Y, record.Flags, record.Extra));
            }
            catch (InvalidOperationException)
            {
                // Disposed between the check and the add: the record is dropped.
            }
        }
        return SpikeNative.CallNextHookEx(0, code, message, data);
    }
}

internal static class SpikeNative
{
    public const uint NoRedirectionBitmap = 0x00200000;

    private const int GwlExStyle = -20;

    public delegate nint HookProc(int code, nint message, nint data);

    public static uint ExtendedStyle(nint window) => unchecked((uint)GetWindowLongPtr(window, GwlExStyle));

    public static void SetExtendedStyle(nint window, uint style) => SetWindowLongPtr(window, GwlExStyle, (nint)style);

    public static void LayeredOpaque(nint window) => SetLayeredWindowAttributes(window, 0, 255, 2);

    /// <summary>Sets a window's region (client pixels, a round rect) or an empty region when null.</summary>
    public static void SetRegion(nint window, PixelRect? rect, int diameter = 0)
    {
        if (window == 0) return;
        var region = rect is { } r ? CreateRoundRectRgn(r.X, r.Y, r.Right + 1, r.Bottom + 1, diameter, diameter) : CreateRectRgn(0, 0, 0, 0);
        if (region != 0 && !SetWindowRgn(window, region, true)) DeleteObject(region);
    }

    /// <summary>The root window under a screen point.</summary>
    public static nint RootAt(int x, int y)
    {
        var window = WindowFromPoint(new NativePoint { X = x, Y = y });
        return window == 0 ? 0 : GetAncestor(window, 2);
    }

    /// <summary>The primary display's refresh rate (VREFRESH).</summary>
    public static int RefreshHz()
    {
        var dc = GetDC(0);
        if (dc == 0) return 0;
        try
        {
            return GetDeviceCaps(dc, 116);
        }
        finally
        {
            ReleaseDC(0, dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseRecord
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(nint dc, int index);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowsHookEx(int kind, HookProc callback, nint module, uint thread);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out NativeMessage message, nint window, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint thread, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? name);
}
#endif
