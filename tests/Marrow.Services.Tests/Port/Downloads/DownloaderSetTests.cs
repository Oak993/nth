using Marrow.Services.Port.Downloads;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Downloads;

// SK-3 (DL-3, section 3.2 "Skill sets"): a set is fetched file by file, six in flight, every file hashed on its own; one bad file fails the set.
[Trait("Spec", "DL-3")]
public sealed class DownloaderSetTests
{
    private static FakeArtifact Artifact(int n) => FakeArtifact.Pattern(3_000 + n, seed: 100 + n);

    private static List<(string Path, FakeArtifact Artifact)> Serve(DownloadRig rig, int count, Func<int, FakeBehavior?>? behave = null)
    {
        var files = new List<(string, FakeArtifact)>();
        for (var n = 0; n < count; n++)
        {
            var artifact = Artifact(n);
            var path = $"/skills/s{n:D2}/SKILL.md";
            rig.Server.Serve(path, artifact, behave?.Invoke(n));
            files.Add((path, artifact));
        }
        return files;
    }

    private static List<DownloadRequest> Requests(DownloadRig rig, List<(string Path, FakeArtifact Artifact)> files) =>
        [.. files.Select((f, n) => rig.Request(f.Path, f.Artifact, destName: $"s{n:D2}.md"))];

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_set_downloads_every_file_and_reports_one_progress_over_all_of_them()
    {
        await using var rig = new DownloadRig();
        var files = Serve(rig, 10);
        var events = new List<DownloadProgress>();
        var job = rig.Create().StartSet(Requests(rig, files), events.Add);

        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.True(outcome.Succeeded);
        Assert.Equal(10, outcome.Files.Count);
        Assert.Equal(files.Select((_, n) => rig.Dest($"s{n:D2}.md")), outcome.Files.Select(f => f.DestinationPath)); // in request order
        for (var n = 0; n < 10; n++) Assert.Equal(files[n].Artifact.Bytes, await System.IO.File.ReadAllBytesAsync(rig.Dest($"s{n:D2}.md")));
        var total = files.Sum(f => f.Artifact.Length);
        DownloadProgress[] seen;
        lock (events) seen = [.. events];
        Assert.Equal(new DownloadProgress(total, total, 10, 10), seen[^1]);
        Assert.All(seen, p => Assert.Equal((total, 10), (p.BytesTotal, p.FilesTotal)));
        Assert.Empty(Directory.GetFiles(rig.PartFolder));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Six_files_are_in_flight_at_once_and_no_seventh_starts_until_one_finishes()
    {
        await using var rig = new DownloadRig();
        var files = Serve(rig, 14);
        var hold = rig.Server.HoldRequests();
        var job = rig.Create().StartSet(Requests(rig, files));

        await hold.WaitForArrivals(6);
        // A seventh would arrive within moments if the downloader allowed it; this bounded real-time look is the only wall-clock wait in the unit.
        await Assert.ThrowsAsync<TimeoutException>(() => hold.WaitForArrivals(7).WaitAsync(TimeSpan.FromMilliseconds(400)));
        Assert.Equal(6, rig.Server.PeakConcurrentRequests);
        hold.Release();
        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.True(outcome.Succeeded);
        Assert.Equal(6, rig.Server.PeakConcurrentRequests);
        Assert.Equal(14, rig.Server.Requests.Count);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task The_number_in_flight_is_an_option()
    {
        await using var rig = new DownloadRig();
        var files = Serve(rig, 6);
        var hold = rig.Server.HoldRequests();
        var job = rig.Create(new DownloadOptions { MaxInFlight = 2 }).StartSet(Requests(rig, files));

        await hold.WaitForArrivals(2);
        await Assert.ThrowsAsync<TimeoutException>(() => hold.WaitForArrivals(3).WaitAsync(TimeSpan.FromMilliseconds(400)));
        hold.Release();
        await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(2, rig.Server.PeakConcurrentRequests);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task One_bad_file_fails_the_set_stops_the_file_in_flight_and_starts_no_further_file()
    {
        await using var rig = new DownloadRig();
        var files = Serve(rig, 5, n => n switch
        {
            0 => new FakeBehavior { Trickle = new FakeTrickle(1_000, TimeSpan.FromMinutes(5)) }, // stays in flight
            1 => new FakeBehavior { WrongByteAt = 100 },
            _ => null,
        });
        var requests = Requests(rig, files);
        var job = rig.Create(new DownloadOptions { MaxInFlight = 2 }).StartSet(requests);

        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(DownloadStatus.Failed, outcome.Status);
        Assert.Equal(DownloadFailureKind.Mismatch, outcome.Failure!.Kind);
        Assert.Equal(requests[1].DestinationPath, outcome.Failure.DestinationPath);
        Assert.Equal(DownloadJobState.Failed, job.State);
        Assert.Empty(outcome.Files);
        Assert.False(System.IO.File.Exists(requests[1].DestinationPath));
        string[] asked;
        lock (rig.Counting.Urls) asked = [.. rig.Counting.Urls.Select(u => new Uri(u).AbsolutePath).Order()];
        Assert.Equal(["/skills/s00/SKILL.md", "/skills/s01/SKILL.md"], asked); // files 2 to 4 were never asked for
        Assert.False(System.IO.File.Exists(rig.PartPath(files[1].Artifact))); // the bad one's partial is gone
        Assert.True(System.IO.File.Exists(rig.PartPath(files[0].Artifact))); // the stopped one keeps its bytes for Retry
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Files_finished_before_the_failure_stay_where_they_are()
    {
        await using var rig = new DownloadRig();
        var files = Serve(rig, 4, n => n == 2 ? new FakeBehavior { WrongByteAt = 5 } : null);
        var requests = Requests(rig, files);

        var outcome = await rig.Create(new DownloadOptions { MaxInFlight = 1 }).StartSet(requests).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(DownloadFailureKind.Mismatch, outcome.Failure!.Kind);
        Assert.True(System.IO.File.Exists(requests[0].DestinationPath));
        Assert.True(System.IO.File.Exists(requests[1].DestinationPath));
        Assert.False(System.IO.File.Exists(requests[2].DestinationPath));
        Assert.False(System.IO.File.Exists(requests[3].DestinationPath));
        Assert.Equal(3, rig.Counting.Sent);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_retry_of_a_failed_set_fetches_only_what_is_missing_when_the_finished_files_are_left_out()
    {
        await using var rig = new DownloadRig();
        var files = Serve(rig, 3, n => n == 1 ? new FakeBehavior { WrongByteAt = 5, FaultyRequests = 1 } : null);
        var requests = Requests(rig, files);
        var downloader = rig.Create(new DownloadOptions { MaxInFlight = 1 });
        var first = await downloader.StartSet(requests).Completion.WaitAsync(FakeDownloadServer.Guard);

        var again = await downloader.StartSet(requests.Where(r => !System.IO.File.Exists(r.DestinationPath)).ToList()).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.False(first.Succeeded);
        Assert.True(again.Succeeded);
        Assert.Equal(2, again.Files.Count);
        Assert.All(requests, r => Assert.True(System.IO.File.Exists(r.DestinationPath)));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Cancelling_a_set_stops_every_file_and_keeps_the_partials()
    {
        await using var rig = new DownloadRig();
        var files = Serve(rig, 4, _ => new FakeBehavior { Trickle = new FakeTrickle(1_000, TimeSpan.FromMinutes(5)) });
        var job = rig.Create().StartSet(Requests(rig, files));
        await DownloadRig.Until(() => job.BytesReceived >= 4_000);

        job.Cancel();
        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(DownloadStatus.Cancelled, outcome.Status);
        foreach (var f in files) Assert.Equal(1_000, new FileInfo(rig.PartPath(f.Artifact)).Length);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Pausing_a_set_holds_every_file_and_resuming_finishes_them_all()
    {
        await using var rig = new DownloadRig();
        var files = Serve(rig, 3, _ => new FakeBehavior { Trickle = new FakeTrickle(1_000, TimeSpan.FromMinutes(5)) });
        var job = rig.Create().StartSet(Requests(rig, files));
        await DownloadRig.Until(() => job.BytesReceived >= 3_000);

        job.Pause();
        Assert.Equal(DownloadJobState.Paused, job.State);
        for (var n = 0; n < 3; n++)
            await DownloadRig.Until(() => System.IO.File.Exists(rig.PartPath(files[n].Artifact)) && new FileInfo(rig.PartPath(files[n].Artifact)).Length == 1_000);
        for (var n = 0; n < 3; n++) rig.Server.Behave(files[n].Path, new FakeBehavior());
        job.Resume();
        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.True(outcome.Succeeded);
        Assert.Equal(3, outcome.Files.Count);
        Assert.All(outcome.Files, f => Assert.Equal(1_000L, f.ResumedFrom));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Two_files_of_a_set_with_the_same_bytes_both_land()
    {
        // Windows review (ED-304 (15)): a skill set often holds byte-identical files (the same LICENSE.txt in several skills, empty
        // __init__.py files). They share a pinned hash and so one <sha8>.part; each trickles until the server's clock moves, so a second
        // writer meets the first one's open partial (on Windows it cannot even open it).
        await using var rig = new DownloadRig();
        var same = Artifact(0);
        var slow = new FakeBehavior { Trickle = new FakeTrickle(1_000, TimeSpan.FromSeconds(1)) };
        rig.Server.Serve("/skills/a/LICENSE.txt", same, slow);
        rig.Server.Serve("/skills/b/LICENSE.txt", same, slow);
        var job = rig.Create().StartSet([rig.Request("/skills/a/LICENSE.txt", same, destName: "a.txt"), rig.Request("/skills/b/LICENSE.txt", same, destName: "b.txt")]);

        using (var stop = new CancellationTokenSource(FakeDownloadServer.Guard))
            while (!job.Completion.IsCompleted)
            {
                await DownloadRig.Until(() => rig.ServerClock.ActiveTimers > 0 || job.Completion.IsCompleted);
                rig.ServerClock.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(1, stop.Token);
            }
        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.True(outcome.Succeeded, outcome.Failure?.Message);
        Assert.Equal(same.Bytes, await System.IO.File.ReadAllBytesAsync(rig.Dest("a.txt")));
        Assert.Equal(same.Bytes, await System.IO.File.ReadAllBytesAsync(rig.Dest("b.txt")));
        Assert.Empty(Directory.GetFiles(rig.PartFolder));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task An_empty_set_succeeds_at_once_and_touches_nothing()
    {
        await using var rig = new DownloadRig();

        var outcome = await rig.Create().StartSet([]).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Files);
        Assert.Equal(0, rig.Counting.Sent);
    }
}
