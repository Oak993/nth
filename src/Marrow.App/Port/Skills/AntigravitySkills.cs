// PORT-ONLY: SK-17 / OD-0099 Q-SKILLS-8 A. The app supplies and owns a generation for every Antigravity launch.
using System.Text;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Store;

namespace Marrow.App.Port.Skills;

internal static class AntigravitySkills
{
    // Lead-approved on 2026-10-05: conservative admission cap, not a claim about the CLI's real limit (WU-11).
    private static readonly GenerationBudget Budget = new(8192);
    public static Ambient<Func<string, string, Task<Generation>>> Prepare { get; } = new(PrepareOwned);

    private static Task<Generation> PrepareOwned(string chat, string directory)
    {
        // Program binds Storage and opens exactly one host for that same root. The registry lookup also handles
        // SK-13's fail-soft startup: an unavailable host is a normal provider failure, never an unbound-seam trap.
        var host = SkillsHost.Find(Storage.Root)
            ?? throw new ProviderError(new ProviderError.Case.Failed("The skills host is unavailable for this run."));
        var home = LoginEnvironment.HomeDirectory;
        return Task.Run(() =>
        {
            try { return AntigravitySkillsComposer.Prepare(host, chat, directory, home, Budget); }
            catch (Exception error) when (SwiftTry.IsThrownError(error)
                && error is IOException or UnauthorizedAccessException or JsonException or DecoderFallbackException or ArgumentException)
            {
                throw new ProviderError(new ProviderError.Case.Failed(error.Message));
            }
        });
    }
}
