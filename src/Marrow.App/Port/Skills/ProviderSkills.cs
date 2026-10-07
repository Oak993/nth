// PORT-ONLY: RR-15 / OD-0099. Runtime preparation of the SK-11 provider families.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Store;

namespace Marrow.App.Port.Skills;

internal static class ProviderSkills
{
    // Same conservative admission cap as the existing Cursor/Antigravity composers; WU-11 measures live limits.
    private static readonly GenerationBudget Budget = new(8192);
    public static Ambient<Func<ProviderKind, string, string, IReadOnlyDictionary<string, string>, Task<Generation?>>> Prepare { get; } = new(PrepareOwned);
    private static Task<Generation?> PrepareOwned(ProviderKind provider, string chat, string directory, IReadOnlyDictionary<string, string> environment)
    {
        var host = SkillsHost.Find(Storage.Root);
        if (host is null) return Task.FromResult<Generation?>(null); // startup logs the unavailable host; the rest of the app remains usable
        var home = LoginEnvironment.HomeDirectory;
        return Task.Run(() => ProviderGenerationComposer.Prepare(host, ProviderSkillsComposition.Family(provider), chat, directory, home, Budget, environment));
    }
}
