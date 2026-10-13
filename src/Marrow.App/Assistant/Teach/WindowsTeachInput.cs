// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 1, 12.6; unit AS-19; ED-NEW-dextra-as19-2, -9). No Swift twin.
// The mouse and keyboard hooks of a teach recording: two low-level hooks on a thread of their own, installed when a recording starts or
// resumes and removed when it pauses or ends, never otherwise. The hooks keep no keystroke: a typed character only adds to a count, and a
// shortcut (a key with Ctrl, Alt or the Windows key, or Enter, Tab, Escape, Delete, an F key) is reported as its chord. A burst of typing ends
// after 1.2 s without a key, or at a click or a chord, and is reported as how many characters it had. Input that another program injected
// (a macro, Tactus) is not the owner's and is not heard. The callbacks do almost nothing, so Windows never drops the hooks for being slow.
// Never used by automated tests; TeachInputNativeTests is the lead's to run.
using System.Runtime.InteropServices;

namespace Marrow.App.Assistant.Teach;

public sealed class WindowsTeachInput : ITeachInput
{
    /// <summary>A burst of typing ends this long after its last key.</summary>
    public static readonly TimeSpan BurstIdle = TimeSpan.FromMilliseconds(1200);

    private const int VkBack = 0x08;
    private const int VkTab = 0x09;
    private const int VkReturn = 0x0D;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkEscape = 0x1B;
    private const int VkSpace = 0x20;
    private const int VkDelete = 0x2E;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkF1 = 0x70;
    private const int VkF24 = 0x87;

    private readonly object gate = new();
    private readonly TeachNative.HookProc mouseProc;
    private readonly TeachNative.HookProc keyProc;
    private Action<TeachInput>? sink;
    private Thread? loop;
    private uint loopId;
    private nint mouseHook;
    private nint keyHook;
    private int burst;
    private Timer? idle;
    private DateTime lastLeft = DateTime.MinValue;
    private TeachNative.Point lastLeftAt;

    public WindowsTeachInput()
    {
        // Kept in fields: a hook's delegate must live as long as the hook, or Windows calls a freed function.
        mouseProc = OnMouse;
        keyProc = OnKey;
    }

    public void Start(Action<TeachInput> onInput)
    {
        ArgumentNullException.ThrowIfNull(onInput);
        lock (gate)
        {
            if (loop is not null) throw new InvalidOperationException("The hooks are already on.");
            sink = onInput;
            burst = 0;
            lastLeft = DateTime.MinValue;
        }
        var ready = new ManualResetEventSlim();
        var installed = false;
        var thread = new Thread(() =>
        {
            // The first PeekMessage makes this thread's message queue, so the posted quit lands.
            TeachNative.PeekMessage(out _, 0, 0, 0, 0);
            loopId = TeachNative.GetCurrentThreadId();
            var module = TeachNative.GetModuleHandle(null);
            mouseHook = TeachNative.SetWindowsHookEx(TeachNative.WhMouseLl, mouseProc, module, 0);
            keyHook = TeachNative.SetWindowsHookEx(TeachNative.WhKeyboardLl, keyProc, module, 0);
            installed = mouseHook != 0 && keyHook != 0;
            ready.Set();
            // Low-level hook callbacks are delivered to the thread that installed them, while it pumps messages.
            while (installed && TeachNative.GetMessage(out _, 0, 0, 0) > 0)
            {
            }
            if (mouseHook != 0) TeachNative.UnhookWindowsHookEx(mouseHook);
            if (keyHook != 0) TeachNative.UnhookWindowsHookEx(keyHook);
            mouseHook = 0;
            keyHook = 0;
        })
        { IsBackground = true, Name = "nth teach input" };
        thread.Start();
        var answered = ready.Wait(TimeSpan.FromSeconds(2));
        ready.Dispose();
        if (!answered || !installed)
        {
            lock (gate) sink = null;
            if (answered) TeachNative.PostThreadMessage(loopId, TeachNative.WmQuit, 0, 0);
            thread.Join(TimeSpan.FromSeconds(1));
            throw new IOException("Windows refused the mouse and keyboard hooks.");
        }
        lock (gate) loop = thread;
    }

    public TeachInput? Stop()
    {
        Thread? thread;
        lock (gate)
        {
            thread = loop;
            loop = null;
        }
        if (thread is null) return null;
        TeachNative.PostThreadMessage(loopId, TeachNative.WmQuit, 0, 0);
        thread.Join(TimeSpan.FromSeconds(2));
        TeachInput? tail = null;
        lock (gate)
        {
            sink = null;
            idle?.Dispose();
            idle = null;
            if (burst > 0) tail = new TeachInput.Typing(burst);
            burst = 0;
        }
        return tail;
    }

    public void Dispose() => Stop();

    // ---- the mouse ----

