using Marrow.Core.Accounts.Keys;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

public sealed class RequestPriceContractSession : OpenAICompatibleSession
{
    public RequestPriceContractSession(IApiKeyRotation rotation, bool privateHead) : base(new Config
{
    Kind = ProviderKind.Deepseek, Title = "Offline quote", AgentIdentity = _ => "offline", DefaultModel = "fixture-quote",
    ChatURL = new("https://example.invalid/never-send"), ContextWindow = 50, MaxOutputTokens = 20, IsVisionModel = _ => false,
}, new SessionConfiguration
{
    Provider = ProviderKind.Deepseek, WorkingDirectory = SwiftURL.Create("marrow-native-path:/offline")!,
    Environment = ValueMap<string, string>.Empty, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
})
    {
        KeyRotation = rotation;
        UsesPrivateHeadPricingContract = privateHead;
    }
    public Task<StreamRound> Round(string model = "fixture-quote") => StreamOneRound(model, null, false);
}
