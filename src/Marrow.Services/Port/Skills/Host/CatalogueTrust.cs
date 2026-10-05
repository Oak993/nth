// PORT-ONLY: SK-13 / ED-326 (6)(a). Rebuilds the trusted installed descriptors from the embedded catalogue after a restart.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Host;

/// <summary>
/// ENGINEERING DECISION (ED-339): the one trust root the app has after a restart is the embedded catalogue (ED-321 (3), ED-325). A skill set or plugin installed at the pin the
/// catalogue holds now gets a descriptor whose inventory is exactly the catalogue's files; its folder is the one its immutable route sidecar names (the pipeline cross-checks it).
/// Everything else has none and is listed in <see cref="TrustedInstalls.Unrestored"/>: a tool or package set (its installed-tree hashes are derived from the archives at install time
/// and are not persisted anywhere trusted), and a set installed at an older pin (the catalogue that pinned it is gone). Persisting those descriptors is SK-16's (UNRESOLVED here).
/// </summary>
public static class CatalogueTrust
{
    /// <summary>
    /// The folder and spares the immutable route sidecar names for an installed record (a record whose descriptor could not be rebuilt); empty when the sidecar is missing or does not
    /// validate, which the pipeline reads as "folders unknown, retain every tool and store tree".
    /// </summary>
    public static string[] OrphanFolders(StorePaths paths, ItemRecord record)
    {
        try
        {
            var installed = AddonsState.Restore([new ItemRecord(record.Id, ItemState.Installed, record.InstalledPin, null, null)], [], [], []);
            var route = PipelineRoutes.Read(paths.StateFile, installed).Single();
            return route.SparePaths.Prepend(route.Folder).ToArray();
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or IOException or KeyNotFoundException or InvalidOperationException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static TrustedInstalls Restore(StorePaths paths, SkillsCatalogue catalogue, AddonsState state)
    {
        var versions = new List<StoreItem>();
        var directories = new Dictionary<(string Id, string Pin), IReadOnlyList<string>>();
        var unrestored = new List<string>();
        foreach (var record in state.Items.Where(r => r.InstalledPin is not null && r.State != ItemState.Removing))
        {
            var item = catalogue.Items.FirstOrDefault(i => i.Id == record.Id);
            if (item is null || item.Kind is not (ItemKind.Skillset or ItemKind.Plugin)) { unrestored.Add(record.Id); continue; }
            // SK-16 review: at the pin the catalogue holds now the inventory is the catalogue's files; at an earlier pin (the app was updated) it is the catalogue entry that install was verified against, kept in addons\manifests.
            var frozen = UpdateCheck.CataloguePin(item) == record.InstalledPin ? item : PipelineManifests.Read(paths.StateFile, record.Id, record.InstalledPin!);
            if (frozen is null) { unrestored.Add(record.Id); continue; }
            try
            {
                // The route is read as for an installed record, whatever state the record is in (a damaged item keeps its descriptor so Repair can run).
                var installed = AddonsState.Restore([new ItemRecord(record.Id, ItemState.Installed, record.InstalledPin, null, null)], [], [], []);
                var route = PipelineRoutes.Read(paths.StateFile, installed).Single();
                var store = new StoreItem(frozen, route.Folder, new StoreManifest(frozen.Files), record.InstalledPin);
                versions.Add(store);
                directories[(record.Id, record.InstalledPin!)] = store.Manifest.Directories.ToArray();
            }
            catch (Exception error) when (error is ArgumentException or InvalidDataException or IOException or KeyNotFoundException or InvalidOperationException or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
                unrestored.Add(record.Id);
            }
        }
        return new TrustedInstalls(versions, directories, unrestored);
    }
}
