// PORT-ONLY: SK-17. The existing ThreadRuntime CLI switch, with an async-flow factory seam for offline launch tests.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Runtime;

internal static class CliSessionFactory
{
    public static Ambient<Func<SessionConfiguration, ProviderSession>> Current { get; } = new(Create);
    private static ProviderSession Create(SessionConfiguration configuration) => configuration.Provider switch
    {
        ProviderKind.Codex => new CodexSession(configuration),
        ProviderKind.Claude => new ClaudeSession(configuration),
        ProviderKind.Antigravity => new AntigravitySession(configuration),
        ProviderKind.Copilot => new CopilotSession(configuration),
        ProviderKind.Commandcode => new CommandCodeSession(configuration),
        ProviderKind.Pi => new PiSession(configuration),
        ProviderKind.Cursor or ProviderKind.Opencode or ProviderKind.OpencodeGo or ProviderKind.Grok or ProviderKind.Devin => new ACPSession(configuration),
        ProviderKind.Deepseek => new DeepSeekSession(configuration),
        ProviderKind.Meta => new MetaSession(configuration),
        ProviderKind.Zai => new ZaiSession(configuration),
        ProviderKind.OpenRouter => new OpenRouterSession(configuration),
        _ => throw new InvalidOperationException($"Unsupported provider session: {configuration.Provider}."),
    };
}
