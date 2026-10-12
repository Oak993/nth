// PORT-ONLY: Tactus (ED-NEW-pcctl-23). A grab zone with a fixed answer, for the hook's grab-in-flight tests.
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Fixtures;

public sealed class PcFixedGrabZone(bool inside) : IPcGrabZone
{
    public bool Contains(int x, int y, uint tick) => inside;
}
