// PORT-ONLY: first-release feature unit SK-2 (DL-2 of porting/first-release/skills-ONDEMAND-PLAN.md section 7); there is no Swift twin.
// Writes catalogue.json: the exact inverse of SkillsCatalogueLoader, canonical, for the catalogue builder's output.
using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Marrow.Core.Port.Skills.Catalogue;

/// <summary>
/// ENGINEERING DECISIONS (ED-298): the text is indented with two spaces, line feeds only, ends in one line feed, writes the keys in the order the schema declares
/// them, writes a required key that has no value as an explicit null (SK-1's loader refuses a missing key) and leaves an optional key out when it has nothing (no pin hint,
/// no notes, no skill count, no false <c>skillsListed</c>, no empty skills, folders or packages, no per-artifact hash hint, no per-skill terms class or pin), writes enums as
/// their camelCase words and numbers the invariant way, and does not escape letters that need no escape (an apostrophe stays an apostrophe). One catalogue, one text.
/// </summary>
public static class SkillsCatalogueWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { LeaveOutOptionalWhenEmpty } },
    };

    private static readonly Dictionary<Type, string[]> Optional = new()
    {
        [typeof(CatalogueItem)] = ["pinHint", "licenceNotes", "skillCount", "skillsListed", "skills", "folders", "packages", "install"],
        [typeof(CatalogueArtifact)] = ["sha256Hint"],
        [typeof(CatalogueSkill)] = ["termsClass", "pin"],
        [typeof(CatalogueInstall)] = ["archives", "python", "nodeTree", "markers", "shims", "exports", "pythonPackages"],
        [typeof(CatalogueInstallArchive)] = ["prefix"],
        [typeof(CatalogueShim)] = ["target", "item", "shim", "arguments", "python", "libreOffice"],
    };

    private static void LeaveOutOptionalWhenEmpty(JsonTypeInfo info)
    {
        if (!Optional.TryGetValue(info.Type, out var names)) return;
        foreach (var property in info.Properties.Where(p => names.Contains(p.Name, StringComparer.Ordinal)))
            property.ShouldSerialize = (_, value) => value switch
            {
                null => false,
                bool flag => flag,
                ICollection collection => collection.Count > 0,
                _ => true,
            };
    }

    public static string Serialize(SkillsCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        return JsonSerializer.Serialize(catalogue, Options) + "\n";
    }

    /// <summary>ED-NEW-dl-7: an item's installer parameters in the catalogue's own canonical text (null when it has none), so the change report can name any change of them.</summary>
    public static string? InstallText(CatalogueInstall? install) => install is null ? null : JsonSerializer.Serialize(install, Options);

    /// <summary>
    /// Sets every item's <see cref="PinState"/> to what its values say (SK-1's rule, <see cref="CatalogueGate.Missing"/>): pinned only when nothing is missing. The builder
    /// never types a pin state by hand, so the gate's "declared state equals derived state" rule cannot fail on its output.
    /// </summary>
    public static SkillsCatalogue WithDerivedPinStates(SkillsCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        return catalogue with
        {
            Items = catalogue.Items.Select(i => i with { PinState = CatalogueGate.Missing(i).Count == 0 ? PinState.Pinned : PinState.Unpinned }).ToList(),
        };
    }
}
