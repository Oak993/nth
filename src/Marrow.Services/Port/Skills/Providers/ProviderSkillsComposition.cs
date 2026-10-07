// PORT-ONLY: RR-15/16. Provider family identity and observations shared by launch composition and pages.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Store;

namespace Marrow.Services.Port.Skills.Providers;

public static class ProviderSkillsComposition
{
    public static string Family(ProviderKind provider) => provider switch
    {
        ProviderKind.Claude => "claude", ProviderKind.Codex => "codex", ProviderKind.Copilot => "copilot",
        ProviderKind.Opencode => "opencode", ProviderKind.OpencodeGo => "opencode-go", ProviderKind.Pi => "pi",
        ProviderKind.Commandcode => "commandcode", ProviderKind.Grok => "grok", ProviderKind.Antigravity => "agy",
        ProviderKind.Cursor => "cursor", ProviderKind.Devin => "devin",
        ProviderKind.Deepseek or ProviderKind.Meta or ProviderKind.Zai or ProviderKind.OpenRouter => "native",
        _ => throw new ArgumentException("Unsupported skill provider.", nameof(provider)),
    };

    public static void Observe(ProviderKind provider, string directory, IEnumerable<SlashCommand> commands)
    {
        SkillsHost? host;
        try { host = SkillsHost.Find(Storage.Root); }
        catch (SwiftTrapException) { return; } // the app binds Storage; offline registry tests do not
        host?.ProviderSkills.Observe(Family(provider), provider.DisplayName, directory, commands);
    }
}
