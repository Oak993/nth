// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-12 items 1 and 2, ED-NEW-pcctl-15; DESIGN.md 2.7, 4.7, 10.1,
// 10.2). The low-level keyboard hook Tactus keeps while a stream is working or paused, installed on the hotkey thread
// (which pumps its messages). It only observes: every key goes on to its app untouched. Outside a borrow it tells Tactus
// one of three things about a key-down and nothing more: Esc, a key of a Tactus chord (by its code, for the pause chord's
// injected check), or "another key" (no code kept, so no typing is ever recorded). Auto-repeats are told apart by the
// key's held state, injected keys by LLKHF_INJECTED. The hook's work is a few comparisons, well under the system's hook
// timeout. Never used by automated tests.
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.PcControl.Native;

/// <summary>What the hook reports of one key-down.</summary>
public enum PcHookKey
{
    Escape,
    Chord,
    Other,
}

/// <summary>One key-down: its kind, the key code for Esc and chord keys (0 for any other key), and its repeat and injected flags.</summary>
public readonly record struct PcHookKeyDown(PcHookKey Kind, ushort Key, bool Repeat, bool Injected);

public sealed class PcLowLevelKeyboard : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10;
    private const ushort VK_ESCAPE = 0x1B;

    private readonly HookProc proc;
    private readonly Func<ushort, bool> isChordKey;
    private readonly Action<PcHookKeyDown> keyDown;
    private readonly HashSet<ushort> held = [];
    private nint hook;

    private PcLowLevelKeyboard(Func<ushort, bool> isChordKey, Action<PcHookKeyDown> keyDown)
    {
        this.isChordKey = isChordKey;
        this.keyDown = keyDown;
        proc = Proc;
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    /// <summary>
    /// Installs the hook for the calling thread's message loop (the hotkey thread). <paramref name="keyDown"/> runs on that
    /// thread for every key-down, with nothing but Esc and the keys <paramref name="isChordKey"/> names identified.
    /// </summary>
    public static PcLowLevelKeyboard Install(Func<ushort, bool> isChordKey, Action<PcHookKeyDown> keyDown)
    {
        var keyboard = new PcLowLevelKeyboard(isChordKey, keyDown);
        keyboard.hook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboard.proc, GetModuleHandle(null), 0);
        if (keyboard.hook == 0) throw new InvalidOperationException("the keyboard hook could not be installed (" + Marshal.GetLastPInvokeError() + ")");
        return keyboard;
    }

    /// <summary>Removes the hook; call on the thread that installed it.</summary>
    public void Dispose()
    {
        if (hook == 0) return;
        UnhookWindowsHookEx(hook);
        hook = 0;
        held.Clear();
    }

    private nint Proc(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            try
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var key = (ushort)(info.Code & 0xFFFF);
                var watched = key == VK_ESCAPE || isChordKey(key);
                var message = (int)wParam;
                if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    var repeat = watched && !held.Add(key);
                    var kind = key == VK_ESCAPE ? PcHookKey.Escape : watched ? PcHookKey.Chord : PcHookKey.Other;
                    keyDown(new PcHookKeyDown(kind, watched ? key : (ushort)0, repeat, (info.Flags & LLKHF_INJECTED) != 0));
                }
                else if (message is WM_KEYUP or WM_SYSKEYUP && watched)
                {
                    held.Remove(key);
                }
            }
            catch (Exception)
            {
                // A fault in Tactus's handler never holds up the user's keys.
            }
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

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc procedure, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
}
