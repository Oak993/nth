// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.5, 13.2; unit AS-15; ED-NEW-dextra-as15-4). No Swift twin.
// What the cost guard reads of the reserve: which usage windows an account reports, so a percent cap counts only those.
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

/// <summary>Use the owner made outside nth in one window of one account over a span (11.5), in points of that window.</summary>
public sealed record ReserveOutsideRow(string AccountKey, string AccountName, ReserveWindowKind Kind, string Title, double Points);

public sealed partial class ReserveGuard
{
    /// <summary>
    /// The use outside nth the guard found in the last <paramref name="span"/>, one row per account window (AS-11: the morning digest names it, and
    /// names the window: points of one window are not points of another). Empty when it found none.
    /// </summary>
    public IReadOnlyList<ReserveOutsideRow> OutsideUses(TimeSpan span)
    {
        var since = clock.GetUtcNow() - span;
        lock (gate)
        {
            var rows = new List<ReserveOutsideRow>();
            foreach (var (key, state) in accounts.OrderBy(a => refs[a.Key].Name, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var found in state.OutsideUses.Where(u => u.At > since).GroupBy(u => u.WindowId, StringComparer.Ordinal))
                {
                    var window = state.Window(found.Key);
                    rows.Add(new ReserveOutsideRow(key, refs[key].Name, window?.Kind ?? ReserveWindowKind.Other, window?.Title ?? "", found.Sum(u => u.Points)));
                }
            }
            return rows;
        }
    }

    /// <summary>The kinds of usage window the account's last reading reported (the 5-hour, the weekly); empty when it has not been read.</summary>
    public IReadOnlyList<ReserveWindowKind> WindowKinds(string accountKey)
    {
        lock (gate)
        {
            return accounts.TryGetValue(accountKey, out var state) ? [.. state.Windows.Select(w => w.Kind).Distinct().Order()] : [];
        }
    }
}
