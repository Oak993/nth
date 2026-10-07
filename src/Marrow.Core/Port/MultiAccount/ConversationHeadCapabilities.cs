// PORT-ONLY: RR-49 / ED-462. Selection and execution share this fail-closed allowlist.
using Marrow.Core.Models;

namespace Marrow.Core.Port.MultiAccount;

public static class ConversationHeadCapabilities
{
    // A checking/opinion turn must have a verified CLI/runtime restriction, never merely a prompt.
    // Claude and Pi still prove that restriction at launch. API/Local payloads disable tools.
    // All other CLI services (and future unknown values) are omitted from head selection.
    public static bool Supports(ProviderKind service) => service is ProviderKind.Claude or ProviderKind.Pi
        or ProviderKind.Deepseek or ProviderKind.Meta or ProviderKind.Zai or ProviderKind.OpenRouter or ProviderKind.Local;
}
