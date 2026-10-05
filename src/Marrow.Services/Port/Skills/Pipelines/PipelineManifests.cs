using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Store;
namespace Marrow.Services.Port.Skills.Pipelines;

/// <summary>
/// PORT-ONLY: SK-16 review fix (ED-368, closing ED-NEW-f-sk-13 item 6 for skill sets and plugins). The catalogue entry (its pinned files above all) an install was verified against, written next to the
/// route sidecar before state.json selects the pin: <c>addons\manifests\&lt;sha256 of id and pin&gt;.json</c>. It lives in the folder only Marrow writes (the one state.json and the routes are in), is made from the verified
/// install and never from installed bytes, and is checked on read (same id and pin, fully pinned, a valid inventory); anything off and the item stays unrestored, as before. After an app update whose catalogue holds another
/// pin, it is what lets the installed set stay installed (and offered as an update) instead of being dropped.
/// </summary>
public static class PipelineManifests
{
    private static string FileFor(string stateFile, string id, string pin) => Path.Combine(Path.GetDirectoryName(stateFile)!, "manifests",
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id + "\n" + pin))) + ".json");

    public static void Prepare(string stateFile, string pin, CatalogueItem item)
    {
        var json = SkillsCatalogueWriter.Serialize(new SkillsCatalogue { Schema = 1, ReadOn = "installed", Items = [item] });
        var file = FileFor(stateFile, item.Id, pin);
        StorePaths.EnsureAncestors(file);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        FileOps.WriteAtomic(file, JsonSerializer.SerializeToUtf8Bytes(new PipelineManifest(item.Id, pin, json), PipelineManifestJsonContext.Default.PipelineManifest));
    }

    /// <summary>The catalogue entry the item was installed at the pin from, or null when none is kept or it does not hold up.</summary>
    public static CatalogueItem? Read(string stateFile, string id, string pin)
    {
        try
        {
            var file = FileFor(stateFile, id, pin);
            if (!File.Exists(file)) return null;
            var manifest = JsonSerializer.Deserialize(File.ReadAllBytes(file), PipelineManifestJsonContext.Default.PipelineManifest);
            if (manifest is null || manifest.Id != id || manifest.Pin != pin || manifest.Item is null) return null;
            var item = SkillsCatalogueLoader.Parse(manifest.Item).Items.SingleOrDefault();
            if (item is null || item.Id != id || item.PinState != PinState.Pinned || CatalogueGate.Missing(item).Count != 0 || UpdateCheck.CataloguePin(item) != pin) return null;
            _ = new StoreManifest(item.Files);
            return item;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or CatalogueFormatException)
        {
            return null;
        }
    }
}
