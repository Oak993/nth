// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-12 item 1, ED-NEW-pcctl-15, ED-NEW-pcctl-17; DESIGN.md 2.7, 4.4,
// 10.1, 10.2). The thread the Tactus chords live on: its own message queue and loop, never the UI thread, so a busy UI
// never delays a stop. RegisterHotKey with no window posts WM_HOTKEY to this thread's queue (the design's message-only
// window, with no window: the queue is the thread's own, and nothing else runs on it). MOD_NOREPEAT keeps a held chord
// from repeating. The keys come from the physical input monitor's hooks (kept installed while the chords are watched);
// its key reports are handed on through this queue, so the IME check (a cross-process message with a 50 ms limit) never
// runs on the hook's reader. Every call from another thread is posted here and waited on. Never used by automated tests:
// a test binds a fake ITactusHotkeyHost.
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Port.PcControl;

public sealed class HotkeyThread : ITactusHotkeyHost
{
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_QUIT = 0x0012;
    private const uint WorkMessage = 0x8000 + 0x51; // WM_APP + 0x51
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint WM_IME_CONTROL = 0x0283;
    private const nint IMC_GETOPENSTATUS = 0x0005;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint SMTO_BLOCK = 0x0001;

    private readonly ConcurrentQueue<Action> work = new();
    private readonly ConcurrentQueue<PcHookKeyDown> keys = new();
    private readonly Thread loop;
    private readonly ManualResetEventSlim ready = new();
    private readonly IPhysicalInput input;
    private IDisposable? kept;
    private uint threadId;
    private bool disposed;

    public HotkeyThread(IPhysicalInput input)
    {
        this.input = input;
        loop = new Thread(Run) { IsBackground = true, Name = "Tactus hotkeys" };
        loop.Start();
        ready.Wait();
    }

    public event Action<int>? Pressed;

    public event Action<PcHookKeyDown>? KeyDown;

    public bool Register(int id, PcHotkey chord) => Invoke(() => RegisterHotKey(0, id, (uint)chord.Modifiers | MOD_NOREPEAT, chord.Key));

    public void Unregister(int id) => Invoke(() => UnregisterHotKey(0, id));

    public void Watch(bool on, Func<ushort, bool> isChordKey) => Invoke(() =>
    {
        // Hooks that cannot be installed only lose Esc three times and the injected check: the chords still work.
        if (on && kept is null)
        {
            input.WatchChords(isChordKey);
            input.KeyDown += Report;
            kept = input.Keep();
        }
        else if (!on && kept is not null)
        {
            input.KeyDown -= Report;
            kept.Dispose();
            kept = null;
        }
        return true;
    });

    public nint Foreground() => GetForegroundWindow();

    public bool ImeOpen(nint window)
    {
        if (window == 0) return false;
        var ime = ImmGetDefaultIMEWnd(window);
        if (ime == 0) return false;
        var sent = SendMessageTimeout(ime, WM_IME_CONTROL, IMC_GETOPENSTATUS, 0, SMTO_ABORTIFHUNG | SMTO_BLOCK, 50, out var open);
        return sent != 0 && open != 0;
    }

    public bool TypesCharacter(PcHotkey chord)
    {
        var count = GetKeyboardLayoutList(0, null);
        if (count <= 0) return false;
        var layouts = new nint[count];
        count = GetKeyboardLayoutList(count, layouts);
        var state = new byte[256];
        if (chord.Modifiers.HasFlag(PcHotkeyModifiers.Control)) state[0x11] = state[0xA2] = 0x80;
        if (chord.Modifiers.HasFlag(PcHotkeyModifiers.Alt)) state[0x12] = state[0xA4] = 0x80;
        if (chord.Modifiers.HasFlag(PcHotkeyModifiers.Shift)) state[0x10] = state[0xA0] = 0x80;
        var text = new char[8];
        for (var i = 0; i < count; i++)
        {
            var scan = MapVirtualKeyEx(chord.Key, 0, layouts[i]);
            // Flag 4: the call leaves the keyboard state (dead keys) as it was (Windows 10 1607 and later).
            var made = ToUnicodeEx(chord.Key, scan, state, text, text.Length, 4, layouts[i]);
            for (var c = 0; c < made; c++)
            {
                // A printable character: control codes (Ctrl+letter) and white space type nothing a user would see as typed.
                if (!char.IsControl(text[c]) && !char.IsWhiteSpace(text[c])) return true;
            }
        }
        return false;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        work.Enqueue(() =>
        {
            input.KeyDown -= Report;
            kept?.Dispose();
            kept = null;
        });
        PostThreadMessage(threadId, WorkMessage, 0, 0);
        PostThreadMessage(threadId, WM_QUIT, 0, 0);
        loop.Join(TimeSpan.FromSeconds(2));
        ready.Dispose();
    }

    private bool Invoke(Func<bool> action)
    {
        if (Environment.CurrentManagedThreadId == loop.ManagedThreadId) return action();
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Enqueue(() =>
        {
            try { done.TrySetResult(action()); }
            catch (Exception error) { done.TrySetException(error); }
        });
        PostThreadMessage(threadId, WorkMessage, 0, 0);
        return done.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
    }

    // On the hook's reader: queue the report and return at once.
    private void Report(PcHookKeyDown key)
    {
        keys.Enqueue(key);
        PostThreadMessage(threadId, WorkMessage, 0, 0);
    }

    private void Run()
    {
        // The first PeekMessage makes this thread's message queue, so posts from other threads land.
        PeekMessage(out _, 0, 0, 0, 0);
        threadId = GetCurrentThreadId();
        ready.Set();
        while (GetMessage(out var message, 0, 0, 0) > 0)
        {
            if (message.Window != 0)
            {
                DispatchMessage(ref message);
                continue;
            }
            if (message.Message == WM_HOTKEY)
            {
                Pressed?.Invoke((int)message.WParam);
                continue;
            }
            if (message.Message != WorkMessage) continue;
            while (work.TryDequeue(out var action)) action();
            while (keys.TryDequeue(out var key)) KeyDown?.Invoke(key);
        }
        while (work.TryDequeue(out var action)) action();
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG message, nint window, uint min, uint max, uint remove);

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out MSG message, nint window, uint min, uint max);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern nint DispatchMessage(ref MSG message);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint thread, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("imm32.dll")]
    private static extern nint ImmGetDefaultIMEWnd(nint window);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeoutMs, out nint result);

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
    private static extern uint MapVirtualKeyEx(uint code, uint mapType, nint layout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint key, uint scan, byte[] state, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] char[] buffer, int size, uint flags, nint layout);
}
