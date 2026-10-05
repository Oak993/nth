// PORT-ONLY: OD-0114; ED-NEW-ma-ui. Detached and live launches share one account-bound construction boundary.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Port.Local;

namespace Marrow.Services.Port.MultiAccount;

public static class AccountSessionFactory
{
    // Scoped offline construction seam; all production launches use the same account boundary.
    public static Ambient<Func<ProviderRegistry, SessionConfiguration, Guid, bool, AccountSessionLaunch>> Launcher { get; } = new(Build);
    public static AccountSessionLaunch Create(ProviderRegistry registry, SessionConfiguration configuration, Guid accountId, bool freshContext = false)
        => Launcher.Current(registry, configuration, accountId, freshContext);

    private static AccountSessionLaunch Build(ProviderRegistry registry, SessionConfiguration configuration, Guid accountId, bool freshContext)
    {
        var source = freshContext ? configuration with
        {
            ResumeID = null, ResumeAt = null, ResumePath = null, Transcript = ValueList<TranscriptMessage>.Empty,
        } : configuration;
        var prepared = registry.PrepareSession(source, accountId, useAccountDefaults: false);
        ProviderSession session = prepared.Provider switch
        {
            ProviderKind.Codex => new CodexSession(prepared),
            ProviderKind.Claude => new ClaudeSession(prepared),
            ProviderKind.Antigravity => new AntigravitySession(prepared),
            ProviderKind.Copilot => new CopilotSession(prepared),
            ProviderKind.Commandcode => new CommandCodeSession(prepared),
            ProviderKind.Pi => new PiSession(prepared),
            ProviderKind.Cursor or ProviderKind.Opencode or ProviderKind.OpencodeGo or ProviderKind.Grok or ProviderKind.Devin => new ACPSession(prepared),
            ProviderKind.Deepseek => new DeepSeekSession(prepared),
            ProviderKind.Meta => new MetaSession(prepared),
            ProviderKind.Zai => new ZaiSession(prepared),
            ProviderKind.OpenRouter => new OpenRouterSession(prepared),
            ProviderKind.Local => new LocalSession(prepared, registry.Local.Resolve(prepared.Model)
                ?? throw new InvalidOperationException("That local server or model is unavailable. Refresh the model list and select it again.")),
            _ => throw new InvalidOperationException($"Unsupported provider session: {prepared.Provider}."),
        };
        return new(prepared, session);
    }
}
