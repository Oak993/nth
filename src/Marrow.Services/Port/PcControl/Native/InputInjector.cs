// PORT-ONLY: PC control (OD-0140, ED-NEW-pcctl-1; DESIGN.md 2.3, 3.1). Input through SendInput: the mouse by absolute
// coordinates over the whole virtual desktop (MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK), keys by virtual key with the
// extended flag where Windows wants it, and text as KEYEVENTF_UNICODE in chunks of 64 with a short gap (ED-NEW-pcctl-2:
// text is text, a newline is Enter, modifiers go down first and up last in reverse). The events are planned by
// PcInputPlan, which is pure and tested; only Win32SendInput touches Windows, and no automated test uses it.
using System.Runtime.InteropServices;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl.Native;

public enum PcInputKind
{
    Mouse,
    Key,
}

/// <summary>One INPUT, as planned: a mouse event (Dx, Dy, MouseData, Flags) or a key event (Vk, Scan, Flags).</summary>
public readonly record struct PcInputEvent(PcInputKind Kind, int Dx, int Dy, int MouseData, uint Flags, ushort Vk, ushort Scan)
{
    public static PcInputEvent Mouse(int dx, int dy, int data, uint flags) => new(PcInputKind.Mouse, dx, dy, data, flags, 0, 0);
    public static PcInputEvent Key(ushort vk, ushort scan, uint flags) => new(PcInputKind.Key, 0, 0, 0, flags, vk, scan);
}

/// <summary>Where planned events go: SendInput in the app, a recorder in tests. Returns how many events were inserted.</summary>
public interface ISendInput
{
    int Send(IReadOnlyList<PcInputEvent> events);
    ushort ScanCode(ushort vk);
}

/// <summary>SendInput inserted fewer events than asked (another desktop, or UIPI blocked a higher-integrity window).</summary>
public sealed class PcInputBlockedException(string message) : Exception(message);

public static class PcInputPlan
{
    public const uint MOVE = 0x0001;
    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;
    public const uint RIGHTDOWN = 0x0008;
    public const uint RIGHTUP = 0x0010;
    public const uint MIDDLEDOWN = 0x0020;
    public const uint MIDDLEUP = 0x0040;
    public const uint WHEEL = 0x0800;
    public const uint HWHEEL = 0x1000;
    public const uint VIRTUALDESK = 0x4000;
    public const uint ABSOLUTE = 0x8000;
    public const uint KEY_EXTENDED = 0x0001;
    public const uint KEY_UP = 0x0002;
    public const uint KEY_UNICODE = 0x0004;
    public const int WheelDelta = 120;
    public const int TextChunk = 64;

    public static PcInputEvent MoveTo(PcPoint point, PcRect desktop)
    {
        var (dx, dy) = CoordinateMapper.ToAbsolute(point, desktop);
        return PcInputEvent.Mouse(dx, dy, 0, MOVE | ABSOLUTE | VIRTUALDESK);
    }

    public static (uint Down, uint Up) Buttons(PcMouseButton button) => button switch
    {
        PcMouseButton.Left => (LEFTDOWN, LEFTUP),
        PcMouseButton.Right => (RIGHTDOWN, RIGHTUP),
        PcMouseButton.Middle => (MIDDLEDOWN, MIDDLEUP),
    };

    public static List<PcInputEvent> Click(PcPoint point, PcRect desktop, PcMouseButton button, int count, IReadOnlyList<ushort> modifiers, Func<ushort, ushort> scan)
    {
        var events = new List<PcInputEvent> { MoveTo(point, desktop) };
        events.AddRange(ModifiersDown(modifiers, scan));
        var (down, up) = Buttons(button);
        for (var i = 0; i < Math.Clamp(count, 1, 3); i++)
        {
            events.Add(PcInputEvent.Mouse(0, 0, 0, down));
            events.Add(PcInputEvent.Mouse(0, 0, 0, up));
        }
        events.AddRange(ModifiersUp(modifiers, scan));
        return events;
    }

    public static List<PcInputEvent> Wheel(PcPoint point, PcRect desktop, PcScrollDirection direction, int notches)
    {
        var amount = WheelDelta * Math.Clamp(notches, 1, 100);
        var (flag, data) = direction switch
        {
            PcScrollDirection.Up => (WHEEL, amount),
            PcScrollDirection.Down => (WHEEL, -amount),
            PcScrollDirection.Right => (HWHEEL, amount),
            PcScrollDirection.Left => (HWHEEL, -amount),
        };
        return [MoveTo(point, desktop), PcInputEvent.Mouse(0, 0, data, flag)];
    }

    public static List<PcInputEvent> Chord(KeyChord chord, int repeat, Func<ushort, ushort> scan)
    {
        var events = ModifiersDown(chord.Modifiers, scan);
        for (var i = 0; i < Math.Clamp(repeat, 1, 20); i++) events.AddRange(Press(chord.Key, scan));
        events.AddRange(ModifiersUp(chord.Modifiers, scan));
        return events;
    }

