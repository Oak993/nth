// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.6; unit AS-6; ED-NEW-dextra-as6-2). No Swift twin.
// The thread the stop chord lives on: its own message queue and loop, never the UI thread, so a busy UI never delays a
// stop. RegisterHotKey with no window posts WM_HOTKEY to this thread's queue; MOD_NOREPEAT keeps a held chord from
// repeating. Every call from another thread is posted here and waited on (2 s at most); a press is handled on this thread
// and only sets flags downstream. Never used by automated tests: a test binds a fake IStopChordHost, and the Native tests
// (GlobalStopNativeTests) are the lead's to run with focus.
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Safety;

public sealed class StopChordThread : IStopChordHost
{
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_QUIT = 0x0012;
    private const uint WorkMessage = 0x8000 + 0x52; // WM_APP + 0x52
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly ConcurrentQueue<Action> work = new();
    private readonly Thread loop;
    private readonly ManualResetEventSlim ready = new();
    private uint threadId;
    private bool disposed;

    public StopChordThread()
    {
        loop = new Thread(Run) { IsBackground = true, Name = "nth stop chord" };
        loop.Start();
        ready.Wait();
    }

    public event Action<int>? Pressed;

    public bool Register(int id, PcHotkey chord) => Invoke(() => RegisterHotKey(0, id, (uint)chord.Modifiers | MOD_NOREPEAT, chord.Key));

    public void Unregister(int id) => Invoke(() => UnregisterHotKey(0, id));

    public bool TypesCharacter(PcHotkey chord) => TypesAnyCharacter(chord);

    /// <summary>Whether the chord types a printable character on any installed keyboard layout (no thread needed).</summary>
    public static bool TypesAnyCharacter(PcHotkey chord)
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
        PostThreadMessage(threadId, WorkMessage, 0, 0);
        PostThreadMessage(threadId, WM_QUIT, 0, 0);
        loop.Join(TimeSpan.FromSeconds(2));
        ready.Dispose();
    }

    private bool Invoke(Func<bool> action)
    {
        if (Environment.CurrentManagedThreadId == loop.ManagedThreadId) return action();
        if (disposed) return false;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Enqueue(() =>
        {
            try { done.TrySetResult(action()); }
            catch (Exception error) { done.TrySetException(error); }
        });
        PostThreadMessage(threadId, WorkMessage, 0, 0);
        // A host that never answers (a hung thread) refuses the chord rather than hanging a start (fail closed).
        return done.Task.Wait(TimeSpan.FromSeconds(2)) && done.Task.Result;
    }

    private void Run()
    {
        // The first PeekMessage makes this thread's message queue, so posts from other threads land.
        PeekMessage(out _, 0, 0, 0, 0);
        threadId = GetCurrentThreadId();
        ready.Set();
        while (GetMessage(out var message, 0, 0, 0) > 0)
        {
            if (message.Message == WM_HOTKEY)
            {
                Pressed?.Invoke((int)message.WParam);
                continue;
            }
            if (message.Message != WorkMessage) continue;
            while (work.TryDequeue(out var action)) action();
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

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint thread, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
    private static extern uint MapVirtualKeyEx(uint code, uint mapType, nint layout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint key, uint scan, byte[] state, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] char[] buffer, int size, uint flags, nint layout);
}
