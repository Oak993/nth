// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.6; unit AS-14; ED-NEW-dextra-as14-3). No Swift twin.
// "The owner's pill order as the tiebreak": the order of his accounts as his pills give it.
using Marrow.Core.Accounts;

namespace Marrow.Core.Port.Assistant;

public static class PillOrder
{
    /// <summary>
    /// The account ids in the owner's order: the pill he uses for new threads first, then his other pills in the order he keeps them,
    /// each in its step order. An account takes the place where it first appears; a step whose account is gone is skipped.
    /// </summary>
    public static IReadOnlyList<string> Sequence(IReadOnlyList<Pill> pills, Guid? useForNewThreads, IAccountStore store)
    {
        ArgumentNullException.ThrowIfNull(pills);
        ArgumentNullException.ThrowIfNull(store);
        var first = pills.Where(p => useForNewThreads is { } id && p.Id == id);
        var rest = pills.Where(p => !(useForNewThreads is { } id && p.Id == id));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var pill in first.Concat(rest))
        {
            foreach (var step in pill.Steps)
            {
                if (PillStepAccountResolver.Resolve(store, step) is { } account && seen.Add(account.Id)) order.Add(account.Id);
            }
        }
        return order;
    }
}
