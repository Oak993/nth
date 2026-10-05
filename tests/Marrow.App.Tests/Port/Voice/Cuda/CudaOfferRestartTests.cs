using Marrow.App.Port.Voice.Cuda;
using Marrow.App.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.App.Tests.Port.Voice.Cuda;

// VT-17 review (OD-0106 CUDA A; the x cancels): a Download pressed the instant a cancel has returned the row to the offer is a new download with its own cancel. The first run's end
// must not take that cancel away (it once disposed whatever source was current, so the x of the second download did nothing and it ran on).
public sealed class CudaOfferRestartTests
{
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C; Oracle OD-0106
    public async Task A_download_started_as_a_cancel_returns_to_the_offer_can_itself_be_cancelled()
    {
        var companions = new ScriptedCudaCompanions { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var rig = new CudaOfferRig(companions: companions);
        await rig.Offer.EvaluateAsync(CancellationToken.None);
        Task? second = null;
        var restarted = false;
        rig.Offer.Changed += row =>
        {
            if (row.State != CudaOfferState.Offer || restarted) return;
            restarted = true;
            companions.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            second = rig.Offer.StartAsync(); // pressed at the moment the first cancel is announced
        };

        var first = rig.Offer.StartAsync();
        await rig.Until(() => companions.Started); // the pack is in; the libraries wait
        rig.Offer.Cancel();
        await first.WaitAsync(FakeDownloadServer.Guard);
        Assert.NotNull(second);

        rig.Offer.Cancel(); // the x of the second download
        await second.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(CudaOfferState.Offer, rig.Offer.Row.State);
    }
}
