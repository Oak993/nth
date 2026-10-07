using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

// Offline request_tool offer; returns a scripted decision without opening UI or downloading.
internal sealed class BackendFixtureOffer : IToolRequestOffer
{
    public int Calls { get; private set; }
    public bool Available => true;
    public bool InCatalogue(string name) => name == "qpdf";
    public Task<ToolRequestOutcome> Ask(string name, string reason, CancellationToken cancellation)
    {
        Calls++;
        return Task.FromResult(ToolRequestOutcome.Declined);
    }
}
