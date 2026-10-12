using System.Globalization;
using Marrow.Core.Port;

// PORT-ONLY: web surfaces W2, the pane workspace's kinds and sizes (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.1
// and 3.2, owner pick 4; ED-NEW-web-w2-1; owner 2026-10-11 "Web Surfaces Approved. Visus and Tabula approved"). No
// Swift counterpart. The numbers are the 3.2 table and the approved canvas's K, MINW, MAXW, MINH, RANK and DEFC
// (E:/Droppy/work/design/web-surfaces/index.html, "1. pane workspace").

namespace Marrow.Core.Models;

/// <summary>Every pane the workspace can hold (DESIGN 3.1). One pane of each kind per thread.</summary>
/// <remarks>The raw values are the persisted ids (3.11); a kind this build does not know is dropped from view.</remarks>
public enum PaneKind
{
    [RawValue("chat")] Chat,
    [RawValue("side")] SideChat,
    [RawValue("tasks")] BackgroundTasks,
    [RawValue("changes")] Changes,
    [RawValue("artifact")] Artifacts,
    [RawValue("browser")] Browser,
    [RawValue("terminal")] Terminal,
    [RawValue("app")] App,
    [RawValue("transcript")] Transcript,
}

/// <summary>A pane kind's minimum width, minimum height and maximum width in DIP (DESIGN 3.2 table).</summary>
public readonly record struct PaneSize(double MinWidth, double MinHeight, double MaxWidth);

/// <summary>The workspace constants and the per-kind table of DESIGN 3.2.</summary>
public static class PaneKinds
{
    /// <summary>The gap between cards, which is also every gutter's width.</summary>
    public const double Gap = 8;

    /// <summary>A collapsed side's rail.</summary>
    public const double Rail = 22;

    /// <summary>How far past a pane's minimum a gutter drag must go before that side collapses (3.5, 4.2).</summary>
    public const double CollapseThreshold = 56;

    /// <summary>A pane header's height (3.12); a header drop target is this tall (3.6).</summary>
    public const double HeaderHeight = 36;

    /// <summary>The narrow mode's segmented tab strip at the top of the detail area (3.8; the canvas's tabsH).</summary>
    public const double NarrowTabStrip = 44;

    /// <summary>The chat's minimum readable width; the chat never goes under it and never collapses.</summary>
    public const double ChatMinWidth = 400;

    /// <summary>Side chat's default share of the width right of the chat (3.2 rule 5).</summary>
    public const double DefaultSideRatio = 0.30;

    /// <summary>The kinds that live in the grid, in declaration order (Chat and Side chat do not).</summary>
    public static IReadOnlyList<PaneKind> GridKinds { get; } =
    [
        PaneKind.BackgroundTasks, PaneKind.Changes, PaneKind.Artifacts, PaneKind.Browser, PaneKind.Terminal,
        PaneKind.App, PaneKind.Transcript,
    ];

    /// <summary>The 3.2 table. A kind with no maximum has <see cref="double.PositiveInfinity"/>.</summary>
    public static PaneSize Size(PaneKind kind) => kind switch
    {
        PaneKind.Chat => new(ChatMinWidth, 240, double.PositiveInfinity),
        PaneKind.Browser => new(360, 240, double.PositiveInfinity),
        PaneKind.Artifacts => new(320, 200, double.PositiveInfinity),
        PaneKind.App => new(320, 200, double.PositiveInfinity),
        PaneKind.SideChat => new(280, 240, 640),
        PaneKind.Terminal => new(320, 150, double.PositiveInfinity),
        PaneKind.BackgroundTasks => new(280, 150, 560),
        PaneKind.Changes => new(280, 150, double.PositiveInfinity),
        PaneKind.Transcript => new(280, 150, 720),
    };

    /// <summary>
    /// Insertion rank (3.2 rule 9): a newly opened pane goes before the first slot of a higher rank, so wide panes land
    /// in the bottom, spanning row. Terminal is not ranked by the design (it reaches the grid by a drag); it takes the
    /// wide-pane rank 2 so a reopened grid terminal sits with Tabula and App, before Visus.
    /// </summary>
    public static int Rank(PaneKind kind) => kind switch
    {
        PaneKind.BackgroundTasks => 0,
        PaneKind.Changes or PaneKind.Transcript => 1,
        PaneKind.Artifacts or PaneKind.App or PaneKind.Terminal => 2,
        PaneKind.Browser => 3,
        PaneKind.Chat or PaneKind.SideChat => -1,
    };

    /// <summary>Whether the kind lives in the grid's order (everything but Chat and Side chat).</summary>
    public static bool IsGridKind(PaneKind kind) => kind is not (PaneKind.Chat or PaneKind.SideChat) && Enum.IsDefined(kind);

    /// <summary>The chat column's default share by slot count (3.2 rule 4): 1 slot 0.50, 2 0.42, 3 0.36, 4 or more 0.31.</summary>
    public static double DefaultChatRatio(int slots) => slots switch
    {
        <= 0 => 1,
        1 => 0.50,
        2 => 0.42,
        3 => 0.36,
        _ => 0.31,
    };

    /// <summary>The short name gutter labels use (the canvas's NAMES): "Resize Tasks and Tabula".</summary>
    public static string ShortName(PaneKind kind) => kind switch
    {
        PaneKind.Chat => "Chat",
        PaneKind.SideChat => "Side chat",
        PaneKind.BackgroundTasks => "Tasks",
        PaneKind.Changes => "Changes",
        PaneKind.Artifacts => "Tabula",
        PaneKind.Browser => "Visus",
        PaneKind.Terminal => "Terminal",
        PaneKind.App => "App",
        PaneKind.Transcript => "Transcript",
    };

    /// <summary>The pane header's title (the canvas headers).</summary>
    public static string Title(PaneKind kind) => kind switch
    {
        PaneKind.BackgroundTasks => "Background tasks",
        _ => ShortName(kind),
    };

    /// <summary>The pane region's accessible name (the canvas's aria-label): the plain kind, then the product name.</summary>
    public static string AccessibleName(PaneKind kind) => kind switch
    {
        PaneKind.Browser => "Browser, Visus",
        PaneKind.Artifacts => "Artifacts, Tabula",
        PaneKind.BackgroundTasks => "Background tasks",
        _ => ShortName(kind),
    };

    /// <summary>The persisted id (3.11).</summary>
    public static string Id(PaneKind kind) => SwiftEnum<PaneKind>.RawValue(kind);

    /// <summary>The kind for a persisted id, or null for an id this build does not know (kept on disk, not shown).</summary>
    public static PaneKind? FromId(string? id) => SwiftEnum<PaneKind>.FromRawValue(id);

    /// <summary>JavaScript's Math.round, which the canvas's readouts use (half away from zero for positives).</summary>
    internal static string RoundText(double value) =>
        Math.Floor(value + 0.5).ToString("0", CultureInfo.InvariantCulture);
}
