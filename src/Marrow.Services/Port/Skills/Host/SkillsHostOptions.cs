// PORT-ONLY: SK-13 / ED-326 (6)(a). What the one owner of a data root is composed from.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Custom;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Port.Skills.Terms;

namespace Marrow.Services.Port.Skills.Host;

/// <summary>
/// Every dependency of <see cref="SkillsHost"/>. The seams that reach a network, a process or a vendor (<see cref="Downloads"/>, <see cref="Bindings"/>, <see cref="LicenceText"/>,
/// <see cref="InstallProcess"/>) are null or fail closed until the unit that wires them binds them (SK-16 for the download and binding seams). Tests inject fakes for all of it.
/// </summary>
public sealed record SkillsHostOptions
{
    public required SkillsCatalogue Catalogue { get; init; }

    public required IStoreProtection Protection { get; init; }

    public required IInstallLock InstallLock { get; init; }

    public IInstallProcess? InstallProcess { get; init; }

    public IStoreDeletion? Deletion { get; init; }

    /// <summary>The download seam of SK-8: fetches an item's pinned inputs and returns the installer's plan. Unbound: updates, repairs and installs refuse ("Pipeline seams are not bound").</summary>
    public Func<CatalogueItem, ItemRecord, IProgress<double?>, CancellationToken, Task<InstallPlan>>? Downloads { get; init; }

    /// <summary>
    /// Which items the bound download seam can make an install plan for (ED-NEW-dlhonest-1). Null: every item with a complete pin. The production seams (SkillsProductionSeams.WithDownloads) bind
    /// CatalogueInstallPlans.CanPlan, so an item whose plan would fail closed is never offered as a download.
    /// </summary>
    public Func<CatalogueItem, bool>? Installable { get; init; }

    /// <summary>The binding seam of SK-8: the routes (shims and spares) a verified install is published with.</summary>
    public Func<CatalogueItem, InstallResult, PipelineRoute>? Bindings { get; init; }

    /// <summary>Reads an item's pinned licence file for the terms step. Unbound: fails closed.</summary>
    public ILicenceText? LicenceText { get; init; }

    /// <summary>The clash snapshot of SK-9 per provider. Null: a generation refuses to compose without an explicit one.</summary>
    public Func<string, IReadOnlyList<ExistingSkill>>? ClashSource { get; init; }

    /// <summary>Rebuilds the trusted descriptors at open; <see cref="CatalogueTrust.Restore"/> when null.</summary>
    public Func<StorePaths, SkillsCatalogue, AddonsState, TrustedInstalls>? Trust { get; init; }

    public TimeProvider? Clock { get; init; }

    /// <summary>RR-14 (ED-NEW-dl-10): the NativeAOT launcher <c>addons\bin</c> holds copies of; null: the one shipped beside the app (<see cref="PipelineShimSync.Launcher"/>).</summary>
    public string? ShimLauncher { get; init; }

    public ICustomSourceFiles? CustomFiles { get; init; }

    public ICustomGitHubSource? CustomGitHub { get; init; }

    /// <summary>
    /// The app's composition: the embedded catalogue, the Windows ACL protection and lock, a process runner with an EMPTY executable allow-list (it runs nothing until an executable is pinned
    /// by path, size and hash), and no download, binding or licence-text seam.
    /// </summary>
    public static SkillsHostOptions Production(SkillsCatalogue? catalogue = null) => new()
    {
        Catalogue = catalogue ?? SkillsCatalogueLoader.LoadEmbedded(),
        Protection = new WindowsStoreProtection(),
        InstallLock = new WindowsInstallLock(),
        InstallProcess = new WindowsInstallProcess([]),
    };
}
