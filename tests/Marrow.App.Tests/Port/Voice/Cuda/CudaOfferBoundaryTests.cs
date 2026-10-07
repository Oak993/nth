using Marrow.App.Port.Voice.Cuda;
using Marrow.App.Tests.Fixtures;
using Marrow.Testing.Downloads;
namespace Marrow.App.Tests.Port.Voice.Cuda;

[Trait("Decision", "ED-351")]
public sealed class CudaOfferBoundaryTests
{
    [Fact]
    public async Task Cancellation_after_the_probe_leaves_the_previous_row_without_throwing()
    {
        using var cancellation = new CancellationTokenSource();
        await using var rig = new CudaOfferRig(probe: new CudaCancellingProbe(cancellation));
        await rig.Offer.EvaluateAsync(cancellation.Token);
        Assert.Equal(CudaOfferRow.Hidden, rig.Offer.Row);
    }

    [Fact]
    public async Task The_worker_starts_only_after_the_initial_downloading_notification_returns()
    {
        await using var rig = new CudaOfferRig();
        await rig.Offer.EvaluateAsync(CancellationToken.None);
        var enteredDuringInitialNotification = false;
        rig.Offer.Changed += row =>
        {
            if (row.State == CudaOfferState.Downloading && row.Progress == 0)
                enteredDuringInitialNotification = rig.Server.WaitForRequests(1).Wait(TimeSpan.FromSeconds(.5));
        };
        await rig.Offer.StartAsync().WaitAsync(FakeDownloadServer.Guard);
        Assert.False(enteredDuringInitialNotification, "progress/completion must not overtake the initial state delivery");
        Assert.Equal(CudaOfferState.Installed, rig.Offer.Row.State);
    }
}
