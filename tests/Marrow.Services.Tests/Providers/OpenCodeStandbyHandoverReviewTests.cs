using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

[Trait("Rule", "R-TEST-4")]
public sealed class OpenCodeStandbyHandoverReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stopping_during_ACP_start_cannot_publish_a_live_session_after_the_stop(bool cancel)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new HandoffSession { StartScript = () => { entered.TrySetResult(); return release.Task; } };
        var server = new FakeOpenCodeServer(FakeOpenCodeServer.WithNoReply);
        using var launcher = OpenCodeHistoryInjection.Server.Override((_, _) => Task.FromResult<IOpenCodeServer>(server));
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Opencode, WorkingDirectory = SwiftURL.Create("marrow-native-path:C:/offline")!,
            Executable = SwiftURL.Create("marrow-native-path:C:/offline/review-handover/opencode.exe")!,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
        };
        var session = new OpenCodeInjectionSession(configuration, _ => inner);
        using var cancellation = new CancellationTokenSource();
        await session.StartInjectedStandby(CancellationToken.None);
        var handover = session.EndStandbyAsync(cancellation.Token);
        await entered.Task;
        if (cancel) cancellation.Cancel();
        session.Stop();
        release.TrySetResult("ses_standby");
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handover);
            Assert.False(session.IsRunning);
            Assert.False(inner.IsRunning);
            Assert.True(inner.Stopped);
        }
        finally { inner.Stop(); session.Stop(); }
    }
}
