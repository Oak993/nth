// PORT-ONLY: MA-4-1, plain Handshake presentation maths and sound recipe.
using Marrow.Core.Accounts;
namespace Marrow.Core.Port.MultiAccount.Handshake;
public sealed record HandshakeMotionPlan(HandshakePhase Phase,double VirtualMilliseconds,double Progress,string Status,bool ShowTick,bool UseCrossfade,
    (byte R,byte G,byte B) OutgoingColor,(byte R,byte G,byte B) IncomingColor,HandshakeFrame Geometry);
