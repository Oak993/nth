using Marrow.Services.Port.Downloads;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Downloads;

// SK-3 (DL-3: "progress at least every 100 ms on a slow trickle"; section 3.2 "about ten times a second"; voice-SPEC V-9.5 item 2 "at most 10 per
// second"). The server trickles 500 bytes every 30 ms of ITS clock; the downloader's clock is moved in step, client first, so every chunk arrives at a
// known client time. Reading the rule both ways: no two reports closer than 100 ms, and a received byte is reported within 100 ms of arriving.
[Trait("Spec", "DL-3")]
public sealed class DownloaderProgressTests
{
    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(10_000, seed: 51);

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task On_a_slow_trickle_reports_come_at_most_every_100_ms_and_no_byte_waits_longer_than_100_ms_to_be_reported()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { Trickle = new FakeTrickle(500, TimeSpan.FromMilliseconds(30)) });
        var reports = new List<(TimeSpan At, DownloadProgress Progress)>();
        var job = rig.Create().Start(rig.Request("/a.bin", Artifact), p =>
        {
            lock (reports) reports.Add((rig.Runtime.Time.Elapsed, p));
        });
        var arrivals = new List<(TimeSpan At, long Bytes)>();

        for (var chunk = 1; chunk <= 20; chunk++)
        {
            await DownloadRig.Until(() => job.BytesReceived >= 500L * chunk);
            arrivals.Add((rig.Runtime.Time.Elapsed, 500L * chunk));
            if (chunk == 20) break;
            await DownloadRig.Until(() => rig.ServerClock.ActiveTimers > 0);
            rig.Runtime.Advance(TimeSpan.FromMilliseconds(30)); // the downloader's clock first,
            rig.ServerClock.Advance(TimeSpan.FromMilliseconds(30)); // then the server's: the next chunk is sent
        }
        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.True(outcome.Succeeded);
        (TimeSpan At, DownloadProgress Progress)[] seen;
        lock (reports) seen = [.. reports];
        Assert.Equal(TimeSpan.FromMilliseconds(570), arrivals[^1].At);
        // At most one report per 100 ms, except that the last report (the end) may follow a report closer than that.
        for (var i = 1; i < seen.Length - 1; i++)
            Assert.True(seen[i].At - seen[i - 1].At >= TimeSpan.FromMilliseconds(100), $"reports {i - 1} and {i} are {(seen[i].At - seen[i - 1].At).TotalMilliseconds} ms apart");
        // Every byte is reported within 100 ms of arriving.
        foreach (var (at, bytes) in arrivals)
        {
            var report = seen.First(r => r.Progress.BytesDone >= bytes);
            Assert.True(report.At - at <= TimeSpan.FromMilliseconds(100), $"{bytes} bytes arrived at {at.TotalMilliseconds} ms and were reported at {report.At.TotalMilliseconds} ms");
        }
        Assert.Equal(10_000, seen[^1].Progress.BytesDone);
        Assert.True(seen.Length >= 6, "a 570 ms trickle must produce a report about every 100 ms, not just the first and the last");
        Assert.True(seen.Length <= 8, "and not more than ten a second: " + seen.Length);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task The_progress_interval_is_an_option()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { Trickle = new FakeTrickle(500, TimeSpan.FromMilliseconds(30)) });
        var count = 0;
        var job = rig.Create(new DownloadOptions { ProgressInterval = TimeSpan.FromMilliseconds(300) }).Start(rig.Request("/a.bin", Artifact), _ => Interlocked.Increment(ref count));

        for (var chunk = 1; chunk <= 20; chunk++)
        {
            await DownloadRig.Until(() => job.BytesReceived >= 500L * chunk);
            if (chunk == 20) break;
            await DownloadRig.Until(() => rig.ServerClock.ActiveTimers > 0);
            rig.Runtime.Advance(TimeSpan.FromMilliseconds(30));
            rig.ServerClock.Advance(TimeSpan.FromMilliseconds(30));
        }
        await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.InRange(count, 2, 4); // 0, 300, then the end (570 ms)
    }
}
