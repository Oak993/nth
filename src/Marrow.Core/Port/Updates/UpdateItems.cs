// PORT-ONLY: first-release feature unit UE-2 (BUILD-PLAN.md 2.4, OD-0100); there is no Swift twin. The item model of "Update everything".
using Marrow.Core.Models;

namespace Marrow.Core.Port.Updates;

/// <summary>What an update row is. The order is the order a run takes them: provider CLIs, the user's developer tools, skills and tools, and Marrow itself last
/// (OD-0100 "Marrow last"; ONDEMAND-PLAN.md 3.7 "after the provider CLIs and before Marrow itself"; where the dev tools go is ENGINEERING DECISION ED-308).</summary>
public enum UpdateItemKind
{
    ProviderCli,
    DevTool,
    SkillsAndTools,
    /// <summary>PORT-ONLY (OD-0134, ED-NEW-ignis-settings): Ignis's model points, a data file; it runs after the tools and before Marrow itself, and the page lists it right after Marrow.</summary>
    ModelPoints,
    Marrow,
}

public enum UpdateRowState
{
    /// <summary>Not checked yet.</summary>
    Unchecked,

    /// <summary>The item is not installed, so there is nothing to update (a provider CLI the user does not have).</summary>
    NotInstalled,

    UpToDate,
    UpdateAvailable,

    /// <summary>No public listing of the latest version: the row says "Checks when updated" and the update run decides (BUILD-PLAN.md 2.4).</summary>
    ChecksWhenUpdated,

    Queued,
    Updating,
    Updated,
    Failed,

    /// <summary>The updater accepted an asynchronous request; installation has not yet been verified (lead UE-3 ruling).</summary>
    UpdatingInBackground,
}

/// <summary>What one item's update did. <see cref="Changed"/> is true when the installed version moved; <see cref="RestartRequired"/> is true when the app must restart to use it (Marrow's own update, UE-1).</summary>
public sealed record UpdateOutcome(bool Succeeded, string? NewVersion, bool Changed, bool RestartRequired, string? Message, bool UpdatingInBackground = false);

/// <summary>One row of the Updates page and of the pill's list (places A and D of OD-0100). Progress is null while the amount is not known (a CLI's own updater reports none).</summary>
public sealed record UpdateRow(string Id, string Title, UpdateItemKind Kind, UpdateRowState State, string? Installed, string? Latest, double? Progress, string? Message, bool RestartRequired);

/// <summary>
/// Something "Update everything" can update. Marrow itself is one (UE-1 implements it over Velopack), every provider CLI with an updater is one (<see cref="ProviderCliUpdateItem"/>),
/// the skills and tools group is one (SK-8) and so is each of the user's developer tools (UE-3).
/// </summary>
public interface IUpdateItem
{
    string Id { get; }

    string Title { get; }

    UpdateItemKind Kind { get; }

    /// <summary>The installed version's text, or null when the item is not installed.</summary>
    Task<string?> InstalledVersion(CancellationToken cancellation);

    /// <summary>The latest version's text from a public listing, or null when there is no listing.</summary>
    Task<string?> LatestVersion(CancellationToken cancellation);

    Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation);
}

public enum CliUpdateStatus
{
    /// <summary>The updater ran and the CLI's version changed.</summary>
    Updated,

    /// <summary>The updater ran and the version is the same.</summary>
    Unchanged,

    /// <summary>The updater failed, or the version could not be read afterwards.</summary>
    Failed,

    NotInstalled,

    /// <summary>No updater was run: the provider has none, or the user set a binary path of their own.</summary>
    Skipped,
}

public sealed record CliUpdateResult(CliUpdateStatus Status, string? Before, string? After);

/// <summary>ProviderRegistry's update run, as the update items see it (Core cannot name ProviderRegistry).</summary>
public interface ICliUpdateRunner
{
    Task<string?> InstalledCLIVersion(ProviderKind provider);

    Task<CliUpdateResult> UpdateCLI(ProviderKind provider);
}
