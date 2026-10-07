// PORT-ONLY: MA-4-3, engine readiness and optional sound policy are supplied by W, off by default.
using Marrow.Core.Accounts;
using Marrow.Core.Port.MultiAccount.Handshake;
namespace Marrow.App.Port.MultiAccount.Handshake;
internal sealed record HandshakePresentationContext(Func<HandshakeEvent, HandshakeReadiness>? Readiness = null, Func<bool>? SoundEnabled = null, Action<IReadOnlyList<HandshakeTone>>? Play = null);
