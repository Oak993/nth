// PORT-ONLY: Tactus pointers (ED-NEW-pcctl-21). Makes fake pointer windows and keeps every one it made.
using Marrow.App.Port.PcControl.Overlay;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Tests.Fixtures;

public sealed class PcFakePointerSurfaces : IPointerSurfaces
{
    public List<PcFakePointerSurface> Opened { get; } = [];

    public IPointerSurface Open(PcPointerAim aim)
    {
        var surface = new PcFakePointerSurface(aim.App);
        Opened.Add(surface);
        return surface;
    }
}
