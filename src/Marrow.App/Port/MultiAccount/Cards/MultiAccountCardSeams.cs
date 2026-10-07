// PORT-ONLY: MA-3b-12, thread-specific note actions remain absent until real wiring W.
using Marrow.Core.Port;
namespace Marrow.App.Port.MultiAccount.Cards;
internal static class MultiAccountCardSeams
{
    internal static Ambient<Func<Guid, MultiAccountCardContext?>> Offer { get; } = new(_ => null);
}
