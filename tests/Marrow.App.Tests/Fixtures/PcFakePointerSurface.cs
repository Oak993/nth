// PORT-ONLY: Tactus pointers (ED-NEW-pcctl-21). A pointer window that records where it was placed and what it drew.
using Avalonia;
using Marrow.App.Port.PcControl.Overlay;
using Marrow.Core.Port.PcControl.Pointer;

namespace Marrow.App.Tests.Fixtures;

public sealed class PcFakePointerSurface(string app) : IPointerSurface
{
    public string App { get; } = app;
    public List<(PixelRect Rect, double Scale)> Places { get; } = [];
    public List<(PointerPose Pose, Point Origin)> Draws { get; } = [];
    public bool Closed { get; private set; }

    public void Place(PixelRect rect, double scale) => Places.Add((rect, scale));

    public void Draw(PointerPose pose, Point origin) => Draws.Add((pose, origin));

    public void Close() => Closed = true;
}
