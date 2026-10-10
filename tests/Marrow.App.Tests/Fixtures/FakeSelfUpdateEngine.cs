using Marrow.Core.Port.SelfUpdate;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY test fixture, no Swift twin (UE-1, OD-0062/OD-0100): the self-update seam of the checker and the updater. Records the calls
// made, answers what a test sets and never reaches Velopack, GitHub or an updater process.
public sealed class FakeSelfUpdateEngine : ISelfUpdateEngine
{
    public List<string> Calls { get; } = [];

    public bool Installed { get; set; } = true;

    public SelfUpdateOffer? Offer { get; set; }

    public Exception? CheckThrows { get; set; }

    public Exception? DownloadThrows { get; set; }

    public Exception? ApplyThrows { get; set; }

    /// <summary>The percent values a download reports, in order, before it returns.</summary>
    public int[] Percents { get; set; } = [0, 25, 100];

    public string PackagePath { get; set; } = Path.Combine(Directory.CreateTempSubdirectory("ue1-engine-").FullName, "NthApp-1.3.0-full.nupkg");

    public bool IsInstalled
    {
        get
        {
            Calls.Add("IsInstalled");
            return Installed;
        }
    }

    public Task<SelfUpdateOffer?> CheckAsync(CancellationToken cancellationToken)
    {
        Calls.Add("Check");
        if (CheckThrows is { } error) throw error;
        return Task.FromResult(Offer);
    }

    /// <summary>When set, a download waits for this after reporting its percent values, so a test can look at the pill mid-download.</summary>
    public Task? DownloadGate { get; set; }

    public async Task<string> DownloadAsync(string version, Action<int> percent, CancellationToken cancellationToken)
    {
        Calls.Add("Download(" + version + ")");
        if (DownloadThrows is { } error) throw error;
        foreach (var p in Percents) percent(p);
        if (DownloadGate is { } gate) await gate;
        return PackagePath;
    }

    public void WaitExitThenApplyAndRestart()
    {
        Calls.Add("Apply");
        if (ApplyThrows is { } error) throw error;
    }
}
