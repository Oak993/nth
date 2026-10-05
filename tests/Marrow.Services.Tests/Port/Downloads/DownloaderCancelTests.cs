using Marrow.Services.Port.Downloads;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Downloads;

// SK-3 (DL-3, section 3.2 "Cancel"; OD-0099 pick "Pause/Resume, Cancel keeps finished items"): cancel and pause stop the transfer at once and keep
// the partial file; a paused job holds no connection and waits; Resume continues with Range. The slow body is the fake server's trickle on its
// own manual clock, so each stop happens at a known byte.
[Trait("Spec", "DL-3")]
public sealed class DownloaderCancelTests
{
    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(10_000, seed: 31);
    private static readonly FakeBehavior Slow = new() { Trickle = new FakeTrickle(2_000, TimeSpan.FromMinutes(1)) };

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Cancel_stops_the_transfer_keeps_the_partial_and_ends_without_a_failure()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, Slow);
        var job = rig.Create().Start(rig.Request("/a.bin", Artifact));
        await DownloadRig.Until(() => job.BytesReceived >= 2_000);

        job.Cancel();
        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(DownloadStatus.Cancelled, outcome.Status);
        Assert.Null(outcome.Failure);
        Assert.Equal(DownloadJobState.Cancelled, job.State);
        Assert.Equal(Artifact.Bytes[..2_000], await File.ReadAllBytesAsync(rig.PartPath(Artifact)));
        Assert.False(File.Exists(rig.Dest("a.bin")));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_new_run_after_a_cancel_resumes_from_the_kept_partial()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, Slow);
        var request = rig.Request("/a.bin", Artifact);
        var first = rig.Create().Start(request);
        await DownloadRig.Until(() => first.BytesReceived >= 2_000);
        first.Cancel();
        await first.Completion.WaitAsync(FakeDownloadServer.Guard);
        rig.Server.Behave("/a.bin", new FakeBehavior());

        var second = await rig.Run(request);

        Assert.True(second.Succeeded);
        Assert.Equal(2_000L, second.Files[0].ResumedFrom);
        Assert.Equal(Artifact.Bytes, await File.ReadAllBytesAsync(request.DestinationPath));
        // The cut trickle request is logged only when its server side next writes; the resume is what this test is about.
        await DownloadRig.Until(() => rig.Server.Requests.Any(r => r.RangeStart is not null));
        var resumed = Assert.Single(rig.Server.Requests, r => r.RangeStart is not null);
        Assert.Equal((2_000L, 206, 8_000L), (resumed.RangeStart, resumed.Status, resumed.BodyBytesSent));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Pause_stops_the_transfer_without_ending_the_job_and_Resume_continues_with_Range()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, Slow);
        var job = rig.Create().Start(rig.Request("/a.bin", Artifact));
        await DownloadRig.Until(() => job.BytesReceived >= 2_000);

        job.Pause();

        Assert.Equal(DownloadJobState.Paused, job.State);
        await DownloadRig.Until(() => File.Exists(rig.PartPath(Artifact)) && new FileInfo(rig.PartPath(Artifact)).Length == 2_000);
        rig.Runtime.Advance(TimeSpan.FromMinutes(30)); // nothing happens to a paused job, however long it waits
        rig.ServerClock.Advance(TimeSpan.FromMinutes(30));
        await Task.Yield();
        Assert.False(job.Completion.IsCompleted);
        Assert.Equal(DownloadJobState.Paused, job.State);
        rig.Server.Behave("/a.bin", new FakeBehavior());

        job.Resume();
        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.True(outcome.Succeeded);
        Assert.Equal(2_000L, outcome.Files[0].ResumedFrom);
        Assert.Equal(Artifact.Bytes, await File.ReadAllBytesAsync(rig.Dest("a.bin")));
        await DownloadRig.Until(() => rig.Server.Requests.Any(r => r.RangeStart is not null));
        var resumed = Assert.Single(rig.Server.Requests, r => r.RangeStart is not null);
        Assert.Equal((2_000L, 206, 8_000L), (resumed.RangeStart, resumed.Status, resumed.BodyBytesSent));
        Assert.Equal(DownloadJobState.Succeeded, job.State);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_paused_job_can_be_cancelled()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, Slow);
        var job = rig.Create().Start(rig.Request("/a.bin", Artifact));
        await DownloadRig.Until(() => job.BytesReceived >= 2_000);
        job.Pause();

        job.Cancel();
        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(DownloadStatus.Cancelled, outcome.Status);
        Assert.Equal(DownloadJobState.Cancelled, job.State);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Cancel_while_waiting_for_the_response_ends_the_job_at_once()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var hold = rig.Server.HoldRequests();
        var job = rig.Create().Start(rig.Request("/a.bin", Artifact));
        await hold.WaitForArrivals(1);

        job.Cancel();
        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);
        hold.Release();

        Assert.Equal(DownloadStatus.Cancelled, outcome.Status);
        Assert.False(File.Exists(rig.Dest("a.bin")));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Cancel_during_a_retry_wait_ends_the_job_without_another_request()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { DropAfterBytes = 1_000 });
        var job = rig.Create(new DownloadOptions { RetryDelays = [TimeSpan.FromSeconds(5)] }).Start(rig.Request("/a.bin", Artifact));
        await DownloadRig.Until(() => job.IsWaitingToRetry);

        job.Cancel();
        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);
        rig.Runtime.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(DownloadStatus.Cancelled, outcome.Status);
        Assert.Single(rig.Server.Requests);
        Assert.Equal(1_000, new FileInfo(rig.PartPath(Artifact)).Length);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Pause_Resume_and_Cancel_on_a_finished_job_do_nothing()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var job = rig.Create().Start(rig.Request("/a.bin", Artifact));
        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        job.Pause();
        job.Resume();
        job.Cancel();

        Assert.True(outcome.Succeeded);
        Assert.Equal(DownloadJobState.Succeeded, job.State);
        Assert.Same(outcome, await job.Completion);
    }
}
