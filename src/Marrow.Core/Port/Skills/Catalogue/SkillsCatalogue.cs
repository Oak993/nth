// PORT-ONLY: first-release feature unit SK-1 (OD-0099 amendments, OD-0103); there is no Swift twin. The schema of the embedded skills and tools catalogue,
// ONDEMAND-PLAN.md 2.3 (porting/first-release/skills-ONDEMAND-PLAN.md) with BUILD-PLAN.md 2.5 SK-1's `kind` and licence file path.
using System.Text.Json.Serialization;

namespace Marrow.Core.Port.Skills.Catalogue;

/// <summary>
/// What an item is. <c>skillset</c> and <c>plugin</c> are the vendor sets of the Skills and Plugins pages (BUILD-PLAN.md 2.5 SK-1, "Pages" of the
/// OD-0099 amendment table); <c>tool</c>, <c>pyset</c> and <c>nodeset</c> are ONDEMAND-PLAN.md 2.2's tools and package sets (2.3's list of kinds).
/// ENGINEERING DECISION (ED-294): one schema for all five, because <c>needs</c> and <c>provides</c> cross between them.
/// </summary>
public enum ItemKind
{
    Skillset,
    Plugin,
    Tool,
    Pyset,
    Nodeset,
}

/// <summary>ONDEMAND-PLAN.md 2.3: <c>open</c>, <c>proprietary</c>, <c>none</c> (no licence stated).</summary>
public enum TermsClass
{
    Open,
    Proprietary,
    None,
}

/// <summary>
/// <c>pinned</c> only when every value ONDEMAND-PLAN.md 2.3 asks for is real; the gate derives the state from the values and refuses a declaration
/// that disagrees (ED-294), so an item cannot claim a pin it does not have.
/// </summary>
public enum PinState
{
    Pinned,
    Unpinned,
}

/// <summary><c>plan</c>: counted in the totals ONDEMAND-PLAN.md prints (10 sets, 615 skills); <c>added</c>: added by the OD-0099 amendment, counted when SK-2 re-reads.</summary>
public enum CountState
{
    Plan,
    Added,
}

/// <summary>ONDEMAND-PLAN.md 2.3 "provides" and 5.3: executables, Python modules, Node modules and Playwright's missing-browser message.</summary>
public enum ProvideKind
{
    Exe,
    PyModule,
    NodeModule,
    Message,
}

/// <summary>The whole catalogue file. <c>schema</c> versions the shape; <c>readOn</c> is the date the plan read its pins.</summary>
public sealed record SkillsCatalogue
{
    public required int Schema { get; init; }

    public required string ReadOn { get; init; }

    public required IReadOnlyList<CatalogueItem> Items { get; init; }
}

/// <summary>
/// One catalogue item (ONDEMAND-PLAN.md 2.3). A key whose value is not known yet is present with an explicit <c>null</c>: nothing is guessed, and the
/// item stays <see cref="PinState.Unpinned"/> until the pin run fills it (SK-2).
/// </summary>
public sealed record CatalogueItem
{
    public required string Id { get; init; }

    public required ItemKind Kind { get; init; }

    /// <summary>Marrow's own title (never copied from a SKILL.md).</summary>
    public required string Title { get; init; }

    /// <summary>Marrow's own one-line summary (never copied; the twelve-word check holds it to that).</summary>
    public required string Summary { get; init; }

    public required string Vendor { get; init; }

    public required CountState CountState { get; init; }

    public required PinState PinState { get; init; }

    /// <summary>A full 40-hex commit for repository items, the version for tools, <c>null</c> when only per-skill pins or nothing is known.</summary>
    public required string? Pin { get; init; }

    /// <summary>The abbreviated commit the plan prints where it never prints the full one (skills-PLAN.md 6).</summary>
    public string? PinHint { get; init; }

    public required string? SourceUrl { get; init; }

    public required string? LicenceName { get; init; }

    /// <summary>The licence file as downloaded (raw.githubusercontent.com at the pin for repository items).</summary>
    public required string? LicenceUrl { get; init; }

    /// <summary>The licence file's path inside the source at the pin.</summary>
    public required string? LicenceFile { get; init; }

