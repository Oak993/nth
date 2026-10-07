// PORT-ONLY: MA-4-1, plain Handshake presentation maths and sound recipe.

namespace Marrow.Core.Port.MultiAccount.Handshake;
public readonly record struct HandshakeTone(double Hertz,double Seconds,double Gain,double DelaySeconds,double AttackSeconds,double EndGain,double StopTailSeconds);
