using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// Dextra AS-4 (ED-NEW-dextra-as4-5, -6): an API session whose rounds are scripted but whose tools are the real ones, so
/// the native nth-dextra tools and the fenced-text fallback run exactly as in a live session. ToolCalling false stands in
/// for a local model with tool calling off.
/// </summary>
public sealed class DextraSessionSeam(OpenAICompatibleSession.Config config, SessionConfiguration configuration, bool toolCalling = true)
    : OpenAICompatibleSession(config, configuration)
{
    public Queue<OpenAICompatibleSession.StreamRound> Rounds { get; } = new();
    public List<(string Model, string? Effort, bool WithTools)> RoundRequests { get; } = [];
    public List<ProviderEvent> Events { get; } = [];

    public DextraSessionSeam Wired()
    {
        OnEvent = Events.Add;
        return this;
    }

    protected override bool ToolCallingAvailable => toolCalling;

    protected override Task<StreamRound> StreamOneRound(string model, string? effort, bool withTools = true)
    {
        RoundRequests.Add((model, effort, withTools));
        if (Rounds.Count == 0) throw new InvalidOperationException("Test bug: no StreamRound queued for this call.");
        return Task.FromResult(Rounds.Dequeue());
    }
}
