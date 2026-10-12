// PORT-ONLY: Tactus, PC control (OD-0144, OD-0145, ED-NEW-pcctl-12, ED-NEW-pcctl-15, ED-NEW-pcctl-17; DESIGN.md 2.7
// "Physical input monitor" and "Hook health", 10.2; critiques C2, C3, C17). The persistent WH_MOUSE_LL and WH_KEYBOARD_LL
// hooks, on one dedicated thread at time-critical priority with its own message loop, installed while someone keeps the
// monitor. The callbacks are static [UnmanagedCallersOnly] functions: they run PcHookCore (a few comparisons, no
// allocation, no lock), write one record into a preallocated ring and return, swallowing the event only while a borrow
// holds the user's input. A reader thread turns records into monitor events (resolving the window under the pointer
// and the foreground's focus) and runs the 250 ms heartbeat: when GetLastInputInfo shows input newer than the hooks
// delivered, the hooks are presumed dead and reinstalled at once, and the monitor reads unhealthy (R4 off) until a
// fresh event agrees. Never used by automated tests: they drive PhysicalInputMonitor and PcHookCore directly.
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl.Native;

public sealed unsafe class PcLowLevelHooks : IDisposable
{
    public const int HeartbeatMs = 250;

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private const int WM_MOUSEHWHEEL = 0x020E;
    private const uint LLKHF_EXTENDED = 0x01;
    private const uint LLKHF_INJECTED = 0x10;
    private const uint LLMHF_INJECTED = 0x01;
    private const uint WorkMessage = 0x8000 + 0x52; // WM_APP + 0x52
    private const int THREAD_PRIORITY_TIME_CRITICAL = 15;

    // The callbacks are static, so the core and the ring are too: one set of hooks per process.
    private static readonly PcHookCore Core = new();
    private static readonly PcInputRing Ring = new(8192);

    private readonly PhysicalInputMonitor monitor;
    private readonly IPcClock clock;
    private readonly ConcurrentQueue<Action> work = new();
    private readonly ManualResetEventSlim ready = new();
    private readonly ManualResetEventSlim active = new();
    private readonly IForegroundProtection? protection;
    private readonly Thread hookThread;
    private readonly Thread readerThread;
    private uint hookThreadId;
    private nint keyboardHook;
    private nint mouseHook;
    private volatile bool installed;
    private volatile bool running = true;

    public PcLowLevelHooks(PhysicalInputMonitor monitor, IPcClock clock, IForegroundProtection? protection = null)
    {
        this.monitor = monitor;
        this.clock = clock;
        this.protection = protection;
        Core.Watch(monitor.ChordKey);
        hookThread = new Thread(RunHooks) { IsBackground = true, Name = "Tactus input hooks" };
        hookThread.Start();
        ready.Wait();
        readerThread = new Thread(RunReader) { IsBackground = true, Name = "Tactus input reader" };
        readerThread.Start();
        monitor.KeepChanged += OnKeepChanged;
        monitor.ChordsWatched += Core.Watch;
        if (monitor.Kept) Post(Install);
    }

    /// <summary>The hook core, for the borrow's hold (swallow on and off, the abort flag).</summary>
    public static PcHookCore Hold => Core;

    public void Dispose()
    {
        monitor.KeepChanged -= OnKeepChanged;
        monitor.ChordsWatched -= Core.Watch;
        Core.EndSwallow();
        Post(Uninstall);
        running = false;
        active.Set();
        PostThreadMessage(hookThreadId, 0x0012, 0, 0); // WM_QUIT
        hookThread.Join(TimeSpan.FromSeconds(2));
        readerThread.Join(TimeSpan.FromSeconds(2));
        ready.Dispose();
        active.Dispose();
    }

    private void OnKeepChanged(bool keep) => Post(keep ? Install : Uninstall);

    private void Post(Action action)
    {
        work.Enqueue(action);
        PostThreadMessage(hookThreadId, WorkMessage, 0, 0);
    }

