// PORT-ONLY: MA-3b-9. Thread-scoped usage model offer; unbound preserves the pinned single-account panel. Job W binds it.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Usage;
namespace Marrow.App.Port.MultiAccount.Usage;
internal static class MultiAccountUsageSeams
{
    internal static Ambient<Func<ProviderKind, UsagePanelModel?>> Offer { get; } = new(_ => null);
    internal static MultiAccountUsageView? Try(ProviderKind provider) => Offer.Current(provider) is { MoreCount: >= 1 } model ? new(model) : null;
}
