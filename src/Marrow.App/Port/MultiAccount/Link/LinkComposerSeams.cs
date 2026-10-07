// PORT-ONLY: optional link view model for a thread; W owns model lifetime and the real adapters.
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Link;
namespace Marrow.App.Port.MultiAccount.Link;
internal static class LinkComposerSeams
{
    internal static Ambient<Func<Guid, LinkUpModel?>> Offer { get; } = new(_ => null);
}