    private void RunHooks()
    {
        SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);
        PeekMessage(out _, 0, 0, 0, 0);
        hookThreadId = GetCurrentThreadId();
        ready.Set();
        while (GetMessage(out var message, 0, 0, 0) > 0)
        {
            if (message.Message == WorkMessage)
            {
                while (work.TryDequeue(out var action)) action();
            }
        }
        while (work.TryDequeue(out var action)) action();
        Uninstall();
    }

    private void Install()
    {
        if (keyboardHook != 0 && mouseHook != 0) return;
        var module = GetModuleHandle(null);
        keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, (nint)(delegate* unmanaged<int, nint, nint, nint>)&KeyboardProc, module, 0);
        mouseHook = SetWindowsHookEx(WH_MOUSE_LL, (nint)(delegate* unmanaged<int, nint, nint, nint>)&MouseProc, module, 0);
        if (keyboardHook == 0 || mouseHook == 0)
        {
            // Fail closed: without both hooks the monitor reads unhealthy and no borrow starts.
            Uninstall();
            return;
        }
        installed = true;
        active.Set();
        monitor.Installed(true);
    }

    private void Uninstall() => Remove(endHold: true);

    private void Remove(bool endHold)
    {
        if (endHold) Core.EndSwallow();
        if (keyboardHook != 0) UnhookWindowsHookEx(keyboardHook);
        if (mouseHook != 0) UnhookWindowsHookEx(mouseHook);
        keyboardHook = mouseHook = 0;
        installed = false;
        active.Reset();
        monitor.Installed(false);
    }

    // The hooks presumed dead: out and back in at once. A borrow's hold carries over (its keys are still held back).
    private void Reinstall()
    {
        if (!installed) return;
        Remove(endHold: false);
        Install();
    }

    private void RunReader()
    {
        using var dpi = Dpi.PerMonitorV2();
        var nextBeat = clock.NowMs;
        while (running)
        {
            if (!installed)
            {
                // Nothing to read while no one keeps the monitor: sleep until the hooks go in.
                while (Ring.TryRead(out var late)) monitor.Record(Convert(late));
                active.Wait(1_000);
                continue;
            }
            var any = false;
            while (Ring.TryRead(out var record))
            {
                any = true;
                monitor.Record(Convert(record));
            }
            if (installed && clock.NowMs >= nextBeat)
            {
                nextBeat = clock.NowMs + HeartbeatMs;
                // Input the hooks missed: reinstall, unless the foreground is one a user process's hooks cannot see
                // (an elevated or UIAccess window, the secure desktop); the monitor reads it as the user's activity anyway.
                if (LastInputMs() is { } tick && monitor.LastInputSeen(tick) && !Unseeable()) Post(Reinstall);
            }
            Thread.Sleep(any ? 1 : 4);
        }
    }

    private bool Unseeable() => protection?.Read() is PcForegroundProtection.Elevated or PcForegroundProtection.UiAccess
        or PcForegroundProtection.SecureDesktop or PcForegroundProtection.Locked;

    // The hook's tick time moved onto the monitor's clock; the window and focus resolved here, never in the hook.
    private PcRawInput Convert(in PcHookRecord record)
    {
        var age = (long)unchecked(Win32.GetTickCount() - record.Time);
        var at = clock.NowMs - Math.Max(0, age);
        nint window;
        nint focus = 0;
        if (record.Kind == PcRawKind.Key)
        {
            var front = Win32.GetForegroundWindow();
            window = front == 0 ? 0 : Win32.GetAncestor(front, Win32.GA_ROOT);
            var thread = front == 0 ? 0 : Win32.GetWindowThreadProcessId(front, out _);
            var info = new Win32.GUITHREADINFO { Size = Marshal.SizeOf<Win32.GUITHREADINFO>() };
            if (thread != 0 && Win32.GetGUIThreadInfo(thread, ref info)) focus = info.Focus;
        }
        else
        {
            var under = Win32.WindowFromPoint(new Win32.POINT { X = record.X, Y = record.Y });
            window = under == 0 ? 0 : Win32.GetAncestor(under, Win32.GA_ROOT);
        }
        return new PcRawInput(at, record.Kind, record.Down, record.Code, record.Injected, record.Tag)
        {
            Point = new PcPoint(record.X, record.Y),
            Moved = record.Kind == PcRawKind.Move ? new PcPoint(record.Dx, record.Dy) : default,
            Delta = record.Kind == PcRawKind.Wheel ? record.Dx : 0,
            Window = window,
            Focus = focus,
            Swallowed = record.Swallowed,
            Repeat = record.Repeat,
            Scan = record.Scan,
            Extended = record.Extended,
            Modifiers = record.Modifiers,
            HeldKeys = record.HeldKeys,
            HeldButtons = record.HeldButtons,
        };
    }

    /// <summary>GetLastInputInfo on the monitor's clock; null when it cannot be read.</summary>
    public long? LastInputMs()
    {
        var last = new Win32.LASTINPUTINFO { Size = Marshal.SizeOf<Win32.LASTINPUTINFO>() };
        if (!Win32.GetLastInputInfo(ref last)) return null;
        return clock.NowMs - (long)unchecked(Win32.GetTickCount() - last.Time);
    }

    [UnmanagedCallersOnly]
    private static nint KeyboardProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = (KBDLLHOOKSTRUCT*)lParam;
            var message = (int)wParam;
            var down = message is WM_KEYDOWN or WM_SYSKEYDOWN;
            if (down || message is WM_KEYUP or WM_SYSKEYUP)
            {
                var record = Core.Key(info->Code, info->Scan, down, (info->Flags & LLKHF_INJECTED) != 0, info->Extra, info->Time, out var swallow,
                    (info->Flags & LLKHF_EXTENDED) != 0);
                Ring.Write(in record);
                if (swallow) return 1;
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    [UnmanagedCallersOnly]
    private static nint MouseProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = (MSLLHOOKSTRUCT*)lParam;
            var message = (int)wParam;
            var injected = (info->Flags & LLMHF_INJECTED) != 0;
            var high = (short)(info->MouseData >> 16);
            PcRawKind kind;
            ushort button = 0;
            var down = false;
            switch (message)
            {
                case WM_MOUSEMOVE: kind = PcRawKind.Move; break;
                case WM_LBUTTONDOWN or WM_LBUTTONUP: kind = PcRawKind.Button; button = 1; down = message == WM_LBUTTONDOWN; break;
                case WM_RBUTTONDOWN or WM_RBUTTONUP: kind = PcRawKind.Button; button = 2; down = message == WM_RBUTTONDOWN; break;
                case WM_MBUTTONDOWN or WM_MBUTTONUP: kind = PcRawKind.Button; button = 3; down = message == WM_MBUTTONDOWN; break;
                case WM_XBUTTONDOWN or WM_XBUTTONUP: kind = PcRawKind.Button; button = (ushort)(high == 2 ? 5 : 4); down = message == WM_XBUTTONDOWN; break;
                case WM_MOUSEWHEEL: kind = PcRawKind.Wheel; break;
                case WM_MOUSEHWHEEL: kind = PcRawKind.Wheel; button = 1; break;
                default: return CallNextHookEx(0, code, wParam, lParam);
            }
            var record = Core.Mouse(kind, button, down, info->X, info->Y, kind == PcRawKind.Wheel ? high : 0, injected, info->Extra, info->Time, out var swallow);
            Ring.Write(in record);
            if (swallow) return 1;
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint Code;
        public uint Scan;
        public uint Flags;
        public uint Time;
        public nuint Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, nint procedure, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG message, nint window, uint min, uint max, uint remove);

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out MSG message, nint window, uint min, uint max);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint thread, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentThread();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadPriority(nint thread, int priority);
}
