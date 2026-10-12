// PORT-ONLY: Tactus pointers (ED-NEW-pcctl-21). Two monitors side by side, the second at 150 %, and a switch for an
// exclusive-fullscreen app on one of them.
using Marrow.App.Port.PcControl.Overlay;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Tests.Fixtures;

public sealed class PcFakePointerScreens : IPointerScreens
{
    public List<PcMonitor> List { get; } =
    [
        new PcMonitor(0, new PcRect(0, 0, 1920, 1080), new PcRect(0, 0, 1920, 1040), 96, true),
        new PcMonitor(1, new PcRect(1920, 0, 2880, 1620), new PcRect(1920, 0, 2880, 1560), 144, false),
    ];

    public HashSet<int> Fullscreen { get; } = [];

    public IReadOnlyList<PcMonitor> Monitors() => List;

    public bool NothingDrawnOn(int monitor) => Fullscreen.Contains(monitor);
}
