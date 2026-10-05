using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

[Trait("Feature", "SK-14")]
[Trait("Spec", "DL-18")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class W3_AttachRaceTests
{
    [Fact]
    public async Task An_attach_losing_a_later_flight_during_replay_never_starts_another_download()
    {
        using var rig = new FetchRig();
        rig.Hold["S1"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Hold["S3"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = rig.Host.Downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None);
        var later = rig.Host.Downloads.Install(rig.Item("S3"), new PipelineProgress([]), CancellationToken.None);
        Task<IReadOnlyDictionary<string, string>>? attach = null;
        try
        {
            await rig.Entered("S1");
            await rig.Entered("S3");
            var stopped = 0;
            attach = rig.Host.OfferDownloads.Attach([rig.Item("S1"), rig.Item("S3")], _ =>
            {
                // The first flight's synchronous replay occurs after validation and before joining S3.
                if (Interlocked.Exchange(ref stopped, 1) == 0) rig.Host.Downloads.Stop("S3");
            }, CancellationToken.None);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => attach.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("No existing download is bound.", error.Message);
            Assert.Equal(1, rig.Downloaded.Count(id => id == "S3"));
            Assert.False(rig.Hold["S1"].Task.IsCompleted);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => later.WaitAsync(TimeSpan.FromSeconds(20)));
        }
        finally
        {
            rig.Hold["S1"].TrySetResult();
            rig.Hold["S3"].TrySetResult();
            try { await Task.WhenAll(first, later, (Task?)attach ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(20)); }
            catch (Exception error) when (error is OperationCanceledException or InvalidOperationException) { }
        }
        Assert.Equal(1, rig.Downloaded.Count(id => id == "S3"));
    }
}
