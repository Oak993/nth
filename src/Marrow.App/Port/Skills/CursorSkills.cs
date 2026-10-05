// PORT-ONLY: SK-17 owner's follow-up. Supply a verified generation for Cursor's live-proven session MCP route.
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

internal static class CursorSkills
{
    private static readonly GenerationBudget Budget = new(8192); // Approved conservative SK-17 cap; WU-11 measures the real CLI limit.
    public static Ambient<Func<string, string, Task<Generation>>> Prepare { get; } = new(PrepareOwned);
    private static Task<Generation> PrepareOwned(string chat, string directory)
    {
        var host = SkillsHost.Find(Storage.Root)
            ?? throw new ProviderError(new ProviderError.Case.Failed("The skills host is unavailable for this run."));
        var home = LoginEnvironment.HomeDirectory;
        return Task.Run(() =>
        {
            try { return CursorSkillsComposer.Prepare(host, chat, directory, home, Budget); }
            catch (Exception error) when (SwiftTry.IsThrownError(error)
                && error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or DecoderFallbackException or ArgumentException)
            { throw new ProviderError(new ProviderError.Case.Failed(error.Message)); }
        });
    }
}
