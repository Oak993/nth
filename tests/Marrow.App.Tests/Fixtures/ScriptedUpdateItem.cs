// UE-4 test support: an update item scripted by the test (UE-2's IUpdateItem). Nothing here runs a CLI, reads a PATH or opens the network.
using Marrow.Core.Port.Updates;

namespace Marrow.App.Tests.Fixtures;

internal sealed class ScriptedUpdateItem(string id, string title, UpdateItemKind kind, string? installed, string? latest) : IUpdateItem
{
    public string Id { get; } = id;

    public string Title { get; } = title;

    public UpdateItemKind Kind { get; } = kind;

    public string? Installed { get; set; } = installed;

    public string? Latest { get; set; } = latest;

    /// <summary>What the update does; when null it succeeds, installs <see cref="Latest"/> and reports the version changed.</summary>
    public Func<IProgress<double?>, Task<UpdateOutcome>>? OnUpdate { get; set; }

    public int Updates { get; private set; }

    public int Checks { get; private set; }

    /// <summary>When set, reading the installed version waits for it: a check the test holds open.</summary>
    public Task? InstalledWait { get; set; }

    public async Task<string?> InstalledVersion(CancellationToken cancellation)
    {
        Checks++;
        if (InstalledWait is { } wait) await wait;
        return Installed;
    }

    public Task<string?> LatestVersion(CancellationToken cancellation) => Task.FromResult(Latest);

    public async Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation)
    {
        Updates++;
        var outcome = OnUpdate is null ? new UpdateOutcome(true, Latest, true, false, null) : await OnUpdate(progress);
        if (outcome.Succeeded && outcome.NewVersion is not null) Installed = outcome.NewVersion;
        return outcome;
    }
}
