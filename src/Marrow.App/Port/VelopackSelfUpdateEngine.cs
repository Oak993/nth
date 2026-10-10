// PORT-ONLY (OD-0062 Velopack, OD-0100 the owner's GitHub; first-release UE-1): the production ISelfUpdateEngine. Velopack's UpdateManager
// reads the feed (GithubSource for the one repository of MarrowReleaseFeed), downloads the full package with percent progress and checks
// its checksum, and its Update.exe swaps the install and restarts Marrow. Both the source and the downloader are Velopack's own seams
// (IUpdateSource, IFileDownloader): a test gives them a loopback fake, so nothing here reaches GitHub on its own. The UpdateManager is
// made on first use, so a development run or a test host that never updates never touches Velopack (ED-235: Velopack writes its log on use).
using Marrow.Core.Port.SelfUpdate;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace Marrow.App.Port;

/// <summary>Marrow's self-update through Velopack.</summary>
public sealed class VelopackSelfUpdateEngine : ISelfUpdateEngine
{
    private readonly bool customLocator;
    private readonly Lazy<IVelopackLocator> locator;
    private readonly Lazy<UpdateManager> manager;
    private readonly Action<UpdateManager, VelopackAsset> waitExitThenApply;
    private UpdateInfo? latest;
    private VelopackAsset? downloaded;

    /// <param name="source">Where releases come from (production: <see cref="ForRepository"/>'s GithubSource).</param>
    /// <param name="locator">The install layout; null finds the running install (a test passes a TestVelopackLocator).</param>
    /// <param name="waitExitThenApply">What "Restart Marrow" does with the downloaded package; null starts Velopack's Update.exe (a test records the call instead).</param>
    public VelopackSelfUpdateEngine(IUpdateSource source, IVelopackLocator? locator = null, Action<UpdateManager, VelopackAsset>? waitExitThenApply = null)
    {
        customLocator = locator is not null;
        this.locator = new Lazy<IVelopackLocator>(() => locator ?? VelopackLocator.Current);
        manager = new Lazy<UpdateManager>(() => new UpdateManager(source, options: null, locator: this.locator.Value));
        this.waitExitThenApply = waitExitThenApply ?? ((m, asset) => m.WaitExitThenApplyUpdates(asset, silent: false, restart: true));
    }

    /// <summary>The engine over a GitHub repository's releases (<see cref="MarrowReleaseFeed.Url"/> in production), read without a token, stable releases only.</summary>
    public static VelopackSelfUpdateEngine ForRepository(string repositoryUrl, IFileDownloader? downloader = null, IVelopackLocator? locator = null,
        Action<UpdateManager, VelopackAsset>? waitExitThenApply = null) =>
        new(new GithubSource(repositoryUrl, accessToken: null, prerelease: false, downloader), locator, waitExitThenApply);

    // With Velopack's own locator, only the Velopack layout counts (Update.exe one folder above the app's own, as Program.InstalledByVelopack
    // reads it, ED-235), so a development run or a test host never reaches Velopack at all: it writes its log on every use.
    public bool IsInstalled => (customLocator || Program.InstalledByVelopack(AppContext.BaseDirectory)) && manager.Value.IsInstalled;

    public async Task<SelfUpdateOffer?> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = await manager.Value.CheckForUpdatesAsync();
        latest = info;
        return info is null ? null : OfferOf(info.TargetFullRelease);
    }

    public async Task<string> DownloadAsync(string version, Action<int> percent, CancellationToken cancellationToken)
    {
        // the offer the last check made, or the feed's newest now (the app may have restarted since the check that found it)
        var info = latest is not null && VersionText(latest.TargetFullRelease) == version ? latest : await manager.Value.CheckForUpdatesAsync();
        if (info is null) throw new UpdateError(new UpdateError.Case.NoRelease());
        if (!string.Equals(VersionText(info.TargetFullRelease), version, StringComparison.Ordinal))
            throw new InvalidOperationException($"The offered nth version {version} is no longer available. Check for updates again.");
        await manager.Value.DownloadUpdatesAsync(info, percent, cancellationToken);
        downloaded = info.TargetFullRelease;
        return Path.Combine(locator.Value.PackagesDir!, downloaded.FileName);
    }

    public void WaitExitThenApplyAndRestart()
    {
        var asset = downloaded ?? throw new InvalidOperationException("No update has been downloaded.");
        waitExitThenApply(manager.Value, asset);
    }

    internal static string VersionText(VelopackAsset asset) => asset.Version.ToNormalizedString();

    private static SelfUpdateOffer OfferOf(VelopackAsset asset) =>
        new(VersionText(asset), string.IsNullOrWhiteSpace(asset.NotesMarkdown) ? null : asset.NotesMarkdown, asset.Size > 0 ? asset.Size : null);
}
