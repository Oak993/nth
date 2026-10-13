using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Support;

public sealed class C7ConnectionLifetimeTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_disconnected_connection_cancels_its_host_without_stopping_the_server()
    {
        using var serverStop = new CancellationTokenSource();
        using var disconnected = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new ScriptedControlHost(_ => new NthControlAnswer(ScriptedControlHost.Ok()))
        {
            Gate = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); },
        };
        var pending = NthControlServer.HandleConnectedAsync(host, NthControlRequest.Status, serverStop.Token, disconnected.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            disconnected.Cancel();
            await Task.Delay(100);
            Assert.True(pending.IsCompleted);
            Assert.False(serverStop.IsCancellationRequested);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        finally
        {
            serverStop.Cancel();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { }
        }
    }
}
