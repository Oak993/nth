// PORT-ONLY: MA-4-3. Allocation-free value counterpart of Core's HandshakeGeometry ghost, page :3101-3108.
using Marrow.Core.Port.MultiAccount.Handshake;
namespace Marrow.App.Port.MultiAccount.Handshake;
internal static class HandshakeGhostLayout
{
    internal static HandshakeGhost Read(double width, double height, HandshakeFrameSnapshot frame)
    {
        var t = frame.VirtualMilliseconds; var sb = HandshakeGeometry.Smooth(1000, 1600, t); var sw = Math.Clamp((t - 4400) / 620, 0, 1);
        if (frame.UseCrossfade) { var point = HandshakeGeometry.Arc(width, height, 1); return new(point.X, point.Y, sb * (1 - sw), 1); }
        var ease = HandshakeGeometry.Ease(sw); var p = HandshakeGeometry.Arc(width, height, 1 - ease); return new(p.X, p.Y, sb * (1 - HandshakeGeometry.Smooth(.75, 1, sw)), 1 - .25 * ease);
    }
}