    /// <summary>Text as chunks of events: each character a Unicode press, a line break Enter, a tab Tab.</summary>
    public static List<List<PcInputEvent>> Text(string text, Func<ushort, ushort> scan)
    {
        var chunks = new List<List<PcInputEvent>>();
        var current = new List<PcInputEvent>();
        var characters = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') continue;
            if (c is '\n' or '\r') current.AddRange(Press(KeyChordParser.Return, scan));
            else if (c == '\t') current.AddRange(Press(0x09, scan));
            else
            {
                current.Add(PcInputEvent.Key(0, c, KEY_UNICODE));
                current.Add(PcInputEvent.Key(0, c, KEY_UNICODE | KEY_UP));
            }
            characters++;
            // Never end a chunk between the two halves of a surrogate pair.
            if (characters >= TextChunk && !char.IsHighSurrogate(c))
            {
                chunks.Add(current);
                current = [];
                characters = 0;
            }
        }
        if (current.Count > 0) chunks.Add(current);
        return chunks;
    }

    public static List<PcInputEvent> ModifiersDown(IReadOnlyList<ushort> modifiers, Func<ushort, ushort> scan) =>
        modifiers.Select(vk => PcInputEvent.Key(vk, scan(vk), Extended(vk))).ToList();

    public static List<PcInputEvent> ModifiersUp(IReadOnlyList<ushort> modifiers, Func<ushort, ushort> scan) =>
        modifiers.Reverse().Select(vk => PcInputEvent.Key(vk, scan(vk), Extended(vk) | KEY_UP)).ToList();

    private static List<PcInputEvent> Press(ushort vk, Func<ushort, ushort> scan) =>
        [PcInputEvent.Key(vk, scan(vk), Extended(vk)), PcInputEvent.Key(vk, scan(vk), Extended(vk) | KEY_UP)];

    private static uint Extended(ushort vk) => KeyChordParser.IsExtended(vk) ? KEY_EXTENDED : 0;
}

/// <summary>The injector: plans events, sends them through <see cref="ISendInput"/>, and paces text and drags.</summary>
public sealed class InputInjector(ISendInput sink, Func<PcRect> virtualDesktop, IPcClock clock) : IInputInjector
{
    public const int TextGapMs = 12;
    public const int DragStepMs = 16;

    public Task Move(PcPoint point, CancellationToken cancellationToken)
    {
        Send([PcInputPlan.MoveTo(point, virtualDesktop())]);
        return Task.CompletedTask;
    }

    public Task Click(PcPoint point, PcMouseButton button, int count, IReadOnlyList<ushort> modifiers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Send(PcInputPlan.Click(point, virtualDesktop(), button, count, modifiers, sink.ScanCode));
        return Task.CompletedTask;
    }

    public Task Wheel(PcPoint point, PcScrollDirection direction, int notches, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Send(PcInputPlan.Wheel(point, virtualDesktop(), direction, notches));
        return Task.CompletedTask;
    }

    public async Task Drag(IReadOnlyList<PcPoint> path, IReadOnlyList<ushort> modifiers, int holdMs, CancellationToken cancellationToken)
    {
        if (path.Count < 2) throw new ArgumentException("A drag needs at least two points.", nameof(path));
        var desktop = virtualDesktop();
        Send([PcInputPlan.MoveTo(path[0], desktop), .. PcInputPlan.ModifiersDown(modifiers, sink.ScanCode), PcInputEvent.Mouse(0, 0, 0, PcInputPlan.LEFTDOWN)]);
        try
        {
            if (holdMs > 0) await clock.Delay(holdMs, cancellationToken).ConfigureAwait(false);
            for (var i = 1; i < path.Count; i++)
            {
                await clock.Delay(DragStepMs, cancellationToken).ConfigureAwait(false);
                Send([PcInputPlan.MoveTo(path[i], desktop)]);
            }
        }
        finally
        {
            // The button and the modifiers always come back up, even when the user stopped the drag.
            Send([PcInputEvent.Mouse(0, 0, 0, PcInputPlan.LEFTUP), .. PcInputPlan.ModifiersUp(modifiers, sink.ScanCode)]);
        }
    }

    public Task Keys(KeyChord chord, int repeat, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Send(PcInputPlan.Chord(chord, repeat, sink.ScanCode));
        return Task.CompletedTask;
    }

    public async Task Text(string text, CancellationToken cancellationToken)
    {
        var chunks = PcInputPlan.Text(text, sink.ScanCode);
        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (i > 0) await clock.Delay(TextGapMs, cancellationToken).ConfigureAwait(false);
            Send(chunks[i]);
        }
    }

    private void Send(IReadOnlyList<PcInputEvent> events)
    {
        if (events.Count == 0) return;
        var sent = sink.Send(events);
        if (sent != events.Count) throw new PcInputBlockedException($"Windows took {sent} of {events.Count} input events.");
    }
}

/// <summary>The real sink: SendInput. Never used by automated tests.</summary>
public sealed class Win32SendInput : ISendInput
{
    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    public ushort ScanCode(ushort vk) => unchecked((ushort)MapVirtualKey(vk, 0));

    public int Send(IReadOnlyList<PcInputEvent> events)
    {
        var inputs = new Win32.INPUT[events.Count];
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            inputs[i] = e.Kind == PcInputKind.Mouse
                ? new Win32.INPUT { Type = Win32.INPUT_MOUSE, Union = new Win32.INPUTUNION { Mouse = new Win32.MOUSEINPUT { Dx = e.Dx, Dy = e.Dy, MouseData = e.MouseData, Flags = e.Flags } } }
                : new Win32.INPUT { Type = Win32.INPUT_KEYBOARD, Union = new Win32.INPUTUNION { Keyboard = new Win32.KEYBDINPUT { Vk = e.Vk, Scan = e.Scan, Flags = e.Flags } } };
        }
        return (int)Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());
    }
}
