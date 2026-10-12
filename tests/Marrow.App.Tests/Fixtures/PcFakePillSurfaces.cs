// PORT-ONLY: Tactus (ED-NEW-pcctl-24). Band surfaces for the pill host: a plain panel per window, made when the host first
// asks for a band, with a fake glass under each.
using Avalonia.Controls;
using Marrow.App.Port.PcControl.Overlay;

namespace Marrow.App.Tests.Fixtures;

public sealed class PcFakePillSurfaces(bool shadowWindow = true, bool glass = true) : IPillBandSurfaces
{
    private readonly Dictionary<string, PillBandSurface> surfaces = new(StringComparer.OrdinalIgnoreCase);

    public PillBandSurface? For(string monitorId)
    {
        if (!surfaces.TryGetValue(monitorId, out var surface))
        {
            surface = new PillBandSurface(new Canvas(), shadowWindow ? new Canvas() : null, new PcFakePillGlass(glass), shadowWindow ? new PcFakePillGlass(glass) : null);
            surfaces[monitorId] = surface;
        }
        return surface;
    }

    public PcFakePillGlass InputGlass(string monitorId) => (PcFakePillGlass)surfaces[monitorId].InputGlass;

    public PcFakePillGlass? ShadowGlass(string monitorId) => surfaces[monitorId].ShadowGlass as PcFakePillGlass;

    public PillBandSurface Surface(string monitorId) => surfaces[monitorId];
}
