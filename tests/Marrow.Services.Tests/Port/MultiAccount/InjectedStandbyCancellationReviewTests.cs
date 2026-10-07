using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class InjectedStandbyCancellationReviewTests
{
    [Fact]
    public async Task A_cancelled_injected_start_discards_a_session_that_finishes_starting_later()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new ReviewInjectionSession { Starting = () => { entered.TrySetResult(); return release.Task; } };
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Pi, WorkingDirectory = SwiftURL.Create("marrow-native-path:C:/offline")!,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
        };
        using var cancellation = new CancellationTokenSource();
        var start = InjectedStandbySession.StartAsync(session, configuration, cancellation.Token);
        await entered.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);

        release.TrySetResult();
        try
        {
            await session.DiscardedAfterStart.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(session.IsRunning);
        }
        finally { session.Stop(); }
    }
}
