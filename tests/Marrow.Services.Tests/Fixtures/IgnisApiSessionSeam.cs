using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test seam for Ignis unit U2 (OD-0134): the real StreamOneRound (payload, key, price and retry path) runs,
// and only the single stream attempt is replaced. Each attempt records the exact payload it would have sent and
// answers with the next queued round, so no request ever leaves the process (R-TEST-3: helpers live in Fixtures/).
public sealed class IgnisApiSessionSeam(OpenAICompatibleSession.Config config, SessionConfiguration configuration)
    : OpenAICompatibleSession(config, configuration)
{
    public List<ProviderEvent> Events { get; } = [];
    public List<Dictionary<string, JSONValue>> Payloads { get; } = [];
    public Queue<OpenAICompatibleSession.StreamRound> Rounds { get; } = new();

    public IgnisApiSessionSeam Wired()
    {
        OnEvent = Events.Add;
        SeedMcpToolsForTests([]);
        return this;
    }

    protected override Task<StreamRound> PerformStreamAttempt(Dictionary<string, JSONValue> payload)
    {
        Payloads.Add(new Dictionary<string, JSONValue>(payload, StringComparer.Ordinal));
        if (Rounds.Count == 0) throw new InvalidOperationException("Test bug: no StreamRound queued for this attempt.");
        return Task.FromResult(Rounds.Dequeue());
    }
}
