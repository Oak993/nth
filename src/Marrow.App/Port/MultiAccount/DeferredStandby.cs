// PORT-ONLY: OD-0116, lead decision 2026-10-06 (ED-NEW-ma-engine-4). A destination with no verified text-only standby is never started early: this object only holds the package until the switch.
using Marrow.Core.Accounts;
using Marrow.Services.Providers;

namespace Marrow.App.Runtime;

public sealed class DeferredStandby(SessionConfiguration configuration) : IStandbySession
{
    public SessionConfiguration Configuration { get; } = configuration;
    public StandbyKind Kind => StandbyKind.Deferred;
    public HandoffPackage? ReceivedPackage { get; private set; }

    public Task<string?> ReceiveAsync(HandoffPackage package, bool readBack, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (package.Bring == Bring.AiSummary && string.IsNullOrWhiteSpace(package.Briefing))
            throw new InvalidOperationException("The outgoing account did not produce an AI briefing.");
        if (package.Bring == Bring.Full && package.Summary.FilePath is null)
            throw new InvalidOperationException("The full conversation archive is unavailable.");
        ReceivedPackage = package;
        // Nothing is sent and nothing can be read back: the destination reads the context with its first real turn.
        return Task.FromResult<string?>(null);
    }

    public Task FillGapsAsync(IReadOnlyList<string> gaps, CancellationToken ct) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
