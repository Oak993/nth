// PORT-ONLY: SK-16 / T9 regression for whole-download pipeline blocking (ED-368). Offline gated downloads; no network or CLI.
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

[Trait("Feature", "SK-16")]
[Trait("Spec", "DL-19")]
[Trait("Decision", "ED-368")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class FetchDownloadBlockingTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Terms_acceptance_returns_while_another_items_download_is_held()
    {
        using var rig = new FetchRig();
        rig.Hold["S3"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = rig.Host.Downloads.Install(rig.Item("S3"), new PipelineProgress([]), CancellationToken.None);
        await rig.Entered("S3");
        var terms = Task.Run(() => rig.Host.Pipeline.AcceptTerms("N1", new DateTimeOffset(2026, 10, 5, 20, 0, 0, TimeSpan.Zero)));
        try
        {
            await terms.WaitAsync(Guard);
            Assert.False(rig.Hold["S3"].Task.IsCompleted);
            Assert.False(ItemTerms.Required(rig.Item("N1"), rig.Host.Pipeline.State));
        }
        finally
        {
            rig.Hold["S3"].TrySetResult();
            await held.WaitAsync(Guard);
            await terms.WaitAsync(Guard);
        }
    }

    [Fact]
    public async Task A_reset_during_a_held_download_returns_and_prevents_its_later_install()
    {
        using var rig = new FetchRig();
        rig.Hold["S3"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = rig.Host.Downloads.Install(rig.Item("S3"), new PipelineProgress([]), CancellationToken.None);
        await rig.Entered("S3");
        var reset = rig.Host.Pipeline.ResetAsync();
        try
        {
            await reset.WaitAsync(Guard);
            Assert.False(rig.Hold["S3"].Task.IsCompleted);
            rig.Hold["S3"].SetResult();
            await Assert.ThrowsAsync<InvalidOperationException>(() => held.WaitAsync(Guard));
            Assert.Null(rig.Host.Pipeline.InstalledFolder("S3"));
        }
        finally
        {
            rig.Hold["S3"].TrySetResult();
            try { await held.WaitAsync(Guard); } catch (InvalidOperationException) { }
            await reset.WaitAsync(Guard);
        }
    }
}