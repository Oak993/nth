// PORT-ONLY: CU-DX-OPEN2 item 1, DESIGN.md 4.3; ED-NEW-dextra-open-7.
// Dextra's private routes do not expand the generic checking-head capability contract.
using Marrow.Core.Models;
namespace Marrow.Services.Port.MultiAccount;
public static class DextraTextRoutes
{
    public static bool Supports(ProviderKind provider) => TextOnlyAccountHead.Supports(provider) || provider == ProviderKind.Commandcode;
    public static string? UnavailableReason(ProviderKind provider) => Supports(provider) ? null : provider switch
    {
        ProviderKind.Codex => "Cannot turn off all built-in tools for answers.",
        ProviderKind.Copilot => "Cannot block every tool before it runs.",
        ProviderKind.Antigravity => "Cannot turn off all built-in tools for answers.",
        _ => "Cannot turn off its built-in tools for answers.",
    };
}
