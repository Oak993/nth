using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

/// <summary>W13: the chat's model, recorded fixtures only: every request is kept and answered with the canned reply.</summary>
internal sealed class FakeArtifactModelClient : IArtifactModelClient
{
    public string? Name { get; set; } = "Claude Sonnet";

    public List<(Guid Thread, ArtifactCompletionRequest Request)> Requests { get; } = [];

    public ArtifactCompletionResult Reply { get; set; } = new("Sunny, 21°C.", 120, 8);

    /// <summary>Text pieces streamed before the reply.</summary>
    public IReadOnlyList<string> Deltas { get; set; } = [];

    /// <summary>When set, calls wait on it (a call in flight).</summary>
    public TaskCompletionSource<ArtifactCompletionResult>? Held { get; set; }

    public string? ModelName(Guid thread) => Name;

    public Task<ArtifactCompletionResult> CompleteAsync(Guid thread, ArtifactCompletionRequest request, Action<string>? onDelta, CancellationToken cancellationToken)
    {
        Requests.Add((thread, request));
        foreach (var delta in Deltas) onDelta?.Invoke(delta);
        if (Held is { } held)
        {
            cancellationToken.Register(() => held.TrySetCanceled(cancellationToken));
            return held.Task;
        }

        return Task.FromResult(Reply);
    }
}
