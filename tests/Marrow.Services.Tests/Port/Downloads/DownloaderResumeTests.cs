using Marrow.Services.Port.Downloads;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Downloads;

// SK-3 (DL-3, section 3.2 "Big files"): resume after a drop with Range, bytes not refetched (the fake server's log is the witness); restart from zero when
// the server cannot resume. Section 3.3: a mismatch after a resume is the one case that restarts silently, once.
[Trait("Spec", "DL-3")]
public sealed class DownloaderResumeTests
{
    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(100_000, seed: 5);

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task After_a_drop_the_partial_is_kept_and_the_next_run_resumes_with_Range_and_refetches_no_byte()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { DropAfterBytes = 40_000, FaultyRequests = 1 });
        var request = rig.Request("/a.bin", Artifact);

        var first = await rig.Run(request);

        Assert.Equal(DownloadStatus.Failed, first.Status);
        Assert.Equal((DownloadFailureKind.Unreachable, DownloadCause.Interrupted), (first.Failure!.Kind, first.Failure.Cause));
        Assert.Equal(40_000, new FileInfo(rig.PartPath(Artifact)).Length);
        Assert.False(File.Exists(request.DestinationPath));

        var second = await rig.Run(request);

        Assert.True(second.Succeeded);
        Assert.Equal((40_000L, false), (second.Files[0].ResumedFrom, second.Files[0].RestartedFromZero));
        Assert.Equal(Artifact.Bytes, await File.ReadAllBytesAsync(request.DestinationPath));
        Assert.False(File.Exists(rig.PartPath(Artifact)));
        var log = await rig.Server.WaitForRequests(2);
        Assert.Equal([null, 40_000L], log.Select(r => r.RangeStart));
        Assert.Equal([200, 206], log.Select(r => r.Status));
        Assert.Equal(100_000L, log.Sum(r => r.BodyBytesSent)); // every byte crossed the wire exactly once
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_partial_left_by_an_earlier_process_is_resumed_from_its_length()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        rig.SeedPart(Artifact, Artifact.Bytes[..30_000]);

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact));

        Assert.True(outcome.Succeeded);
        Assert.Equal(30_000L, outcome.Files[0].ResumedFrom);
        var log = Assert.Single(await rig.Server.WaitForRequests(1));
        Assert.Equal((30_000L, 206, 70_000L), (log.RangeStart, log.Status, log.BodyBytesSent));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Progress_after_a_resume_counts_the_bytes_the_partial_already_held()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        rig.SeedPart(Artifact, Artifact.Bytes[..30_000]);
        var events = new List<DownloadProgress>();

        var outcome = await rig.Create().Start(rig.Request("/a.bin", Artifact), events.Add).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.True(outcome.Succeeded);
        DownloadProgress[] seen;
        lock (events) seen = [.. events];
        Assert.True(seen[0].BytesDone >= 30_000);
        Assert.Equal(100_000, seen[^1].BytesDone);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_server_that_cannot_resume_is_started_over_from_zero()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { AcceptRanges = false });
        rig.SeedPart(Artifact, Artifact.Bytes[..30_000]);

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact));

        Assert.True(outcome.Succeeded);
        Assert.Equal((0L, true), (outcome.Files[0].ResumedFrom, outcome.Files[0].RestartedFromZero));
        Assert.Equal(Artifact.Bytes, await File.ReadAllBytesAsync(rig.Dest("a.bin")));
        var log = Assert.Single(await rig.Server.WaitForRequests(1));
        Assert.Equal((30_000L, 200, 100_000L), (log.RangeStart, log.Status, log.BodyBytesSent)); // asked for the tail, was sent the whole
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_partial_longer_than_the_pinned_size_is_thrown_away_and_the_file_fetched_whole()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        rig.SeedPart(Artifact, new byte[150_000]);

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact));

        Assert.True(outcome.Succeeded);
        var log = Assert.Single(await rig.Server.WaitForRequests(1));
        Assert.Null(log.RangeStart);
        Assert.Equal(Artifact.Bytes, await File.ReadAllBytesAsync(rig.Dest("a.bin")));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_complete_correct_partial_is_verified_and_moved_without_any_request()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        rig.SeedPart(Artifact, Artifact.Bytes);

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact));

        Assert.True(outcome.Succeeded);
        Assert.Equal(100_000L, outcome.Files[0].ResumedFrom);
        Assert.Equal(0, rig.Server.ConnectionCount);
        Assert.Equal(0, rig.Counting.Sent);
        Assert.Equal(Artifact.Bytes, await File.ReadAllBytesAsync(rig.Dest("a.bin")));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_complete_but_wrong_partial_is_discarded_and_the_file_fetched_whole_without_failing()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var wrong = (byte[])Artifact.Bytes.Clone();
        wrong[50_000] ^= 0xFF;
        rig.SeedPart(Artifact, wrong);

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact));

        Assert.True(outcome.Succeeded);
        var log = Assert.Single(await rig.Server.WaitForRequests(1));
        Assert.Equal((null, 200, 100_000L), (log.RangeStart, log.Status, log.BodyBytesSent));
        Assert.Equal(Artifact.Bytes, await File.ReadAllBytesAsync(rig.Dest("a.bin")));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_resume_that_ends_in_the_wrong_hash_restarts_from_zero_once_and_then_succeeds()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var corrupt = (byte[])Artifact.Bytes[..30_000].Clone();
        corrupt[10] ^= 0xFF; // a bad partial of the right length: the tail the server sends is right, the whole is wrong
        rig.SeedPart(Artifact, corrupt);

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact));

        Assert.True(outcome.Succeeded);
        Assert.Equal(Artifact.Bytes, await File.ReadAllBytesAsync(rig.Dest("a.bin")));
        var log = await rig.Server.WaitForRequests(2);
        Assert.Equal([30_000L, null], log.Select(r => r.RangeStart));
        Assert.Equal([206, 200], log.Select(r => r.Status));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_second_mismatch_after_the_silent_restart_is_a_failure_and_never_a_third_try()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { WrongByteAt = 70_000 });
        rig.SeedPart(Artifact, Artifact.Bytes[..30_000]);

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact));

        Assert.Equal(DownloadStatus.Failed, outcome.Status);
        Assert.Equal(DownloadFailureKind.Mismatch, outcome.Failure!.Kind);
        Assert.Equal(2, (await rig.Server.WaitForRequests(2)).Count);
        Assert.False(File.Exists(rig.PartPath(Artifact)));
        Assert.False(File.Exists(rig.Dest("a.bin")));
        await Task.Yield();
        Assert.Equal(2, rig.Server.Requests.Count);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_server_that_answers_416_to_the_resume_is_restarted_from_zero_and_cannot_loop()
    {
        await using var rig = new DownloadRig();
        var shorter = FakeArtifact.Pattern(50_000, seed: 9); // the server's file is shorter than the pin says
        rig.Server.Serve("/a.bin", shorter);
        var request = new DownloadRequest(rig.Server.Url("/a.bin"), rig.Dest("a.bin"), 100_000, Artifact.Sha256Hex);
        rig.SeedPart(Artifact, Artifact.Bytes[..60_000]);

        var outcome = await rig.Run(request);

        Assert.Equal(DownloadFailureKind.Mismatch, outcome.Failure!.Kind);
        var log = await rig.Server.WaitForRequests(2);
        Assert.Equal([416, 200], log.Select(r => r.Status));
        Assert.Equal([60_000L, null], log.Select(r => r.RangeStart));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Retry_delays_run_on_the_clock_and_each_retry_resumes_where_the_last_try_was_cut()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { DropAfterBytes = 40_000, FaultyRequests = 2 });
        var options = new DownloadOptions { RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)] };
        var job = rig.Create(options).Start(rig.Request("/a.bin", Artifact));

        await DownloadRig.Until(() => job.IsWaitingToRetry && job.AttemptsStarted == 1); // the 2 s wait is armed
        Assert.Single(rig.Server.Requests); // nothing is retried before the clock moves
        rig.Runtime.Advance(TimeSpan.FromSeconds(1.9));
        await Task.Yield();
        Assert.Single(rig.Server.Requests);
        Assert.Equal(1, job.AttemptsStarted);
        rig.Runtime.Advance(TimeSpan.FromSeconds(0.1));
        await DownloadRig.Until(() => job.IsWaitingToRetry && job.AttemptsStarted == 2); // now the 4 s wait
        rig.Runtime.Advance(TimeSpan.FromSeconds(3.9));
        await Task.Yield();
        Assert.Equal(2, job.AttemptsStarted);
        rig.Runtime.Advance(TimeSpan.FromSeconds(0.1));
        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.True(outcome.Succeeded);
        Assert.Equal([null, 40_000L, 80_000L], rig.Server.Requests.Select(r => r.RangeStart));
        Assert.Equal(100_000L, rig.Server.Requests.Sum(r => r.BodyBytesSent));
        Assert.Equal(Artifact.Bytes, await File.ReadAllBytesAsync(rig.Dest("a.bin")));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task When_the_retry_delays_run_out_the_download_fails_as_unreachable()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { DropAfterBytes = 1_000 });
        var options = new DownloadOptions { RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)] };
        var job = rig.Create(options).Start(rig.Request("/a.bin", Artifact));

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await DownloadRig.Until(() => job.IsWaitingToRetry && job.AttemptsStarted == attempt);
            rig.Runtime.Advance(TimeSpan.FromSeconds(1));
        }
        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(DownloadFailureKind.Unreachable, outcome.Failure!.Kind);
        Assert.Equal(3, rig.Server.Requests.Count);
        Assert.Equal(3_000, new FileInfo(rig.PartPath(Artifact)).Length);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_missing_file_is_not_retried_even_when_retries_are_allowed()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { Status = 404 });

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact), new DownloadOptions { RetryDelays = [TimeSpan.FromSeconds(1)] });

        Assert.Equal(DownloadFailureKind.NotFound, outcome.Failure!.Kind);
        Assert.Single(await rig.Server.WaitForRequests(1));
    }
}
