using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

// Test seam for OpenAICompatibleSessionSecondTests (PR-PROVAPI-118..136). Captures emitted events
// and the status code FriendlyError was called with, over the real (non-seamed) StreamOneRound/
// PerformStream this part implements (R-TEST-3: helpers live in Fixtures/, not nested in the test
// class).
public sealed class OpenAICompatibleSessionCapturingSeam(OpenAICompatibleSession.Config config, SessionConfiguration configuration)
    : OpenAICompatibleSession(config, configuration)
{
    public List<ProviderEvent> Events { get; } = [];
    public int? LastFriendlyErrorStatusCode;

    public OpenAICompatibleSessionCapturingSeam Wired()
    {
        OnEvent = Events.Add;
        return this;
    }

    public override string FriendlyError(string message, int statusCode)
    {
        LastFriendlyErrorStatusCode = statusCode;
        return base.FriendlyError(message, statusCode);
    }
}
