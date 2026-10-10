using System.Text;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.SelfUpdate;
using Velopack;
using Velopack.Locators;
using Xunit;

namespace Marrow.App.Tests.App;

// UE-1 (OD-0062 Velopack; OD-0100 the owner's GitHub repository): the production engine over Velopack's UpdateManager, run against a fake
// "marrow-releases" repository answered in this process through Velopack's IFileDownloader seam (FakeFeedDownloader). Velopack's own
// check, delta choice, download and checksum code run for real; nothing reaches GitHub, nothing is installed, and "Restart Marrow" is a
// recorder (Update.exe never starts).
public sealed class VelopackSelfUpdateEngineTests
{
    private const string ListUrl = "https://api.github.com/repos/Oak993/marrow-releases/releases";
    private static readonly byte[] Package = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("a fake full package ", 400)));

    private static VelopackEngineRig Make(string installed, Action<FakeFeedDownloader> serve)
    {
        var feed = new FakeFeedDownloader();
        serve(feed);
        var packages = Directory.CreateTempSubdirectory("ue1-velopack-").FullName;
        var applied = new List<VelopackAsset>();
        // channel "win": the feed file is releases.win.json, whatever system the test runs on (Velopack's default channel is the system's own)
        var root = Directory.CreateTempSubdirectory("ue1-root-").FullName;
        var locator = new TestVelopackLocator("NthApp", installed, packages, root, root, Path.Combine(root, "Update.exe"), "win", null, null, "Marrow.App.exe");
        var engine = VelopackSelfUpdateEngine.ForRepository(MarrowReleaseFeed.Url, feed, locator, (_, asset) => applied.Add(asset));
        return new VelopackEngineRig(engine, feed, applied, packages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-360")]
    [Trait("Feature", "UE-6")]
    [Trait("Spec", "UE-T83")]
    public async Task A_newer_feed_cannot_substitute_its_package_for_the_version_whose_notes_were_offered(bool checkedEarlier)
    {
        var rig = Make("1.7.0", f => f.ServeRelease(ListUrl, "1.8.0", Package));
        if (checkedEarlier) await rig.Engine.CheckAsync(CancellationToken.None);
        rig.Feed.ServeRelease(ListUrl, "1.9.0", Package);
        if (checkedEarlier) await rig.Engine.CheckAsync(CancellationToken.None);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Engine.DownloadAsync("1.8.0", _ => { }, CancellationToken.None));
        Assert.Contains("1.8.0", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(rig.PackagesDir, "*.nupkg"));
        Assert.Throws<InvalidOperationException>(rig.Engine.WaitExitThenApplyAndRestart);
        Assert.Empty(rig.Applied);
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public async Task The_repository_is_read_through_githubs_release_list_and_only_that()
    {
        var rig = Make("1.7.0", f => f.ServeRelease(ListUrl, "1.8.0", Package));
        await rig.Engine.CheckAsync(CancellationToken.None);
        Assert.StartsWith(ListUrl + "?", rig.Feed.Requested[0], StringComparison.Ordinal);            // api.github.com/repos/Oak993/marrow-releases/releases
        Assert.All(rig.Feed.Requested, url => Assert.True(url.StartsWith("https://api.github.com/repos/Oak993/marrow-releases/", StringComparison.Ordinal) || url.StartsWith("https://fake.invalid/", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public async Task A_newer_release_is_offered_with_its_notes_and_size()
    {
        var rig = Make("1.7.0", f => f.ServeRelease(ListUrl, "1.8.0", Package, notes: "## 1.8.0\n\n- faster"));
        Assert.True(rig.Engine.IsInstalled);
        var offer = await rig.Engine.CheckAsync(CancellationToken.None);
        Assert.Equal(new SelfUpdateOffer("1.8.0", "## 1.8.0\n\n- faster", Package.Length), offer);
    }

    [Theory]
    [Trait("Decision", "ED-235")]
    [InlineData("1.8.0")]
    [InlineData("1.9.0")]
    public async Task The_same_or_an_older_release_offers_nothing(string installed)
    {
        var rig = Make(installed, f => f.ServeRelease(ListUrl, "1.8.0", Package));
        Assert.Null(await rig.Engine.CheckAsync(CancellationToken.None));
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public async Task A_download_reports_percent_writes_the_package_and_verifies_the_feeds_checksum()
    {
        var rig = Make("1.7.0", f => f.ServeRelease(ListUrl, "1.8.0", Package));
        await rig.Engine.CheckAsync(CancellationToken.None);
        var percents = new List<int>();
        var path = await rig.Engine.DownloadAsync("1.8.0", percents.Add, CancellationToken.None);
        Assert.Equal(Path.Combine(rig.PackagesDir, "NthApp-1.8.0-full.nupkg"), path);
        Assert.Equal(Package, await File.ReadAllBytesAsync(path));
        Assert.NotEmpty(percents);
        Assert.Equal(percents.Order().ToList(), percents);                                         // never backwards
        Assert.Equal(100, percents[^1]);
        Assert.All(percents, p => Assert.InRange(p, 0, 100));
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public async Task A_package_that_does_not_match_the_feeds_checksum_is_refused_and_nothing_is_applied()
    {
        var rig = Make("1.7.0", f => f.ServeRelease(ListUrl, "1.8.0", Package.Concat(new byte[] { 9 }).ToArray(), checksumOf: Package));
        await rig.Engine.CheckAsync(CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Engine.DownloadAsync("1.8.0", _ => { }, CancellationToken.None));
        Assert.Throws<InvalidOperationException>(rig.Engine.WaitExitThenApplyAndRestart);           // nothing was downloaded to apply
        Assert.Empty(rig.Applied);
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public async Task Restart_Marrow_hands_the_downloaded_package_to_the_updater_once_downloaded()
    {
        var rig = Make("1.7.0", f => f.ServeRelease(ListUrl, "1.8.0", Package));
        await rig.Engine.CheckAsync(CancellationToken.None);
        Assert.Throws<InvalidOperationException>(rig.Engine.WaitExitThenApplyAndRestart);           // before a download: nothing to apply
        await rig.Engine.DownloadAsync("1.8.0", _ => { }, CancellationToken.None);
        rig.Engine.WaitExitThenApplyAndRestart();
        var asset = Assert.Single(rig.Applied);
        Assert.Equal("NthApp-1.8.0-full.nupkg", asset.FileName);
        Assert.Equal("1.8.0", asset.Version.ToNormalizedString());
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public async Task A_download_after_a_restart_looks_the_feed_up_again()
    {
        var rig = Make("1.7.0", f => f.ServeRelease(ListUrl, "1.8.0", Package));
        var path = await rig.Engine.DownloadAsync("1.8.0", _ => { }, CancellationToken.None);       // no CheckAsync first: the persisted offer is all the app has
        Assert.True(File.Exists(path));
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public async Task A_download_when_the_feed_has_nothing_newer_is_the_no_release_error()
    {
        var rig = Make("1.8.0", f => f.ServeRelease(ListUrl, "1.8.0", Package));
        var error = await Assert.ThrowsAsync<UpdateError>(() => rig.Engine.DownloadAsync("1.8.0", _ => { }, CancellationToken.None));
        Assert.IsType<UpdateError.Case.NoRelease>(error.ErrorCase);
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public async Task A_closed_network_fails_the_check_and_the_download_without_a_partial_state()
    {
        var rig = Make("1.7.0", _ => { });                                                          // the fake network answers nothing
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Engine.CheckAsync(CancellationToken.None));
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Engine.DownloadAsync("1.8.0", _ => { }, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(rig.PackagesDir, "*.nupkg"));                         // Velopack's own .betaId marker may be there; no package
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public void Without_a_locator_only_the_velopack_layout_counts_as_an_install_so_a_test_host_never_reaches_velopack()
    {
        var engine = VelopackSelfUpdateEngine.ForRepository(MarrowReleaseFeed.Url, new FakeFeedDownloader());
        Assert.False(engine.IsInstalled);                                                           // the test host's folder has no Update.exe above it
    }
}
