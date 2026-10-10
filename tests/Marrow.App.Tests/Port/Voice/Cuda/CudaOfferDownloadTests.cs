using Marrow.App.Port.Voice.Cuda;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Port.Voice.Models;
using Marrow.Testing.Downloads;

namespace Marrow.App.Tests.Port.Voice.Cuda;

// VT-17 (OD-0106 CUDA A; OD-0099 Q-V-9 C; OD-0104): the offer's download goes through VT-7's pack installer on the shared downloader, against SK-0's loopback fake server (no real
// network). The companions seam is scripted. Progress, cancel and every end state are checked on the row the card draws.
public sealed class CudaOfferDownloadTests
{
    private static async Task<CudaOfferRig> Shown(ScriptedCudaCompanions? companions = null, bool wrongSha512 = false, string? packBody = "serve")
    {
        var rig = new CudaOfferRig(companions: companions, wrongSha512: wrongSha512, packBody: packBody);
        await rig.Offer.EvaluateAsync(CancellationToken.None);
        Assert.Equal(CudaOfferState.Offer, rig.Offer.Row.State);
        return rig;
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C; Oracle OD-0106
    public async Task A_download_fetches_the_pack_installs_the_libraries_and_ends_Installed()
    {
        await using var rig = await Shown();

        await rig.Offer.StartAsync().WaitAsync(FakeDownloadServer.Guard);

        var folder = rig.Installer.FolderFor(rig.Pin);
        Assert.Equal(CudaOfferState.Installed, rig.Offer.Row.State);
        Assert.Equal("Installed. Restart Nth to use it", rig.Offer.Row.Detail);
        Assert.True(rig.Installer.IsInstalled(rig.Pin));
        Assert.Equal([folder], rig.Companions.Installs); // the libraries go beside the pack's files
        Assert.True(rig.Companions.IsInstalled(folder));
        Assert.Single(rig.Server.Requests); // one request to the fake server: the pack
        var states = rig.Rows.Select(r => r.State).Distinct().ToList();
        Assert.Equal([CudaOfferState.Offer, CudaOfferState.Downloading, CudaOfferState.Installed], states);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C; progress
    public async Task Progress_runs_from_0_to_1_in_order_and_counts_the_libraries_as_their_share_of_the_size()
    {
        var companions = new ScriptedCudaCompanions(bytes: 3_000, steps: [0.5, 1.0]);
        await using var rig = await Shown(companions);

        await rig.Offer.StartAsync().WaitAsync(FakeDownloadServer.Guard);

        var progress = rig.Rows.Where(r => r.State == CudaOfferState.Downloading).Select(r => r.Progress).ToList();
        Assert.NotEmpty(progress);
        Assert.Equal(progress.Order(), progress); // never back
        Assert.All(progress, p => Assert.InRange(p, 0.0, 1.0));
        var pack = (double)rig.Pin.Bytes;
        // while the libraries install, the share done is the pack's whole size plus the libraries' part
        Assert.Contains(progress, p => Math.Abs(p - (pack + 1_500) / (pack + 3_000)) < 1e-9);
        Assert.Contains(progress, p => Math.Abs(p - 1.0) < 1e-9);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C; progress
    public async Task While_the_pack_is_on_its_way_the_row_shows_the_bytes_received_so_far()
    {
        var rig = new CudaOfferRig();
        rig.Server.Serve("/pack.nupkg", rig.Nupkg, new FakeBehavior { Trickle = new FakeTrickle(1_000, TimeSpan.FromMinutes(1)) });
        await using var _ = rig;
        await rig.Offer.EvaluateAsync(CancellationToken.None);

        var running = rig.Offer.StartAsync();
        await rig.Until(() => rig.Installer.Job?.BytesReceived >= 1_000);
        await rig.Until(() => rig.Rows.Any(r => r.State == CudaOfferState.Downloading && r.Progress > 0));

        var row = rig.Offer.Row;
        Assert.Equal(CudaOfferState.Downloading, row.State);
        Assert.Equal(CudaOfferAction.Cancel, row.Action);
        Assert.True(row.ShowsBar);
        Assert.InRange(row.Progress, 0.01, 0.99);
        Assert.EndsWith("%", row.StateText, StringComparison.Ordinal);
        rig.Offer.Cancel();
        await running.WaitAsync(FakeDownloadServer.Guard);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C
    public async Task Cancel_stops_the_download_keeps_the_partial_installs_nothing_and_returns_to_the_offer()
    {
        var rig = new CudaOfferRig();
        rig.Server.Serve("/pack.nupkg", rig.Nupkg, new FakeBehavior { Trickle = new FakeTrickle(1_000, TimeSpan.FromMinutes(1)) });
        await using var _ = rig;
        await rig.Offer.EvaluateAsync(CancellationToken.None);
        var running = rig.Offer.StartAsync();
        await rig.Until(() => rig.Installer.Job?.BytesReceived >= 1_000);

        rig.Offer.Cancel();
        await running.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(CudaOfferState.Offer, rig.Offer.Row.State);
        Assert.Equal(CudaOfferAction.Download, rig.Offer.Row.Action);
        Assert.False(rig.Installer.IsInstalled(rig.Pin));
        Assert.False(Directory.Exists(rig.Installer.FolderFor(rig.Pin)));
        Assert.Equal(1_000, new FileInfo(Path.Combine(rig.PartFolder, rig.Nupkg.Sha256Hex[..8] + ".part")).Length); // the x keeps the partial
        Assert.Empty(rig.Companions.Installs);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C
    public async Task A_second_press_while_downloading_is_the_same_download()
    {
        var rig = new CudaOfferRig();
        rig.Server.Serve("/pack.nupkg", rig.Nupkg, new FakeBehavior { Trickle = new FakeTrickle(1_000, TimeSpan.FromMinutes(1)) });
        await using var _ = rig;
        await rig.Offer.EvaluateAsync(CancellationToken.None);
        var first = rig.Offer.StartAsync();
        await rig.Until(() => rig.Installer.Job?.BytesReceived >= 1_000);

        var second = rig.Offer.StartAsync();
        rig.Offer.Cancel();
        await Task.WhenAll(first, second).WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(1, rig.Server.ConnectionCount); // one connection: the second press started nothing
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C
    public async Task A_server_error_ends_Failed_and_Download_tries_again_to_success()
    {
        var rig = new CudaOfferRig();
        rig.Server.Serve("/pack.nupkg", rig.Nupkg, new FakeBehavior { Status = 404 });
        await using var _ = rig;
        await rig.Offer.EvaluateAsync(CancellationToken.None);

        await rig.Offer.StartAsync().WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(CudaOfferState.Failed, rig.Offer.Row.State);
        Assert.Equal("The download didn’t finish. Nothing was installed", rig.Offer.Row.Detail);
        Assert.Equal(CudaOfferAction.Download, rig.Offer.Row.Action);
        Assert.False(rig.Installer.IsInstalled(rig.Pin));
        Assert.Empty(rig.Companions.Installs);

        rig.Server.Serve("/pack.nupkg", rig.Nupkg);
        await rig.Offer.StartAsync().WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(CudaOfferState.Installed, rig.Offer.Row.State);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C
    [Trait("Decision", "ED-267")] // ED-351, numbered by the lead at merge
    public async Task A_pack_whose_hash_does_not_match_ends_Failed_and_nothing_is_unpacked()
    {
        await using var rig = await Shown(wrongSha512: true);

        await rig.Offer.StartAsync().WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(CudaOfferState.Failed, rig.Offer.Row.State);
        Assert.False(Directory.Exists(rig.Installer.FolderFor(rig.Pin)));
        Assert.Empty(rig.Companions.Installs);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C
    [Trait("Decision", "ED-267")] // ED-351, numbered by the lead at merge
    public async Task Libraries_that_fail_end_Failed_and_the_next_try_does_not_fetch_the_pack_again()
    {
        var companions = new ScriptedCudaCompanions { Outcome = CudaCompanionOutcome.Failed };
        await using var rig = await Shown(companions);

        await rig.Offer.StartAsync().WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(CudaOfferState.Failed, rig.Offer.Row.State);
        Assert.Single(rig.Server.Requests);
        companions.Outcome = CudaCompanionOutcome.Installed;
        await rig.Offer.StartAsync().WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(CudaOfferState.Installed, rig.Offer.Row.State);
        Assert.Single(rig.Server.Requests); // the pack was already there
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C
    public async Task Cancelling_while_the_libraries_install_returns_to_the_offer()
    {
        var companions = new ScriptedCudaCompanions { Hold = new TaskCompletionSource() };
        await using var rig = await Shown(companions);
        var running = rig.Offer.StartAsync();
        await rig.Until(() => companions.Started);

        rig.Offer.Cancel();
        await running.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(CudaOfferState.Offer, rig.Offer.Row.State);
        Assert.Equal(1, companions.Cancelled);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")] // no real network
    public async Task Every_request_the_download_made_went_to_the_loopback_fake()
    {
        await using var rig = await Shown();

        await rig.Offer.StartAsync().WaitAsync(FakeDownloadServer.Guard);

        Assert.All(rig.Server.Requests, r => Assert.Equal("/pack.nupkg", r.Path));
        Assert.Empty(rig.Handler.Refusals); // nothing reached for a host the rig did not set up
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req Q-V-9 C
    public async Task Starting_while_hidden_does_nothing()
    {
        await using var rig = new CudaOfferRig(Marrow.Services.Port.Voice.Probe.ScriptedMachines.CpuOnlyLaptop);
        await rig.Offer.EvaluateAsync(CancellationToken.None);

        await rig.Offer.StartAsync();

        Assert.False(rig.Offer.Row.Shown);
        Assert.Empty(rig.Server.Requests);
    }
}
