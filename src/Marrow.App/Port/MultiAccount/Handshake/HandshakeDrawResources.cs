// PORT-ONLY: MA-4-2. A changed geometry/colour owns a new resource set; in-flight render operations retain the old set.
using Marrow.Core.Port.MultiAccount.Handshake;
namespace Marrow.App.Port.MultiAccount.Handshake;
internal sealed class HandshakeDrawResources
{
    private int references = 1;
    private readonly double width, height;
    private readonly (byte R, byte G, byte B) outgoing, incoming;
    internal HandshakePainter Painter { get; }
    internal HandshakeDrawResources(double width, double height, HandshakeFrameSnapshot frame)
    {
        this.width = width; this.height = height; outgoing = frame.OutgoingColor; incoming = frame.IncomingColor; Painter = new HandshakePainter(width, height, outgoing, incoming);
    }
    internal bool Matches(double w, double h, HandshakeFrameSnapshot frame) => width == w && height == h && outgoing == frame.OutgoingColor && incoming == frame.IncomingColor;
    internal void Acquire() => Interlocked.Increment(ref references);
    internal void Release() { if (Interlocked.Decrement(ref references) == 0) Painter.Dispose(); }
}
