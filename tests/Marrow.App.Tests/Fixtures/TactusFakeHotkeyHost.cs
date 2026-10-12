using Marrow.App.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: a stand-in for the Tactus hotkey thread (ED-NEW-pcctl-15). No RegisterHotKey, no hook, no foreground read:
// it keeps what was registered, refuses the ids a test names, and raises presses and key reports from its own thread, as
// the real one does.
public sealed class TactusFakeHotkeyHost : ITactusHotkeyHost
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, PcHotkey> registered = [];
    private readonly HashSet<int> refused = [];

    public event Action<int>? Pressed;

    public event Action<PcHookKeyDown>? KeyDown;

    public Func<PcHotkey, bool> Types { get; set; } = _ => false;

    public nint Front { get; set; } = 0x10;

    public bool ImeIsOpen { get; set; }

    public bool Watching { get; private set; }

    public int WatchCalls { get; private set; }

    public IReadOnlyDictionary<int, PcHotkey> Registered
    {
        get
        {
            lock (gate) return new Dictionary<int, PcHotkey>(registered);
        }
    }

    public void Refuse(int id)
    {
        lock (gate) refused.Add(id);
    }

    public bool Register(int id, PcHotkey chord)
    {
        lock (gate)
        {
            if (refused.Contains(id) || registered.ContainsValue(chord)) return false;
            registered[id] = chord;
            return true;
        }
    }

    public void Unregister(int id)
    {
        lock (gate) registered.Remove(id);
    }

    public void Watch(bool on, Func<ushort, bool> isChordKey)
    {
        Watching = on;
        WatchCalls++;
    }

    public nint Foreground() => Front;

    public bool ImeOpen(nint window) => ImeIsOpen;

    public bool TypesCharacter(PcHotkey chord) => Types(chord);

    /// <summary>A press of a registered chord, raised on the host's own thread; false when it did not finish within 5 s.</summary>
    public bool Press(int id)
    {
        bool known;
        lock (gate) known = registered.ContainsKey(id);
        return !known || OnOwnThread(() => Pressed?.Invoke(id));
    }

    /// <summary>A key report from the hook, raised on the host's own thread.</summary>
    public bool Key(PcHookKeyDown key) => OnOwnThread(() => KeyDown?.Invoke(key));

    public void Dispose()
    {
        lock (gate) registered.Clear();
    }

    private static bool OnOwnThread(Action raise)
    {
        var thread = new Thread(() => raise()) { IsBackground = true, Name = "Tactus fake hotkeys" };
        thread.Start();
        return thread.Join(TimeSpan.FromSeconds(5));
    }
}
