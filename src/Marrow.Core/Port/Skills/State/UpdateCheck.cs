// PORT-ONLY: first-release feature unit SK-7. "Update available", computed from the catalogue's pins (ONDEMAND-PLAN.md 3.7).
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Port.Skills.State;

/// <summary>An offered update: installed pin, catalogue pin and the plan's approximate download size (3.7 "old -> new and the download size").</summary>
public sealed record UpdateInfo(string Id, string FromPin, string ToPin, double? ApproxSizeMb);

/// <summary>The state after the check, the updates on offer, installed items the catalogue no longer has, and installed items whose catalogue pin is not known yet.</summary>
public sealed record UpdateCheckResult(AddonsState State, IReadOnlyList<UpdateInfo> Updates, IReadOnlyList<string> Orphans, IReadOnlyList<string> Undecidable);

public static class UpdateCheck
{
    /// <summary>
    /// The pin the catalogue holds for an item: its pin, or for a set pinned one skill at a time (O3) the list <c>folder=pin</c> of every skill, sorted by folder and joined
    /// with semicolons; null when the catalogue has no pin for it yet (ED-NEW-f-sk-7).
    /// </summary>
    public static string? CataloguePin(CatalogueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!string.IsNullOrEmpty(item.Pin)) return item.Pin;
        if (!item.SkillsListed || item.Skills.Count == 0 || item.Skills.Any(s => string.IsNullOrEmpty(s.Pin))) return null;
        return string.Join(';', item.Skills.OrderBy(s => s.Folder, StringComparer.Ordinal).Select(s => s.Folder + "=" + s.Pin));
    }

    /// <summary>
    /// Run after an app update (the catalogue is compiled in, so it only changes with the app). An Installed item whose catalogue pin differs becomes UpdateAvailable
    /// for that pin; an offered update follows the catalogue (re-pointed when it moves again, back to Installed when it matches the installed pin again). Comparison is
    /// plain text equality, never version order. Items that are queued, downloading, checking, failed, damaged, removing or not installed are left alone.
    /// </summary>
    public static UpdateCheckResult Compute(AddonsState state, SkillsCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalogue);
        var updates = new List<UpdateInfo>();
        var orphans = new List<string>();
        var undecidable = new List<string>();
        foreach (var record in state.Items.Where(r => r.State is ItemState.Installed or ItemState.UpdateAvailable))
        {
            var item = catalogue.Items.FirstOrDefault(i => i.Id == record.Id);
            if (item is null)
            {
                orphans.Add(record.Id);
                continue;
            }
            var pin = CataloguePin(item);
            if (pin is null)
            {
                undecidable.Add(record.Id);
                continue;
            }
            if (pin == record.InstalledPin)
            {
                if (record.State == ItemState.UpdateAvailable) state = state.Transition(record.Id, ItemState.Installed);
            }
            else if (record.State == ItemState.Installed || record.TargetPin != pin)
            {
                state = state.Transition(record.Id, ItemState.UpdateAvailable, pin);
            }
            if (state.Get(record.Id).State == ItemState.UpdateAvailable) updates.Add(new UpdateInfo(record.Id, record.InstalledPin!, pin, item.ApproxSizeMb));
        }
        return new UpdateCheckResult(state, updates.OrderBy(u => u.Id, StringComparer.Ordinal).ToList(), orphans, undecidable);
    }
}
