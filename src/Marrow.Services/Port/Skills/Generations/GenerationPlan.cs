// PORT-ONLY: SK-9 trusted in-memory composition. Nothing from a disk manifest becomes authority.
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Generations;

internal sealed record GenerationPlan(string Key, StoreManifest Manifest, IReadOnlyDictionary<string, byte[]> Files,
    IReadOnlyList<GenerationSkill> Skills, IReadOnlyDictionary<string, string> Locations, IReadOnlyList<string> LeftOut, IReadOnlyList<string> PluginRoots);
