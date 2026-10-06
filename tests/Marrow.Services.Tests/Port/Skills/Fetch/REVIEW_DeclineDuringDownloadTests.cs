using Marrow.Core.Port.Skills.State;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

// A decline is synchronous on the main actor. Holding an unrelated scripted download must
// not prevent the decline from being persisted/answered. Only Marrow-made fixture files.
[Collection("Dev data root env")]
[Trait("Feature", "SK-14")]
[Trait("Spec", "DL-18")]
[Trait("Review", "SK-14")]
[Trait("Rule", "R-TEST-9")]
public sealed class REVIEW_DeclineDuringDownloadTests
{
    [Fact]
    public async Task REVIEW_Declining_an_offer_does_not_wait_for_an_unrelated_Settings_download()
    {
        using var rig = new FetchRig();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Hold["S1"] = held;
        var downloading = rig.Host.Downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None);
        await rig.Entered("S1");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var declined = Task.Run(() =>
        {
            started.SetResult();
            rig.Host.Pipeline.SetDeclined(rig.Host.Pipeline.State.Decline("chat", "S3"));
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var completed = await Task.WhenAny(declined, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(declined, completed);
            Assert.Contains(rig.Host.Pipeline.State.Declined, d => d.ChatId == "chat" && d.ItemIds.Contains("S3"));
        }
        finally
        {
            held.TrySetResult();
            await Task.WhenAll(downloading, declined).WaitAsync(TimeSpan.FromSeconds(20));
        }
    }
}
