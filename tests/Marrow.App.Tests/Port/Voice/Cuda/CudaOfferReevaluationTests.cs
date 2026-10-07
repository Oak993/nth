using Marrow.App.Port.Voice.Cuda;
using Marrow.App.Tests.Fixtures;
namespace Marrow.App.Tests.Port.Voice.Cuda;

[Trait("Decision", "ED-351")]
public sealed class CudaOfferReevaluationTests
{
    [Fact]
    public async Task Reevaluating_during_a_download_keeps_the_same_download_and_cancel_target()
    {
        await using var rig = new CudaOfferRig();
        rig.Companions.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await rig.Offer.EvaluateAsync(CancellationToken.None);
        var running = rig.Offer.StartAsync();
        await rig.Until(() => rig.Companions.Started);
        await rig.Offer.EvaluateAsync(CancellationToken.None);
        Assert.Equal(CudaOfferState.Downloading, rig.Offer.Row.State);
        Assert.Same(running, rig.Offer.StartAsync());
        rig.Offer.Cancel();
        await running;
        Assert.Equal(CudaOfferState.Offer, rig.Offer.Row.State);
    }
}
