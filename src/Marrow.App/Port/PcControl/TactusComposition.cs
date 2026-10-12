// PORT-ONLY: Tactus, PC control (OD-0140, OD-0144, OD-0145, ED-NEW-pcctl-40; DESIGN.md 3.2, 3.3, 4.4, 10.2). The app's one
// composition point for Tactus. Program.Main binds it through PcControlStartup; it builds nothing while Tactus is off: the
// first time a session or an MCP call finds the master switch on, it makes the native backends (window catalog, UI
// Automation, change events, the physical input monitor and its hooks, which install only while kept, the IME signals,
// capture through Windows.Graphics.Capture with the GDI fallbacks), the shared parts (PcAssembly), the Tactus chords on
// their own thread (TactusHotkeys over HotkeyThread, the stop key the engine arms; fail closed) and the tray icon (shown
// only while Tactus is live). From then on it hands every chat its parts, answers "attended" for Hydra heads, ends a
// chat's Tactus turn when its runtime goes idle, and treats the master switch going off as stop path 9. The real mouse and
// keyboard (rung R4) stay off unless the user turns them on. Tests build it with fake backends and a fake hotkey host.
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;

namespace Marrow.App.Port.PcControl;

/// <summary>What the composition needs from the desktop and the app; the app binds the native ones, tests bind fakes.</summary>
public sealed class TactusHost
{
    public required Func<PcSettings> Settings { get; init; }
    public required Func<PcBackends> Backends { get; init; }
    public required Func<IPhysicalInput, ITactusHotkeyHost> Hotkeys { get; init; }

    /// <summary>Milliseconds since the user's last input in any app (GetLastInputInfo).</summary>
    public Func<long> IdleMs { get; init; } = () => 0;

    /// <summary>Whether every nth main window is minimized (heads are then not attended).</summary>
    public Func<bool> LeadMinimized { get; init; } = () => false;

    /// <summary>Calls back once when the chat's runtime goes idle (its turn ended); null when the chat has no runtime.</summary>
    public Action<string, Action>? WatchTurnEnd { get; init; }

    /// <summary>Interrupts the provider turns of the given chats (the pill's Stop, DESIGN.md 10.2 path 2).</summary>
    public Action<IReadOnlyList<string>>? InterruptTurns { get; init; }

    /// <summary>Makes the tray icon (on the UI thread); null in tests.</summary>
    public Func<PcAssembly, IDisposable?>? Tray { get; init; }

    /// <summary>The pieces the pill, its cards and the grant store bring; safe defaults until they are bound.</summary>
    public Func<PcSurface>? Surface { get; init; }

    /// <summary>"Answer Tactus": puts the keyboard in the pill's card (P27); none until bound.</summary>
    public Func<Action?>? Answer { get; init; }
}

public sealed class TactusComposition : IDisposable
{
    private readonly TactusHost host;
    private readonly Lock gate = new();
    private readonly HashSet<string> watched = new(StringComparer.Ordinal);
    private PcAssembly? assembly;
    private TactusHotkeys? hotkeys;
    private ITactusHotkeyHost? hotkeyHost;
    private IDisposable? tray;
    private bool wasOn;
    private bool disposed;

    public TactusComposition(TactusHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        this.host = host;
    }

    /// <summary>The app's composition, once Program.Main bound it.</summary>
    public static TactusComposition? Running { get; private set; }

    /// <summary>The shared parts, once Tactus was first on; null before (nothing is built while it is off).</summary>
    public PcAssembly? Assembly
    {
        get
        {
            lock (gate) return assembly;
        }
    }

    /// <summary>The Tactus chords (for the pill's "Pause key is taken"), once built.</summary>
    public TactusHotkeys? Hotkeys
    {
        get
        {
            lock (gate) return hotkeys;
        }
    }

    /// <summary>Program.Main's binding: the native host, set as the process defaults of the nth-pc seams.</summary>
    public static void Bind()
    {
        var composition = new TactusComposition(TactusDesktop.Host());
        Running = composition;
        PcControlBridge.Parts.SetProcessDefault(composition.SharedParts);
        PcControlBridge.PartsFor.SetProcessDefault(composition.PartsFor);
        PcControlBridge.Attended.SetProcessDefault(composition.Attended);
    }

    /// <summary>The shared parts while Tactus is on; null while it is off. The first "on" builds everything.</summary>
    public PcEngineParts? SharedParts() => On() is { } built ? built.Shared : null;

    /// <summary>A chat's parts while Tactus is on (its own audit log), and its turn end is watched from then on.</summary>
    public PcEngineParts? PartsFor(string thread)
    {
        if (On() is not { } built) return null;
        var parts = built.PartsFor(thread);
        bool first;
        lock (gate) first = watched.Add(thread);
        if (first) host.WatchTurnEnd?.Invoke(thread, () => TurnEnded(thread));
        return parts;
    }

    /// <summary>Whether a Hydra head is attended now (the lead chat is never asked).</summary>
    public bool Attended(string thread) => PcAttendance.Attended(true, host.IdleMs(), host.LeadMinimized());

    /// <summary>The master switch went off (the Settings page calls this; a later check also notices it): stop path 9.</summary>
    public void SwitchedOff()
    {
        PcAssembly? built;
        lock (gate)
        {
            built = assembly;
            wasOn = false;
        }
        built?.SwitchedOff();
    }

