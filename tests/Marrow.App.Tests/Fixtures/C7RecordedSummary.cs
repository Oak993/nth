using Marrow.Core.Accounts;

namespace Marrow.App.Tests.Fixtures;

internal sealed class C7RecordedSummary : IHandoffSummary
{
    internal static HandoffSummary Recorded { get; } = new("Recorded offline facts.", null, []);
    public Task<HandoffSummary> BuildAsync(Guid threadId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Recorded);
    }
    public IReadOnlyList<string> FindGaps(HandoffSummary summary, string readBack) => [];
}
