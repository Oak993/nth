// PORT-ONLY: MA-4-3. Production readiness is a per-thread engine fact, never inferred from a UI timer.
using Marrow.Core.Port;
namespace Marrow.App.Port.MultiAccount.Handshake;
internal static class HandshakePresentationSeams
{
    internal static Ambient<Func<Guid, HandshakePresentationContext>> Offer { get; } = new(_ => new());
}