    public void Dispose()
    {
        PcAssembly? built;
        IDisposable? icon;
        ITactusHotkeyHost? keysHost;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            built = assembly;
            icon = tray;
            keysHost = hotkeyHost;
            assembly = null;
            tray = null;
            hotkeys = null;
            hotkeyHost = null;
        }
        icon?.Dispose();
        // The assembly disposes its stop key (the chords) before their thread goes.
        built?.Dispose();
        keysHost?.Dispose();
    }

    private void TurnEnded(string thread)
    {
        lock (gate) watched.Remove(thread);
        PcControlBridge.EndTurn(thread);
    }

    // Tactus on: the parts (built on the first call); off: null, and a switch from on to off stops everything once.
    private PcAssembly? On()
    {
        var settings = host.Settings();
        PcAssembly? stopNow = null;
        PcAssembly? built;
        lock (gate)
        {
            if (disposed) return null;
            if (!settings.Enabled)
            {
                if (wasOn) stopNow = assembly;
                wasOn = false;
                built = null;
            }
            else
            {
                wasOn = true;
                built = assembly ??= Build();
            }
        }
        stopNow?.SwitchedOff();
        return built;
    }

    // Under gate, once.
    private PcAssembly Build()
    {
        var backends = host.Backends();
        var made = new PcAssembly(backends, host.Settings, shared =>
        {
            hotkeyHost = host.Hotkeys(shared.Input);
            var keys = new TactusHotkeys(hotkeyHost, host.Settings, shared.Stop, shared.Pause, shared.Lanes, shared.IsTactusWindow,
                () => shared.Clock.NowMs, () => host.Answer?.Invoke()?.Invoke());
            hotkeys = keys;
            return keys;
        }, host.Surface?.Invoke(), () => host.InterruptTurns?.Invoke(PcControlBridge.Threads));
        if (host.Tray is { } makeTray) tray = makeTray(made);
        return made;
    }
}

/// <summary>The native host: Windows backends, the hotkey thread, the tray, idle time and the app's runtimes.</summary>
internal static class TactusDesktop
{
    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000L;

    public static TactusHost Host() => new()
    {
        Settings = Settings,
        Backends = Backends,
        Hotkeys = input => new HotkeyThread(input),
        IdleMs = IdleMs,
        LeadMinimized = LeadMinimized,
        WatchTurnEnd = WatchTurnEnd,
        InterruptTurns = InterruptTurns,
        Tray = assembly =>
        {
            TactusTray? made = null;
            Dispatcher.UIThread.Post(() => made = new TactusTray(assembly.Pause, assembly.Stop, assembly.Lanes, () => assembly.Asks.Count > 0));
            return new Deferred(() => Dispatcher.UIThread.Post(() => made?.Dispose()));
        },
    };

    public static PcSettings Settings() => PcSettingsStore.Read(UserDefaults.Standard.Current);

    public static PcBackends Backends()
    {
        var clock = new SystemPcClock();
        var security = new SecurityProbe();
        var protection = new ForegroundProtection(security);
        var input = new PhysicalInputMonitor(clock, protection);
        var hooks = new PcLowLevelHooks(input, clock, protection);
        var ime = new ImeSignals(input);
        var reader = new UiaReader();
        var events = new WinEvents();
        var feed = new PcChangeFeed([events], clock);
        var sources = new List<IWindowCapture>();
        if (PcCaptureSeams.Primary.Current() is { } primary) sources.Add(primary);
        sources.Add(new GdiCapture(fromScreen: false));
        sources.Add(new GdiCapture(fromScreen: true));
        var encoder = PcCaptureSeams.Encoder.Current();
        return new PcBackends
        {
            Catalog = new WindowCatalog(),
            Security = security,
            Reader = reader,
            Feed = feed,
            Messages = new MessageSender(),
            Foreground = new Foreground(),
            Own = new OwnWindows(),
            Launcher = new AppLauncher(),
            Clock = clock,
            Input = input,
            Hold = PcLowLevelHooks.Hold,
            RealHost = new RealInputHost(clock),
            Capture = encoder is null ? null : new PcCapturePipeline(sources, encoder),
            Owned = [hooks, ime, events, feed, reader],
        };
    }

    public static long IdleMs()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.dwTime) : long.MaxValue;
    }

    /// <summary>True when nth shows no captioned top-level window that is not minimized.</summary>
    public static bool LeadMinimized()
    {
        var processId = (uint)Environment.ProcessId;
        var open = false;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == processId && IsWindowVisible(window) && ((long)GetWindowLongPtrW(window, GWL_STYLE) & WS_CAPTION) == WS_CAPTION && !IsIconic(window))
            {
                open = true;
                return false;
            }
            return true;
        }, 0);
        return !open;
    }

    private static void WatchTurnEnd(string thread, Action ended)
    {
        if (!Guid.TryParse(thread, out var id)) return;
        MainActor.Post(() =>
        {
            if (Marrow.App.Window.WindowManager.Shared.Model.ExistingRuntime(id) is not { } runtime) return;
            void Changed(object? sender, PropertyChangedEventArgs args)
            {
                if (args.PropertyName != nameof(runtime.Phase) || runtime.Phase != Marrow.App.Runtime.RuntimePhase.Idle) return;
                runtime.PropertyChanged -= Changed;
                ended();
            }
            runtime.PropertyChanged += Changed;
        });
    }

    private static void InterruptTurns(IReadOnlyList<string> threads) => MainActor.Post(() =>
    {
        foreach (var thread in threads)
            if (Guid.TryParse(thread, out var id)) Marrow.App.Window.WindowManager.Shared.Model.ExistingRuntime(id)?.Interrupt();
    });

    private sealed class Deferred(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint window, int index);
}
