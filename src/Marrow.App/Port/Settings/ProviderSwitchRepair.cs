// PORT-ONLY (ED-NEW-providersmerge-switch-repair-1). Builds before ED-NEW-providers-switch-1 had a Providers switch whose click wrote the provider into "disabledProviders" while its knob stayed
// on: the person saw the provider on, nth treated it as off (on the owner's PC, Claude and Z.ai: Claude's models were missing from the picker). Nothing else writes that list. So, once per
// data folder, before anything reads the settings, every provider such a build wrote there is switched back on. A name the switch cannot write (a provider this build does not know) is
// kept, as AppSettings keeps it. A provider switched off after this has run stays off: the repair never runs again.
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Port.Settings;

internal static class ProviderSwitchRepair
{
    /// <summary>Set once the repair has run in a data folder.</summary>
    internal const string Marker = "providerSwitchRepair1";

    private const string Disabled = "disabledProviders"; // AppSettings.Key.DisabledProviders

    /// <summary>Switches back on, once, every provider the old switch wrote into "disabledProviders"; returns their raw names (none when the repair has run before).</summary>
    internal static IReadOnlyList<string> RunOnce(ISettingsStore store)
    {
        if (store.Object(Marker) is not null) return [];
        var cleared = new List<string>();
        if (store.StringArray(Disabled) is { } stored)
        {
            var known = SwiftEnum<ProviderKind>.AllCases.Select(SwiftEnum<ProviderKind>.RawValue).ToHashSet(StringComparer.Ordinal);
            cleared = stored.Where(known.Contains).Distinct(StringComparer.Ordinal).ToList();
            if (cleared.Count > 0)
            {
                var kept = stored.Where(name => !known.Contains(name)).ToList<object>();
                if (kept.Count == 0) store.RemoveObject(Disabled);
                else store.Set(Disabled, kept);
            }
        }
        store.Set(Marker, true);
        return cleared;
    }
}
