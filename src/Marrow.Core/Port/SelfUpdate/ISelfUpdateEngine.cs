// PORT-ONLY (OD-0062, OD-0100; first-release UE-1): the seam between Marrow's update flow and Velopack (the production engine is
// Marrow.App.Port.VelopackSelfUpdateEngine). Tests put a fake behind it; nothing here reaches the network by itself.
namespace Marrow.Core.Port.SelfUpdate;

/// <summary>Checks the feed, downloads the offered package and hands it to the updater.</summary>
public interface ISelfUpdateEngine
{
    /// <summary>False when this run is not a Velopack install (a development run, a copy): there is nothing to update in place.</summary>
    bool IsInstalled { get; }

    /// <summary>The newer release the feed offers, or null when this one is the newest.</summary>
    Task<SelfUpdateOffer?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Downloads the package for <paramref name="version"/> (the feed's newest when that has moved on), reporting whole percent 0 to 100 as bytes arrive; returns the package's path. The feed's checksum is verified before it returns.</summary>
    Task<string> DownloadAsync(string version, Action<int> percent, CancellationToken cancellationToken);

    /// <summary>"Restart Marrow": starts the updater, which waits for this process to exit, swaps in the downloaded package and starts the new Marrow. It does not exit this process; the caller quits through the app's own way.</summary>
    void WaitExitThenApplyAndRestart();
}
