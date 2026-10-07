using Marrow.Services.Port.Downloads;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Downloads;

// SK-3 (DL-3 of skills-ONDEMAND-PLAN.md section 7, sections 3.2 and 3.3): a whole file, fetched, hashed while it streams, verified against the pin,
// moved into place only then, with progress. Against SK-0's fake server; the request log is what proves what crossed the wire.
[Trait("Spec", "DL-3")]
public sealed class DownloaderFullTests
{
    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(100_000, seed: 3);

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_full_download_lands_verified_at_its_destination_with_no_partial_left_behind()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var request = rig.Request("/a.bin", Artifact);

        var outcome = await rig.Run(request);

        Assert.Equal(DownloadStatus.Succeeded, outcome.Status);
        Assert.Null(outcome.Failure);
        var file = Assert.Single(outcome.Files);
        Assert.Equal((request.DestinationPath, 100_000L, Artifact.Sha256Hex, "127.0.0.1", 0L, false),
            (file.DestinationPath, file.Size, file.Sha256, file.FinalHost, file.ResumedFrom, file.RestartedFromZero));
        Assert.Equal(Artifact.Bytes, await File.ReadAllBytesAsync(request.DestinationPath));
        Assert.Empty(Directory.GetFiles(rig.PartFolder));
        var log = Assert.Single(await rig.Server.WaitForRequests(1));
        Assert.Equal((null, 200, 100_000L), (log.RangeStart, log.Status, log.BodyBytesSent));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_destination_deeper_than_260_characters_is_created_and_written()
    {
        // Windows review: skill sets land under store\<source>@<pin8>\<skill>\..., which can pass the old 260-character limit.
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var deep = Path.Combine([rig.Root, "out", .. Enumerable.Range(0, 12).Select(n => $"folder-{n:D2}-of-a-deep-skill-tree"), "a.bin"]);
        Assert.True(deep.Length > 300, "the path is " + deep.Length + " characters");

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact) with { DestinationPath = deep });

        Assert.True(outcome.Succeeded, outcome.Failure?.Message);
        Assert.Equal(Artifact.Bytes, await File.ReadAllBytesAsync(deep));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task The_partial_file_is_named_for_the_first_eight_digits_of_the_pinned_hash_and_the_destination_appears_only_when_verified()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { Trickle = new FakeTrickle(20_000, TimeSpan.FromSeconds(1)) });
        var request = rig.Request("/a.bin", Artifact);
        var job = rig.Create().Start(request);

        await DownloadRig.Until(() => job.Progress.BytesDone >= 20_000);

        Assert.True(File.Exists(Path.Combine(rig.PartFolder, Artifact.Sha256Hex[..8] + ".part")));
        Assert.False(File.Exists(request.DestinationPath));
        Assert.Equal(DownloadJobState.Running, job.State);
        job.Cancel();
        await job.Completion.WaitAsync(FakeDownloadServer.Guard);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Progress_counts_bytes_over_the_pinned_size_is_throttled_and_ends_at_the_total()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var events = new List<DownloadProgress>();
        var job = rig.Create().Start(rig.Request("/a.bin", Artifact), events.Add);

        var outcome = await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.True(outcome.Succeeded);
        DownloadProgress[] seen;
        lock (events) seen = [.. events];
        Assert.Equal(new DownloadProgress(100_000, 100_000, 1, 1), seen[^1]);
        Assert.Equal(new DownloadProgress(100_000, 100_000, 1, 1), job.Progress);
        Assert.All(seen, p => Assert.Equal(100_000, p.BytesTotal));
        Assert.Equal(seen.Select(p => p.BytesDone).Order(), seen.Select(p => p.BytesDone));
        // The clock is manual and never moved: nothing may be reported twice within one 100 ms, so the first chunk's report, then the final one.
        Assert.True(seen.Length <= 2, "progress was not throttled: " + seen.Length + " events");
        Assert.Equal(DownloadJobState.Succeeded, job.State);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task The_job_event_carries_the_same_numbers_as_the_callback()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var viaCallback = new List<DownloadProgress>();
        var viaEvent = new List<DownloadProgress>();
        var job = rig.Create().Start(rig.Request("/a.bin", Artifact), viaCallback.Add);
        job.ProgressChanged += viaEvent.Add;

        await job.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(new DownloadProgress(100_000, 100_000, 1, 1), viaCallback[^1]);
        Assert.Equal(new DownloadProgress(100_000, 100_000, 1, 1), viaEvent[^1]);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_missing_destination_folder_is_created_and_an_older_file_there_is_replaced_only_on_success()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var request = rig.Request("/a.bin", Artifact, destName: "deep/er/a.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationPath)!);
        await File.WriteAllTextAsync(request.DestinationPath, "older");

        var outcome = await rig.Run(request);

        Assert.True(outcome.Succeeded);
        Assert.Equal(Artifact.Bytes, await File.ReadAllBytesAsync(request.DestinationPath));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_destination_another_handle_holds_is_a_disk_error_that_keeps_the_verified_partial_for_the_next_run()
    {
        // WINDOWS-ONLY in effect (Windows review): Windows will not replace a file a reader holds without delete sharing (a model being read, a
        // scanner); elsewhere the replace succeeds at once. The verified bytes are not lost: the next run moves them with no request.
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var request = rig.Request("/a.bin", Artifact);
        Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationPath)!);
        await File.WriteAllTextAsync(request.DestinationPath, "older");

        DownloadOutcome held;
        using (new FileStream(request.DestinationPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            held = await rig.Run(request);
        var next = await rig.Run(request);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal((DownloadFailureKind.DiskError, DownloadMessages.DiskError), (held.Failure!.Kind, held.Failure.Message));
            Assert.Equal(1, rig.Counting.Sent); // the next run found the complete partial and verified it where it lay
            Assert.Equal(Artifact.Length, Assert.Single(next.Files).ResumedFrom);
        }
        Assert.True(next.Succeeded);
        Assert.Equal(Artifact.Bytes, await File.ReadAllBytesAsync(request.DestinationPath));
        Assert.Empty(Directory.GetFiles(rig.PartFolder));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task The_pinned_hash_is_accepted_in_either_case()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var request = rig.Request("/a.bin", Artifact) with { Sha256 = Artifact.Sha256Hex.ToUpperInvariant() };

        var outcome = await rig.Run(request);

        Assert.True(outcome.Succeeded);
        Assert.Equal(Artifact.Sha256Hex, Assert.Single(outcome.Files).Sha256);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task No_credential_or_cookie_is_sent_and_the_request_names_a_user_agent_free_plain_get()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);

        await rig.Run(rig.Request("/a.bin", Artifact));

        var log = Assert.Single(await rig.Server.WaitForRequests(1));
        Assert.Equal("GET", log.Method);
        Assert.Null(log.Header("Authorization"));
        Assert.Null(log.Header("Cookie"));
    }
}