    public string? LicenceNotes { get; init; }

    public required TermsClass TermsClass { get; init; }

    /// <summary>The plan's approximate download size in decimal megabytes (a transcription, never a measurement).</summary>
    public required double? ApproxSizeMb { get; init; }

    public required IReadOnlyList<string> Needs { get; init; }

    public required IReadOnlyList<CatalogueProvide> Provides { get; init; }

    public required IReadOnlyList<CatalogueArtifact> Artifacts { get; init; }

    /// <summary>For skill sets and plugins: every file under the item's folders, filled by the pin run.</summary>
    public required IReadOnlyList<CatalogueFile> Files { get; init; }

    public int? SkillCount { get; init; }

    /// <summary><c>true</c> when <see cref="Skills"/> lists every skill of the item.</summary>
    public bool SkillsListed { get; init; }

    public IReadOnlyList<CatalogueSkill> Skills { get; init; } = [];

    public IReadOnlyList<CatalogueFolder> Folders { get; init; } = [];

    public IReadOnlyList<CataloguePackage> Packages { get; init; } = [];

    /// <summary>
    /// ED-NEW-dl-13: the skills found at the pin that the item leaves out, each with the reason (no clear licence of its own, a name the name rule refuses, a name another skill of
    /// the item already uses, a skill inside another skill's folder, a skill outside every plugin). None of their files is in <see cref="Files"/>, so none is fetched; the Download
    /// all list names them.
    /// </summary>
    public IReadOnlyList<CatalogueSkipped> Skipped { get; init; } = [];

    /// <summary>
    /// How a tool or package set is installed from its verified artifacts (ONDEMAND-PLAN.md 3.4's kinds) and what it publishes (3.6's shims, the Python package folders and the
    /// environment a tool exports). ENGINEERING DECISION (ED-NEW-dl-7): installer parameters are catalogue data, pinned with the artifacts by the same pin run and reviewed with them;
    /// absent for skill sets and plugins, and an item without it is never offered as a download.
    /// </summary>
    public CatalogueInstall? Install { get; init; }
}

/// <summary>ONDEMAND-PLAN.md 3.4: <c>zip</c>, <c>pythonEmbed</c>, <c>pyset</c>, <c>nodeset</c>, <c>msiAdmin</c>, <c>sfx7z</c>.</summary>
public enum InstallKind
{
    Zip,
    PythonEmbed,
    Pyset,
    Nodeset,
    MsiAdmin,
    Sfx7z,
}

/// <summary>The installer parameters of one tool or package set (ED-NEW-dl-7). Paths are relative to the installed folder, with <c>/</c>.</summary>
public sealed record CatalogueInstall
{
    public required InstallKind Kind { get; init; }

    /// <summary><c>zip</c>: every archive artifact and the folder it unpacks into (none: the item folder). <c>pythonEmbed</c>: the interpreter archive.</summary>
    public IReadOnlyList<CatalogueInstallArchive> Archives { get; init; } = [];

    /// <summary><c>pythonEmbed</c>: the pip wheel and the <c>._pth</c> layout (3.4).</summary>
    public CataloguePythonEmbed? Python { get; init; }

    /// <summary><c>nodeset</c>: each tarball artifact, where the lock puts it (<c>node_modules/...</c>) and its npm integrity.</summary>
    public IReadOnlyList<CatalogueNodePackage> NodeTree { get; init; } = [];

    /// <summary>Empty files the tool's own runtime looks for (Playwright's <c>INSTALLATION_COMPLETE</c>).</summary>
    public IReadOnlyList<string> Markers { get; init; } = [];

    /// <summary>The executables published into <c>addons\bin</c> (3.6), one per provided name.</summary>
    public IReadOnlyList<CatalogueShim> Shims { get; init; } = [];

    /// <summary>Environment every shimmed child gets while the item is installed; <c>{folder}</c> in a value is the installed folder.</summary>
    public IReadOnlyList<CatalogueExport> Exports { get; init; } = [];

    /// <summary><c>pyset</c>: the installed folder is listed on the Python path of every Python shim run (3.4).</summary>
    public bool PythonPackages { get; init; }
}

