// PORT-ONLY: RR-15 / SK-11a..f. Production composition of the existing provider launch adapters.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.Skills.Providers;

public static class SkillSessionFactory
{
    public static ProviderSession Create(SessionConfiguration configuration)
    {
        var generation = configuration.SkillGeneration;
        ProviderSkillLaunch? launch = null;
        if (generation is not null)
        {
            if (configuration.SkillDataRoot is not { } root) throw new InvalidDataException("A generation requires its owning data root.");
            SessionSkillOverlay.RequireFolder(generation, ProviderSkillsComposition.Family(configuration.Provider));
            if (generation.Skills.Count != 0 || generation.PluginFolders.Count != 0) launch = new(generation.Folder, root, generation.PluginFolders);
            if (configuration.Provider is ProviderKind.Opencode or ProviderKind.OpencodeGo)
            {
                var home = configuration.SkillHome ?? throw new InvalidDataException("An OpenCode generation requires its resolved home.");
                var openCode = new OpenCodeSkillLaunch(generation.Folder, configuration.WorkingDirectory.Path, root, home);
                // ProviderRegistry already merged MCP content into this exact child environment.
                configuration = configuration with { Environment = new ValueMap<string, string>(OpenCodeSkillWiring.Apply(configuration.Environment, openCode, new Dictionary<string, string>()), StringComparer.OrdinalIgnoreCase) };
            }
        }
        ProviderSession session = configuration.Provider switch
        {
            ProviderKind.Codex => new CodexSession(configuration, generation is null ? null : new CodexSkillWiring(generation, new StorePaths(configuration.SkillDataRoot!))),
            ProviderKind.Claude => new ClaudeSession(configuration, generation is null ? null : () => generation),
            ProviderKind.Antigravity => new AntigravitySession(configuration),
            ProviderKind.Copilot => new CopilotSession(configuration, generation),
            ProviderKind.Commandcode => new CommandCodeSession(configuration) { SkillLaunch = launch },
            ProviderKind.Pi => new PiSession(configuration) { SkillLaunch = launch },
            ProviderKind.Cursor or ProviderKind.Opencode or ProviderKind.OpencodeGo or ProviderKind.Grok or ProviderKind.Devin => new ACPSession(configuration) { SkillLaunch = launch },
            ProviderKind.Deepseek => new DeepSeekSession(configuration),
            ProviderKind.Meta => new MetaSession(configuration),
            ProviderKind.Zai => new ZaiSession(configuration),
            ProviderKind.OpenRouter => new OpenRouterSession(configuration),
            _ => throw new InvalidOperationException($"Unsupported provider session: {configuration.Provider}."),
        };
        if (session is OpenAICompatibleSession api) api.SkillTools = NativeTools(configuration);
        return session;
    }

    public static NativeSkillTools NativeTools(SessionConfiguration configuration) =>
        new(configuration.SkillGeneration, offer: configuration.SkillToolOffer, advisor: Marrow.Services.Port.Advisor.AdvisorTool.Registration(configuration)); // + ED-NEW-ignisadvisor-2
}
