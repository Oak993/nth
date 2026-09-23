using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

// Test seam for the transport-retry ladder (PR-PROVAPI-121, PR-PROVAPI-122, PR-PROVAPI-123):
// replaces a single stream attempt with a scripted queue of results, so StreamOneRound's retry
// loop (OpenAICompatibleSession.swift:506-557) is testable deterministically, over TestRuntime's
// manual clock, without a live dropped connection (R-TEST-3: helpers live in Fixtures/).
public sealed class OpenAICompatibleSessionRetryLadderSeam(OpenAICompatibleSession.Config config, SessionConfiguration configuration)
    : OpenAICompatibleSession(config, configuration)
{
    public List<ProviderEvent> Events { get; } = [];
    public int AttemptCount { get; private set; }
    public Queue<Func<OpenAICompatibleSession.StreamRound>> Results { get; } = new();

    public OpenAICompatibleSessionRetryLadderSeam Wired()
    {
        OnEvent = Events.Add;
        return this;
    }

    protected override Task<StreamRound> PerformStreamAttempt(Dictionary<string, JSONValue> payload)
    {
        AttemptCount++;
        if (Results.Count == 0) throw new InvalidOperationException("Test bug: no attempt result queued.");
        var factory = Results.Dequeue();
        try
        {
            return Task.FromResult(factory());
        }
        catch (Exception ex)
        {
            return Task.FromException<StreamRound>(ex);
        }
    }
}

// A test-only error with a defined Swift-style description (ILocalizedError), used to prove
// PR-PROVAPI-123's message-composition formula without the O-14 gap that SwiftErrors.
// LocalizedDescription has for a real URLError (Marrow.Core.Tests HttpTests
// "SwiftErrors.LocalizedDescription(new URLError(...)) throws NotSupportedException"; also noted
// at OpenAICompatibleSession.cs:652-658). Any LocalizedError produces the same composed sentence,
// since :566-567's formatting does not depend on the error's own type.
public sealed class FakeTransportReasonError(string? description) : Exception, ILocalizedError
{
    public string? ErrorDescription => description;
}
