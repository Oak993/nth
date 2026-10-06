using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

// The real encoder/transport boundary runs; a payload hook deterministically revokes after construction.
public sealed class ConsentBoundarySession(SessionConfiguration configuration, Action built) : OpenAICompatibleSession(new()
{
    Kind = configuration.Provider, Title = "Offline", AgentIdentity = _ => "Offline", DefaultModel = "offline",
    ChatURL = new("https://offline.invalid/chat"), ContextWindow = 64000, IsVisionModel = _ => false,
    RequiresAPIKey = configuration.Provider != Marrow.Core.Models.ProviderKind.Local,
}, configuration)
{
    public override void ApplyProviderPayload(Dictionary<string, JSONValue> payload, string model, string? effort, bool withTools)
    {
        base.ApplyProviderPayload(payload, model, effort, withTools);
        built();
    }
}
