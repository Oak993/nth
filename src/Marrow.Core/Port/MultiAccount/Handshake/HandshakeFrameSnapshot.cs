// PORT-ONLY: MA-4-2 frame API. A value snapshot; renderer retains buffers and never asks the allocating geometry projection per frame.
using Marrow.Core.Accounts;
namespace Marrow.Core.Port.MultiAccount.Handshake;
public readonly record struct HandshakeFrameSnapshot(HandshakePhase Phase, double VirtualMilliseconds, double Progress,
    string Status, bool ShowTick, bool UseCrossfade, (byte R, byte G, byte B) OutgoingColor, (byte R, byte G, byte B) IncomingColor);