public sealed record CatalogueInstallArchive
{
    public required string Artifact { get; init; }

    public string? Prefix { get; init; }
}

public sealed record CataloguePythonEmbed
{
    public required string PipArtifact { get; init; }

    public required string PthFile { get; init; }

    public required string StandardLibraryZip { get; init; }

    public required string AgentVersion { get; init; }
}

public sealed record CatalogueNodePackage
{
    public required string Artifact { get; init; }

    public required string Path { get; init; }

    public required string Integrity { get; init; }
}

/// <summary>
/// One shim: <see cref="Name"/> runs <see cref="Target"/> (a file of this item) or, when <see cref="Item"/> is set, the shim <see cref="Shim"/> of that needed item
/// (pip runs Python's own interpreter). <see cref="Arguments"/> go first; <c>{folder}</c> in one is this item's installed folder.
/// </summary>
public sealed record CatalogueShim
{
    public required string Name { get; init; }

    public string? Target { get; init; }

    public string? Item { get; init; }

    public string? Shim { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>The target is the embedded Python: its <c>._pth</c> is rewritten per run with the installed package sets (3.6).</summary>
    public bool Python { get; init; }

    /// <summary>The target is LibreOffice: each run gets its own profile (3.6).</summary>
    public bool LibreOffice { get; init; }
}

public sealed record CatalogueExport
{
    public required string Name { get; init; }

    public required string Value { get; init; }
}

public sealed record CatalogueProvide
{
    public required ProvideKind Kind { get; init; }

    public required string Name { get; init; }
}

public sealed record CatalogueArtifact
{
    public required string Name { get; init; }

    public required string? Url { get; init; }

    public required long? Size { get; init; }

    public required string? Sha256 { get; init; }

    /// <summary>The leading hex digits of the digest the plan prints, kept so SK-2 can cross-check its own run.</summary>
    public string? Sha256Hint { get; init; }
}

public sealed record CatalogueFile
{
    public required string Path { get; init; }

    public required long Size { get; init; }

    public required string Sha256 { get; init; }
}

public sealed record CatalogueSkill
{
    /// <summary>The skill's folder name.</summary>
    public required string Folder { get; init; }

    /// <summary>The frontmatter <c>name</c>, <c>null</c> until it is read from the pinned SKILL.md.</summary>
    public required string? Name { get; init; }

    public required string? Path { get; init; }

    public required IReadOnlyList<string> Needs { get; init; }

    public TermsClass? TermsClass { get; init; }

    /// <summary>A per-skill commit where the set is not at one commit (O3).</summary>
    public string? Pin { get; init; }

    /// <summary>
    /// ED-NEW-dl-13: in a plugin whose licences are recorded per plugin, the licence of a skill that carries a licence file of its own inside its folder (the file is in the item's
    /// files). A skill without one is under its plugin folder's licence.
    /// </summary>
    public string? LicenceName { get; init; }

    public string? LicenceFile { get; init; }
}

/// <summary>
/// One plugin of a plugin item: <see cref="Name"/> is the plugin's folder in the repository (where its plugin.json sits), the key its on/off switch is stored under.
/// </summary>
public sealed record CatalogueFolder
{
    public required string Name { get; init; }

    public required int SkillCount { get; init; }

    /// <summary>
    /// ED-NEW-dl-13: the plugin's own licence, when the item records its licences per plugin: the name and the file in the item's files that states it (the plugin's nearest
    /// licence file, or its plugin.json when the licence is stated only there).
    /// </summary>
    public string? LicenceName { get; init; }

    public string? LicenceFile { get; init; }
}

/// <summary>ED-NEW-dl-13: a skill the item leaves out. <see cref="Name"/> is its frontmatter name as found (null when it has none).</summary>
public sealed record CatalogueSkipped
{
    public required string Path { get; init; }

    public required string? Name { get; init; }

    public required string Reason { get; init; }
}

public sealed record CataloguePackage
{
    public required string Name { get; init; }

    public required string? Version { get; init; }

    public required string? Licence { get; init; }
}
