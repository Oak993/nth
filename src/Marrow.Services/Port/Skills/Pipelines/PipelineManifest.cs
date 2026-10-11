using System.Text.Json.Serialization;
using Marrow.Core.Port.Skills.Catalogue;
namespace Marrow.Services.Port.Skills.Pipelines;
// PORT-ONLY: SK-16 review fix (ED-368). The catalogue entry an install was verified against (a one-item catalogue, as the catalogue file writes it), kept so a restart after a catalogue change can still trust that exact tree.
public sealed record PipelineManifest(string Id, string Pin, string Item)
{
    /// <summary>
    /// ED-NEW-dl-9 (RR-14 restart trust): for a tool or package set, the whole-tree inventory the verified install produced (every file with its size and SHA-256) and the empty folders it
    /// legitimately made. The catalogue pins only the downloads; the tree unpacked from them is what a restart, a seal check and a Repair compare against.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CatalogueFile[]? Inventory { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Directories { get; init; }
}