    private nint OnMouse(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && (wParam == (nint)TeachNative.WmLButtonDown || wParam == (nint)TeachNative.WmRButtonDown))
        {
            var hook = Marshal.PtrToStructure<TeachNative.MouseHook>(lParam);
            if ((hook.Flags & TeachNative.MouseInjected) == 0) Click(hook.Pt, wParam == (nint)TeachNative.WmRButtonDown);
        }
        return TeachNative.CallNextHookEx(0, code, wParam, lParam);
    }

    private void Click(TeachNative.Point at, bool right)
    {
        Flush();
        var verb = "click";
        if (right)
        {
            verb = "right-click";
        }
        else
        {
            var now = DateTime.UtcNow;
            var near = Math.Abs(at.X - lastLeftAt.X) <= TeachNative.GetSystemMetrics(TeachNative.SmCxDoubleClick) / 2
                && Math.Abs(at.Y - lastLeftAt.Y) <= TeachNative.GetSystemMetrics(TeachNative.SmCyDoubleClick) / 2;
            if (near && (now - lastLeft).TotalMilliseconds <= TeachNative.GetDoubleClickTime())
            {
                verb = "double-click";
                lastLeft = DateTime.MinValue; // a third click is a click again
            }
            else
            {
                lastLeft = now;
                lastLeftAt = at;
            }
        }
        Raise(new TeachInput.Click(at.X, at.Y, verb));
    }

    // ---- the keyboard ----

    private nint OnKey(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && (wParam == (nint)TeachNative.WmKeyDown || wParam == (nint)TeachNative.WmSysKeyDown))
        {
            var hook = Marshal.PtrToStructure<TeachNative.KeyboardHook>(lParam);
            if ((hook.Flags & TeachNative.KeyInjected) == 0) Key((int)hook.VkCode);
        }
        return TeachNative.CallNextHookEx(0, code, wParam, lParam);
    }

    private void Key(int vk)
    {
        if (vk is VkShift or VkControl or VkMenu or VkLWin or VkRWin or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5) return;
        var ctrl = Down(VkControl);
        var alt = Down(VkMenu);
        var win = Down(VkLWin) || Down(VkRWin);
        var shift = Down(VkShift);
        var printable = IsPrintable(vk);
        // Ctrl and Alt together are AltGr on many layouts, and make a character: that is typing, not a shortcut.
        var altGr = ctrl && alt && printable;
        if (!altGr && (ctrl || alt || win))
        {
            Flush();
            Raise(new TeachInput.Chord(Chord(ctrl, alt, shift, win, vk)));
            return;
        }
        if (vk is VkReturn or VkTab or VkEscape or VkDelete || vk is >= VkF1 and <= VkF24)
        {
            Flush();
            Raise(new TeachInput.Chord(Chord(false, false, shift, false, vk)));
            return;
        }
        if (!printable) return; // arrows, Home, End, Backspace and the rest move or correct; they type nothing new
        lock (gate)
        {
            if (sink is null) return;
            var began = burst == 0;
            burst++;
            idle ??= new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
            idle.Change(BurstIdle, Timeout.InfiniteTimeSpan);
            if (began) sink(new TeachInput.TypingStarted());
        }
    }

    private void Flush()
    {
        Action<TeachInput>? send;
        int count;
        lock (gate)
        {
            send = sink;
            count = burst;
            burst = 0;
            idle?.Change(Timeout.Infinite, Timeout.Infinite);
        }
        if (count > 0) send?.Invoke(new TeachInput.Typing(count));
    }

    private void Raise(TeachInput input)
    {
        Action<TeachInput>? send;
        lock (gate) send = sink;
        send?.Invoke(input);
    }

    private static bool Down(int vk) => (TeachNative.GetAsyncKeyState(vk) & 0x8000) != 0;

    private static bool IsPrintable(int vk) =>
        vk is VkSpace or (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A) or (>= 0x60 and <= 0x6F) or (>= 0xBA and <= 0xC0) or (>= 0xDB and <= 0xDF) or 0xE2;

    /// <summary>"Ctrl+Shift+S": the modifiers in a fixed order, then the key's name.</summary>
    public static string Chord(bool ctrl, bool alt, bool shift, bool win, int vk)
    {
        var parts = new List<string>(5);
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (win) parts.Add("Win");
        parts.Add(KeyName(vk));
        return string.Join('+', parts);
    }

    /// <summary>A key's name in a chord: its letter or digit, or Enter, Tab, Esc, Delete, Space, an arrow, an F key.</summary>
    public static string KeyName(int vk) => vk switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        VkReturn => "Enter",
        VkTab => "Tab",
        VkEscape => "Esc",
        VkDelete => "Delete",
        VkSpace => "Space",
        VkBack => "Backspace",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x2D => "Insert",
        >= VkF1 and <= VkF24 => "F" + (vk - VkF1 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        >= 0x60 and <= 0x69 => "Num" + (vk - 0x60).ToString(System.Globalization.CultureInfo.InvariantCulture),
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        0xBA => ";",
        _ => "Key" + vk.ToString("X2", System.Globalization.CultureInfo.InvariantCulture),
    };
}
