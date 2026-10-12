using Marrow.Core.Port;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Downloads;

// Web surfaces W5 (DESIGN 3.10 "downloads (DownloadJob)"; ED-NEW-web-w5-1): the app's downloader notes every job it starts, so the
// Background tasks pane lists the downloads of skills, tools and voice models without each caller reporting them. The registry
// only watches: it holds a job until ten minutes after it ended, and never more than 64.
[Trait("Decision", "ED-NEW-web-w5-1")]
public sealed class DownloadJobRegistryTests
{
    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(3_000, seed: 5);

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    public async Task The_downloader_notes_each_job_it_starts_with_the_file_name_and_when_it_ended()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/model.gguf", Artifact, new FakeBehavior());
        var started = SwiftClock.Provider.Current.GetUtcNow();
        var job = rig.Create().Start(rig.Request("/model.gguf", Artifact));

        var entry = Assert.Single(DownloadJobRegistry.Shared.Snapshot(started), e => ReferenceEquals(e.Job, job));
        Assert.Equal("model.gguf", entry.Title);
        Assert.Equal(started, entry.StartedAt);
        Assert.NotEqual(Guid.Empty, entry.Id);

        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);
        Assert.True(outcome.Succeeded);
        await DownloadRig.Until(() => entry.FinishedAt is not null);
        Assert.Equal(DownloadJobState.Succeeded, entry.Job.State);
    }

    [Fact]
    public void A_set_is_titled_by_its_first_file_and_how_many_more()
    {
        DownloadRequest Req(string file) => new(new Uri("https://example.test/" + file), "C:/out/" + file, 10, new string('a', 64), null);
        Assert.Equal("a.bin", DownloadJobRegistry.TitleOf([Req("a.bin")]));
        Assert.Equal("a.bin and 2 more", DownloadJobRegistry.TitleOf([Req("a.bin"), Req("b.bin"), Req("c.bin")]));
        Assert.Equal("Download", DownloadJobRegistry.TitleOf([]));
        Assert.Equal("example.test", DownloadJobRegistry.TitleOf([new DownloadRequest(new Uri("https://example.test/x"), "C:/out/", 10, new string('a', 64), null)]));
        Assert.Equal("libreoffice.org", DownloadJobRegistry.TitleOf([new DownloadRequest(new Uri("https://example.test/x"), "", 10, new string('a', 64), "libreoffice.org")]));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    public async Task An_ended_job_is_forgotten_eleven_minutes_after_it_ended_and_the_registry_never_holds_more_than_64()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior());
        var job = rig.Create().Start(rig.Request("/a.bin", Artifact));
        await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        var clock = new ManualTimeProvider();
        var registry = new DownloadJobRegistry();
        var entry = registry.Track(job, [rig.Request("/a.bin", Artifact)], clock);
        await DownloadRig.Until(() => entry.FinishedAt is not null);
        Assert.Equal(clock.GetUtcNow(), entry.FinishedAt!.Value);

        Assert.Single(registry.Snapshot(clock.GetUtcNow()));
        clock.Advance(DownloadJobRegistry.Keep - TimeSpan.FromSeconds(1));
        Assert.Single(registry.Snapshot(clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(registry.Snapshot(clock.GetUtcNow()));

        for (var i = 0; i < 90; i++) registry.Track(job, [rig.Request("/a.bin", Artifact)], clock);
        await DownloadRig.Until(() => registry.Snapshot(clock.GetUtcNow()).All(e => e.FinishedAt is not null));
        Assert.True(registry.Snapshot(clock.GetUtcNow()).Count <= 64);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    public async Task A_running_job_is_kept_however_long_it_runs()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/slow.bin", Artifact, new FakeBehavior { Trickle = new FakeTrickle(1_000, TimeSpan.FromMinutes(1)) });
        var job = rig.Create().Start(rig.Request("/slow.bin", Artifact));
        await DownloadRig.Until(() => job.BytesReceived >= 1_000);
        var clock = new ManualTimeProvider();
        var registry = new DownloadJobRegistry();
        var entry = registry.Track(job, [rig.Request("/slow.bin", Artifact)], clock);
        clock.Advance(TimeSpan.FromHours(3));
        Assert.Same(entry, Assert.Single(registry.Snapshot(clock.GetUtcNow())));
        Assert.Null(entry.FinishedAt);
        job.Cancel();
        await job.Completion.WaitAsync(FakeDownloadServer.Guard);
    }
}
