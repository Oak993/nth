using Marrow.Services.Port.Skills.Shims;
namespace Marrow.Services.Port.Skills.Pipelines;
// PORT-ONLY: SK-8. Immutable version routing prepared before state.json selects its pin.
public sealed record PipelineRoute(string Id, string Pin, string Folder, string[] SparePaths, Dictionary<string, ShimDefinition> Shims);
