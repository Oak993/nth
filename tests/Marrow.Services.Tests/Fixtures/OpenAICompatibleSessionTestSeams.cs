using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

// Test seam for OpenAICompatibleSessionTests (PR-PROVAPI-089..112). StreamOneRound and ExecuteTool
// are protected virtual seams on OpenAICompatibleSession in this part of the unit (the request
// payload/SSE streaming and the built-in tool bodies land in later parts); this subclass supplies
// controlled results so the orchestration this part's records cover is testable now (R-TEST-3:
// helpers live in Fixtures/, not nested in the test class).
public sealed class OpenAICompatibleSessionTestSeam(OpenAICompatibleSession.Config config, SessionConfiguration configuration)
    : OpenAICompatibleSession(config, configuration)
{
    public Queue<OpenAICompatibleSession.StreamRound> Rounds { get; } = new();
    public List<(string Model, string? Effort, bool WithTools)> RoundRequests { get; } = [];
    public List<ProviderEvent> Events { get; } = [];
    public Func<PendingToolCall, Task<string>>? ToolHandler;
    public List<PendingToolCall> ToolCallsSeen { get; } = [];

    public OpenAICompatibleSessionTestSeam Wired()
    {
        OnEvent = Events.Add;
        return this;
    }

    protected override Task<StreamRound> StreamOneRound(string model, string? effort, bool withTools = true)
    {
        RoundRequests.Add((model, effort, withTools));
        if (Rounds.Count == 0) throw new InvalidOperationException("Test bug: no StreamRound queued for this call.");
        return Task.FromResult(Rounds.Dequeue());
    }

    protected override async Task<string> ExecuteTool(PendingToolCall tool)
    {
        ToolCallsSeen.Add(tool);
        if (ToolHandler is { } handler) return await handler(tool);
        return $"ok:{tool.Name}";
    }
}

// A non-Swift error used only to prove PR-PROVAPI-110's generic catch-all maps an unexpected error
// through FriendlyError with status code 0 (R-TEST-8: a scripted stand-in, not a real failure mode).
public sealed class ToolBoomTestError : Exception, ISwiftErrorIdentity
{
    public string Domain => "Test.ToolBoom";
    public long Code => 1;
}
