// PORT-ONLY: shared helpers of the UE-2 tests (first-release feature unit UE-2, BUILD-PLAN.md 2.4). Fake items and fake runners: nothing here runs a CLI, reads a PATH or opens the network.
using Marrow.Core.Models;
using Marrow.Core.Port.Updates;

namespace Marrow.Core.Tests.Port.Updates;

internal sealed class FakeItem(string id, UpdateItemKind kind, string? installed, string? latest, Func<IProgress<double?>, Task<UpdateOutcome>>? update = null) : IUpdateItem
{
    public static readonly List<string> Log = [];
    public static int Running;
    public static int MaxRunning;

    public string Id { get; } = id;

    public string Title { get; } = "Item " + id;

    public UpdateItemKind Kind { get; } = kind;

    public string? Installed { get; set; } = installed;

    public string? Latest { get; set; } = latest;

    public bool LatestThrows { get; set; }

    public bool InstalledThrows { get; set; }

    public int Updates { get; private set; }

    public Task<string?> InstalledVersion(CancellationToken cancellation) =>
        InstalledThrows ? Task.FromException<string?>(new InvalidOperationException("no version")) : Task.FromResult(Installed);

    public Task<string?> LatestVersion(CancellationToken cancellation) =>
        LatestThrows ? Task.FromException<string?>(new InvalidOperationException("offline")) : Task.FromResult(Latest);

    public async Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation)
    {
        MaxRunning = Math.Max(MaxRunning, Interlocked.Increment(ref Running));
        Log.Add("start " + Id);
        try
        {
            Updates++;
            await Task.Yield();
            var outcome = update is null ? new UpdateOutcome(true, Latest, true, false, null) : await update(progress);
            if (outcome.Succeeded && outcome.NewVersion is not null) Installed = outcome.NewVersion;
            return outcome;
        }
        finally
        {
            Log.Add("end " + Id);
            Interlocked.Decrement(ref Running);
        }
    }

    public static void Reset()
    {
        Log.Clear();
        Running = 0;
        MaxRunning = 0;
    }
}

internal sealed class FakeRunner : ICliUpdateRunner
{
    public Dictionary<ProviderKind, string?> Versions { get; } = [];

    public Dictionary<ProviderKind, CliUpdateResult> Results { get; } = [];

    public List<ProviderKind> Ran { get; } = [];

    public Task<string?> InstalledCLIVersion(ProviderKind provider) => Task.FromResult(Versions.GetValueOrDefault(provider));

    public Task<CliUpdateResult> UpdateCLI(ProviderKind provider)
    {
        Ran.Add(provider);
        return Task.FromResult(Results[provider]);
    }
}
