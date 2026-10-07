// PORT-ONLY: MA-4-1, plain Handshake presentation maths and sound recipe.
using Marrow.Core.Accounts;
namespace Marrow.Core.Port.MultiAccount.Handshake;
internal sealed class HandshakeStage(HandshakePhase phase,DateTimeOffset starts,HandshakeReadiness readiness,bool ready)
{
    internal HandshakePhase Phase {get;}=phase;
    internal DateTimeOffset Starts {get;}=starts;
    internal HandshakeReadiness Readiness {get;set;}=readiness;
    internal bool Ready {get;set;}=ready;
    internal bool Entered {get;set;}
    internal bool TickPlayed {get;set;}
}
