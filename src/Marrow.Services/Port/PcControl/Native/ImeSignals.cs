// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-17, ED-NEW-pcctl-44; DESIGN.md 2.7 "IME composition" and "The
// gate", critique C4). The IME signals the gate reads, while someone keeps the monitor, in DESIGN's order: out-of-context
// WinEvent hooks for the IME's composition window shown and hidden (a composition is open in the foreground until it
// hides, or the foreground changes); UI Automation TextEditPattern.GetActiveComposition on the focused element, read four
// times a second while the language is served by an IME or a TSF text service (only then can a composition be open); and
// once a second, and at every foreground change, whether the foreground thread's input language is so served (an IMM
// IME, a composed language, or a TSF input processor such as Hindi or Urdu phonetic input), when the keyboard needs 2 s of
// stillness. One STA thread of its own with a message loop (TSF wants an apartment). Reading only. Never used by
// automated tests: they call PcImeLanguage, PhysicalInputMonitor.Composition and ImeLanguage.
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

    private const uint TickMs = 250;
    private const int TicksPerLanguageCheck = 4;

    private readonly PhysicalInputMonitor monitor;
    private readonly Win32.WinEventProc onIme;
    private readonly Win32.WinEventProc onForeground;
    private readonly ConcurrentQueue<Action> work = new();
    private readonly ManualResetEventSlim ready = new();
    private readonly Thread loop;
    private uint threadId;
    private nint imeHook;
    private nint foregroundHook;
    private readonly TsfProfiles tsf = new();
    private readonly UiaComposition uia = new();
    private nuint timer;
    private bool disposed;
    private int ticks;
    private bool served;
    private int reading;

    // The two composition signals, each the foreground root it was seen in (0: none); either one holds the gate.
    private nint shownRoot;
    private nint editRoot;

    public ImeSignals(PhysicalInputMonitor monitor)
    {
        this.monitor = monitor;
        // Held in fields: the hooks call these delegates for as long as they live.
        onIme = OnIme;
        onForeground = OnForeground;
        loop = new Thread(Run) { IsBackground = true, Name = "Tactus IME signals" };
        loop.SetApartmentState(ApartmentState.STA);
        loop.Start();
        ready.Wait();
        monitor.KeepChanged += OnKeepChanged;
        if (monitor.Kept) Post(Start);
    }

    /// <summary>A keyboard layout handle's primary language is one whose input is composed, or Windows calls it an IME.</summary>
    public static bool Composed(nint layout) => PcImeLanguage.Served((ushort)(layout & 0xFFFF), PcInputProfileType.KeyboardLayout, ImmIsIME(layout));

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        monitor.KeepChanged -= OnKeepChanged;
        Post(Stop);
        Win32.PostThreadMessage(threadId, Win32.WM_QUIT, 0, 0);
        loop.Join(TimeSpan.FromSeconds(2));
        uia.Dispose();
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
        timer = SetTimer(0, 0, TickMs, 0);
        CheckLanguage();
    }

    private void Stop()
    {
        if (imeHook != 0) Win32.UnhookWinEvent(imeHook);
        if (foregroundHook != 0) Win32.UnhookWinEvent(foregroundHook);
        if (timer != 0) KillTimer(0, timer);
        imeHook = foregroundHook = 0;
        timer = 0;
        shownRoot = editRoot = 0;
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
                Tick();
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
        if (eventType == EVENT_OBJECT_IME_SHOW) shownRoot = root;
        else if (eventType == EVENT_OBJECT_IME_HIDE) shownRoot = 0;
        Publish();
    }

    private void OnForeground(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time)
    {
        // A composition does not survive a change of window: the IME commits or drops it.
        shownRoot = editRoot = 0;
        Publish();
        CheckLanguage();
    }

    private void Tick()
    {
        if (++ticks % TicksPerLanguageCheck == 0) CheckLanguage();
        if (served) ReadComposition();
    }

    private void CheckLanguage()
    {
        var front = Win32.GetForegroundWindow();
        var thread = front == 0 ? 0 : Win32.GetWindowThreadProcessId(front, out _);
        if (thread == 0)
        {
            served = false;
        }
        else
        {
            var layout = GetKeyboardLayout(thread);
            var language = (ushort)(layout & 0xFFFF);
            served = PcImeLanguage.Served(language, tsf.TypeOf(language), ImmIsIME(layout));
        }
        monitor.ImeLanguage(served);
        if (!served && editRoot != 0)
        {
            editRoot = 0;
            Publish();
        }
    }

    // One TextEditPattern read at a time, off this thread; its answer comes back through the work queue.
    private void ReadComposition()
    {
        if (Interlocked.Exchange(ref reading, 1) == 1) return;
        var front = Win32.GetForegroundWindow();
        var root = front == 0 ? 0 : Win32.GetAncestor(front, Win32.GA_ROOT);
        if (root == 0)
        {
            Volatile.Write(ref reading, 0);
            return;
        }
        Win32.GetWindowThreadProcessId(front, out var process);
        _ = uia.Open(process).ContinueWith(read =>
        {
            var open = read.IsCompletedSuccessfully && read.Result == true;
            Post(() =>
            {
                Volatile.Write(ref reading, 0);
                // A read that comes back after the foreground moved on says nothing about the new window.
                var now = Win32.GetForegroundWindow();
                if ((now == 0 ? 0 : Win32.GetAncestor(now, Win32.GA_ROOT)) != root) return;
                editRoot = open ? root : 0;
                Publish();
            });
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private void Publish()
    {
        var open = shownRoot != 0 ? shownRoot : editRoot;
        monitor.Composition(open, open != 0);
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
