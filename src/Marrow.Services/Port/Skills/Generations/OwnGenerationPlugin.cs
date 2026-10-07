// PORT-ONLY: RR-18. User-approved plugin copies need no fabricated catalogue descriptor or licence.
namespace Marrow.Services.Port.Skills.Generations;

public sealed record OwnGenerationPlugin(string Id, IReadOnlyDictionary<string, byte[]> Files);
public interface IOwnPluginSource
{
    IReadOnlyList<OwnGenerationPlugin> SnapshotPlugins();
}
