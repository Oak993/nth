// PORT-ONLY: MA-4-1, plain Handshake presentation maths and sound recipe.

namespace Marrow.Core.Port.MultiAccount.Handshake;
public sealed record HandshakeStroke(double Alpha,double Width,IReadOnlyList<HandshakePoint> Points);
