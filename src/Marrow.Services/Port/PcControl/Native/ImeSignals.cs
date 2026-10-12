// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-17; DESIGN.md 2.7 "The gate", critique C4). The IME signals the
// gate reads, while someone keeps the monitor: out-of-context WinEvent hooks for the IME's composition window shown and
// hidden (a composition is open in the foreground until it hides, or the foreground changes), and once a second, and at
// every foreground change, whether the foreground thread's input language is served by an IME (then the keyboard needs
// 2 s of stillness, since a TSF text service may give no composition signal at all). One thread of its own with a
// message loop. Reading only. Never used by automated tests: they call PhysicalInputMonitor.Composition and ImeLanguage.
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.PcControl.Native;

public sealed class ImeSignals : IDisposable
{
    private const uint EVENT_OBJECT_IME_SHOW = 0x8027;
    private const uint EVENT_OBJECT_IME_HIDE = 0x8028;
    private const uint EVENT_OBJECT_IME_CHANGE = 0x8029;
    private const uint WM_TIMER = 0x0113;
    private const uint WorkMessage = 0x8000 + 0x53; // WM_APP + 0x53

    // Primary languages whose input is normally composed: Chinese, Japanese, Korean, Vietnamese.
    private static readonly int[] ComposedLanguages = [0x04, 0x11, 0x12, 0x2A];

    private readonly PhysicalInputMonitor monitor;
    private readonly Win32.WinEventProc onIme;
    private readonly Win32.WinEventProc onForeground;
    private readonly ConcurrentQueue<Action> work = new();
    private readonly ManualResetEventSlim ready = new();
    private readonly Thread loop;
    private uint threadId;
    private nint imeHook;
    private nint foregroundHook;
    private nuint timer;
    private bool disposed;

    public ImeSignals(PhysicalInputMonitor monitor)
    {
        this.monitor = monitor;
        // Held in fields: the hooks call these delegates for as long as they live.
        onIme = OnIme;
        onForeground = OnForeground;
        loop = new Thread(Run) { IsBackground = true, Name = "Tactus IME signals" };
        loop.Start();
        ready.Wait();
        monitor.KeepChanged += OnKeepChanged;
        if (monitor.Kept) Post(Start);
    }

    /// <summary>A keyboard layout handle's primary language is one whose input is composed, or Windows calls it an IME.</summary>
    public static bool Composed(nint layout) => ComposedLanguages.Contains((int)(layout & 0x3FF)) || ImmIsIME(layout);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        monitor.KeepChanged -= OnKeepChanged;
        Post(Stop);
        Win32.PostThreadMessage(threadId, Win32.WM_QUIT, 0, 0);
        loop.Join(TimeSpan.FromSeconds(2));
        ready.Dispose();
    }

    private void OnKeepChanged(bool keep) => Post(keep ? Start : Stop);

    private void Post(Action action)
    {
        work.Enqueue(action);
        Win32.PostThreadMessage(threadId, WorkMessage, 0, 0);
    }

    private void Start()
    {
        if (imeHook != 0) return;
        imeHook = Win32.SetWinEventHook(EVENT_OBJECT_IME_SHOW, EVENT_OBJECT_IME_CHANGE, 0, onIme, 0, 0, Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
        foregroundHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, 0, onForeground, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        timer = SetTimer(0, 0, 1_000, 0);
        CheckLanguage();
    }

    private void Stop()
    {
        if (imeHook != 0) Win32.UnhookWinEvent(imeHook);
        if (foregroundHook != 0) Win32.UnhookWinEvent(foregroundHook);
        if (timer != 0) KillTimer(0, timer);
        imeHook = foregroundHook = 0;
        timer = 0;
        monitor.Composition(0, false);
    }

    private void Run()
    {
        PeekMessage(out _, 0, 0, 0, 0);
        threadId = Win32.GetCurrentThreadId();
        ready.Set();
        while (Win32.GetMessage(out var message, 0, 0, 0) > 0)
        {
            if (message.Window == 0 && message.Message == WorkMessage)
            {
                while (work.TryDequeue(out var action)) action();
                continue;
            }
            if (message.Window == 0 && message.Message == WM_TIMER)
            {
                CheckLanguage();
                continue;
            }
            Win32.DispatchMessage(ref message);
        }
        while (work.TryDequeue(out var action)) action();
    }

    private void OnIme(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time)
    {
        var front = Win32.GetForegroundWindow();
        var root = front == 0 ? 0 : Win32.GetAncestor(front, Win32.GA_ROOT);
        if (eventType == EVENT_OBJECT_IME_SHOW) monitor.Composition(root, true);
        else if (eventType == EVENT_OBJECT_IME_HIDE) monitor.Composition(0, false);
    }

    private void OnForeground(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time)
    {
        // A composition does not survive a change of window: the IME commits or drops it.
        monitor.Composition(0, false);
        CheckLanguage();
    }

    private void CheckLanguage()
    {
        var front = Win32.GetForegroundWindow();
        var thread = front == 0 ? 0 : Win32.GetWindowThreadProcessId(front, out _);
        monitor.ImeLanguage(thread != 0 && Composed(GetKeyboardLayout(thread)));
    }

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out Win32.MSG message, nint window, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    private static extern nuint SetTimer(nint window, nuint id, uint elapseMs, nint callback);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KillTimer(nint window, nuint id);

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint thread);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmIsIME(nint layout);
}
