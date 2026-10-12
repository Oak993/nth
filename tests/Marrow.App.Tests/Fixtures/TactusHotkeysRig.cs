using Marrow.App.Port.PcControl;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: the Tactus chords wired to a fake hotkey thread, a real pause control, stop control and lanes, and a clock
// the test moves (ED-NEW-pcctl-15). Window 0x10 is the Tactus window.
public sealed class TactusHotkeysRig : IDisposable
{
    public const nint TactusWindow = 0x10;

    public TactusHotkeysRig(PcSettings? settings = null, Action? answer = null)
    {
        var current = settings ?? new PcSettings();
        Control.Stopped += (scope, source) =>
        {
            lock (Stops) Stops.Add((scope, source));
        };
        Pause = new PcPauseControl(new PcFakeClock(), Control);
        Stop = new PcStopControl(Control);
        Hotkeys = new TactusHotkeys(Host, () => current, Stop, Pause, Lanes, window => window == TactusWindow, () => Now, answer);
    }

    public TactusFakeHotkeyHost Host { get; } = new();

    public PcRunControl Control { get; } = new();

    public PcPauseControl Pause { get; }

    public PcStopControl Stop { get; }

    public PcLanes Lanes { get; } = new();

    public TactusHotkeys Hotkeys { get; }

    public List<(PcScope Scope, PcStopSource Source)> Stops { get; } = [];

    public long Now { get; set; } = 1_000;

    public void Dispose()
    {
        Hotkeys.Dispose();
        Pause.Dispose();
        Host.Dispose();
    }
}
