// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 3.3 "a meta line: ... spend per account (Codex A 2.1% · Claude B 1.2%)"; ED-NEW-dextra-as8-3).
// No Swift twin. The board's read of what a task spent: the percentage points of each account's shortest known window.
namespace Marrow.App.Assistant;

public sealed partial class ReserveGuard
{
    /// <summary>What <paramref name="mission"/> spent of each account so far, in points of its shortest known window, largest first.</summary>
    public IReadOnlyList<(string Account, double Percent)> SpendOf(Guid mission)
    {
        lock (gate)
        {
            if (!missions.TryGetValue(mission, out var m)) return [];
            var spend = new List<(string Account, double Percent)>();
            foreach (var use in m.Used.Values)
            {
                foreach (var kind in new[] { Marrow.Core.Port.Assistant.ReserveWindowKind.FiveHour, Marrow.Core.Port.Assistant.ReserveWindowKind.Day,
                             Marrow.Core.Port.Assistant.ReserveWindowKind.Weekly, Marrow.Core.Port.Assistant.ReserveWindowKind.Month, Marrow.Core.Port.Assistant.ReserveWindowKind.Other })
                {
                    if (!use.Points.TryGetValue(kind, out var points)) continue;
                    if (points > 0) spend.Add((use.Account.Name, points));
                    break;
                }
            }
            return [.. spend.OrderByDescending(s => s.Percent)];
        }
    }
}
