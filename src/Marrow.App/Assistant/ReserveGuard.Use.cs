// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.5, 13.2; unit AS-15; ED-NEW-dextra-as15-4). No Swift twin.
// What the cost guard reads of the reserve: which usage windows an account reports, so a percent cap counts only those.
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class ReserveGuard
{
    /// <summary>The kinds of usage window the account's last reading reported (the 5-hour, the weekly); empty when it has not been read.</summary>
    public IReadOnlyList<ReserveWindowKind> WindowKinds(string accountKey)
    {
        lock (gate)
        {
            return accounts.TryGetValue(accountKey, out var state) ? [.. state.Windows.Select(w => w.Kind).Distinct().Order()] : [];
        }
    }
}
