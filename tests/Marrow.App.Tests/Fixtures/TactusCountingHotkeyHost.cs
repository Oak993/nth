using Marrow.App.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: the fake Tactus hotkey thread with its disposals counted (ED-NEW-pcctl-40), for the composition's tests.
public sealed class TactusCountingHotkeyHost : ITactusHotkeyHost
{
    private readonly TactusFakeHotkeyHost inner = new();

    public int Disposals { get; private set; }

    public event Action<int>? Pressed
    {
        add => inner.Pressed += value;
        remove => inner.Pressed -= value;
    }

    public event Action<PcHookKeyDown>? KeyDown
    {
        add => inner.KeyDown += value;
        remove => inner.KeyDown -= value;
    }

    public bool Register(int id, PcHotkey chord) => inner.Register(id, chord);

    public void Unregister(int id) => inner.Unregister(id);

    public void Watch(bool on, Func<ushort, bool> isChordKey) => inner.Watch(on, isChordKey);

    public nint Foreground() => inner.Foreground();

    public bool ImeOpen(nint window) => inner.ImeOpen(window);

    public bool TypesCharacter(PcHotkey chord) => inner.TypesCharacter(chord);

    public void Dispose()
    {
        Disposals++;
        inner.Dispose();
    }
}
