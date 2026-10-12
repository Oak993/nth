using System.Text.Json;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

/// <summary>W13: the thread's MCP tools as the test answers them (after the normal approvals, which the fake skips).</summary>
internal sealed class FakeArtifactToolClient : IArtifactToolClient
{
    public List<(string Server, string Tool, string Arguments)> Calls { get; } = [];

    public Task<ArtifactToolCallResult> CallAsync(Guid thread, string server, string tool, JsonElement arguments, CancellationToken cancellationToken)
    {
        Calls.Add((server, tool, arguments.GetRawText()));
        using var document = JsonDocument.Parse("{\"releases\":[\"2.0\",\"1.9\"]}");
        return Task.FromResult(new ArtifactToolCallResult(document.RootElement.Clone()));
    }
}
