using Marrow.Services.Port.Downloads;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Downloads;

// SK-3 (DL-3, section 3.9): 404, 410, 500, TLS failure, offline and the two timeouts, each mapped to its message, each keeping the partial file the
// way the table says; timeouts run on the manual clock.
[Trait("Spec", "DL-3")]
public sealed class DownloaderFailureTests
{
    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(10_000, seed: 13);

    [Theory]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    [InlineData(404)]
    [InlineData(410)]
    public async Task A_404_or_410_is_the_no_longer_at_its_source_message(int status)
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { Status = status });

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact, source: "libreoffice.org"));

        Assert.Equal(DownloadStatus.Failed, outcome.Status);
        Assert.Equal((DownloadFailureKind.NotFound, status), (outcome.Failure!.Kind, outcome.Failure.HttpStatus));
        Assert.Equal("The file is no longer at its source. A Marrow update will fix this.", outcome.Failure.Message);
        Assert.False(File.Exists(rig.Dest("a.bin")));
    }

    [Theory]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    [InlineData(500)]
    [InlineData(503)]
    public async Task A_server_error_reads_as_not_reaching_the_source(int status)
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { Status = status });

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact, source: "libreoffice.org"));

        Assert.Equal((DownloadFailureKind.ServerError, status), (outcome.Failure!.Kind, outcome.Failure.HttpStatus));
        Assert.Equal("Couldn't reach libreoffice.org.", outcome.Failure.Message);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Without_a_source_name_the_message_names_the_url_host()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { Status = 500 });

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact));

        Assert.Equal("Couldn't reach 127.0.0.1.", outcome.Failure!.Message);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_failed_status_keeps_a_partial_that_was_already_there_for_Retry()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { Status = 404 });
        rig.SeedPart(Artifact, Artifact.Bytes[..4_000]);

        await rig.Run(rig.Request("/a.bin", Artifact));

        Assert.Equal(4_000, new FileInfo(rig.PartPath(Artifact)).Length);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_certificate_nobody_trusts_is_a_tls_failure_that_reads_as_not_reaching_the_source()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        rig.Server.PresentUntrustedCertificate = true;

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact, source: "python.org"));

        Assert.Equal((DownloadFailureKind.Unreachable, DownloadCause.Tls), (outcome.Failure!.Kind, outcome.Failure.Cause));
        Assert.Equal("Couldn't reach python.org.", outcome.Failure.Message);
        Assert.Empty(rig.Server.Requests);
        Assert.False(File.Exists(rig.Dest("a.bin")));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task No_connection_at_all_is_offline_and_keeps_the_partial()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        rig.SeedPart(Artifact, Artifact.Bytes[..2_000]);
        var offline = FakeDownloadTransport.CreateHandler(); // no fake server: every request is refused before any connection
        var downloader = new Downloader(rig.PartFolder, new DownloadPolicy(["127.0.0.1"]), handler: offline);

        var outcome = await downloader.Start(rig.Request("/a.bin", Artifact, source: "nodejs.org")).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal((DownloadFailureKind.Unreachable, DownloadCause.Offline), (outcome.Failure!.Kind, outcome.Failure.Cause));
        Assert.Equal("Couldn't reach nodejs.org.", outcome.Failure.Message);
        Assert.Equal(2_000, new FileInfo(rig.PartPath(Artifact)).Length);
        Assert.Equal(0, rig.Server.ConnectionCount);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_server_that_never_answers_times_out_after_30_s_on_the_clock()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var hold = rig.Server.HoldRequests();
        var job = rig.Create().Start(rig.Request("/a.bin", Artifact, source: "nodejs.org"));

        await hold.WaitForArrivals(1);
        rig.Runtime.Advance(TimeSpan.FromSeconds(29.9));
        await Task.Yield();
        Assert.False(job.Completion.IsCompleted);
        rig.Runtime.Advance(TimeSpan.FromSeconds(0.1));
        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);
        hold.Release();

        Assert.Equal((DownloadFailureKind.Unreachable, DownloadCause.Timeout), (outcome.Failure!.Kind, outcome.Failure.Cause));
        Assert.Equal("Couldn't reach nodejs.org.", outcome.Failure.Message);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_stalled_body_times_out_after_60_s_without_bytes_and_keeps_what_arrived()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { Trickle = new FakeTrickle(1_000, TimeSpan.FromMinutes(10)) });
        var job = rig.Create().Start(rig.Request("/a.bin", Artifact));

        await DownloadRig.Until(() => job.BytesReceived >= 1_000);
        rig.Runtime.Advance(TimeSpan.FromSeconds(59.9));
        await Task.Yield();
        Assert.False(job.Completion.IsCompleted);
        rig.Runtime.Advance(TimeSpan.FromSeconds(0.1));
        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal((DownloadFailureKind.Unreachable, DownloadCause.Timeout), (outcome.Failure!.Kind, outcome.Failure.Cause));
        Assert.Equal(1_000, new FileInfo(rig.PartPath(Artifact)).Length);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task The_stall_timer_restarts_with_every_chunk()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { Trickle = new FakeTrickle(1_000, TimeSpan.FromSeconds(50)) });
        var job = rig.Create().Start(rig.Request("/a.bin", Artifact));

        for (var chunk = 1; chunk <= 3; chunk++)
        {
            await DownloadRig.Until(() => job.BytesReceived >= 1_000 * chunk);
            await DownloadRig.Until(() => rig.ServerClock.ActiveTimers > 0);
            rig.Runtime.Advance(TimeSpan.FromSeconds(50)); // 50 s of silence each time: under 60 s, so no timeout while bytes keep coming
            rig.ServerClock.Advance(TimeSpan.FromSeconds(50));
        }

        await DownloadRig.Until(() => job.BytesReceived >= 4_000);
        Assert.False(job.Completion.IsCompleted);
        job.Cancel();
        await job.Completion.WaitAsync(FakeDownloadServer.Guard);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task The_timeouts_are_options()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var hold = rig.Server.HoldRequests();
        var job = rig.Create(new DownloadOptions { ConnectTimeout = TimeSpan.FromSeconds(5) }).Start(rig.Request("/a.bin", Artifact));

        await hold.WaitForArrivals(1);
        rig.Runtime.Advance(TimeSpan.FromSeconds(5));
        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);
        hold.Release();

        Assert.Equal(DownloadCause.Timeout, outcome.Failure!.Cause);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_403_is_final_but_a_503_is_retried_when_retries_are_allowed()
    {
        await using var forbidden = new DownloadRig();
        forbidden.Server.Serve("/a.bin", Artifact, new FakeBehavior { Status = 403 });
        var refused = await forbidden.Run(forbidden.Request("/a.bin", Artifact), new DownloadOptions { RetryDelays = [TimeSpan.FromSeconds(1)] });
        Assert.Equal((DownloadFailureKind.ServerError, 403), (refused.Failure!.Kind, refused.Failure.HttpStatus));
        Assert.Single(forbidden.Server.Requests);

        await using var busy = new DownloadRig();
        busy.Server.Serve("/a.bin", Artifact, new FakeBehavior { Status = 503, FaultyRequests = 1 });
        var job = busy.Create(new DownloadOptions { RetryDelays = [TimeSpan.FromSeconds(1)] }).Start(busy.Request("/a.bin", Artifact));
        await DownloadRig.Until(() => job.IsWaitingToRetry);
        busy.Runtime.Advance(TimeSpan.FromSeconds(1));
        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);
        Assert.True(outcome.Succeeded);
        Assert.Equal([503, 200], busy.Server.Requests.Select(r => r.Status));
    }
}
