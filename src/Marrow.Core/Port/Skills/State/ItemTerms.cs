// PORT-ONLY: first-release feature unit SK-7. Whether the terms step is owed (ONDEMAND-PLAN.md 2.5 and 3.7).
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Port.Skills.State;

public static class ItemTerms
{
    /// <summary>
    /// The SHA-256 the catalogue holds for the item's licence file, or null while the catalogue has none (the pin run fills it). ED-NEW-dl-13: a plugin item whose licences are
    /// recorded per plugin is keyed by all its licence files together (<see cref="PluginLicences.Hash"/>).
    /// </summary>
    public static string? LicenceHash(CatalogueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.LicenceFile is null) return PluginLicences.Hash(item);
        return item.Files.FirstOrDefault(f => f.Path == item.LicenceFile)?.Sha256;
    }

    /// <summary>
    /// True when the user must accept terms before a download: the item's terms class is not open and its licence hash is not accepted. A licence whose hash the catalogue
    /// does not hold yet cannot be keyed, so it is always required (fail closed; ED-295). A changed licence has a new hash, so it asks again.
    /// </summary>
    public static bool Required(CatalogueItem item, AddonsState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (item.TermsClass == TermsClass.Open) return false;
        var hash = LicenceHash(item);
        return hash is null || !state.HasAccepted(hash);
    }
}
