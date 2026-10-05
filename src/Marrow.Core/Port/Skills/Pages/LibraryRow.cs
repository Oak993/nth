// PORT-ONLY: first-release feature unit SK-15 (DL-17). One row of a library page and the detail the row opens.
using Marrow.Core.Port.Skills.State;

namespace Marrow.Core.Port.Skills.Pages;

/// <summary>What the Needs line of a detail names: the need's own title and, for a tool the probe found on this PC, where (ONDEMAND-PLAN.md 3.6, "Found on this PC").</summary>
public sealed record NeedLine(string Id, string Title, string? FoundPath)
{
    public bool FoundOnThisPc => FoundPath is not null;
}

public enum SwitchKind
{
    /// <summary>One skill of a skill set, keyed <c>itemId/path</c> in enabled.json (SkillSwitches.Skills).</summary>
    Skill,

    /// <summary>One plugin folder of a plugin item, keyed <c>itemId/folder</c> in enabled.json (SkillSwitches.Plugins).</summary>
    PluginFolder,
}

/// <summary>
/// One switch of the "In every session" box. <see cref="Detail"/> is a plugin folder's skill count ("36 skills"). <see cref="Enabled"/> is false while the item is not
/// downloaded: the line shows what the set holds, and its switch waits for the download.
/// </summary>
public sealed record SwitchLine(SwitchKind Kind, string ItemId, string Key, string Label, string Detail, bool On, bool Enabled);

/// <summary>The switches' truth for the page: enabled.json read through SK-9's SkillSwitches (the controller adapts it; the model never reads a file).</summary>
public interface ISwitchReader
{
    bool Skill(string itemId, string path);

    bool PluginFolder(string itemId, string folder);
}

/// <summary>
/// A row and its detail. <see cref="State"/> is the item's effective state (the pipeline's, or the page's own run); <see cref="Progress"/> is 0 to 1 while it downloads and
/// <c>null</c> while the size of the work is unknown; <see cref="Failure"/> is the message of a Failed item. <see cref="TermsLabel"/> is the row label of an item under a vendor's
/// terms ("Under Anthropic's terms"); <see cref="TermsOwed"/> says the terms step is still to do before the download.
/// </summary>
public sealed record LibraryRow(
    string Id,
    string Title,
    string Summary,
    string Meta,
    ItemState State,
    double? Progress,
    string? Failure,
    double? SizeMb,
    string? TermsLabel,
    bool TermsOwed,
    string Source,
    string? Licence,
    string? Update,
    IReadOnlyList<NeedLine> Needs,
    IReadOnlyList<SwitchLine> Switches,
    int ListedOn,
    int ListedOf,
    bool Plugins)
{
    /// <summary>Installed on this PC in any way: its switches are live and its skills are listed (an update on offer, a damaged copy and a removal in progress keep it).</summary>
    public bool OnThisPc => State is ItemState.Installed or ItemState.UpdateAvailable or ItemState.Damaged or ItemState.Removing;

    public bool Busy => State is ItemState.Queued or ItemState.Downloading or ItemState.Checking;

    /// <summary>The right side of the box heading: "4 of 4 listed", "36 skills listed", or the not-downloaded words.</summary>
    public string ListedText => !OnThisPc ? LibraryWords.NotListed : Plugins ? LibraryWords.PluginSkillsListed(ListedOn) : LibraryWords.ListedOf(ListedOn, ListedOf);
}
