// PORT-ONLY: Tactus (ED-NEW-pcctl-23). Monitors a test sets; reading them counts.
using Marrow.App.Port.PcControl.Overlay;

namespace Marrow.App.Tests.Fixtures;

public sealed class PcFakeOverlayMonitors : IOverlayMonitors
{
    public List<OverlayMonitor> Now { get; set; } = [];

    public int Reads { get; private set; }

    public IReadOnlyList<OverlayMonitor> Read()
    {
        Reads++;
        return Now.ToList();
    }
}
