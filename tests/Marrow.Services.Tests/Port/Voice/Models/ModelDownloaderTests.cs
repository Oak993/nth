using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Voice.Models;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Voice.Models;

// VT-7 (voice-SPEC V-9.5, V-T43): model downloads through the shared downloader, against SK-0's fake server. The files are small synthetic ones with
// manifest-style rows; the size and hash they are checked against are the row's.
[Trait("Spec", "V-T43")]
public sealed class ModelDownloaderTests
{
    private static readonly FakeArtifact Tiny = FakeArtifact.Pattern(120_000, seed: 81);
    private static readonly FakeArtifact Base = FakeArtifact.Pattern(90_000, seed: 82);

    private static ModelDownloadSetup Make(DownloadRig rig, FakeBehavior? tinyBehavior = null, bool allowLoopback = true)
    {
        var tiny = VoiceModelFixtures.Model(Tiny, "tiny-q5_1");
        var baseFile = VoiceModelFixtures.Model(Base, "base-q5_1");
        rig.Server.Serve("/" + tiny.FileName, Tiny, tinyBehavior);
        rig.Server.Serve("/" + baseFile.FileName, Base);
        var store = new ModelStore(Path.Combine(rig.Root, "data"), () => null, rig.Runtime.Time);
        var downloader = new ModelDownloader(store, ModelDownloader.CreateDownloader(rig.PartFolder, allowLoopback ? ["127.0.0.1"] : null, rig.Counting),
            f => rig.Server.Url("/" + f.FileName));
        return new ModelDownloadSetup(rig, store, downloader, tiny, baseFile);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_full_download_installs_the_verified_file_records_it_and_reports_the_fraction()
    {
        await using var rig = new DownloadRig();
        var s = Make(rig);
        var fractions = new List<double>();
        var download = s.Downloader.Start(s.Tiny);
        download.FractionChanged += fractions.Add;

        var result = await download.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(ModelDownloadStatus.Installed, result.Status);
        Assert.Equal(Tiny.Bytes, await File.ReadAllBytesAsync(s.Store.PathFor(s.Tiny)));
        Assert.Equal(ModelInstallKind.Installed, s.Store.State(s.Tiny).Kind);
        Assert.Equal(s.Tiny.Id, Assert.Single(s.Store.Records).Id);
        Assert.Equal(1.0, download.Fraction);
        Assert.Empty(Directory.GetFiles(rig.PartFolder));
        var log = Assert.Single(await rig.Server.WaitForRequests(1));
        Assert.Equal((null, 200), (log.RangeStart, log.Status));
        Assert.Null(log.Header("Authorization")); // no credentials are sent (V-9.5 item 7)
        Assert.Equal(120_000, download.Job.Progress.BytesDone);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task Progress_is_at_most_ten_a_second_and_ends_at_one()
    {
        await using var rig = new DownloadRig();
        var s = Make(rig);
        var fractions = new List<double>();
        var download = s.Downloader.Start(s.Tiny);
        download.FractionChanged += f => { lock (fractions) fractions.Add(f); };

        await download.Completion.WaitAsync(FakeDownloadServer.Guard);

        double[] seen;
        lock (fractions) seen = [.. fractions];
        Assert.Equal(1.0, seen[^1]);
        Assert.True(seen.Length <= 2, "the clock never moved, so nothing may be reported twice: " + seen.Length);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_cut_stream_is_retried_after_2_s_on_the_clock_and_resumes_with_Range()
    {
        await using var rig = new DownloadRig();
        var s = Make(rig, new FakeBehavior { DropAfterBytes = 50_000, FaultyRequests = 1 });
        var download = s.Downloader.Start(s.Tiny);

        await DownloadRig.Until(() => download.Job.IsWaitingToRetry);
        Assert.Single(rig.Server.Requests);
        rig.Runtime.Advance(TimeSpan.FromSeconds(1.9));
        await Task.Yield();
        Assert.Single(rig.Server.Requests);
        rig.Runtime.Advance(TimeSpan.FromSeconds(0.1));
        var result = await download.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(ModelDownloadStatus.Installed, result.Status);
        Assert.Equal([null, 50_000L], rig.Server.Requests.Select(r => r.RangeStart));
        Assert.Equal(120_000L, rig.Server.Requests.Sum(r => r.BodyBytesSent));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task The_back_off_is_2_then_4_then_8_seconds_and_then_the_download_fails()
    {
        await using var rig = new DownloadRig();
        var s = Make(rig, new FakeBehavior { DropAfterBytes = 1_000 });
        var download = s.Downloader.Start(s.Tiny);

        foreach (var (attempt, wait) in new[] { (1, 2), (2, 4), (3, 8) })
        {
            await DownloadRig.Until(() => download.Job.IsWaitingToRetry && download.Job.AttemptsStarted == attempt);
            rig.Runtime.Advance(TimeSpan.FromSeconds(wait - 0.1));
            await Task.Yield();
            Assert.Equal(attempt, download.Job.AttemptsStarted);
            rig.Runtime.Advance(TimeSpan.FromSeconds(0.1));
        }
        var result = await download.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(ModelDownloadStatus.Failed, result.Status);
        Assert.Equal(DownloadFailureKind.Unreachable, result.Failure!.Kind);
        Assert.Equal(4, rig.Server.Requests.Count);
        Assert.Equal(ModelInstallKind.NotInstalled, s.Store.State(s.Tiny).Kind);
        Assert.Equal(4_000, new FileInfo(Path.Combine(rig.PartFolder, Tiny.Sha256Hex[..8] + ".part")).Length); // the partial stays for the next try
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_server_that_cannot_resume_restarts_the_download_from_zero()
    {
        await using var rig = new DownloadRig();
        var s = Make(rig, new FakeBehavior { AcceptRanges = false });
        Directory.CreateDirectory(rig.PartFolder);
        await File.WriteAllBytesAsync(Path.Combine(rig.PartFolder, Tiny.Sha256Hex[..8] + ".part"), Tiny.Bytes[..30_000]);

        var result = await s.Downloader.Start(s.Tiny).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(ModelDownloadStatus.Installed, result.Status);
        var log = Assert.Single(await rig.Server.WaitForRequests(1));
        Assert.Equal((30_000L, 200, 120_000L), (log.RangeStart, log.Status, log.BodyBytesSent));
        Assert.Equal(Tiny.Bytes, await File.ReadAllBytesAsync(s.Store.PathFor(s.Tiny)));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_full_length_partial_goes_straight_to_verification_with_no_request()
    {
        await using var rig = new DownloadRig();
        var s = Make(rig);
        Directory.CreateDirectory(rig.PartFolder);
        await File.WriteAllBytesAsync(Path.Combine(rig.PartFolder, Tiny.Sha256Hex[..8] + ".part"), Tiny.Bytes);

        var result = await s.Downloader.Start(s.Tiny).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(ModelDownloadStatus.Installed, result.Status);
        Assert.Equal(0, rig.Counting.Sent);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task Wrong_bytes_are_Corrupt_nothing_is_installed_and_the_file_is_never_there_to_load()
    {
        await using var rig = new DownloadRig();
        var s = Make(rig, new FakeBehavior { WrongByteAt = 777 });

        var result = await s.Downloader.Start(s.Tiny).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(ModelDownloadStatus.Corrupt, result.Status);
        Assert.Equal(DownloadFailureKind.Mismatch, result.Failure!.Kind);
        Assert.False(File.Exists(s.Store.PathFor(s.Tiny)));
        Assert.Equal(ModelInstallKind.NotInstalled, s.Store.State(s.Tiny).Kind);
        Assert.Empty(s.Store.Records);
        Assert.Equal(ModelVerification.Missing, await s.Store.VerifyForLoad(s.Tiny, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(rig.PartFolder));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task Cancel_keeps_the_partial_and_the_next_download_resumes_it()
    {
        await using var rig = new DownloadRig();
        var s = Make(rig, new FakeBehavior { Trickle = new FakeTrickle(40_000, TimeSpan.FromMinutes(1)) });
        var download = s.Downloader.Start(s.Tiny);
        await DownloadRig.Until(() => download.Job.BytesReceived >= 40_000);
        Assert.Equal(ModelInstallKind.Downloading, s.Store.State(s.Tiny).Kind);
        Assert.InRange(s.Store.State(s.Tiny).Fraction, 0.3, 0.4);

        download.Cancel();
        var cancelled = await download.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(ModelDownloadStatus.Cancelled, cancelled.Status);
        Assert.Equal(ModelInstallKind.NotInstalled, s.Store.State(s.Tiny).Kind);
        Assert.Equal(40_000, new FileInfo(Path.Combine(rig.PartFolder, Tiny.Sha256Hex[..8] + ".part")).Length);
        rig.Server.Behave("/" + s.Tiny.FileName, new FakeBehavior());
        var again = await s.Downloader.Start(s.Tiny).Completion.WaitAsync(FakeDownloadServer.Guard);
        Assert.Equal(ModelDownloadStatus.Installed, again.Status);
        await DownloadRig.Until(() => rig.Server.Requests.Any(r => r.RangeStart == 40_000));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task Several_downloads_run_at_once_one_progress_each_and_one_start_per_file()
    {
        await using var rig = new DownloadRig();
        var s = Make(rig);
        var hold = rig.Server.HoldRequests();

        var a = s.Downloader.Start(s.Tiny);
        var b = s.Downloader.Start(s.Base);
        Assert.Same(a, s.Downloader.Start(s.Tiny)); // a second press while it runs is the same download
        Assert.Same(a, s.Downloader.Active(s.Tiny));
        await hold.WaitForArrivals(2);
        Assert.Equal(2, rig.Server.PeakConcurrentRequests);
        hold.Release();
        var results = await Task.WhenAll(a.Completion, b.Completion).WaitAsync(FakeDownloadServer.Guard);

        Assert.All(results, r => Assert.Equal(ModelDownloadStatus.Installed, r.Status));
        Assert.Equal(2, s.Store.Records.Count);
        Assert.Null(s.Downloader.Active(s.Tiny));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task Only_huggingface_is_on_the_models_list_and_the_manifest_urls_are_on_it()
    {
        Assert.Equal(["huggingface.co"], ModelDownloader.AllowedHosts);
        Assert.True(new DownloadPolicy(ModelDownloader.AllowedHosts).AllowsFirstUrl(new Uri(ModelCatalog.Files[0].Url)));
        await using var rig = new DownloadRig();
        var s = Make(rig, allowLoopback: false); // the default list refuses the loopback server before any connection

        var result = await s.Downloader.Start(s.Tiny).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(ModelDownloadStatus.Failed, result.Status);
        Assert.Equal(DownloadFailureKind.Refused, result.Failure!.Kind);
        Assert.Equal(0, rig.Counting.Sent);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_download_that_ends_at_once_never_leaves_its_row_downloading()
    {
        // Review (ED-305 (8)): a refused download ends before Start has returned; its end must not be overtaken by Start's own bookkeeping.
        await using var rig = new DownloadRig();
        var s = Make(rig, allowLoopback: false);

        for (var n = 0; n < 300; n++)
        {
            var result = await s.Downloader.Start(s.Tiny).Completion.WaitAsync(FakeDownloadServer.Guard);
            Assert.Equal(ModelDownloadStatus.Failed, result.Status);
            Assert.Equal(ModelInstallKind.NotInstalled, s.Store.State(s.Tiny).Kind);
            Assert.Null(s.Downloader.Active(s.Tiny));
        }
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_verified_download_whose_installed_json_cannot_be_written_still_ends_installed_and_gains_no_trust_on_disk()
    {
        // WINDOWS-ONLY in effect (ED-305 (8)): a reader holding installed.json (another store, a scanner, a backup tool) makes Windows refuse
        // the rename that replaces it; elsewhere the rename succeeds and the test passes trivially.
        await using var rig = new DownloadRig();
        var s = Make(rig);
        Assert.Equal(ModelDownloadStatus.Installed, (await s.Downloader.Start(s.Base).Completion.WaitAsync(FakeDownloadServer.Guard)).Status);

        ModelDownloadResult result;
        using (new FileStream(s.Store.InstalledJsonPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            result = await s.Downloader.Start(s.Tiny).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(ModelDownloadStatus.Installed, result.Status);
        Assert.Equal(ModelInstallKind.Installed, s.Store.State(s.Tiny).Kind);
        Assert.Null(s.Downloader.Active(s.Tiny));
        Assert.Equal(Tiny.Bytes, await File.ReadAllBytesAsync(s.Store.PathFor(s.Tiny)));
        var reopened = new ModelStore(Path.Combine(rig.Root, "data"), () => null, rig.Runtime.Time);
        // The record that could not be written is not on disk, so the next process hashes the file before trusting it.
        Assert.Equal(OperatingSystem.IsWindows() ? ModelVerification.Rehashed : ModelVerification.Trusted, await reopened.VerifyForLoad(s.Tiny, CancellationToken.None));
    }
}
