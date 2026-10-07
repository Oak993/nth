using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

// Never reaches HTTP: replace each attempted stream with a deterministic text/usage report.
public sealed class OfflineOpinionApiSession(SessionConfiguration configuration, bool reportUsd = true) : OpenAICompatibleSession(new Config
{
    Kind = configuration.Provider, Title = "Offline opinion", AgentIdentity = _ => "offline", DefaultModel = "offline",
    ChatURL = new("https://example.invalid/never-send"), ContextWindow = 64000, IsVisionModel = _ => false,
}, configuration)
{
    public List<Dictionary<string, JSONValue>> Payloads { get; } = [];
    protected override Task<StreamRound> PerformStreamAttempt(Dictionary<string, JSONValue> payload)
    {
        Payloads.Add(payload);
        OnEvent?.Invoke(new ProviderEvent.MessageCompleted("offline", "Opinion text"));
        return Task.FromResult(new StreamRound { Content = "Opinion text", TokenUsage = new() { Total = 10, ReportedCostUsd = reportUsd ? 1 : null } });
    }
}
