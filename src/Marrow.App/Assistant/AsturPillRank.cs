// PORT-ONLY: DESIGN.md 9.1, 11.6; ED-NEW-dextra-open-4. Read on every routing decision; no profile cache.
using Marrow.Core.Port;

namespace Marrow.App.Assistant;

public static class AsturPillRank
{
    public static int Of(ValueList<string> order, RouterAccount account)
    {
        for (var i = 0; i < order.Count; i++)
            if (order[i] == account.Id.ToString("D") || order[i] == SwiftEnum<Marrow.Core.Models.ProviderKind>.RawValue(account.Provider)) return i;
        return order.Count == 0 ? account.Rank : order.Count + account.Rank;
    }
}
