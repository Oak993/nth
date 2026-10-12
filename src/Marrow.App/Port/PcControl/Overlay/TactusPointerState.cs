// PORT-ONLY: Tactus pill v2, where the user's pointer is and whether a mouse button is held (OD-0145, ED-NEW-pcctl-34;
// DESIGN.md 10.3 "Auto-dodge" row 3, "Dodge rules", 10.4 C12, 10.5 auto-expand, 10.6 "Hit testing"). The hooks the engine
// keeps while a stream works are the first source: the physical input monitor's snapshot (the pointer and the held
// buttons) and its user pointer events. While they are not in (or not healthy), GetCursorPos and GetAsyncKeyState answer.
// Three readers: the pill host (the pointer in a monitor's DIP, for the card's shift away from the pointer, and "a button
// is held anywhere", for the auto-expand), the band's hit test (a press that began in another app still holds: the
// region waits, so a drag in that app is never cut by the pill's hit region changing under it) and the style-flip hit
// test (each pointer record). A press on one of Tactus's own windows (the pill's drag, its buttons) is not elsewhere.
using System.Runtime.InteropServices;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Port.PcControl.Overlay;

/// <summary>The cursor and the buttons straight from Windows, for when the hooks are not in. The real one never runs in tests.</summary>
public interface IPointerProbe
{
    /// <summary>The cursor in screen pixels; null when it cannot be read (a secure desktop).</summary>
    PcPoint? Cursor();

    /// <summary>A mouse button physically down.</summary>
    bool ButtonDown();
}

public sealed class TactusPointerState : IDisposable
{
    private readonly PhysicalInputMonitor input;
    private readonly IPointerProbe probe;
    private readonly Func<double> now;
    private readonly Lock gate = new();
    private HashSet<nint> own = [];
    private bool held;
    private bool elsewhere;
    private bool disposed;

    public TactusPointerState(PhysicalInputMonitor input, IPointerProbe probe, Func<double> now)
    {
        this.input = input;
        this.probe = probe;
        this.now = now;
        input.UserPointer += OnPointer;
    }

    /// <summary>A pointer record for the style-flip test: screen x, y and the pill clock's seconds (the hook reader's thread).</summary>
    public event Action<int, int, double>? Moved;

    /// <summary>A press that began in another app started (true) or every button is up again (false) (the hook reader's thread).</summary>
    public event Action<bool>? HeldElsewhereChanged;

    /// <summary>A press that began in another app holds now.</summary>
    public bool HeldElsewhere
    {
        get
        {
            lock (gate) return elsewhere;
        }
    }

    /// <summary>The cursor in screen pixels: the hooks' newest point while they are healthy, else Windows'.</summary>
    public PcPoint? Cursor()
    {
        var snapshot = input.Snapshot();
        return snapshot.Healthy && snapshot.LastPointerMs is not null ? snapshot.Pointer : probe.Cursor();
    }

    /// <summary>A mouse button is held anywhere (the pill's own press too): the hooks' held buttons, else Windows'.</summary>
    public bool ButtonHeld()
    {
        var snapshot = input.Snapshot();
        return snapshot.Healthy ? snapshot.HeldButtons != 0 : probe.ButtonDown();
    }

    /// <summary>The pointer in the monitor's DIP (its top-left at 0, 0), or null when it is on another monitor or unknown.</summary>
    public PillVector? PointerAt(OverlayMonitor monitor)
    {
        if (Cursor() is not { } point || !monitor.Contains(point)) return null;
        return new PillVector((point.X - monitor.Bounds.X) / monitor.Scale, (point.Y - monitor.Bounds.Y) / monitor.Scale);
    }

    /// <summary>Tactus's own windows now (the bands' input and shadow windows; the UI thread hands them over at each change).</summary>
    public void Own(IEnumerable<nint> windows)
    {
        var next = windows.Where(w => w != 0).ToHashSet();
        lock (gate) own = next;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        input.UserPointer -= OnPointer;
    }

    private void OnPointer(PcRawInput raw)
    {
        // a swallowed event never moved the cursor (a borrow held it back for the replay)
        if (!raw.Swallowed) Moved?.Invoke(raw.Point.X, raw.Point.Y, now());
        bool? changed = null;
        lock (gate)
        {
            var was = elsewhere;
            if (raw.HeldButtons == 0)
            {
                elsewhere = false;
            }
            else if (!held)
            {
                // the first button down: a press on one of our windows is the pill's own; a held button the hooks met
                // without its press (they went in mid-press) is taken as another app's
                elsewhere = raw.Kind != PcRawKind.Button || !raw.Down || !own.Contains(raw.Window);
            }
            held = raw.HeldButtons != 0;
            if (elsewhere != was) changed = elsewhere;
        }
        if (changed is { } value) HeldElsewhereChanged?.Invoke(value);
    }
}

/// <summary>GetCursorPos and GetAsyncKeyState. Never run by tests.</summary>
public sealed class Win32PointerProbe : IPointerProbe
{
    private static readonly int[] Buttons = [0x01, 0x02, 0x04, 0x05, 0x06];

    public PcPoint? Cursor() => GetCursorPos(out var point) ? new PcPoint(point.X, point.Y) : null;

    public bool ButtonDown() => Buttons.Any(vk => (GetAsyncKeyState(vk) & 0x8000) != 0);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
